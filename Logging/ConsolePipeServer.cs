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
	private Task? _listenerTask;
	private bool _disposed;

	public void Start()
	{
		_listenerTask ??= Task.Run(ListenAsync, _cts.Token);
	}

	private async Task ListenAsync()
	{
		var token = _cts.Token;

		while (!token.IsCancellationRequested)
		{
			using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
				PipeTransmissionMode.Message, PipeOptions.Asynchronous);

			try
			{
				await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
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

				if (line is null)
				{
					break;
				}

				ProcessRemoteLine(line);
			}
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
