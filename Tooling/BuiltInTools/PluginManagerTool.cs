using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling.BuiltInTools;

/// <summary>
/// Built-in tool for managing dynamically loaded plugins.
/// Allows users to discover, load, unload, and hot-reload plugins at runtime.
/// </summary>
public sealed class PluginManagerTool : IOrbitTool
{
    private readonly IServiceProvider _serviceProvider;

    public PluginManagerTool(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Key => "PluginManager";

    public string DisplayName => "Plugin Manager";

    public PackIconMaterialKind Icon => PackIconMaterialKind.Puzzle;

    public FrameworkElement CreateView(object? context = null)
    {
        var pluginManager = (Plugins.PluginManager)_serviceProvider.GetService(typeof(Plugins.PluginManager))!;
        var viewModel = new PluginManagerViewModel(pluginManager);
        var view = new PluginManagerView
        {
            DataContext = viewModel
        };
        return view;
    }
}
