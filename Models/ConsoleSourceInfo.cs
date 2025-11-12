using Orbit.Logging;

namespace Orbit.Models;

public sealed class ConsoleSourceInfo : ObservableObject
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
		set => SetProperty(ref _count, value);
	}

	public int ErrorCount
	{
		get => _errorCount;
		set => SetProperty(ref _errorCount, value);
	}

	public int WarningCount
	{
		get => _warningCount;
		set => SetProperty(ref _warningCount, value);
	}
}
