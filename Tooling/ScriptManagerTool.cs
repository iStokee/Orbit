using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class ScriptManagerTool : IOrbitTool
{
    private readonly IServiceProvider serviceProvider;

    public ScriptManagerTool(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public string Key => "ScriptManager";

    public string DisplayName => "Script Manager";

    public PackIconMaterialKind Icon => PackIconMaterialKind.FileCodeOutline;

    public FrameworkElement CreateView(object? context = null)
    {
        return serviceProvider.GetRequiredService<ScriptManagerPanel>();
    }
}
