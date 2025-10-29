using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
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

	public void Append(string message, ConsoleLogSource source, ConsoleLogLevel level)
	{
		if (string.IsNullOrWhiteSpace(message))
			return;

		var entry = new ConsoleLogEntry(DateTime.Now, source, level, message.TrimEnd());
		if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
		{
			// App is shutting down; mutate directly to preserve tail logs.
			_entries.Add(entry);
			TrimEntries();
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
			_pendingEntries.Clear();
			_entries.Clear();
		}
		else
		{
			_pendingEntries.Clear();
			_dispatcher.BeginInvoke(new Action(() => _entries.Clear()), DispatcherPriority.Background);
		}
	}

	private void ScheduleFlush()
	{
		if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) == 0)
		{
			_dispatcher.BeginInvoke(new Action(FlushPendingEntries), DispatcherPriority.Background);
		}
	}

	private void FlushPendingEntries()
	{
		if (!_dispatcher.CheckAccess())
		{
			_dispatcher.BeginInvoke(new Action(FlushPendingEntries), DispatcherPriority.Background);
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
