using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Logging;

internal sealed class ConsolePipeServer : IDisposable
{
	private const string PipeName = "MESharpConsole";
	private readonly CancellationTokenSource _cts = new();
	private readonly object _pipeSync = new();
	private Task? _listenerTask;
	private NamedPipeServerStream? _activePipe;
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
			try
			{
				using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
					PipeTransmissionMode.Message, PipeOptions.Asynchronous);
				using var cancelRegistration = token.Register(static state =>
				{
					try
					{
						((NamedPipeServerStream)state!).Dispose();
					}
					catch
					{
						// Ignore cancellation disposal races.
					}
				}, pipe);
				SetActivePipe(pipe);

				try
				{
					await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
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
					catch (ObjectDisposedException) when (token.IsCancellationRequested)
					{
						break;
					}

					if (line is null)
					{
						break;
					}

					ProcessRemoteLine(line);
				}
			}
			catch (Exception) when (!token.IsCancellationRequested)
			{
				// Keep the listener alive if a malformed message or transient pipe failure occurs.
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
				SetActivePipe(null);
			}
		}
	}

	private void SetActivePipe(NamedPipeServerStream? pipe)
	{
		lock (_pipeSync)
		{
			_activePipe = pipe;
		}
	}

	private static void ProcessRemoteLine(string line)
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

		lock (_pipeSync)
		{
			try
			{
				_activePipe?.Dispose();
			}
			catch
			{
				// best-effort cancel of blocked IO
			}
			_activePipe = null;
		}

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
