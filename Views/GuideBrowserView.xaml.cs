using System;
using Microsoft.Extensions.DependencyInjection;
using Orbit.ViewModels;
using Application = System.Windows.Application;

namespace Orbit.Views;

/// <summary>
/// Interaction logic for GuideBrowserView.xaml
/// </summary>
public partial class GuideBrowserView : System.Windows.Controls.UserControl
{
	public GuideBrowserView()
	{
		InitializeComponent();
		DataContext = ResolveViewModel();
	}

	public GuideBrowserView(GuideBrowserViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
	}

	private static GuideBrowserViewModel ResolveViewModel()
	{
		var app = Application.Current as App;
		var viewModel = app?.Services.GetService<GuideBrowserViewModel>();
		if (viewModel == null)
		{
			throw new InvalidOperationException("GuideBrowserView requires GuideBrowserViewModel from DI.");
		}

		return viewModel;
	}
}
