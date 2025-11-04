using Orbit.ViewModels;

namespace Orbit.Views;

/// <summary>
/// Interaction logic for GuideBrowserView.xaml
/// </summary>
public partial class GuideBrowserView : System.Windows.Controls.UserControl
{
	public GuideBrowserView()
	{
		InitializeComponent();
		DataContext ??= new GuideBrowserViewModel();
	}

	public GuideBrowserView(GuideBrowserViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
	}
}
