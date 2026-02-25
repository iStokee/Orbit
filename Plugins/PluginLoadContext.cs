using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Orbit.Plugins;

/// <summary>
/// Collectible AssemblyLoadContext for plugin hot-reload.
/// Allows plugin assemblies to be unloaded and reloaded without restarting Orbit.
/// </summary>
internal class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? string.Empty;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (ShouldUseDefaultContext(assemblyName.Name))
        {
            return null;
        }

        // Try to resolve the assembly path using the dependency resolver
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            // Load dependencies from memory so plugin DLL files are not held open.
            return LoadFromStream(new MemoryStream(File.ReadAllBytes(assemblyPath)));
        }

        var localAssemblyPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(localAssemblyPath))
        {
            return LoadFromStream(new MemoryStream(File.ReadAllBytes(localAssemblyPath)));
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }

    private static bool ShouldUseDefaultContext(string? assemblyName)
    {
        return string.Equals(assemblyName, "Orbit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(assemblyName, "csharp_interop", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(assemblyName, "netstandard", StringComparison.OrdinalIgnoreCase) ||
               (assemblyName?.StartsWith("System", StringComparison.OrdinalIgnoreCase) == true) ||
               (assemblyName?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true) ||
               (assemblyName?.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase) == true) ||
               (assemblyName?.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase) == true) ||
               (assemblyName?.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase) == true) ||
               (assemblyName?.StartsWith("MahApps", StringComparison.OrdinalIgnoreCase) == true);
    }
}
