using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orbit.Services;

public sealed class McpInjectorSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public McpInjectorSettings Load()
    {
        var path = ResolveSettingsPath();
        if (!File.Exists(path))
        {
            return new McpInjectorSettings(path, true, string.Empty, false);
        }

        try
        {
            var json = File.ReadAllText(path);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node == null)
            {
                return new McpInjectorSettings(path, true, string.Empty, true);
            }

            var autoStart = node["MCP_AUTOSTART"]?.GetValue<bool>() ?? true;
            var serverPath = node["MCP_SERVER_PATH"]?.GetValue<string>() ?? string.Empty;
            return new McpInjectorSettings(path, autoStart, serverPath, true);
        }
        catch
        {
            return new McpInjectorSettings(path, true, string.Empty, true);
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
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        root["MCP_AUTOSTART"] = settings.AutoStart;
        root["MCP_SERVER_PATH"] = settings.ServerPath ?? string.Empty;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, root.ToJsonString(JsonOptions));
    }

    private static string ResolveSettingsPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = Environment.CurrentDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "MMISettings.json"),
            Path.Combine(currentDir, "MMISettings.json"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json")),
            Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json")),
            Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "..", "ME", "x64", "Build_DLL", "MMISettings.json"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}

public sealed record McpInjectorSettings(string SettingsPath, bool AutoStart, string ServerPath, bool Exists);
