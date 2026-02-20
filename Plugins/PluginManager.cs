using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Orbit.Tooling;
using static Orbit.Plugins.PluginLoader;

namespace Orbit.Plugins;

/// <summary>
/// High-level plugin management service that integrates with Orbit's tool system.
/// Handles plugin discovery, loading, unloading, and registration with the ToolRegistry.
/// </summary>
public class PluginManager
{
    private readonly PluginLoader _loader;
    private readonly IToolRegistry _toolRegistry;
    private readonly string _pluginDirectory;

    public event EventHandler<PluginStatusChangedEventArgs>? PluginStatusChanged;

    public IReadOnlyList<PluginMetadata> LoadedPlugins => _loader.LoadedPlugins;

    public PluginManager(IToolRegistry toolRegistry, string pluginDirectory)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _pluginDirectory = pluginDirectory ?? throw new ArgumentNullException(nameof(pluginDirectory));

        _loader = new PluginLoader();

        // Subscribe to loader events
        _loader.PluginLoaded += OnPluginLoaded;
        _loader.PluginUnloaded += OnPluginUnloaded;

        // Ensure plugin directory exists
        if (!Directory.Exists(_pluginDirectory))
        {
            Directory.CreateDirectory(_pluginDirectory);
        }
    }

    /// <summary>
    /// Discovers all plugins in the plugin directory.
    /// </summary>
    public IEnumerable<string> DiscoverPlugins()
    {
        return _loader.DiscoverPlugins(_pluginDirectory);
    }

    /// <summary>
    /// Loads a plugin from the specified path.
    /// If already loaded, performs a hot reload.
    /// </summary>
    public async Task<PluginLoadResult> LoadPluginAsync(string pluginPath)
    {
        var result = await _loader.LoadPluginAsync(pluginPath);

        if (result.Success && result.Metadata != null)
        {
            var statusMessage = result.WasReloaded
                ? $"Plugin '{result.Metadata.DisplayName}' hot-reloaded successfully"
                : $"Plugin '{result.Metadata.DisplayName}' loaded successfully";

            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs(
                result.Metadata,
                PluginStatus.Loaded,
                statusMessage
            ));
        }
        else
        {
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs(
                result.Metadata,
                PluginStatus.Error,
                $"Failed to load plugin: {result.ErrorMessage ?? "Unknown error"}"));
        }

        return result;
    }

    /// <summary>
    /// Unloads a plugin by its path.
    /// </summary>
    public async Task<bool> UnloadPluginAsync(string pluginPath)
    {
        var metadata = _loader.LoadedPlugins.FirstOrDefault(p =>
            string.Equals(p.PluginPath, pluginPath, StringComparison.OrdinalIgnoreCase));

        var success = await _loader.UnloadPluginAsync(pluginPath);

        if (success)
        {
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs(
                metadata,
                PluginStatus.Unloaded,
                metadata != null
                    ? $"Plugin '{metadata.DisplayName}' unloaded successfully"
                    : "Plugin unloaded successfully"
            ));
        }
        else if (metadata != null)
        {
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs(
                metadata,
                PluginStatus.Error,
                $"Failed to unload plugin '{metadata.DisplayName}'."));
        }

        return success;
    }

    /// <summary>
    /// Auto-loads all plugins in the plugin directory.
    /// Returns count of successfully loaded plugins.
    /// </summary>
    public async Task<int> AutoLoadPluginsAsync()
    {
        var discoveredPlugins = DiscoverPlugins().ToList();
        int successCount = 0;

        foreach (var pluginPath in discoveredPlugins)
        {
            try
            {
                var result = await LoadPluginAsync(pluginPath);
                if (result.Success)
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to auto-load plugin {pluginPath}: {ex.Message}");
            }
        }

        return successCount;
    }

    /// <summary>
    /// Gets the default plugin directory path.
    /// </summary>
    public static string GetDefaultPluginDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "MemoryError", "Orbit_Plugins");
    }

    private void OnPluginLoaded(object? sender, PluginLoadedEventArgs e)
    {
        // Register plugin with tool registry
        _toolRegistry.RegisterPluginTool(e.Instance);
    }

    private void OnPluginUnloaded(object? sender, PluginUnloadedEventArgs e)
    {
        // Unregister plugin from tool registry
        _toolRegistry.UnregisterPluginTool(e.Metadata.Key);
    }
}

public enum PluginStatus
{
    Loaded,
    Unloaded,
    Error
}

public class PluginStatusChangedEventArgs : EventArgs
{
    public PluginMetadata? Metadata { get; }
    public PluginStatus Status { get; }
    public string Message { get; }

    public PluginStatusChangedEventArgs(PluginMetadata? metadata, PluginStatus status, string message)
    {
        Metadata = metadata;
        Status = status;
        Message = message;
    }
}
