using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling.BuiltInTools;

/// <summary>
/// Built-in tool surface for the FSM node editor.
/// </summary>
public sealed class FsmNodeEditorTool : IOrbitTool
{
	private readonly IServiceProvider _serviceProvider;

	public FsmNodeEditorTool(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public string Key => "FsmNodeEditor";

	public string DisplayName => "FSM Node Editor";

	public PackIconMaterialKind Icon => PackIconMaterialKind.GraphOutline;

	public FrameworkElement CreateView(object? context = null)
	{
		var viewModel = (FsmNodeEditorViewModel)_serviceProvider.GetService(typeof(FsmNodeEditorViewModel))!;
		var view = new FsmNodeEditorView
		{
			DataContext = viewModel
		};

		return view;
	}
}
