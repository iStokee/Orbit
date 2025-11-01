using System;
using Orbit.Tooling;

namespace Orbit.Plugins;

/// <summary>
/// Represents a dynamically loadable Orbit plugin.
/// Plugins are loaded from external DLLs at runtime and can be hot-reloaded.
/// </summary>
public interface IOrbitPlugin : IOrbitTool
{
    /// <summary>
    /// Plugin metadata version. Used for compatibility checking.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Plugin author information.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Plugin description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Called when the plugin is loaded into Orbit.
    /// Use this for initialization that doesn't depend on UI.
    /// </summary>
    void OnLoad();

    /// <summary>
    /// Called when the plugin is unloaded from Orbit.
    /// Use this for cleanup (dispose resources, save state, etc.).
    /// </summary>
    void OnUnload();
}
