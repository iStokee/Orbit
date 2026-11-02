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
	private void GuideBody_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
	{
		var compact = e.NewSize.Width < 800;
		GuideBody.ColumnDefinitions[0].Width = new System.Windows.GridLength(compact ? 0 : 280);
		GuideBody.ColumnDefinitions[1].Width = new System.Windows.GridLength(compact ? 0 : 16);
		GuideNavigation.Visibility = compact ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
		CompactGuideSelector.Visibility = compact ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
	}
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
		var viewModel = app?.Services.GetRequiredService<Orbit.Services.OwnedViewFactory>().CreateModel<GuideBrowserViewModel>();
		if (viewModel == null)
		{
			throw new InvalidOperationException("GuideBrowserView requires GuideBrowserViewModel from DI.");
		}

		return viewModel;
	}
}
