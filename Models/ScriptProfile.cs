using System;
using System.ComponentModel;
using System.IO;

namespace Orbit.Models;

public class ScriptProfile : INotifyPropertyChanged
{
	private string _name = string.Empty;
	private string _description = string.Empty;
	private string _filePath = string.Empty;
	private DateTime _lastUsed;
	private bool _isFavorite;

	public string Name
	{
		get => _name;
		set
		{
			if (_name == value) return;
			_name = value;
			OnPropertyChanged(nameof(Name));
		}
	}

	public string Description
	{
		get => _description;
		set
		{
			if (_description == value) return;
			_description = value;
			OnPropertyChanged(nameof(Description));
		}
	}

	public string FilePath
	{
		get => _filePath;
		set
		{
			if (_filePath == value) return;
			_filePath = value;
			OnPropertyChanged(nameof(FilePath));
			OnPropertyChanged(nameof(FileExists));
			OnPropertyChanged(nameof(FileName));
		}
	}

	public DateTime LastUsed
	{
		get => _lastUsed;
		set
		{
			if (_lastUsed == value) return;
			_lastUsed = value;
			OnPropertyChanged(nameof(LastUsed));
			OnPropertyChanged(nameof(LastUsedDisplay));
		}
	}

	public bool IsFavorite
	{
		get => _isFavorite;
		set
		{
			if (_isFavorite == value) return;
			_isFavorite = value;
			OnPropertyChanged(nameof(IsFavorite));
		}
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

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
