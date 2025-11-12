using System;
using System.IO;

namespace Orbit.Models;

public class ScriptProfile : ObservableObject
{
	private string _name = string.Empty;
	private string _description = string.Empty;
	private string _filePath = string.Empty;
	private DateTime _lastUsed;
	private bool _isFavorite;
	private bool _hideFromRecents;

	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value);
	}

	public string Description
	{
		get => _description;
		set => SetProperty(ref _description, value);
	}

	public string FilePath
	{
		get => _filePath;
		set
		{
			if (SetProperty(ref _filePath, value))
			{
				OnPropertyChanged(nameof(FileExists));
				OnPropertyChanged(nameof(FileName));
			}
		}
	}

	public DateTime LastUsed
	{
		get => _lastUsed;
		set
		{
			if (SetProperty(ref _lastUsed, value))
			{
				OnPropertyChanged(nameof(LastUsedDisplay));
			}
		}
	}

	public bool IsFavorite
	{
		get => _isFavorite;
		set => SetProperty(ref _isFavorite, value);
	}

	public bool HideFromRecents
	{
		get => _hideFromRecents;
		set => SetProperty(ref _hideFromRecents, value);
	}

	// Computed properties
	public bool FileExists => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

	public string FileName => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);

	public string LastUsedDisplay
	{
		get
		{
			if (LastUsed == DateTime.MinValue) return "Never";

			var timeSpan = DateTime.Now - LastUsed;
			if (timeSpan.TotalMinutes < 1) return "Just now";
			if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes}m ago";
			if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours}h ago";
			if (timeSpan.TotalDays < 7) return $"{(int)timeSpan.TotalDays}d ago";

			return LastUsed.ToString("MM/dd/yyyy");
		}
	}
	/// <summary>
	/// Returns the file path for display in ComboBox text field.
	/// This is critical for editable ComboBox to show the path instead of type name.
	/// </summary>
	public override string ToString() => FilePath ?? string.Empty;
}
