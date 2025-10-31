using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class SettingsTool : IOrbitTool
{
	private readonly IServiceProvider serviceProvider;

	public SettingsTool(IServiceProvider serviceProvider)
	{
		this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public string Key => "Settings";

	public string DisplayName => "Settings";

	public PackIconMaterialKind Icon => PackIconMaterialKind.Cog;

    public FrameworkElement CreateView(object? context = null)
    {
        return serviceProvider.GetRequiredService<SettingsView>();
    }
}
