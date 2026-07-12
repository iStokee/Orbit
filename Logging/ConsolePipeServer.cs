using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Logging;

/// <summary>
/// Receives console log lines from every injected MESharp client over the shared
/// <c>MESharpConsole</c> pipe. Multi-instance: each connecting session gets its own server
/// instance and reader task, so one long-lived session can never lock the others out
/// (the previous single-instance server silently dropped every other session's logs).
/// Lines are attributed to their session via the connected client's process id.
/// </summary>
internal sealed class ConsolePipeServer : IDisposable
{
	private const string PipeName = "MESharpConsole";
	// Injected clients hold their connection open for their whole lifetime, so allow one
	// instance per plausible concurrent session plus headroom.
	private const int MaxInstances = 16;

	private readonly CancellationTokenSource _cts = new();
	private readonly ConcurrentDictionary<NamedPipeServerStream, byte> _activePipes = new();
	private Task? _listenerTask;
	private bool _disposed;

	public void Start()
	{
		if (_disposed)
		{
			return;
		}

		_listenerTask ??= Task.Run(ListenAsync, _cts.Token);
	}

	private async Task ListenAsync()
	{
		var token = _cts.Token;

		while (!token.IsCancellationRequested)
		{
			NamedPipeServerStream? pipe = null;
			try
			{
				pipe = Orbit.Services.MESharpPipeSecurity.CreateServer(PipeName, PipeDirection.In, MaxInstances,
					PipeTransmissionMode.Message, PipeOptions.Asynchronous);
				_activePipes.TryAdd(pipe, 0);

				await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

				// Hand the connection to its own reader and immediately pend the next accept,
				// so additional sessions can connect while this one streams.
				var connected = pipe;
				pipe = null;
				_ = Task.Run(() => ReadConnectionAsync(connected, token), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (ObjectDisposedException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch (IOException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch (Exception) when (!token.IsCancellationRequested)
			{
				// Keep the listener alive if a transient pipe failure occurs.
				try
				{
					await Task.Delay(100, token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
			finally
			{
				if (pipe != null)
				{
					_activePipes.TryRemove(pipe, out _);
					try { pipe.Dispose(); } catch { /* teardown race */ }
				}
			}
		}
	}

	private async Task ReadConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
	{
		var sessionPid = TryGetClientProcessId(pipe);
		try
		{
			using var reader = new StreamReader(pipe, Encoding.UTF8);

			while (!token.IsCancellationRequested && pipe.IsConnected)
			{
				string? line;
				try
				{
					line = await reader.ReadLineAsync().ConfigureAwait(false);
				}
				catch (IOException)
				{
					break;
				}
				catch (ObjectDisposedException)
				{
					break;
				}

				if (line is null)
				{
					break;
				}

				ProcessRemoteLine(line, sessionPid);
			}
		}
		catch
		{
			// Reader teardown races with Dispose; nothing to surface.
		}
		finally
		{
			_activePipes.TryRemove(pipe, out _);
			try { pipe.Dispose(); } catch { /* already gone */ }
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetNamedPipeClientProcessId(IntPtr pipeHandle, out uint clientProcessId);

	/// <summary>The wire format carries no session identity; recover it from the pipe handle.</summary>
	private static int? TryGetClientProcessId(NamedPipeServerStream pipe)
	{
		try
		{
			return GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)
				? (int)pid
				: null;
		}
		catch
		{
			return null;
		}
	}

	private static void ProcessRemoteLine(string line, int? sessionPid)
	{
		if (string.IsNullOrWhiteSpace(line))
			return;

		int level = 1;
		string message = line;

		int tabIndex = line.IndexOf('\t');
		if (tabIndex > 0 && int.TryParse(line.AsSpan(0, tabIndex), out var parsedLevel))
		{
			level = parsedLevel;
			message = line.Substring(tabIndex + 1);
		}

		if (sessionPid is { } pid)
		{
			message = $"[{pid}] {message}";
		}

		ConsoleLogLevel logLevel = level switch
		{
			0 => ConsoleLogLevel.Debug,
			1 => ConsoleLogLevel.Info,
			2 => ConsoleLogLevel.Warning,
			3 => ConsoleLogLevel.Error,
			4 => ConsoleLogLevel.Critical,
			_ => ConsoleLogLevel.Info
		};

		ConsoleLogService.Instance.AppendExternal(message, logLevel);
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		try
		{
			_cts.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// already torn down
		}

		foreach (var pipe in _activePipes.Keys)
		{
			try
			{
				pipe.Dispose();
			}
			catch
			{
				// best-effort cancel of blocked IO
			}
		}
		_activePipes.Clear();

		try
		{
			_listenerTask?.Wait(1000);
		}
		catch
		{
			// swallow
		}

		_cts.Dispose();
	}
}
