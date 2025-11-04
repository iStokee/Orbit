using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ControlzEx.Theming;
using Dragablz;
using MahApps.Metro.Controls;
using Orbit.API;
using Orbit.Models;
using Orbit.ViewModels;
using Orbit.Views;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Orbit
{
	public partial class MainWindow : MetroWindow
	{
	private readonly DispatcherTimer resizeTimer;
	private readonly MainWindowViewModel viewModel;
	private readonly DispatcherTimer floatingMenuInactivityTimer;
	private readonly DispatcherTimer floatingMenuWakeTimer;
	private static readonly Geometry TaskbarOrbitBodyGeometry = CreateOrbitGeometry("M256.25,20.656c-32.78,0-64.03,6.79-92.438,19-8.182-10.618-20.994-17.468-35.437-17.468-24.716,0-44.78,20.033-44.78,44.75,0,8.356,2.324,16.18,6.31,22.874-42.638,42.655-69.093,101.49-69.093,166.282,0,129.617,105.823,234.72,235.438,234.72,129.615-.002,234.72-105.103,234.72-234.72,0-129.618-105.105-235.438-234.72-235.438Zm0,19.313c119.515,0,216.094,96.607,216.094,216.124s-96.58,216.094-216.094,216.094c-119.515,0-216.813-96.577-216.813-216.094,0-59.568,24.176-113.438,63.22-152.5,7.273,5.113,16.15,8.094,25.718,8.094,24.716,0,44.75-20.034,44.75-44.75,0-3.453-.385-6.804-1.125-10.032C197.91,46,226.396,39.97,256.25,39.97Zm-.125,51.81c-91.3,0-165.875,74.575-165.875,165.876,0,91.3,74.576,165.406,165.875,165.406,35.12,0,67.708-10.965,94.5-29.656,7.13,4.23,15.45,6.656,24.344,6.656,26.396,0,47.81-21.384,47.81-47.78,0-12.763-5.005-24.366-13.155-32.938,7.677-19.067,11.906-39.884,11.906-61.688,0-91.3-74.106-165.875-165.405-165.875Zm0,19.126c81.2,0,146.78,65.55,146.78,146.75,0,17.833-3.172,34.924-8.967,50.72-5.81-2.513-12.237-3.907-18.97-3.907-26.396,0-47.78,21.414-47.78,47.81,0,10.59,3.454,20.362,9.28,28.283-23.065,15.084-50.66,23.843-80.343,23.843-81.2,0-147.22-65.55-147.22-146.75s66.02-146.75,147.22-146.75Zm-1.063,19.625c-7.462,31.99-21.767,62.112-42.906,83.25-21.14,21.14-48.73,32.913-80.72,40.376,31.99,7.462,62.112,21.736,83.25,42.875,21.14,21.14,32.914,48.764,40.376,80.75,7.463-31.986,19.204-59.61,40.344-80.75,21.14-21.138,51.262-35.412,83.25-42.874-32.236-7.428-59.455-19.11-80.72-40.375-21.262-21.263-35.446-51.013-42.873-83.25Zm.094,86.564c20.498,0,37.125,16.627,37.125,37.125,0,20.496-16.626,37.124-37.124,37.124-20.497,0-37.125-16.628-37.125-37.125,0-20.5,16.63-37.126,37.126-37.126Z");
	private static readonly Geometry TaskbarOrbitRingGeometry = CreateOrbitGeometry("M256.219,59.282c-109.882,0-198.813,88.931-198.813,198.813s88.931,198.313,198.813,198.313c28.527,0,55.727-6.064,80.22-17.312-5.983-7.862-9.502-17.646-9.502-28.375,0-26.396,21.384-47.78,47.78-47.78,6.84,0,13.355,1.41,19.214,3.964,5.316-15.913,8.204-32.895,8.204-50.47,0-92.548-75.148-167.653-167.656-167.653Z");
	private bool floatingMenuDragCandidate;
	private bool floatingMenuDragging;
	private Point floatingMenuDragStart;
	private Point floatingMenuOrigin;
	private bool hasCursorBaseline;
	private POINT lastCursorPoint;
	private bool forceAppClose;
	private HwndSource? hwndSource;
	private HwndSourceHook? messageHook;
	private const int WM_MBUTTONUP = 0x0208;
	

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

		public MainWindow(MainWindowViewModel viewModel)
		{
			this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

			InitializeComponent();

			if (TaskbarInfo == null)
			{
				TaskbarInfo = new TaskbarItemInfo();
			}
			TaskbarInfo.Description = "Orbit";
			UpdateTaskbarOverlay();
			ThemeManager.Current.ThemeChanged += OnThemeChanged;
			Loaded += MainWindow_Loaded;

			DataContext = this.viewModel;

			// Initialize Orbit API for external script integration
			OrbitAPI.Initialize(this.viewModel.ScriptIntegration);

			this.viewModel.PropertyChanged += ViewModel_PropertyChanged;
			var interTabController = new InterTabController
			{
				InterTabClient = this.viewModel.InterTabClient,
				Partition = "OrbitMainShell"
			};
			this.SessionTabControl.InterTabController = interTabController;
			this.SessionTabControl.ClosingItemCallback += this.viewModel.TabControl_ClosingItemHandler;

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

		this.viewModel.UpdateHostViewport(ActualWidth, ActualHeight);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		hwndSource = PresentationSource.FromVisual(this) as HwndSource;
		if (hwndSource != null)
		{
			messageHook = WndProcHook;
			hwndSource.AddHook(messageHook);
		}
	}

		protected override async void OnClosing(CancelEventArgs e)
		{
			if (forceAppClose || !ReferenceEquals(this, System.Windows.Application.Current.MainWindow) || viewModel == null)
			{
				base.OnClosing(e);
				return;
			}

			if (viewModel.Sessions.Count == 0)
			{
				base.OnClosing(e);
				return;
			}

			e.Cancel = true;

			try
			{
				await viewModel.CloseAllSessionsAsync(skipConfirmation: true, forceKillOnTimeout: false);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Failed to shutdown sessions on exit: {ex}");
			}
			try
			{
				await viewModel.ShutdownTrackedProcessesAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Failed to close tracked RuneScape processes on exit: {ex}");
			}
			finally
			{
				forceAppClose = true;
				Dispatcher.BeginInvoke(new Action(Close));
			}
		}

	protected override void OnClosed(EventArgs e)
	{
		if (hwndSource != null && messageHook != null)
		{
			hwndSource.RemoveHook(messageHook);
			hwndSource = null;
			messageHook = null;
		}

		ThemeManager.Current.ThemeChanged -= OnThemeChanged;
		Loaded -= MainWindow_Loaded;
		if (TaskbarInfo != null)
		{
			TaskbarInfo.Overlay = null;
			}

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
			foreach (var session in viewModel.Sessions)
			{
				if (session.HostControl is not ChildClientView clientView)
				{
					continue;
				}

				if (PresentationSource.FromVisual(clientView) == null)
				{
					continue;
				}

				if (!clientView.IsVisible)
				{
					// Hidden clients get re-synced when their tab is reactivated.
					continue;
				}

				var viewportSize = clientView.GetHostViewportSize();
				var width = Math.Max(0, (int)Math.Round(viewportSize.Width));
				var height = Math.Max(0, (int)Math.Round(viewportSize.Height));

				if (width <= 0 || height <= 0)
				{
					continue;
				}

				var lastApplied = clientView.LastAppliedViewportSize;
				var hasAppliedSize = lastApplied.Width > 0 && lastApplied.Height > 0;
				var viewportMatchesApplied = hasAppliedSize &&
					Math.Abs(lastApplied.Width - width) < 1 &&
					Math.Abs(lastApplied.Height - height) < 1;

				if (session.ExternalHandle != 0 && !viewportMatchesApplied)
				{
					var adjustedWidth = width + 16;
					var adjustedHeight = height + 40;
					MoveWindow((IntPtr)session.ExternalHandle, -8, -32, adjustedWidth, adjustedHeight, true);
				}

				_ = clientView.ResizeWindowAsync(width, height);
			}
		}

		private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
		{
			Dispatcher.Invoke(UpdateTaskbarOverlay);
		}

		private void UpdateTaskbarOverlay()
		{
			if (TaskbarInfo == null)
			{
				return;
			}

			// Use white for taskbar icon for best visibility on both light and dark taskbars
			var mainIcon = CreateOrbitIconImage(Colors.White, Colors.White, includeRing: false);
			if (mainIcon != null)
			{
				Icon = mainIcon;
			}

			// No overlay needed anymore - main icon handles it
			TaskbarInfo.Overlay = null;
		}

		private static ImageSource? CreateOrbitIconImage(Color accent, Color foreground, bool includeRing)
		{
			const double targetSize = 56d;

			var fillColor = Color.FromArgb(235, accent.R, accent.G, accent.B);
			var fillBrush = new SolidColorBrush(fillColor);
			fillBrush.Freeze();

			var bodyDrawing = new GeometryDrawing(fillBrush, null, TaskbarOrbitBodyGeometry);

			var group = new DrawingGroup();
			group.Children.Add(bodyDrawing);

			if (includeRing)
			{
				var ringColor = AdjustColorBrightness(accent, 0.25);
				var ringBrush = new SolidColorBrush(Color.FromArgb(245, ringColor.R, ringColor.G, ringColor.B));
				ringBrush.Freeze();

				var ringPen = new Pen(ringBrush, 26)
				{
					StartLineCap = PenLineCap.Round,
					EndLineCap = PenLineCap.Round,
					LineJoin = PenLineJoin.Round
				};
				ringPen.Freeze();

				var ringDrawing = new GeometryDrawing(null, ringPen, TaskbarOrbitRingGeometry);
				group.Children.Add(ringDrawing);
			}

			var highlightColor = Color.FromArgb(255, foreground.R, foreground.G, foreground.B);
			var highlightBrush = new SolidColorBrush(highlightColor);
			highlightBrush.Freeze();
			var highlightGeometry = new EllipseGeometry(new Point(357, 190), 24, 24);
			var highlightDrawing = new GeometryDrawing(highlightBrush, null, highlightGeometry);
			group.Children.Add(highlightDrawing);

			var bounds = TaskbarOrbitBodyGeometry.Bounds;
			var scale = targetSize / Math.Max(bounds.Width, bounds.Height);
			var translateX = -bounds.X * scale;
			var translateY = -bounds.Y * scale;
			var marginX = (targetSize - bounds.Width * scale) / 2d;
			var marginY = (targetSize - bounds.Height * scale) / 2d;

			var transform = new MatrixTransform(scale, 0, 0, scale, translateX + marginX, translateY + marginY);
			transform.Freeze();
			group.Transform = transform;
			group.Freeze();

			var drawingImage = new DrawingImage(group);
			drawingImage.Freeze();
			return drawingImage;
		}

		private static ImageSource? CreateOrbitOverlayImage(Color accent, Color foreground)
		{
			// Legacy method - now uses CreateOrbitIconImage
			return CreateOrbitIconImage(accent, foreground, includeRing: true);
		}

		private static Color ExtractColor(object? resource, Color fallback)
		{
			return resource switch
			{
				Color color => color,
				SolidColorBrush brush when brush.Color != default => brush.Color,
				_ => fallback
			};
		}

		private static Geometry CreateOrbitGeometry(string data)
		{
			var geometry = Geometry.Parse(data);
			geometry.Freeze();
			return geometry;
		}

		private static Color AdjustColorBrightness(Color color, double correctionFactor)
		{
			double red = color.R;
			double green = color.G;
			double blue = color.B;

			if (correctionFactor < 0)
			{
				correctionFactor = 1 + correctionFactor;
				red *= correctionFactor;
				green *= correctionFactor;
				blue *= correctionFactor;
			}
			else
			{
				red = (255 - red) * correctionFactor + red;
				green = (255 - green) * correctionFactor + green;
				blue = (255 - blue) * correctionFactor + blue;
			}

			return Color.FromArgb(color.A, (byte)Math.Clamp(red, 0, 255), (byte)Math.Clamp(green, 0, 255), (byte)Math.Clamp(blue, 0, 255));
		}

		private void ScriptControlButton_Click(object sender, RoutedEventArgs e)
		{
			// Surface the combined script manager/controls tool
			if (viewModel?.OpenScriptManagerCommand?.CanExecute(null) == true)
			{
				viewModel.OpenScriptManagerCommand.Execute(null);
			}

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

			// Detect snap zones and clipping
			var overlayWidth = SnapZoneOverlay.ActualWidth;
			var overlayHeight = SnapZoneOverlay.ActualHeight;
			var detection = DetectSnapZone(newLeft, newTop, FloatingMenuHandle.ActualWidth, FloatingMenuHandle.ActualHeight, overlayWidth, overlayHeight);
			viewModel.FloatingMenuDockCandidate = detection.region;
			viewModel.SetFloatingMenuClipping(detection.clipped);
			viewModel.IsFloatingMenuDockOverlayVisible = detection.region != FloatingMenuDockRegion.None || detection.clipped;

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
				viewModel.SetFloatingMenuClipping(false);
				e.Handled = true;
			}

			floatingMenuDragCandidate = false;
		}

		private (FloatingMenuDockRegion region, bool clipped) DetectSnapZone(double left, double top, double handleWidth, double handleHeight, double overlayWidth, double overlayHeight)
		{
			// Use configurable thresholds from settings
			var snapThreshold = Settings.Default.FloatingMenuDockEdgeThreshold;
			var cornerSize = Settings.Default.FloatingMenuDockCornerThreshold;
			var cornerHeight = Settings.Default.FloatingMenuDockCornerHeight;
			var edgeCoverage = Settings.Default.FloatingMenuDockEdgeCoverage;

			// Clamp values to reasonable ranges
			snapThreshold = Math.Clamp(snapThreshold, 40, 200);
			cornerSize = Math.Clamp(cornerSize, 60, 250);
			cornerHeight = Math.Clamp(cornerHeight, 60, 250);
			edgeCoverage = Math.Clamp(edgeCoverage, 0.05d, 0.95d);

			var hostWidth = overlayWidth > 0 ? overlayWidth : ActualWidth;
			var hostHeight = overlayHeight > 0 ? overlayHeight : ActualHeight;

			hostWidth = Math.Max(hostWidth, handleWidth);
			hostHeight = Math.Max(hostHeight, handleHeight);

			// Ensure zones never exceed host bounds
			var handleRect = new Rect(left, top, Math.Max(0d, handleWidth), Math.Max(0d, handleHeight));

			double ClampDimension(double value, double max) => Math.Max(0d, Math.Min(value, max));

			var leftThickness = ClampDimension(snapThreshold, hostWidth);
			var rightThickness = leftThickness;
			var topThickness = ClampDimension(snapThreshold, hostHeight);
			var bottomThickness = topThickness;

			var verticalCoverage = ClampDimension(hostHeight * edgeCoverage, hostHeight);
			var verticalStart = (hostHeight - verticalCoverage) / 2d;
			var horizontalCoverage = ClampDimension(hostWidth * edgeCoverage, hostWidth);
			var horizontalStart = (hostWidth - horizontalCoverage) / 2d;

			var cornerWidth = ClampDimension(cornerSize, hostWidth);
			var cornerHeightClamped = ClampDimension(cornerHeight, hostHeight);

			var leftZone = new Rect(0d, verticalStart, leftThickness, verticalCoverage);
			var rightZone = new Rect(Math.Max(0d, hostWidth - rightThickness), verticalStart, rightThickness, verticalCoverage);
			var topZone = new Rect(horizontalStart, 0d, horizontalCoverage, topThickness);
			var bottomZone = new Rect(horizontalStart, Math.Max(0d, hostHeight - bottomThickness), horizontalCoverage, bottomThickness);

			var topLeftZone = new Rect(0d, 0d, cornerWidth, cornerHeightClamped);
			var topRightZone = new Rect(Math.Max(0d, hostWidth - cornerWidth), 0d, cornerWidth, cornerHeightClamped);
			var bottomLeftZone = new Rect(0d, Math.Max(0d, hostHeight - cornerHeightClamped), cornerWidth, cornerHeightClamped);
			var bottomRightZone = new Rect(Math.Max(0d, hostWidth - cornerWidth), Math.Max(0d, hostHeight - cornerHeightClamped), cornerWidth, cornerHeightClamped);

			bool Intersects(Rect zone) => zone.Width > 0d && zone.Height > 0d && zone.IntersectsWith(handleRect);

			var topLeftActive = Intersects(topLeftZone);
			var topRightActive = Intersects(topRightZone);
			var bottomLeftActive = Intersects(bottomLeftZone);
			var bottomRightActive = Intersects(bottomRightZone);

			var leftActive = Intersects(leftZone);
			var rightActive = Intersects(rightZone);
			var topActive = Intersects(topZone);
			var bottomActive = Intersects(bottomZone);

			var anyActive = topLeftActive || topRightActive || bottomLeftActive || bottomRightActive || leftActive || rightActive || topActive || bottomActive;

			if (topLeftActive)
				return (FloatingMenuDockRegion.TopLeft, anyActive);
			if (topRightActive)
				return (FloatingMenuDockRegion.TopRight, anyActive);
			if (bottomLeftActive)
				return (FloatingMenuDockRegion.BottomLeft, anyActive);
			if (bottomRightActive)
				return (FloatingMenuDockRegion.BottomRight, anyActive);

			if (leftActive)
				return (FloatingMenuDockRegion.Left, anyActive);
			if (rightActive)
				return (FloatingMenuDockRegion.Right, anyActive);
			if (topActive)
				return (FloatingMenuDockRegion.Top, anyActive);
			if (bottomActive)
				return (FloatingMenuDockRegion.Bottom, anyActive);

			return (FloatingMenuDockRegion.None, anyActive);
		}

		private void FloatingMenu_MouseMove(object sender, MouseEventArgs e)
		{
			ResetFloatingMenuInactivityTimer();
		}

		private void FloatingMenuInactivityTimer_Tick(object sender, EventArgs e)
		{
			floatingMenuInactivityTimer.Stop();
		if (viewModel == null)
		{
			return;
		}

			if (viewModel.IsFloatingMenuExpanded || IsPointerOverFloatingMenu())
			{
				ResetFloatingMenuInactivityTimer();
				return;
			}

			floatingMenuInactivityTimer.Stop();
			viewModel.HideFloatingMenu();
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
				var screenPoint = new System.Windows.Point(current.X, current.Y);
				if (IsCursorNearFloatingMenu(screenPoint) && viewModel.ShowFloatingMenu())
				{
					ResetFloatingMenuInactivityTimer();
				}
			}

			lastCursorPoint = current;
		}

		private void ResetFloatingMenuInactivityTimer()
		{
			if (viewModel == null)
			{
				return;
			}

			if (viewModel.IsFloatingMenuVisible)
			{
				viewModel.ShowFloatingMenu(force: true);
			}
			else if (!viewModel.ShowFloatingMenu())
			{
				floatingMenuInactivityTimer.Stop();
				return;
			}

			UpdateFloatingMenuInactivityInterval();
			floatingMenuInactivityTimer.Stop();
			floatingMenuInactivityTimer.Start();
		}

		private bool IsPointerOverFloatingMenu()
		{
			if (FloatingMenuHandle?.IsMouseOver == true)
			{
				return true;
			}

			if (FloatingMenuPopupContent?.IsMouseOver == true)
			{
				return true;
			}

			if (FloatingMenuPopup?.IsMouseOver == true)
			{
				return true;
			}

			return false;
		}

		private bool IsCursorNearFloatingMenu(System.Windows.Point screenPoint)
		{
			if (viewModel == null || FloatingMenuHandle == null || PresentationSource.FromVisual(this) == null)
			{
				return false;
			}

			var windowPoint = PointFromScreen(screenPoint);
			var handleWidth = FloatingMenuHandle.ActualWidth > 0 ? FloatingMenuHandle.ActualWidth : 56;
			var handleHeight = FloatingMenuHandle.ActualHeight > 0 ? FloatingMenuHandle.ActualHeight : 56;

			var handleRect = new Rect(
				new System.Windows.Point(viewModel.FloatingMenuLeft, viewModel.FloatingMenuTop),
				new Size(handleWidth, handleHeight));

			const double proximityPadding = 96;
			handleRect.Inflate(proximityPadding, proximityPadding);

			return handleRect.Contains(windowPoint);
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

		private void RootGrid_MouseUp(object sender, MouseButtonEventArgs e)
		{
			if (e.Handled || viewModel == null)
			{
				return;
			}

			if (viewModel.FloatingMenuQuickToggleMode != FloatingMenuQuickToggleMode.MiddleMouse)
			{
				return;
			}

			if (e.ChangedButton != MouseButton.Middle)
			{
				return;
			}

			e.Handled = true;
			ToggleFloatingMenuQuickAccess();
		}

		private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
		{
			if (viewModel == null)
			{
				return;
			}

			if (viewModel.FloatingMenuQuickToggleMode != FloatingMenuQuickToggleMode.MiddleMouse)
			{
				return;
			}

			if (e.ChangedButton != MouseButton.Middle)
			{
				return;
			}

			e.Handled = true;
			ToggleFloatingMenuQuickAccess();
		}

		private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
		{
			if (viewModel == null)
			{
				return;
			}

			var mode = viewModel.FloatingMenuQuickToggleMode;
			if (mode == FloatingMenuQuickToggleMode.MiddleMouse)
			{
				return;
			}

			if (!CanProcessQuickToggleFromKeyboard(e))
			{
				return;
			}

			if (mode == FloatingMenuQuickToggleMode.HomeKey && e.Key == Key.Home)
			{
				e.Handled = true;
				ToggleFloatingMenuQuickAccess();
			}
			else if (mode == FloatingMenuQuickToggleMode.EndKey && e.Key == Key.End)
			{
				e.Handled = true;
				ToggleFloatingMenuQuickAccess();
			}
		}

		private bool CanProcessQuickToggleFromKeyboard(KeyEventArgs e)
		{
			if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) != ModifierKeys.None)
			{
				return false;
			}

			return !IsTextInputContext(e.OriginalSource);
		}

		private static bool IsTextInputContext(object originalSource)
		{
			if (originalSource is not DependencyObject node)
			{
				return false;
			}

			while (node != null)
			{
				if (node is TextBoxBase || node is PasswordBox || node is ComboBox)
				{
					return true;
				}

				node = VisualTreeHelper.GetParent(node);
			}

			return false;
		}

		private void ToggleFloatingMenuQuickAccess()
		{
			if (viewModel == null)
			{
				return;
			}

			if (viewModel.IsFloatingMenuVisible)
			{
				viewModel.HideFloatingMenu();
				floatingMenuInactivityTimer.Stop();
			}
			else if (viewModel.ShowFloatingMenu(force: true))
			{
				ResetFloatingMenuInactivityTimer();
			}
		}

		private void SessionRenameTextBox_KeyDown(object sender, KeyEventArgs e)
		{
			if (viewModel == null)
			{
				return;
			}

			if (sender is TextBox textBox && textBox.DataContext is SessionModel session)
			{
				if (e.Key == Key.Enter)
				{
					viewModel.CommitSessionRename(session);
					e.Handled = true;
				}
				else if (e.Key == Key.Escape)
				{
					viewModel.CancelSessionRename(session);
					e.Handled = true;
				}
			}
		}

		private void SessionRenameTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
			if (viewModel == null)
			{
				return;
			}

			if (sender is TextBox textBox && textBox.DataContext is SessionModel session && session.IsRenaming)
			{
				viewModel.CommitSessionRename(session);
			}
		}

		private void SessionRenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (e.NewValue is bool isVisible && isVisible && sender is TextBox textBox)
		{
			textBox.Dispatcher.BeginInvoke(new Action(() =>
			{
				textBox.Focus();
				textBox.SelectAll();
			}), DispatcherPriority.Background);
		}
	}

	private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WM_MBUTTONUP && viewModel != null && viewModel.FloatingMenuQuickToggleMode == FloatingMenuQuickToggleMode.MiddleMouse)
		{
			handled = true;
			Dispatcher.BeginInvoke(new Action(ToggleFloatingMenuQuickAccess), DispatcherPriority.Input);
		}

		return IntPtr.Zero;
	}
		
		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			UpdateTaskbarOverlay();

			// Auto-open Orbit View if enabled
			if (Settings.Default.AutoOpenOrbitViewOnStartup)
			{
				viewModel?.OpenOrbitViewCommand?.Execute(null);
			}
		}
	}
}
