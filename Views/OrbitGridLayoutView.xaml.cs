using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Dragablz;
using Orbit.Models;
using Orbit.Services;
using Orbit.ViewModels;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
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
			OrbitInteractionLogger.Log("[OrbitView][View] Loaded.");
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

			SessionLayout.PreviewMouseLeftButtonDown += SessionLayout_PreviewMouseLeftButtonDown;
			SessionLayout.PreviewMouseLeftButtonUp += SessionLayout_PreviewMouseLeftButtonUp;
			SessionLayout.LostMouseCapture += SessionLayout_LostMouseCapture;
		}

		private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
		{
			OrbitInteractionLogger.Log("[OrbitView][View] Unloaded.");
			SessionLayout.PreviewMouseLeftButtonDown -= SessionLayout_PreviewMouseLeftButtonDown;
			SessionLayout.PreviewMouseLeftButtonUp -= SessionLayout_PreviewMouseLeftButtonUp;
			SessionLayout.LostMouseCapture -= SessionLayout_LostMouseCapture;

			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.SetDragOperationActive(false);
				viewModel.DetachLayoutControl();
			}
		}

		private void SessionLayout_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			OrbitInteractionLogger.Log("[OrbitView][Drag] Mouse down on layout.");
			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.SetDragOperationActive(true);
			}
		}

		private void SessionLayout_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			OrbitInteractionLogger.Log("[OrbitView][Drag] Mouse up on layout.");
			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.SetDragOperationActive(false);
			}
		}

		private void SessionLayout_LostMouseCapture(object sender, MouseEventArgs e)
		{
			OrbitInteractionLogger.Log("[OrbitView][Drag] Layout lost mouse capture.");
			if (DataContext is OrbitGridLayoutViewModel viewModel)
			{
				viewModel.SetDragOperationActive(false);
			}
		}

		private void OrbitTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			// Drag gestures can trigger transient selection swaps; skip expensive reactivation/focus
			// during active left-button interactions to keep drag smooth and avoid focus fights.
			if (Mouse.LeftButton == MouseButtonState.Pressed)
			{
				return;
			}

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

			OrbitInteractionLogger.Log($"[OrbitView][Selection] Selected session tab '{session.Name}'.");

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
