using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.ViewModels;
using Orbit.Views;
using Application = System.Windows.Application;

namespace Orbit.Tooling.BuiltInTools
{
	/// <summary>
	/// Built-in tool that provides a management surface for all registered Orbit tools
	/// </summary>
	public sealed class ToolsOverviewTool : IOrbitTool
	{
		private readonly IServiceProvider _serviceProvider;

		public ToolsOverviewTool(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;
		}

		public string Key => "ToolsOverview";

		public string DisplayName => "Tools Management";

		public PackIconMaterialKind Icon => PackIconMaterialKind.Tools;

		public FrameworkElement CreateView(object? context = null)
		{
			// Resolve IToolRegistry lazily to break circular dependency
			var toolRegistry = (IToolRegistry)_serviceProvider.GetService(typeof(IToolRegistry))!;
			var mainWindowViewModel = Application.Current?.MainWindow is FrameworkElement root
				? root.DataContext as MainWindowViewModel
				: null;

			var viewModel = new ToolsOverviewViewModel(toolRegistry, mainWindowViewModel);
			var view = new ToolsOverviewView
			{
				DataContext = viewModel
			};
			return view;
		}
	}
}
