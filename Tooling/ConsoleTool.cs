using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class ConsoleTool : IOrbitTool
{
	private readonly IServiceProvider serviceProvider;

	public ConsoleTool(IServiceProvider serviceProvider)
	{
		this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public string Key => "Console";

	public string DisplayName => "Console";

	public PackIconMaterialKind Icon => PackIconMaterialKind.Console;

    public FrameworkElement CreateView(object? context = null)
    {
        return serviceProvider.GetRequiredService<ConsoleView>();
    }
}
