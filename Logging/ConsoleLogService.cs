using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;

namespace Orbit.Logging;

public sealed class ConsoleLogService
{
	private const int MaxEntries = 5000;
	private static readonly Lazy<ConsoleLogService> _lazy = new(() => new ConsoleLogService());

	private readonly ObservableCollection<ConsoleLogEntry> _entries = new();
	private readonly ReadOnlyObservableCollection<ConsoleLogEntry> _readonlyEntries;
	private TextWriter? _originalOut;
	private TextWriter? _originalError;
	private bool _isCapturing;

	public ConsoleLogService()
	{
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

		void AddEntry()
		{
		_entries.Add(new ConsoleLogEntry(DateTime.Now, source, level, message.TrimEnd()));

			while (_entries.Count > MaxEntries)
			{
				_entries.RemoveAt(0);
			}
		}

		var dispatcher = Application.Current?.Dispatcher;

		if (dispatcher == null)
		{
			AddEntry();
		}
		else if (dispatcher.CheckAccess())
		{
			AddEntry();
		}
		else
		{
			// Use async invoke to avoid blocking the caller thread
			dispatcher.InvokeAsync(AddEntry, System.Windows.Threading.DispatcherPriority.Background);
		}
	}

	public void AppendExternal(string message, ConsoleLogLevel level)
		=> Append(message, ConsoleLogSource.MemoryError, level);

	public void Clear()
	{
		void ClearInternal() => _entries.Clear();

		var dispatcher = Application.Current?.Dispatcher;

		if (dispatcher == null)
		{
			ClearInternal();
		}
		else if (dispatcher.CheckAccess())
		{
			ClearInternal();
		}
		else
		{
			// Use async invoke to avoid blocking the caller thread
			dispatcher.InvokeAsync(ClearInternal, System.Windows.Threading.DispatcherPriority.Normal);
		}
	}
}
