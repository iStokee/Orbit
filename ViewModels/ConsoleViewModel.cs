using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Orbit.Logging;
using Orbit.Models;
using Clipboard = System.Windows.Clipboard;

namespace Orbit.ViewModels;

public sealed class ConsoleViewModel : INotifyPropertyChanged
{
	public static ConsoleViewModel Instance { get; } = new ConsoleViewModel();

	private bool _autoScrollEnabled = true;
	private int _selectedTabIndex;
	private readonly ObservableCollection<ConsoleSourceInfo> _sources;

	private ConsoleViewModel()
	{
		ClearCommand = new RelayCommand(ConsoleLog.Clear);
		CopySelectedCommand = new RelayCommand<IList?>(CopySelected, list => list is IList l && l.Count > 0);
		OpenSourceTabCommand = new RelayCommand<ConsoleSourceInfo?>(OpenSourceTab, info => info != null);

		// Load auto-scroll preference from settings
		_autoScrollEnabled = Settings.Default.ConsoleAutoScroll;

		// Initialize source info for Summary tab
		_sources = new ObservableCollection<ConsoleSourceInfo>
		{
			new ConsoleSourceInfo(ConsoleLogSource.Orbit, "Orbit System", "Orbit launcher and UI logs", "ViewDashboard"),
			new ConsoleSourceInfo(ConsoleLogSource.MemoryError, "MemoryError (ME)", "Native C++ hook and injector logs", "Memory"),
			new ConsoleSourceInfo(ConsoleLogSource.External, "Scripts", "C# scripts and external sources", "CodeBraces")
		};

		// Create filtered collections for each source
		OrbitEntries = CreateFilteredView(ConsoleLogSource.Orbit);
		MemoryErrorEntries = CreateFilteredView(ConsoleLogSource.MemoryError);
		ExternalEntries = CreateFilteredView(ConsoleLogSource.External);

		// Subscribe to collection changes to update source statistics
		if (ConsoleLog.Entries is INotifyCollectionChanged notifyCollection)
		{
			notifyCollection.CollectionChanged += UpdateSourceStatistics;
		}

		// Initial statistics calculation
		UpdateSourceStatistics(null, null);
	}

	private ListCollectionView CreateFilteredView(ConsoleLogSource source)
	{
		var view = new ListCollectionView(ConsoleLog.Entries)
		{
			Filter = obj => obj is ConsoleLogEntry entry && entry.Source == source
		};
		return view;
	}

	private void UpdateSourceStatistics(object? sender, NotifyCollectionChangedEventArgs? e)
	{
		if (ConsoleLog?.Entries == null) return;

		foreach (var sourceInfo in _sources)
		{
			var entries = ConsoleLog.Entries.Where(entry => entry.Source == sourceInfo.Source).ToList();
			sourceInfo.Count = entries.Count;
			sourceInfo.ErrorCount = entries.Count(entry => entry.Level == ConsoleLogLevel.Error);
			sourceInfo.WarningCount = entries.Count(entry => entry.Level == ConsoleLogLevel.Warning);
		}
	}

	private void OpenSourceTab(ConsoleSourceInfo? sourceInfo)
	{
		if (sourceInfo == null) return;

		// Map source to tab index: Summary=0, AllSources=1, Orbit=2, MemoryError=3, External=4
		SelectedTabIndex = sourceInfo.Source switch
		{
			ConsoleLogSource.Orbit => 2,
			ConsoleLogSource.MemoryError => 3,
			ConsoleLogSource.External => 4,
			_ => 1
		};
	}

	public ConsoleLogService ConsoleLog => ConsoleLogService.Instance;

	public ObservableCollection<ConsoleSourceInfo> Sources => _sources;
	public ListCollectionView OrbitEntries { get; }
	public ListCollectionView MemoryErrorEntries { get; }
	public ListCollectionView ExternalEntries { get; }

	public IRelayCommand ClearCommand { get; }
	public IRelayCommand<IList?> CopySelectedCommand { get; }
	public IRelayCommand<ConsoleSourceInfo?> OpenSourceTabCommand { get; }

	public int SelectedTabIndex
	{
		get => _selectedTabIndex;
		set
		{
			if (_selectedTabIndex == value) return;
			_selectedTabIndex = value;
			OnPropertyChanged();
		}
	}

	public bool AutoScrollEnabled
	{
		get => _autoScrollEnabled;
		set
		{
			if (_autoScrollEnabled == value) return;
			_autoScrollEnabled = value;
			Settings.Default.ConsoleAutoScroll = value;
			Settings.Default.Save();
			OnPropertyChanged();
		}
	}

	private void CopySelected(IList? selectedItems)
	{
		if (selectedItems == null || selectedItems.Count == 0)
			return;

		var sb = new StringBuilder();
		foreach (var item in selectedItems.Cast<ConsoleLogEntry>())
		{
			sb.AppendLine(item.DisplayText);
		}

		try
		{
			Clipboard.SetText(sb.ToString());
		}
		catch
		{
			// Clipboard operations can fail, silently ignore
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
