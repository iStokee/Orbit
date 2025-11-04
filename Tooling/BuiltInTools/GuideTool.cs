using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling.BuiltInTools;

/// <summary>
/// Built-in tool that surfaces the Orbiters Guide documentation hub.
/// </summary>
public sealed class GuideTool : IOrbitTool
{
	public string Key => "ApiDocumentation";

	public string DisplayName => "Guide";

	public PackIconMaterialKind Icon => PackIconMaterialKind.BookOpenPageVariant;

	public FrameworkElement CreateView(object? context = null)
	{
		if (context is GuideBrowserViewModel viewModel)
		{
			return new GuideBrowserView(viewModel);
		}

		return new GuideBrowserView();
	}
}
