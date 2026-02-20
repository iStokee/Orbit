using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Orbit.Logging;

public sealed class ConsoleLogService
{
	private const int MaxEntries = 5000;
	private static readonly Lazy<ConsoleLogService> _lazy = new(() => new ConsoleLogService());

	private readonly ObservableCollection<ConsoleLogEntry> _entries = new();
	private readonly ReadOnlyObservableCollection<ConsoleLogEntry> _readonlyEntries;
	private readonly Dispatcher _dispatcher;
	private readonly ConcurrentQueue<ConsoleLogEntry> _pendingEntries = new();
	private TextWriter? _originalOut;
	private TextWriter? _originalError;
	private bool _isCapturing;
	private int _flushScheduled;

	public ConsoleLogService()
	{
		_dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
		_readonlyEntries = new ReadOnlyObservableCollection<ConsoleLogEntry>(_entries);
	}

	public static ConsoleLogService Instance => _lazy.Value;

	public ReadOnlyObservableCollection<ConsoleLogEntry> Entries => _readonlyEntries;

	public void StartCapture()
	{
		if (_isCapturing)
			return;

		_originalOut = Console.Out;
		_originalError = Console.Error;
		Console.SetOut(new ConsoleRedirectWriter(this, ConsoleLogSource.Orbit, ConsoleLogLevel.Info, _originalOut));
		Console.SetError(new ConsoleRedirectWriter(this, ConsoleLogSource.Orbit, ConsoleLogLevel.Error, _originalError));
		_isCapturing = true;
	}

	public void StopCapture()
	{
		if (!_isCapturing)
		{
			return;
		}

		try
		{
			if (_originalOut != null)
			{
				Console.SetOut(_originalOut);
			}

			if (_originalError != null)
			{
				Console.SetError(_originalError);
			}
		}
		catch
		{
			// Best effort during app shutdown.
		}
		finally
		{
			_originalOut = null;
			_originalError = null;
			_isCapturing = false;
		}
	}

	public void Append(string message, ConsoleLogSource source, ConsoleLogLevel level)
	{
		if (string.IsNullOrWhiteSpace(message))
			return;

		var entry = new ConsoleLogEntry(DateTime.Now, source, level, message.TrimEnd());
		if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
		{
			// Dispatcher is no longer reliable; only mutate collection on owning thread.
			if (_dispatcher.CheckAccess())
			{
				_entries.Add(entry);
				TrimEntries();
			}
			return;
		}

		_pendingEntries.Enqueue(entry);
		ScheduleFlush();
	}

	public void AppendExternal(string message, ConsoleLogLevel level)
		=> Append(message, ConsoleLogSource.MemoryError, level);

	public void Clear()
	{
		if (_dispatcher.CheckAccess())
		{
			_pendingEntries.Clear();
			_entries.Clear();
		}
		else if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
		{
			// Avoid cross-thread ObservableCollection mutation during shutdown.
			_pendingEntries.Clear();
		}
		else
		{
			_pendingEntries.Clear();
			try
			{
				_dispatcher.BeginInvoke(new Action(() => _entries.Clear()), DispatcherPriority.Background);
			}
			catch (InvalidOperationException)
			{
				// Dispatcher unavailable; nothing else to clear safely off-thread.
			}
		}
	}

	private void ScheduleFlush()
	{
		if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) == 0)
		{
			try
			{
				_dispatcher.BeginInvoke(new Action(FlushPendingEntries), DispatcherPriority.Background);
			}
			catch (InvalidOperationException)
			{
				Interlocked.Exchange(ref _flushScheduled, 0);
			}
		}
	}

	private void FlushPendingEntries()
	{
		if (!_dispatcher.CheckAccess())
		{
			if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
			{
				Interlocked.Exchange(ref _flushScheduled, 0);
				return;
			}

			try
			{
				_dispatcher.BeginInvoke(new Action(FlushPendingEntries), DispatcherPriority.Background);
			}
			catch (InvalidOperationException)
			{
				Interlocked.Exchange(ref _flushScheduled, 0);
			}
			return;
		}

		try
		{
			while (_pendingEntries.TryDequeue(out var entry))
			{
				_entries.Add(entry);
				TrimEntries();
			}
		}
		finally
		{
			Interlocked.Exchange(ref _flushScheduled, 0);
			if (!_pendingEntries.IsEmpty)
			{
				ScheduleFlush();
			}
		}
	}

	private void TrimEntries()
	{
		while (_entries.Count > MaxEntries)
		{
			_entries.RemoveAt(0);
		}
	}
}
