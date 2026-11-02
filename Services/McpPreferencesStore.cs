using System;
using System.IO;
using System.Text.Json;

namespace Orbit.Services;

public sealed class McpPreferencesStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly System.Collections.Generic.Dictionary<string, McpPreferences> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.Dictionary<string, string> LoadErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private readonly string? _legacyPath;

    public McpPreferencesStore()
    {
        _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryError", "Orbit", "orbit.mcp.settings.json");
        _legacyPath = Path.Combine(AppContext.BaseDirectory, "orbit.mcp.settings.json");
    }
    internal McpPreferencesStore(string path) { _path = Path.GetFullPath(path); }
    public string? LastError { get { lock (Sync) return LoadErrors.GetValueOrDefault(_path); } }

    public McpPreferences Load()
    {
        lock (Sync)
        {
            if (!LoadErrors.ContainsKey(_path) && Cache.TryGetValue(_path, out var cached)) return Copy(cached);
            var source = File.Exists(_path) ? _path : _legacyPath;
            var preferences = McpPreferences.Default();
            if (source != null && File.Exists(source))
            {
                try
                {
                    preferences = JsonSerializer.Deserialize<McpPreferences>(File.ReadAllText(source), JsonOptions)
                        ?? throw new InvalidDataException("MCP preferences must contain an object.");
                    LoadErrors.Remove(_path);
                }
                catch (Exception ex)
                {
                    LoadErrors[_path] = $"Cannot load MCP preferences; original file preserved: {ex.Message}";
                    return preferences;
                }
            }
            else LoadErrors.Remove(_path);
            Cache[_path] = Copy(preferences);
            return preferences;
        }
    }

    public void Save(McpPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (Sync)
        {
            if (LoadErrors.TryGetValue(_path, out var error)) throw new InvalidOperationException(error);
            // Recheck an existing file before replacement, even if another view cached a valid load.
            if (File.Exists(_path))
            {
                try
                {
                    _ = JsonSerializer.Deserialize<McpPreferences>(File.ReadAllText(_path), JsonOptions)
                        ?? throw new InvalidDataException("MCP preferences must contain an object.");
                }
                catch (Exception ex)
                {
                    LoadErrors[_path] = $"Cannot read MCP preferences before saving; original file preserved: {ex.Message}";
                    throw new InvalidOperationException(LoadErrors[_path], ex);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, JsonOptions));
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
                Cache[_path] = Copy(preferences);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
    private static McpPreferences Copy(McpPreferences value) => new()
    {
        AutoStartRuntimeOnInject = value.AutoStartRuntimeOnInject,
        AutoProbeEnabled = value.AutoProbeEnabled,
        AutoProbeSeconds = value.AutoProbeSeconds
    };
}
public sealed class McpPreferences
{
    public bool AutoStartRuntimeOnInject { get; set; } = true;
    public bool AutoProbeEnabled { get; set; }
    public int AutoProbeSeconds { get; set; } = 3;

    public static McpPreferences Default() => new();
}
