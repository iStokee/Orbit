using Newtonsoft.Json;
using Orbit.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Orbit.Services;

/// <summary>
/// Handles persistence, favorites, and library enumeration for script DLL profiles.
/// ViewModels use this as the single source of truth for recents/favorites.
/// </summary>
public class ScriptManagerService
{
	private const int MaxRecentScripts = 10;
	private static readonly string DefaultScriptsDirectory =
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MemoryError", "CSharp_scripts");
	private ObservableCollection<ScriptProfile> _recentScripts = new();

	/// <summary>
	/// Live list of script profiles ordered by most recently used.
	/// </summary>
	public ObservableCollection<ScriptProfile> RecentScripts => _recentScripts;

	/// <summary>
	/// On-disk directory scanned to populate the default script library.
	/// </summary>
	public string DefaultLibraryPath => DefaultScriptsDirectory;

	public ScriptManagerService()
	{
		LoadScriptProfiles();
	}

	/// <summary>
	/// Adds a script to the recent list or bumps it to the top if already tracked. Also refreshes name/description.
	/// </summary>
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
			existing.HideFromRecents = false;
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
				LastUsed = DateTime.Now,
				HideFromRecents = false
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

	/// <summary>
	/// Removes the script from recents and persists the updated list.
	/// </summary>
	public void RemoveScript(ScriptProfile profile)
	{
		_recentScripts.Remove(profile);
		SaveScriptProfiles();
	}

	/// <summary>
	/// Flips the persisted favorite flag for a script profile.
	/// </summary>
	public void ToggleFavorite(ScriptProfile profile)
	{
		profile.IsFavorite = !profile.IsFavorite;
		SaveScriptProfiles();
	}

	/// <summary>
	/// Locates an existing script profile by absolute path.
	/// </summary>
	public ScriptProfile? FindByPath(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
			return null;

		return _recentScripts.FirstOrDefault(s =>
			string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Enumerates DLLs from the default library directory, merging them with tracked profiles.
	/// </summary>
	public IEnumerable<ScriptProfile> EnumerateDefaultScripts()
	{
		if (string.IsNullOrWhiteSpace(DefaultScriptsDirectory) || !Directory.Exists(DefaultScriptsDirectory))
			yield break;

		foreach (var file in Directory.EnumerateFiles(DefaultScriptsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
		{
			var fileName = Path.GetFileName(file);
			if (fileName.Equals("csharp_interop.dll", StringComparison.OrdinalIgnoreCase))
				continue;

			var tracked = FindByPath(file);
			if (tracked != null)
			{
				yield return tracked;
				continue;
			}

			yield return new ScriptProfile
			{
				FilePath = file,
				Name = Path.GetFileNameWithoutExtension(file),
				Description = string.Empty,
				LastUsed = DateTime.MinValue,
				HideFromRecents = false
			};
		}
	}

	/// <summary>
	/// Convenience accessor for the newest script entry.
	/// </summary>
	public ScriptProfile? GetMostRecent()
		=> _recentScripts.FirstOrDefault();

	/// <summary>
	/// Returns scripts flagged as favorites ordered alphabetically.
	/// </summary>
	public IEnumerable<ScriptProfile> GetFavorites()
		=> _recentScripts.Where(s => s.IsFavorite).OrderBy(s => s.Name);

	/// <summary>
	/// Restores recent scripts from user settings, ignoring corrupt payloads.
	/// </summary>
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

	/// <summary>
	/// Persists the current recent list to user settings. Failures are ignored to keep the UI responsive.
	/// </summary>
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

	/// <summary>
	/// Removes recents whose backing files are no longer present on disk.
	/// </summary>
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
