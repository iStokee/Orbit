using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Orbit.Logging;
using Clipboard = System.Windows.Clipboard;

namespace Orbit.ViewModels;

public sealed class ConsoleViewModel : INotifyPropertyChanged
{
	public static ConsoleViewModel Instance { get; } = new ConsoleViewModel();

	private bool _autoScrollEnabled = true;

	private ConsoleViewModel()
	{
		ClearCommand = new RelayCommand(_ => ConsoleLog.Clear());
		CopySelectedCommand = new RelayCommand(p => CopySelected(p as IList), p => p is IList list && list.Count > 0);

		// Load auto-scroll preference from settings
		_autoScrollEnabled = Settings.Default.ConsoleAutoScroll;
	}

	public ConsoleLogService ConsoleLog => ConsoleLogService.Instance;

	public ICommand ClearCommand { get; }
	public ICommand CopySelectedCommand { get; }

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
