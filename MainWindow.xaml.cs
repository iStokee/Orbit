using Dragablz;
using MahApps.Metro.Controls;
using Orbit.Models;
using Orbit.ViewModels;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;


namespace Orbit
{
	public partial class MainWindow : MetroWindow
	{
		private DispatcherTimer resizeTimer;
		ViewModels.MainWindowViewModel viewModel;
		private readonly DispatcherTimer floatingMenuInactivityTimer;
		private readonly DispatcherTimer floatingMenuWakeTimer;
		private bool floatingMenuDragCandidate;
		private bool floatingMenuDragging;
		private Point floatingMenuDragStart;
		private Point floatingMenuOrigin;
		private bool hasCursorBaseline;
		private POINT lastCursorPoint;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		[DllImport("user32.dll")]
		private static extern bool GetCursorPos(out POINT lpPoint);

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		public MainWindow()
		{
			InitializeComponent();

			viewModel = new MainWindowViewModel();
			this.DataContext = viewModel;
			viewModel.PropertyChanged += ViewModel_PropertyChanged;
			this.SessionTabControl.InterTabController = new InterTabController();
			this.SessionTabControl.ClosingItemCallback += viewModel.TabControl_ClosingItemHandler;

			//// Forward SizeChanged event to the ViewModel
			this.SizeChanged += MetroWindow_SizeChanged;
			this.viewModel.Sessions.CollectionChanged += (s,e) => ResizeWindows();

			resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
			resizeTimer.Tick += ResizeTimer_Tick;

			floatingMenuInactivityTimer = new DispatcherTimer();
			UpdateFloatingMenuInactivityInterval();
			floatingMenuInactivityTimer.Tick += FloatingMenuInactivityTimer_Tick;
			// Only track mouse movement on the floating menu itself; avoid resetting timers for any mouse move inside app
			// PreviewMouseMove += HandleWindowMouseMove;
			ResetFloatingMenuInactivityTimer();

			floatingMenuWakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
			floatingMenuWakeTimer.Tick += FloatingMenuWakeTimer_Tick;
			floatingMenuWakeTimer.Start();

			viewModel.UpdateHostViewport(ActualWidth, ActualHeight);
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			floatingMenuInactivityTimer.Stop();
			floatingMenuWakeTimer.Stop();
			if (viewModel != null)
			{
				viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			}
		}

		// Method to handle window size changes
		public void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			// use overlay anchor for accurate content bounds
			var hostWidth = FloatingMenuOverlayAnchor?.ActualWidth > 0 ? FloatingMenuOverlayAnchor.ActualWidth : ActualWidth;
			var hostHeight = FloatingMenuOverlayAnchor?.ActualHeight > 0 ? FloatingMenuOverlayAnchor.ActualHeight : ActualHeight;
			viewModel.UpdateHostViewport(hostWidth, hostHeight);
			if (viewModel.Sessions.Count == 0) return;
			resizeTimer.Stop();
			resizeTimer.Start();
		}


		// Timer tick method to handle resize logic
		private void ResizeTimer_Tick(object sender, EventArgs e)
		{
			// If the left mouse button is pressed, don't proceed with resize
			if (System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed) return;

			resizeTimer.Stop();

			ResizeWindows();
		}

		private void ResizeWindows()
		{
			int width = (int)SessionTabControl.ActualWidth + 15;
			int height = (int)SessionTabControl.ActualHeight + 15;

			foreach (var session in viewModel.Sessions)
			{
				if (session.ExternalHandle == 0)
				{
					continue;
				}

				MoveWindow((IntPtr)session.ExternalHandle, -8, -32, width, height, true);
			}
		}

		private void ScriptControlButton_Click(object sender, RoutedEventArgs e)
		{
			// Open Script Controls as a tab within the main shell
			viewModel?.OpenScriptControlsTab();
			ResetFloatingMenuInactivityTimer();
		}

		private void HandleWindowMouseMove(object sender, MouseEventArgs e)
		{
			if (floatingMenuDragging)
			{
				return;
			}
			ResetFloatingMenuInactivityTimer();
		}

		private void FloatingMenuHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			floatingMenuDragCandidate = true;
			floatingMenuDragStart = e.GetPosition(this);
			floatingMenuOrigin = new Point(viewModel?.FloatingMenuLeft ?? 0, viewModel?.FloatingMenuTop ?? 0);
			ResetFloatingMenuInactivityTimer();
		}

		private void FloatingMenuHandle_PreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (!floatingMenuDragCandidate || viewModel == null || e.LeftButton != MouseButtonState.Pressed)
			{
				return;
			}

			var current = e.GetPosition(this);
			if (!floatingMenuDragging)
			{
				var delta = current - floatingMenuDragStart;
				if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
				{
					return;
				}

				floatingMenuDragging = true;
				((UIElement)sender).CaptureMouse();
			}

			var newLeft = floatingMenuOrigin.X + (current.X - floatingMenuDragStart.X);
			var newTop = floatingMenuOrigin.Y + (current.Y - floatingMenuDragStart.Y);

			newLeft = Math.Clamp(newLeft, 0, Math.Max(0, ActualWidth - FloatingMenuHandle.ActualWidth - 16));
			newTop = Math.Clamp(newTop, 0, Math.Max(0, ActualHeight - FloatingMenuHandle.ActualHeight - 16));

			// Detect snap zones
			var snapZone = DetectSnapZone(newLeft, newTop, FloatingMenuHandle.ActualWidth, FloatingMenuHandle.ActualHeight);
			viewModel.FloatingMenuDockCandidate = snapZone;
			viewModel.IsFloatingMenuDockOverlayVisible = snapZone != FloatingMenuDockRegion.None;

			viewModel.UpdateFloatingMenuPosition(newLeft, newTop, ActualWidth, ActualHeight);
			e.Handled = true;
		}

		private void FloatingMenuHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (floatingMenuDragging)
			{
				((UIElement)sender).ReleaseMouseCapture();
				floatingMenuDragging = false;

				// Apply snap if over a dock zone
				if (viewModel != null && viewModel.FloatingMenuDockCandidate != FloatingMenuDockRegion.None)
				{
					viewModel.ApplyDockRegion(viewModel.FloatingMenuDockCandidate, FloatingMenuHandle.ActualWidth, FloatingMenuHandle.ActualHeight);
				}

				viewModel.IsFloatingMenuDockOverlayVisible = false;
				e.Handled = true;
			}

			floatingMenuDragCandidate = false;
		}

		private FloatingMenuDockRegion DetectSnapZone(double left, double top, double handleWidth, double handleHeight)
		{
			const double snapThreshold = 80; // pixels from edge to trigger snap
			const double cornerSize = 120; // diagonal distance for corner snap

			var centerX = left + handleWidth / 2;
			var centerY = top + handleHeight / 2;

			var distToLeft = centerX;
			var distToRight = ActualWidth - centerX;
			var distToTop = centerY;
			var distToBottom = ActualHeight - centerY;

			// Check corners first (higher priority)
			if (distToLeft < cornerSize && distToTop < cornerSize)
				return FloatingMenuDockRegion.TopLeft;
			if (distToRight < cornerSize && distToTop < cornerSize)
				return FloatingMenuDockRegion.TopRight;
			if (distToLeft < cornerSize && distToBottom < cornerSize)
				return FloatingMenuDockRegion.BottomLeft;
			if (distToRight < cornerSize && distToBottom < cornerSize)
				return FloatingMenuDockRegion.BottomRight;

			// Check edges
			if (distToLeft < snapThreshold)
				return FloatingMenuDockRegion.Left;
			if (distToRight < snapThreshold)
				return FloatingMenuDockRegion.Right;
			if (distToTop < snapThreshold)
				return FloatingMenuDockRegion.Top;
			if (distToBottom < snapThreshold)
				return FloatingMenuDockRegion.Bottom;

			return FloatingMenuDockRegion.None;
		}

		private void FloatingMenu_MouseMove(object sender, MouseEventArgs e)
		{
			ResetFloatingMenuInactivityTimer();
		}

		private void FloatingMenuInactivityTimer_Tick(object sender, EventArgs e)
		{
			floatingMenuInactivityTimer.Stop();
			viewModel?.HideFloatingMenu();
		}

		private void FloatingMenuWakeTimer_Tick(object sender, EventArgs e)
		{
			if (viewModel == null)
			{
				return;
			}

			if (!GetCursorPos(out var current))
			{
				return;
			}

			if (!hasCursorBaseline)
			{
				lastCursorPoint = current;
				hasCursorBaseline = true;
				return;
			}

			var deltaX = Math.Abs(current.X - lastCursorPoint.X);
			var deltaY = Math.Abs(current.Y - lastCursorPoint.Y);

			if (!viewModel.IsFloatingMenuVisible && (deltaX > 8 || deltaY > 8))
			{
				viewModel.ShowFloatingMenu();
				ResetFloatingMenuInactivityTimer();
			}

			lastCursorPoint = current;
		}

		private void ResetFloatingMenuInactivityTimer()
		{
			if (viewModel == null)
			{
				return;
			}

			viewModel.ShowFloatingMenu();
			UpdateFloatingMenuInactivityInterval();
			floatingMenuInactivityTimer.Stop();
			floatingMenuInactivityTimer.Start();
		}

		private void UpdateFloatingMenuInactivityInterval()
		{
			var seconds = viewModel?.FloatingMenuInactivitySeconds ?? 2;
			floatingMenuInactivityTimer.Interval = TimeSpan.FromSeconds(seconds);
		}

		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MainWindowViewModel.FloatingMenuInactivitySeconds))
			{
				UpdateFloatingMenuInactivityInterval();
			}
		}

		private void FloatingMenuSettingsButton_Click(object sender, RoutedEventArgs e)
		{
			viewModel?.OpenSettingsTab();
			ResetFloatingMenuInactivityTimer();
		}

		private void FloatingMenuSetSide_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement fe && fe.Tag is string tag && viewModel != null)
			{
				if (Enum.TryParse<Models.FloatingMenuDirection>(tag, out var side))
				{
					viewModel.FloatingMenuAutoDirection = false;
					viewModel.FloatingMenuDirection = side;
				}
			}
		}
	}
}
