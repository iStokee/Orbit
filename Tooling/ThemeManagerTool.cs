using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class ThemeManagerTool : IOrbitTool
{
	private readonly IServiceProvider serviceProvider;

	public ThemeManagerTool(IServiceProvider serviceProvider)
	{
		this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public string Key => "ThemeManager";

	public string DisplayName => "Theme Manager";

	public PackIconMaterialKind Icon => PackIconMaterialKind.Palette;

    public FrameworkElement CreateView(object? context = null)
    {
        return serviceProvider.GetRequiredService<ThemeManagerPanel>();
    }
}
