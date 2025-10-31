using System.ComponentModel;
using System.Runtime.CompilerServices;
using Orbit.Logging;

namespace Orbit.Models;

public sealed class ConsoleSourceInfo : INotifyPropertyChanged
{
	private int _count;
	private int _errorCount;
	private int _warningCount;

	public ConsoleSourceInfo(ConsoleLogSource source, string displayName, string description, string iconKind)
	{
		Source = source;
		DisplayName = displayName;
		Description = description;
		IconKind = iconKind;
	}

	public ConsoleLogSource Source { get; }
	public string DisplayName { get; }
	public string Description { get; }
	public string IconKind { get; }

	public int Count
	{
		get => _count;
		set
		{
			if (_count == value) return;
			_count = value;
			OnPropertyChanged();
		}
	}

	public int ErrorCount
	{
		get => _errorCount;
		set
		{
			if (_errorCount == value) return;
			_errorCount = value;
			OnPropertyChanged();
		}
	}

	public int WarningCount
	{
		get => _warningCount;
		set
		{
			if (_warningCount == value) return;
			_warningCount = value;
			OnPropertyChanged();
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
