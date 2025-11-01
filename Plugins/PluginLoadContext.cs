using System;
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

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve the assembly path using the dependency resolver
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Don't load Orbit.dll or shared dependencies - use the host's version
        if (assemblyName.Name == "Orbit" ||
            assemblyName.Name == "csharp_interop" ||
            assemblyName.Name?.StartsWith("System") == true ||
            assemblyName.Name?.StartsWith("Microsoft") == true ||
            assemblyName.Name?.StartsWith("WindowsBase") == true ||
            assemblyName.Name?.StartsWith("PresentationCore") == true ||
            assemblyName.Name?.StartsWith("PresentationFramework") == true ||
            assemblyName.Name?.StartsWith("MahApps") == true)
        {
            return null; // Use host's version
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
}
