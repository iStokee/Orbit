using Newtonsoft.Json;
using Orbit.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Orbit.Services;

public class ScriptManagerService
{
	private const int MaxRecentScripts = 10;
	private ObservableCollection<ScriptProfile> _recentScripts = new();

	public ObservableCollection<ScriptProfile> RecentScripts => _recentScripts;

	public ScriptManagerService()
	{
		LoadScriptProfiles();
	}

	public void AddOrUpdateScript(string filePath, string? name = null, string? description = null)
	{
		if (string.IsNullOrWhiteSpace(filePath))
			return;

		// Check if script already exists
		var existing = _recentScripts.FirstOrDefault(s =>
			string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

		if (existing != null)
		{
			// Update existing
			existing.LastUsed = DateTime.Now;
			if (!string.IsNullOrWhiteSpace(name))
				existing.Name = name;
			if (!string.IsNullOrWhiteSpace(description))
				existing.Description = description;

			// Move to top if not already
			_recentScripts.Remove(existing);
			_recentScripts.Insert(0, existing);
		}
		else
		{
			// Create new profile
			var profile = new ScriptProfile
			{
				FilePath = filePath,
				Name = name ?? Path.GetFileNameWithoutExtension(filePath),
				Description = description ?? string.Empty,
				LastUsed = DateTime.Now
			};

			_recentScripts.Insert(0, profile);
		}

		// Trim to max recent scripts
		while (_recentScripts.Count > MaxRecentScripts)
		{
			_recentScripts.RemoveAt(_recentScripts.Count - 1);
		}

		SaveScriptProfiles();
	}

	public void RemoveScript(ScriptProfile profile)
	{
		_recentScripts.Remove(profile);
		SaveScriptProfiles();
	}

	public void ToggleFavorite(ScriptProfile profile)
	{
		profile.IsFavorite = !profile.IsFavorite;
		SaveScriptProfiles();
	}

	public ScriptProfile? GetMostRecent()
		=> _recentScripts.FirstOrDefault();

	public IEnumerable<ScriptProfile> GetFavorites()
		=> _recentScripts.Where(s => s.IsFavorite).OrderBy(s => s.Name);

	private void LoadScriptProfiles()
	{
		var json = Settings.Default.ScriptProfiles;
		if (string.IsNullOrWhiteSpace(json))
		{
			_recentScripts = new ObservableCollection<ScriptProfile>();
			return;
		}

		try
		{
			var profiles = JsonConvert.DeserializeObject<List<ScriptProfile>>(json);
			_recentScripts = profiles != null
				? new ObservableCollection<ScriptProfile>(profiles.OrderByDescending(p => p.LastUsed))
				: new ObservableCollection<ScriptProfile>();
		}
		catch
		{
			_recentScripts = new ObservableCollection<ScriptProfile>();
		}
	}

	private void SaveScriptProfiles()
	{
		try
		{
			var json = JsonConvert.SerializeObject(_recentScripts, Formatting.Indented);
			Settings.Default.ScriptProfiles = json;
			Settings.Default.Save();
		}
		catch
		{
			// Silently fail - not critical
		}
	}

	public void ClearNonExisting()
	{
		var toRemove = _recentScripts.Where(s => !s.FileExists).ToList();
		foreach (var script in toRemove)
		{
			_recentScripts.Remove(script);
		}

		if (toRemove.Any())
			SaveScriptProfiles();
	}
}
