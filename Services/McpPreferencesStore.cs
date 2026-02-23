using System;
using System.IO;
using System.Text.Json;

namespace Orbit.Services;

public sealed class McpPreferencesStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "orbit.mcp.settings.json");

    private McpPreferences? _cached;

    public McpPreferences Load()
    {
        lock (Sync)
        {
            if (_cached != null)
            {
                return _cached;
            }

            if (!File.Exists(SettingsPath))
            {
                _cached = McpPreferences.Default();
                SaveInternal(_cached);
                return _cached;
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                _cached = JsonSerializer.Deserialize<McpPreferences>(json, JsonOptions) ?? McpPreferences.Default();
            }
            catch
            {
                _cached = McpPreferences.Default();
            }

            return _cached;
        }
    }

    public void Save(McpPreferences preferences)
    {
        if (preferences == null)
        {
            throw new ArgumentNullException(nameof(preferences));
        }

        lock (Sync)
        {
            _cached = preferences;
            SaveInternal(preferences);
        }
    }

    private static void SaveInternal(McpPreferences preferences)
    {
        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

public sealed class McpPreferences
{
    public bool AutoStartRuntimeOnInject { get; set; } = true;
    public bool AutoProbeEnabled { get; set; }
    public int AutoProbeSeconds { get; set; } = 3;

    public static McpPreferences Default() => new();
}
