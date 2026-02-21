using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services;

public static class OrbitInteractionLogger
{
	private static readonly ConcurrentQueue<string> _pending = new();
	private static readonly ConcurrentDictionary<string, long> _lastLogTicksByKey = new();
	private static readonly SemaphoreSlim _signal = new(0, int.MaxValue);
	private static readonly object _stateLock = new();

	private static CancellationTokenSource? _loopCts;
	private static Task? _loopTask;
	private static bool _enabled;

	public static bool IsEnabled
	{
		get
		{
			lock (_stateLock)
			{
				return _enabled;
			}
		}
		set
		{
			lock (_stateLock)
			{
				if (_enabled == value)
				{
					return;
				}

				_enabled = value;
				if (_enabled)
				{
					StartLoop_NoLock();
				}
				else
				{
					StopLoop_NoLock();
				}
			}

			if (value)
			{
				Log($"=== Orbit interaction logging enabled. File: {LogFilePath} ===");
			}
		}
	}

	public static string LogsDirectory => Path.Combine(AppContext.BaseDirectory, "logs");
	public static string LogFilePath => Path.Combine(LogsDirectory, $"orbit-interactions-{DateTime.Now:yyyyMMdd}.log");

	public static void Log(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		lock (_stateLock)
		{
			if (!_enabled)
			{
				return;
			}
		}

		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		_pending.Enqueue($"[{timestamp}] {message}");
		try
		{
			_signal.Release();
		}
		catch (SemaphoreFullException)
		{
			// Best effort.
		}
	}

	public static void LogThrottled(string key, string message, int minIntervalMs = 250)
	{
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		var nowTicks = DateTime.UtcNow.Ticks;
		var thresholdTicks = TimeSpan.FromMilliseconds(Math.Max(1, minIntervalMs)).Ticks;

		if (_lastLogTicksByKey.TryGetValue(key, out var lastTicks) && (nowTicks - lastTicks) < thresholdTicks)
		{
			return;
		}

		_lastLogTicksByKey[key] = nowTicks;
		Log(message);
	}

	public static void ClearLog()
	{
		try
		{
			Directory.CreateDirectory(LogsDirectory);
			File.WriteAllText(LogFilePath, string.Empty);
		}
		catch
		{
			// Best effort.
		}
	}

	public static void Shutdown()
	{
		lock (_stateLock)
		{
			StopLoop_NoLock();
		}
	}

	private static void StartLoop_NoLock()
	{
		Directory.CreateDirectory(LogsDirectory);
		_loopCts = new CancellationTokenSource();
		_loopTask = Task.Run(() => RunWriterLoopAsync(_loopCts.Token));
	}

	private static void StopLoop_NoLock()
	{
		try
		{
			_loopCts?.Cancel();
		}
		catch
		{
			// Best effort.
		}

		try
		{
			_signal.Release();
		}
		catch
		{
			// Best effort.
		}

		try
		{
			_loopTask?.Wait(TimeSpan.FromMilliseconds(800));
		}
		catch
		{
			// Best effort.
		}

		_loopCts?.Dispose();
		_loopCts = null;
		_loopTask = null;
	}

	private static async Task RunWriterLoopAsync(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				await _signal.WaitAsync(token).ConfigureAwait(false);
				FlushPending();
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown.
		}
		finally
		{
			FlushPending();
		}
	}

	private static void FlushPending()
	{
		if (_pending.IsEmpty)
		{
			return;
		}

		var sb = new StringBuilder();
		while (_pending.TryDequeue(out var line))
		{
			sb.AppendLine(line);
		}

		if (sb.Length == 0)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(LogsDirectory);
			File.AppendAllText(LogFilePath, sb.ToString());
		}
		catch
		{
			// Best effort.
		}
	}
}
