using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.ViewModels;
using Orbit.Views;
using Application = System.Windows.Application;

namespace Orbit.Tooling.BuiltInTools;

/// <summary>
/// Unified dashboard for managing both built-in tools and dynamically loaded plugins.
/// Combines ToolsOverview and PluginManager functionality.
/// </summary>
public sealed class UnifiedToolsManagerTool : IOrbitTool
{
    private readonly IServiceProvider _serviceProvider;

    public UnifiedToolsManagerTool(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public string Key => "UnifiedToolsManager";

    public string DisplayName => "Tools & Plugins";

    public PackIconMaterialKind Icon => PackIconMaterialKind.ViewDashboard;

    public FrameworkElement CreateView(object? context = null)
    {
        var toolRegistry = _serviceProvider.GetRequiredService<IToolRegistry>();
        var pluginManager = _serviceProvider.GetRequiredService<Plugins.PluginManager>();
        var mainWindowViewModel = context as MainWindowViewModel
            ?? (Application.Current?.MainWindow as FrameworkElement)?.DataContext as MainWindowViewModel;

        var viewModel = new UnifiedToolsManagerViewModel(toolRegistry, pluginManager, mainWindowViewModel);
        var view = new UnifiedToolsManagerView
        {
            DataContext = viewModel
        };
        return view;
    }
}
