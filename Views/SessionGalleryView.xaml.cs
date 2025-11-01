using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views
{
	/// <summary>
	/// Interaction logic for SessionGalleryView.xaml
	/// </summary>
	public partial class SessionGalleryView : UserControl
	{
		public SessionGalleryView(SessionGalleryViewModel viewModel)
		{
			InitializeComponent();
			DataContext = viewModel;
		}
	}
}
