using MahApps.Metro.Controls;
using Orbit.ViewModels;

namespace Orbit.Views
{
	public partial class ThemeManagerView : MetroWindow
	{
		public ThemeManagerViewModel ViewModel { get; }

		public ThemeManagerView()
		{
			InitializeComponent();
			ViewModel = new ThemeManagerViewModel();
			DataContext = ViewModel;
		}
	}
}
