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
/// Uses collectible AssemblyLoadContexts for cooperative unloading of trusted plugins.
/// </summary>
public class PluginLoader
{
    private readonly Dictionary<string, LoadedPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private volatile bool _shuttingDown;
    internal void StopLoading() => _shuttingDown = true;
    internal Func<string, bool>? IsPluginInUse { get; set; }

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
            if (_shuttingDown) return PluginLoadResult.CreateFailure("Orbit is shutting down.");
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
            string fileHash = await CalculatePackageHashAsync(normalizedPath);
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
                if (OnUiThread(() => IsPluginInUse?.Invoke(existingPlugin.Metadata.Key) == true))
                    return PluginLoadResult.CreateFailure("Close this plugin's views before reloading it.");
                return await LoadStagedAsync(normalizedPath, fileHash, existingPlugin).ConfigureAwait(false);
            }

            // Load new plugin
            return await LoadStagedAsync(normalizedPath, fileHash, null).ConfigureAwait(false);
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
            if (!_shuttingDown && OnUiThread(() => IsPluginInUse?.Invoke(plugin.Metadata.Key) == true)) return false;
            try
            {
                OnUiThread(() => { plugin.Instance.OnUnload(); return true; });
            }
            catch (Exception ex)
            {
                // Continue unloading even if plugin cleanup throws.
                Console.WriteLine($"Plugin OnUnload threw exception: {ex.Message}");
            }

            var metadata = plugin.Metadata;

            // Remove from registry before unload/GC polling to drop strong references quickly.
            lock (_lock)
            {
                _loadedPlugins.Remove(normalizedPath);
            }

            // Release the registry reference before requesting collectible unload.
            // Existing views may still hold the plugin; GC cannot remove those references.
            metadata.IsLoaded = false;
            try
            {
                OnUiThread(() => { PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs(metadata, plugin.Instance)); return true; });
            }
            finally { plugin.Context.Unload(); plugin.Package.Dispose(); }

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
            var fileName = Path.GetFileName(dllFile);

            var manifestPath = Path.Combine(Path.GetDirectoryName(dllFile)!, "orbit.plugin.json");
            if (File.Exists(manifestPath))
            {
                if (IsManifestEntry(manifestPath, fileName)) yield return dllFile;
            }
            else if (PluginPackage.IsEntryAssembly(dllFile)) yield return dllFile;
        }
    }

    private static bool IsManifestEntry(string path, string fileName)
    {
        try
        {
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return string.Equals(manifest.RootElement.GetProperty("entryAssembly").GetString(), fileName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            Console.WriteLine($"Cannot discover plugin manifest '{path}': {ex.Message}");
            return false;
        }
    }

    private async Task<PluginLoadResult> LoadStagedAsync(string path, string hash, LoadedPlugin? previous)
    {
        var package = await Task.Run(() => PluginPackage.Stage(path)).ConfigureAwait(false);
        try
        {
            if (await CalculatePackageHashAsync(Path.Combine(package.Folder, package.EntryPath)).ConfigureAwait(false) != hash)
                throw new IOException("Plugin package changed while staging; retry the load.");
            return OnUiThread(() => LoadNewPlugin(path, hash, package, previous));
        }
        catch { package.Dispose(); throw; }
    }

    private PluginLoadResult LoadNewPlugin(string path, string hash, PluginPackage package, LoadedPlugin? previous)
    {
        if (_shuttingDown) { package.Dispose(); return PluginLoadResult.CreateFailure("Orbit is shutting down."); }
        var context = new PluginLoadContext(Path.Combine(package.Folder, package.EntryPath));
        IOrbitPlugin? instance = null;
        PluginMetadata? metadata = null;
        var initialized = false;
        var committed = false;
        var previousStopped = false;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(Path.Combine(package.Folder, package.EntryPath)));
            var assembly = context.LoadFromStream(stream);
            var type = assembly.GetTypes().FirstOrDefault(t => typeof(IOrbitPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                ?? throw new InvalidOperationException("No IOrbitPlugin implementation found in assembly.");
            instance = (IOrbitPlugin?)Activator.CreateInstance(type) ?? throw new InvalidOperationException("Plugin construction returned null.");
            if (string.IsNullOrWhiteSpace(instance.Key)) throw new InvalidOperationException("Plugin key is required.");
            lock (_lock)
                if (_loadedPlugins.Values.Any(p => !ReferenceEquals(p, previous) && p.Metadata.Key == instance.Key))
                    throw new InvalidOperationException($"Plugin key '{instance.Key}' is already loaded.");
            metadata = new PluginMetadata
            {
                PluginPath = path, Key = instance.Key, DisplayName = instance.DisplayName,
                Version = instance.Version, Author = instance.Author, Description = instance.Description,
                LoadedAt = DateTime.Now, IsLoaded = true, FileHash = hash
            };
            // Validate and construct the replacement before stopping the working version.
            // Keep its context and immutable package until replacement initialization commits.
            if (previous != null)
            {
                previousStopped = true;
                previous.Instance.OnUnload();
                lock (_lock) _loadedPlugins.Remove(path);
                previous.Metadata.IsLoaded = false;
                PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs(previous.Metadata, previous.Instance));
            }
            initialized = true;
            instance.OnLoad();
            PluginLoaded?.Invoke(this, new PluginLoadedEventArgs(metadata, instance));
            lock (_lock) _loadedPlugins.Add(path, new LoadedPlugin { Context = context, Instance = instance, Metadata = metadata, Package = package });
            committed = true;
            if (previous != null) { previous.Context.Unload(); previous.Package.Dispose(); }
            return PluginLoadResult.CreateSuccess(metadata, previous != null);
        }
        finally
        {
            if (!committed)
            {
                if (metadata != null && instance != null)
                {
                    metadata.IsLoaded = false;
                    try { PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs(metadata, instance)); } catch { }
                }
                if (initialized) try { instance!.OnUnload(); } catch (Exception error) { Console.WriteLine($"Candidate plugin cleanup failed: {error.Message}"); }
                context.Unload(); package.Dispose();
                if (previousStopped && previous != null)
                {
                    try
                    {
                        previous.Instance.OnLoad();
                        previous.Metadata.IsLoaded = true;
                        PluginLoaded?.Invoke(this, new PluginLoadedEventArgs(previous.Metadata, previous.Instance));
                        lock (_lock) _loadedPlugins[path] = previous;
                    }
                    catch (Exception error)
                    {
                        previous.Metadata.IsLoaded = false;
                        lock (_lock) _loadedPlugins.Remove(path);
                        throw new InvalidOperationException("Plugin replacement failed and the previous plugin could not be restarted.", error);
                    }
                }
            }
        }
    }
    private static T OnUiThread<T>(Func<T> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher == null || dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }

    private static string NormalizePluginPath(string pluginPath)
    {
        return Path.GetFullPath(pluginPath.Trim());
    }

    internal static Task<string> CalculatePackageHashAsync(string pluginPath) => PluginPackage.HashAsync(pluginPath);
    public async Task ShutdownAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        string[] paths;
        try
        {
            _shuttingDown = true;
            lock (_lock) paths = _loadedPlugins.Keys.ToArray();
        }
        finally { _operationGate.Release(); }
        foreach (var path in paths) await UnloadPluginAsync(path).ConfigureAwait(false);
    }

    private class LoadedPlugin
    {
        public required PluginLoadContext Context { get; init; }
        public required PluginPackage Package { get; init; }
        public required IOrbitPlugin Instance { get; init; }
        public required PluginMetadata Metadata { get; init; }
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

        public IOrbitPlugin? Instance { get; }

        public PluginUnloadedEventArgs(PluginMetadata metadata, IOrbitPlugin? instance = null)
        {
            Metadata = metadata;
            Instance = instance;
        }
    }
}
