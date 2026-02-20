using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Tooling;

namespace Orbit.Plugins;

/// <summary>
/// Manages loading, unloading, and hot-reloading of Orbit plugins.
/// Uses collectible AssemblyLoadContexts to enable memory-safe unloading.
/// </summary>
public class PluginLoader
{
    private readonly Dictionary<string, LoadedPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    /// <summary>
    /// Event raised when a plugin is loaded.
    /// </summary>
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <summary>
    /// Event raised when a plugin is unloaded.
    /// </summary>
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <summary>
    /// Gets metadata for all loaded plugins.
    /// </summary>
    public IReadOnlyList<PluginMetadata> LoadedPlugins
    {
        get
        {
            lock (_lock)
            {
                return _loadedPlugins.Values.Select(p => p.Metadata).ToList();
            }
        }
    }

    /// <summary>
    /// Loads a plugin from the specified DLL path.
    /// If the plugin is already loaded, it will be hot-reloaded.
    /// </summary>
    public async Task<PluginLoadResult> LoadPluginAsync(string pluginPath)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
            {
                return PluginLoadResult.CreateFailure("Plugin path is required.");
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizePluginPath(pluginPath);
            }
            catch (Exception ex)
            {
                return PluginLoadResult.CreateFailure($"Invalid plugin path: {ex.Message}");
            }

            if (!File.Exists(normalizedPath))
            {
                return PluginLoadResult.CreateFailure($"Plugin file not found: {normalizedPath}");
            }

            // Calculate file hash for change detection
            string fileHash = await CalculateFileHashAsync(normalizedPath);
            LoadedPlugin? existingPlugin;
            lock (_lock)
            {
                _loadedPlugins.TryGetValue(normalizedPath, out existingPlugin);
            }

            // Check if plugin is already loaded
            if (existingPlugin != null)
            {
                // If file hasn't changed, just return success
                if (existingPlugin.Metadata.FileHash == fileHash)
                {
                    return PluginLoadResult.CreateSuccess(existingPlugin.Metadata, wasReloaded: false);
                }

                // File changed - hot reload
                return HotReloadPlugin(normalizedPath, fileHash);
            }

            // Load new plugin
            return LoadNewPlugin(normalizedPath, fileHash);
        }
        catch (Exception ex)
        {
            return PluginLoadResult.CreateFailure($"Failed to load plugin: {ex.Message}");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Unloads a plugin by its file path.
    /// </summary>
    public async Task<bool> UnloadPluginAsync(string pluginPath)
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
            {
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizePluginPath(pluginPath);
            }
            catch
            {
                return false;
            }
            LoadedPlugin? plugin;

            lock (_lock)
            {
                if (!_loadedPlugins.TryGetValue(normalizedPath, out plugin))
                {
                    return false; // Not loaded
                }
            }

            // Call plugin's OnUnload
            try
            {
                plugin.Instance.OnUnload();
            }
            catch (Exception ex)
            {
                // Continue unloading even if plugin cleanup throws.
                Console.WriteLine($"Plugin OnUnload threw exception: {ex.Message}");
            }

            var contextWeakRef = plugin.WeakRef;
            var metadata = plugin.Metadata;

            // Remove from registry before unload/GC polling to drop strong references quickly.
            lock (_lock)
            {
                _loadedPlugins.Remove(normalizedPath);
            }

            // Unload the context
            plugin.Context.Unload();
            plugin = null;

            // Wait briefly for collectible context to unload
            for (int i = 0; i < 10 && contextWeakRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(100);
            }
            metadata.IsLoaded = false;

            PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs(metadata));

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error unloading plugin {pluginPath}: {ex.Message}");
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Gets a loaded plugin instance by its key.
    /// </summary>
    public IOrbitPlugin? GetPlugin(string key)
    {
        lock (_lock)
        {
            return _loadedPlugins.Values
                .FirstOrDefault(p => p.Metadata.Key == key)?.Instance;
        }
    }

    /// <summary>
    /// Discovers all plugin DLLs in the specified directory.
    /// </summary>
    public IEnumerable<string> DiscoverPlugins(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
        {
            yield break;
        }

        foreach (var dllFile in Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories))
        {
            // Skip known system/framework DLLs
            var fileName = Path.GetFileName(dllFile);
            if (fileName.StartsWith("System.") ||
                fileName.StartsWith("Microsoft.") ||
                fileName.StartsWith("netstandard") ||
                fileName == "Orbit.dll" ||
                fileName == "csharp_interop.dll")
            {
                continue;
            }

            yield return dllFile;
        }
    }

    private PluginLoadResult LoadNewPlugin(string pluginPath, string fileHash)
    {
        // Load plugin assembly in memory to avoid file locking
        byte[] assemblyBytes = File.ReadAllBytes(pluginPath);

        // Check for .pdb for debugging support
        string pdbPath = Path.ChangeExtension(pluginPath, ".pdb");
        byte[]? pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;

        // Create collectible load context
        var context = new PluginLoadContext(pluginPath);

        // Load assembly from memory
        using var assemblyStream = new MemoryStream(assemblyBytes);
        using var pdbStream = pdbBytes != null ? new MemoryStream(pdbBytes) : null;

        Assembly assembly = pdbStream != null
            ? context.LoadFromStream(assemblyStream, pdbStream)
            : context.LoadFromStream(assemblyStream);

        // Find IOrbitPlugin implementations
        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IOrbitPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (pluginType == null)
        {
            context.Unload();
            return PluginLoadResult.CreateFailure("No IOrbitPlugin implementation found in assembly");
        }

        // Create plugin instance
        var pluginInstance = (IOrbitPlugin?)Activator.CreateInstance(pluginType);
        if (pluginInstance == null)
        {
            context.Unload();
            return PluginLoadResult.CreateFailure("Failed to create plugin instance");
        }

        // Create metadata
        var metadata = new PluginMetadata
        {
            PluginPath = pluginPath,
            Key = pluginInstance.Key,
            DisplayName = pluginInstance.DisplayName,
            Version = pluginInstance.Version,
            Author = pluginInstance.Author,
            Description = pluginInstance.Description,
            LoadedAt = DateTime.Now,
            IsLoaded = true,
            FileHash = fileHash
        };

        // Store loaded plugin
        var loadedPlugin = new LoadedPlugin
        {
            Context = context,
            Instance = pluginInstance,
            Metadata = metadata,
            WeakRef = new WeakReference(context)
        };

        // Call OnLoad
        try
        {
            pluginInstance.OnLoad();
        }
        catch (Exception ex)
        {
            try
            {
                context.Unload();
            }
            catch
            {
                // best effort cleanup after failed init
            }

            return PluginLoadResult.CreateFailure($"Plugin OnLoad failed: {ex.Message}");
        }

        _loadedPlugins[pluginPath] = loadedPlugin;
        PluginLoaded?.Invoke(this, new PluginLoadedEventArgs(metadata, pluginInstance));

        return PluginLoadResult.CreateSuccess(metadata, wasReloaded: false);
    }

    private PluginLoadResult HotReloadPlugin(string pluginPath, string newFileHash)
    {
        // Unload old version
        var oldPlugin = _loadedPlugins[pluginPath];
        var oldMetadata = oldPlugin.Metadata;

        try
        {
            oldPlugin.Instance.OnUnload();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Plugin OnUnload threw exception: {ex.Message}");
        }

        oldPlugin.Context.Unload();
        _loadedPlugins.Remove(pluginPath);
        oldMetadata.IsLoaded = false;
        PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs(oldMetadata));

        // Trigger GC
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Load new version
        var result = LoadNewPlugin(pluginPath, newFileHash);
        if (result.Success)
        {
            result = result with { WasReloaded = true };
        }

        return result;
    }

    private static string NormalizePluginPath(string pluginPath)
    {
        return Path.GetFullPath(pluginPath.Trim());
    }

    private async Task<string> CalculateFileHashAsync(string filePath)
    {
        using var md5 = MD5.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await md5.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private class LoadedPlugin
    {
        public required PluginLoadContext Context { get; init; }
        public required IOrbitPlugin Instance { get; init; }
        public required PluginMetadata Metadata { get; init; }
        public required WeakReference WeakRef { get; init; }
    }


    /// <summary>
    /// Result of a plugin load operation.
    /// </summary>
    public record PluginLoadResult(bool Success, string? ErrorMessage, PluginMetadata? Metadata, bool WasReloaded)
    {
        public static PluginLoadResult CreateSuccess(PluginMetadata metadata, bool wasReloaded) =>
            new(true, null, metadata, wasReloaded);

        public static PluginLoadResult CreateFailure(string errorMessage) =>
            new(false, errorMessage, null, false);
    }

    /// <summary>
    /// Event args for plugin loaded event.
    /// </summary>
    public class PluginLoadedEventArgs : EventArgs
    {
        public PluginMetadata Metadata { get; }
        public IOrbitPlugin Instance { get; }

        public PluginLoadedEventArgs(PluginMetadata metadata, IOrbitPlugin instance)
        {
            Metadata = metadata;
            Instance = instance;
        }
    }

    /// <summary>
    /// Event args for plugin unloaded event.
    /// </summary>
    public class PluginUnloadedEventArgs : EventArgs
    {
        public PluginMetadata Metadata { get; }

        public PluginUnloadedEventArgs(PluginMetadata metadata)
        {
            Metadata = metadata;
        }
    }
}
