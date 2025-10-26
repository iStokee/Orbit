using MahApps.Metro.Controls;
using Orbit.ViewModels;

namespace Orbit.Views
{
	public partial class SessionsView : MetroWindow
	{
		public SessionsOverviewViewModel ViewModel { get; }

		public SessionsView(SessionsOverviewViewModel viewModel)
		{
			InitializeComponent();
			ViewModel = viewModel;
			DataContext = ViewModel;
		}

		private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
		{
			Close();
		}
	}
}
