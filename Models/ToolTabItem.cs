using System.Windows;
using MahApps.Metro.IconPacks;

namespace Orbit.Models;

/// <summary>
/// Simple tab model for non-session (tool) tabs so they can live alongside sessions.
/// Matches SessionModel's surface for tab binding: Name + HostControl.
/// </summary>
public sealed class ToolTabItem
{
    public ToolTabItem(string key, string name, FrameworkElement hostControl, PackIconMaterialKind icon = PackIconMaterialKind.Tools)
    {
        Key = key;
        Name = name;
        HostControl = hostControl;
        Icon = icon;
    }

    /// <summary>
    /// Stable key to identify a single-instance tool tab (e.g., "ScriptControls", "Settings", "Console").
    /// </summary>
    public string Key { get; }

    public string Name { get; }

    public FrameworkElement HostControl { get; }

    /// <summary>
    /// Icon to display in the tab header.
    /// </summary>
    public PackIconMaterialKind Icon { get; }
}
