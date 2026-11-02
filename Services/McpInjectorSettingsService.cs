using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orbit.Services;

public sealed class McpInjectorSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string? _path;
    public McpInjectorSettingsService() { }
    internal McpInjectorSettingsService(string path) => _path = path;

    public McpInjectorSettings Load()
    {
        var path = _path ?? ResolveSettingsPath();
        if (!File.Exists(path))
        {
            return new McpInjectorSettings(path, true, false);
        }

        try
        {
            var json = File.ReadAllText(path);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node == null)
            {
                throw new InvalidDataException("Injector settings must contain a JSON object.");
            }

            var autoStart = node["MCP_AUTOSTART"]?.GetValue<bool>() ?? true;
            return new McpInjectorSettings(path, autoStart, true);
        }
        catch (Exception ex)
        {
            return new McpInjectorSettings(path, true, true, $"Cannot read injector settings; original file preserved: {ex.Message}");
        }
    }

    public void Save(McpInjectorSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var path = settings.SettingsPath;
        JsonObject root;

        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException("Injector settings must contain a JSON object. Original file preserved.");
        }
        else
        {
            root = new JsonObject();
        }

        root["MCP_AUTOSTART"] = settings.AutoStart;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        AtomicFile.WriteText(path, root.ToJsonString(JsonOptions));
    }

    private static string ResolveSettingsPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = Environment.CurrentDirectory;
        var profileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userSettingsPath = string.IsNullOrWhiteSpace(profileDir)
            ? string.Empty
            : Path.Combine(profileDir, "MemoryError", "MMISettings.json");
        var candidates = new[]
        {
            userSettingsPath,
            Path.Combine(baseDir, "MMISettings.json"),
            Path.Combine(currentDir, "MMISettings.json"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json")),
            Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json")),
            Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json"))
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(userSettingsPath) ? candidates[1] : userSettingsPath;
    }
}

public sealed record McpInjectorSettings(string SettingsPath, bool AutoStart, bool Exists, string? Error = null);
