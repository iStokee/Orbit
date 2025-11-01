using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views
{
	/// <summary>
	/// Interaction logic for SessionGridView.xaml
	/// </summary>
	public partial class SessionGridView : UserControl
	{
		public SessionGridView(SessionGridViewModel viewModel)
		{
			InitializeComponent();
			DataContext = viewModel;
		}
	}
}
