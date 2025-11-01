using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views
{
	/// <summary>
	/// Orbit View - Live session grid with unified drag-and-drop
	/// Uses Dragablz Layout control for native branching/splitting
	/// NEW IMPLEMENTATION - Replaces old GridLayoutBuilder approach
	/// </summary>
	public partial class OrbitGridLayoutView : UserControl
	{
		public OrbitGridLayoutView()
		{
			InitializeComponent();

			// After layout is loaded, wire up the Layout reference to ViewModel
			Loaded += OnLoaded;
		}

		private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
		{
			// Give the ViewModel access to the Layout control for programmatic branching
			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.SetLayoutControl(SessionLayout);
			}
		}
	}
}
