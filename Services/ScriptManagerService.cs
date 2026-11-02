using Newtonsoft.Json;
using Orbit.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Orbit.Services;

/// <summary>
/// Handles persistence, favorites, and library enumeration for script DLL profiles.
/// ViewModels use this as the single source of truth for recents/favorites.
/// </summary>
public class ScriptManagerService : ObservableObject
{
	private const int MaxRecentScripts = 10;
	private static readonly string DefaultScriptsDirectory =
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MemoryError", "CSharp_scripts");
	private ObservableCollection<ScriptProfile> _profiles = new();
	private readonly Func<string> _readProfiles;
	private readonly Action<string> _writeProfiles;
	private bool _loadFailed;
	private string? _lastError;
	public string? LastError { get => _lastError; private set => SetProperty(ref _lastError, value); }

	/// <summary>
	/// Durable script configuration; recent history is a bounded projection of this catalog.
	/// </summary>
	public ObservableCollection<ScriptProfile> Profiles => _profiles;
	public IEnumerable<ScriptProfile> RecentScripts => _profiles.Where(p => !p.HideFromRecents).OrderByDescending(p => p.LastUsed).Take(MaxRecentScripts);

	/// <summary>
	/// On-disk directory scanned to populate the default script library.
	/// </summary>
	public string DefaultLibraryPath => DefaultScriptsDirectory;

	public ScriptManagerService()
		: this(() => Settings.Default.ScriptProfiles, json => { Settings.Default.ScriptProfiles = json; Settings.Default.Save(); })
	{ }

	internal ScriptManagerService(Func<string> readProfiles, Action<string> writeProfiles)
	{
		_readProfiles = readProfiles;
		_writeProfiles = writeProfiles;
		LoadScriptProfiles();
		TrimRecentHistory();
	}

	/// <summary>
	/// Adds a script to the recent list or bumps it to the top if already tracked. Also refreshes name/description.
	/// </summary>
	public void AddOrUpdateScript(string filePath, string? name = null, string? description = null)
		=> AddOrUpdateScript(filePath, name, description, scriptId: null);

	/// <summary>
	/// Adds/updates a script profile and tracks an explicit script id used for multi-assembly runtime commands.
	/// </summary>
	public void AddOrUpdateScript(string filePath, string? name, string? description, string? scriptId)
	{
		if (_loadFailed) return;
		if (string.IsNullOrWhiteSpace(filePath))
			return;

		// Check if script already exists
		var existing = _profiles.FirstOrDefault(s =>
			string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

		if (existing != null)
		{
			// Update existing
			existing.LastUsed = DateTime.Now;
			existing.HideFromRecents = false;
			existing.ScriptId = NormalizeScriptId(scriptId ?? existing.ScriptId, existing.FilePath);
			if (!string.IsNullOrWhiteSpace(name))
				existing.Name = name;
			if (!string.IsNullOrWhiteSpace(description))
				existing.Description = description;

			// Move to top if not already
			_profiles.Remove(existing);
			_profiles.Insert(0, existing);
		}
		else
		{
			// Create new profile
			var profile = new ScriptProfile
			{
				FilePath = filePath,
				Name = name ?? Path.GetFileNameWithoutExtension(filePath),
				Description = description ?? string.Empty,
				ScriptId = NormalizeScriptId(scriptId, filePath),
				LastUsed = DateTime.Now,
				HideFromRecents = false
			};

			_profiles.Insert(0, profile);
		}

		TrimRecentHistory();

		SaveScriptProfiles();
	}

	/// <summary>
	/// Removes the script from recents and persists the updated list.
	/// </summary>
	public void RemoveScript(ScriptProfile profile)
	{
		if (_loadFailed) return;
		profile.HideFromRecents = true;
		SaveScriptProfiles();
	}

	/// <summary>
	/// Flips the persisted favorite flag for a script profile.
	/// </summary>
	public void ToggleFavorite(ScriptProfile profile)
	{
		if (_loadFailed) return;
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

		return _profiles.FirstOrDefault(s =>
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
			if (!HasScriptEntryPoint(file)) continue;

			yield return new ScriptProfile
			{
				FilePath = file,
				Name = Path.GetFileNameWithoutExtension(file),
				Description = string.Empty,
				ScriptId = DeriveScriptIdFromPath(file),
				LastUsed = DateTime.MinValue,
				HideFromRecents = false
			};
		}
	}

	internal static bool HasScriptEntryPoint(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			using var pe = new PEReader(stream);
			if (!pe.HasMetadata) return false;
			var metadata = pe.GetMetadataReader();
			foreach (var handle in metadata.TypeDefinitions)
			{
				var type = metadata.GetTypeDefinition(handle);
				if (metadata.GetString(type.Namespace) != "MESharp" || metadata.GetString(type.Name) != "ScriptEntry") continue;
				return type.GetMethods().Any(methodHandle =>
				{
					var method = metadata.GetMethodDefinition(methodHandle);
					return metadata.GetString(method.Name) == "Initialize"
						&& (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public
						&& (method.Attributes & MethodAttributes.Static) != 0;
				});
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException) { }
		return false;
	}

	/// <summary>
	/// Convenience accessor for the newest script entry.
	/// </summary>
	public ScriptProfile? GetMostRecent()
		=> RecentScripts.FirstOrDefault();

	/// <summary>
	/// Returns scripts flagged as favorites ordered alphabetically.
	/// </summary>
	public IEnumerable<ScriptProfile> GetFavorites()
		=> _profiles.Where(s => s.IsFavorite).OrderBy(s => s.Name);

	/// <summary>
	/// Restores the durable catalog, preserving corrupt settings for recovery.
	/// </summary>
	private void LoadScriptProfiles()
	{
		try
		{
			var json = _readProfiles();
			if (string.IsNullOrWhiteSpace(json)) return;
			var profiles = JsonConvert.DeserializeObject<List<ScriptProfile>>(json)
				?? throw new InvalidDataException("Script catalog must contain an array.");
			if (profiles != null)
			{
				foreach (var profile in profiles)
				{
					profile.ScriptId = NormalizeScriptId(profile.ScriptId, profile.FilePath);
				}
			}
			_profiles = profiles != null
				? new ObservableCollection<ScriptProfile>(profiles.OrderByDescending(p => p.LastUsed))
				: new ObservableCollection<ScriptProfile>();
		}
		catch (Exception ex)
		{
			_loadFailed = true;
			LastError = $"Cannot load script catalog; original settings preserved: {ex.Message}";
		}
	}

	/// <summary>
	/// Limits history without discarding durable script configuration.
	/// </summary>
	private void TrimRecentHistory()
	{
		foreach (var profile in _profiles.Where(p => !p.HideFromRecents).OrderByDescending(p => p.LastUsed).Skip(MaxRecentScripts).ToList())
			profile.HideFromRecents = true;
	}

	private void SaveScriptProfiles()
	{
		if (_loadFailed) return;
		try
		{
			TrimRecentHistory();
			var json = JsonConvert.SerializeObject(_profiles, Formatting.Indented);
			_writeProfiles(json);
			LastError = null;
		}
		catch (Exception ex)
		{
			LastError = $"Cannot save script catalog: {ex.Message}";
		}
	}

	/// <summary>
	/// Hides missing files from history without deleting their durable configuration.
	/// </summary>
	public void ClearNonExisting()
	{
		var toRemove = _profiles.Where(s => !s.FileExists).ToList();
		foreach (var script in toRemove)
		{
			script.HideFromRecents = true;
		}

		if (toRemove.Any())
			SaveScriptProfiles();
	}

	public static string DeriveScriptIdFromPath(string? filePath)
	{
		var name = string.IsNullOrWhiteSpace(filePath)
			? string.Empty
			: Path.GetFileNameWithoutExtension(filePath);
		if (string.IsNullOrWhiteSpace(name))
		{
			return "default";
		}

		var chars = name.Trim().ToLowerInvariant().ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			var c = chars[i];
			chars[i] = char.IsLetterOrDigit(c) ? c : '.';
		}

		var normalized = new string(chars);
		while (normalized.Contains("..", StringComparison.Ordinal))
		{
			normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
		}

		normalized = normalized.Trim('.');
		return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
	}

	private static string NormalizeScriptId(string? scriptId, string? filePath)
	{
		return string.IsNullOrWhiteSpace(scriptId)
			? DeriveScriptIdFromPath(filePath)
			: scriptId.Trim();
	}
}
