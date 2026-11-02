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
        Orbit.Services.ToolInstanceRegistry.Instance.Register(this);
    }

    /// <summary>
    /// Stable key to identify a single-instance tool tab (e.g., "ScriptControls", "Settings", "Console").
    /// </summary>
    public string Key { get; }

    public string Name { get; }

    public FrameworkElement HostControl { get; }

    public bool IsClosed { get; private set; }
    public void Close(object? ownerContext = null)
    {
        if (IsClosed) return;
        IsClosed = true;
        if (HostControl is Orbit.Services.IToolViewLifetime viewLifetime) viewLifetime.Dispose();
        else if (HostControl.DataContext is System.IDisposable disposable &&
                 HostControl.DataContext is not Orbit.ViewModels.SettingsViewModel &&
                 HostControl.DataContext is not Orbit.ViewModels.MainWindowViewModel &&
                 !ReferenceEquals(HostControl.DataContext, ownerContext)) disposable.Dispose();
    }

    /// <summary>
    /// Icon to display in the tab header.
    /// </summary>
    public PackIconMaterialKind Icon { get; }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Name) ? base.ToString() : Name;
}
