using System.Windows.Controls;
using System.Windows.Threading;
using Dragablz;
using Orbit.Models;
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
		private bool _defaultDensityApplied;

		public OrbitGridLayoutView()
		{
			InitializeComponent();

			// After layout is loaded, wire up the Layout reference to ViewModel
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
		}

		private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
		{
			// Give the ViewModel access to the Layout control for programmatic branching
			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.SetLayoutControl(SessionLayout);
				if (!_defaultDensityApplied)
				{
					viewModel.ApplyDefaultGridDensity();
					_defaultDensityApplied = true;
				}
			}
		}

		private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
		{
			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.DetachLayoutControl();
			}
		}

		private void OrbitTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!ReferenceEquals(sender, e.OriginalSource))
			{
				return;
			}

			if (sender is not TabablzControl tabControl)
			{
				return;
			}

			if (tabControl.SelectedItem is not SessionModel session || session.HostControl == null)
			{
				return;
			}

			var host = session.HostControl;
			host.EnsureActiveAfterLayout();

			var hostWindow = System.Windows.Window.GetWindow(this);
			if (hostWindow?.IsActive == true)
			{
				_ = Dispatcher.BeginInvoke(new System.Action(() =>
				{
					try
					{
						host.FocusEmbeddedClient();
					}
					catch
					{
						// Best effort; focus can fail during drag/drop transitions.
					}
				}), DispatcherPriority.Input);
			}
		}
	}
}
