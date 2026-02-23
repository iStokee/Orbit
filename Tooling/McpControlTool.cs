using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class McpControlTool : IOrbitTool
{
    private readonly IServiceProvider _serviceProvider;

    public McpControlTool(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public string Key => "McpControl";

    public string DisplayName => "MCP Control";

    public PackIconMaterialKind Icon => PackIconMaterialKind.LanConnect;

    public FrameworkElement CreateView(object? context = null)
    {
        return _serviceProvider.GetRequiredService<McpControlCenterView>();
    }
}
