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

	public static IReadOnlyList<LauncherAccountModel> Load()
	{
		var path = GetEnvVarsPath();
		if (!File.Exists(path))
		{
			return Array.Empty<LauncherAccountModel>();
		}

		try
		{
			var json = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(json))
			{
				return Array.Empty<LauncherAccountModel>();
			}

			var doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Array)
			{
				return Array.Empty<LauncherAccountModel>();
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
		catch
		{
			return Array.Empty<LauncherAccountModel>();
		}
	}

	public static void Save(IEnumerable<LauncherAccountModel> accounts)
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

		var directory = GetMemoryErrorDirectory();
		Directory.CreateDirectory(directory);
		var path = GetEnvVarsPath();
		var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(path, json);
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
