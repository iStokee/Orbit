using Orbit.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Text.Json;

namespace Orbit.Services;

public static class LauncherAccountStore
{
	private const string EnvVarsFileName = "env_vars.json";
	private static int _roundRobinCounter = -1;
	private static readonly object LaunchBatchSync = new();
	private static readonly object StorageSync = new();
	private static Queue<LauncherAccountModel>? _pendingSelectedLaunchQueue;

	public static IReadOnlyList<LauncherAccountModel> Load()
		=> Load(GetEnvVarsPath());

	internal static IReadOnlyList<LauncherAccountModel> Load(string path)
	{
		try
		{
			var json = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(json))
			{
				throw new InvalidDataException("Launcher account storage is empty. The original file has been preserved.");
			}

			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Array)
			{
				throw new InvalidDataException("Launcher account storage must contain an array. The original file has been preserved.");
			}

			var list = new List<LauncherAccountModel>();
			foreach (var item in doc.RootElement.EnumerateArray())
			{
				var displayName = ReadString(item, "JX_DISPLAY_NAME");
				if (string.IsNullOrWhiteSpace(displayName))
				{
					continue;
				}

				list.Add(new LauncherAccountModel
				{
					DisplayName = displayName,
					CharacterId = ReadString(item, "JX_CHARACTER_ID"),
					SessionId = ReadString(item, "JX_SESSION_ID"),
					IsSelected = ReadBool(item, "SELECTED") || ReadBool(item, "selected")
				});
			}

			return list;
		}
		catch (FileNotFoundException) { return Array.Empty<LauncherAccountModel>(); }
		catch (DirectoryNotFoundException) { return Array.Empty<LauncherAccountModel>(); }
		catch (JsonException ex) { throw new InvalidDataException("Launcher account storage is invalid. Repair the file before saving.", ex); }
	}

	public static void Save(IEnumerable<LauncherAccountModel> accounts)
		=> Save(accounts, GetEnvVarsPath());

	internal static void Save(IEnumerable<LauncherAccountModel> accounts, string path)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		var normalized = accounts
			.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName))
			.Select(a => new
			{
				JX_ACCESS_TOKEN = string.Empty,
				JX_DISPLAY_NAME = a.DisplayName.Trim(),
				JX_CHARACTER_ID = (a.CharacterId ?? string.Empty).Trim(),
				JX_REFRESH_TOKEN = string.Empty,
				JX_SESSION_ID = (a.SessionId ?? string.Empty).Trim(),
				SELECTED = a.IsSelected
			})
			.ToList();

		var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
		lock (StorageSync)
		{
			// Refuse to overwrite storage we cannot decode, including after a failed UI load.
			Load(path);
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
			var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(temporaryPath, json);
				if (File.Exists(path)) File.Replace(temporaryPath, path, null);
				else File.Move(temporaryPath, path);
			}
			finally
			{
				if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
			}
		}
	}

	public static bool TryGetByDisplayName(string? displayName, out LauncherAccountModel? account)
	{
		account = null;
		if (string.IsNullOrWhiteSpace(displayName))
		{
			return false;
		}

		account = Load().FirstOrDefault(a => string.Equals(a.DisplayName, displayName, StringComparison.Ordinal));
		return account != null;
	}

	public static IReadOnlyList<LauncherAccountModel> LoadSelected()
	{
		return Load()
			.Where(a => a.IsSelected && !string.IsNullOrWhiteSpace(a.DisplayName))
			.ToList();
	}

	public static bool TryGetNextSelected(out LauncherAccountModel? account)
	{
		account = null;
		lock (LaunchBatchSync)
		{
			if (_pendingSelectedLaunchQueue is { Count: > 0 })
			{
				account = _pendingSelectedLaunchQueue.Dequeue();
				if (_pendingSelectedLaunchQueue.Count == 0)
				{
					_pendingSelectedLaunchQueue = null;
				}

				return account != null;
			}
		}

		var selected = LoadSelected();
		if (selected.Count == 0)
		{
			return false;
		}

		var next = Interlocked.Increment(ref _roundRobinCounter);
		var index = (next & int.MaxValue) % selected.Count;
		account = selected[index];
		return true;
	}

	public static void BeginSelectedLaunchBatch()
	{
		var selected = LoadSelected()
			.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName))
			.ToList();

		lock (LaunchBatchSync)
		{
			_pendingSelectedLaunchQueue = selected.Count > 0
				? new Queue<LauncherAccountModel>(selected)
				: null;
		}
	}

	public static bool TryGetFirstConfigured(out LauncherAccountModel? account)
	{
		account = Load().FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.DisplayName));
		return account != null;
	}

	public static string GetMemoryErrorDirectory()
	{
		var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return Path.Combine(profile, "MemoryError");
	}

	private static string GetEnvVarsPath() => Path.Combine(GetMemoryErrorDirectory(), EnvVarsFileName);

	private static string ReadString(JsonElement item, string propertyName)
	{
		if (!item.TryGetProperty(propertyName, out var value))
		{
			return string.Empty;
		}

		return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
	}

	private static bool ReadBool(JsonElement item, string propertyName)
	{
		if (!item.TryGetProperty(propertyName, out var value))
		{
			return false;
		}

		return value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
	}
}
