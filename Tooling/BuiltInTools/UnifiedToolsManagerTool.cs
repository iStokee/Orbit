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

        var view = new UnifiedToolsManagerView();
        var originalOwner = mainWindowViewModel == null ? null : new WeakReference<MainWindowViewModel>(mainWindowViewModel);
        view.DataContext = new UnifiedToolsManagerViewModel(toolRegistry, pluginManager, mainWindowViewModel,
            () => Window.GetWindow(view)?.DataContext as MainWindowViewModel
                ?? (originalOwner?.TryGetTarget(out var owner) == true ? owner : null));
        return view;
    }
}
