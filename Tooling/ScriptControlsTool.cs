using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class ScriptControlsTool : IOrbitTool
{
	private readonly IServiceProvider serviceProvider;

	public ScriptControlsTool(IServiceProvider serviceProvider)
	{
		this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public string Key => "ScriptControls";

	public string DisplayName => "Script Controls";

	public PackIconMaterialKind Icon => PackIconMaterialKind.ScriptText;

    public FrameworkElement CreateView(object? context = null)
    {
        return serviceProvider.GetRequiredService<ScriptControlsView>();
    }
}
