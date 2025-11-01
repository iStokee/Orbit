using System;

namespace Orbit.Plugins;

/// <summary>
/// Metadata about a loaded plugin.
/// </summary>
public class PluginMetadata
{
    public required string PluginPath { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public required string Description { get; init; }
    public DateTime LoadedAt { get; init; }
    public bool IsLoaded { get; set; }

    /// <summary>
    /// File hash to detect changes for hot reload.
    /// </summary>
    public string? FileHash { get; set; }
}
