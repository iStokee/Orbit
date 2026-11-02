using System;
using System.Collections.Specialized;
using System.Collections;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
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
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Orbit
{
	public partial class MainWindow : MetroWindow
	{
	private readonly System.Collections.Generic.Dictionary<TabablzControl, NotifyCollectionChangedEventHandler> branchItemHandlers = new();
	private readonly MainWindowViewModel viewModel;
	private readonly DispatcherTimer floatingMenuInactivityTimer;
	private readonly DispatcherTimer floatingMenuWakeTimer;
	private readonly DispatcherTimer windowPlacementPersistTimer;
	private readonly Services.FloatingMenuQuickToggleService floatingMenuQuickToggleService;
	private readonly Services.FloatingMenuGeometryService floatingMenuGeometryService;
	private readonly Services.ShellWindowPlacementService windowPlacementService;
	private readonly Services.TaskbarOrbitIconService taskbarOrbitIconService;
	private bool floatingMenuDragCandidate;
	private bool floatingMenuDragging;
	private Point floatingMenuDragStart;
	private Point floatingMenuOrigin;
	private bool hasCursorBaseline;
	private POINT lastCursorPoint;
	private bool forceAppClose;
	private bool shutdownInProgress;
	private bool transferringWindowContents;
	private HwndSource? hwndSource;
	private HwndSourceHook? messageHook;
	private bool isPrimaryShellWindow => ReferenceEquals(System.Windows.Application.Current?.MainWindow, this);
	private bool windowPlacementRestored;
	private bool windowPlacementPersistenceReady;
	private const int WM_MBUTTONUP = 0x0208;
	
		[DllImport("user32.dll")]
		private static extern bool GetCursorPos(out POINT lpPoint);

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		public MainWindow(
			MainWindowViewModel viewModel,
			Services.FloatingMenuQuickToggleService floatingMenuQuickToggleService,
			Services.FloatingMenuGeometryService floatingMenuGeometryService,
			Services.ShellWindowPlacementService windowPlacementService,
			Services.TaskbarOrbitIconService taskbarOrbitIconService)
		{
			this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
			this.floatingMenuQuickToggleService = floatingMenuQuickToggleService ?? throw new ArgumentNullException(nameof(floatingMenuQuickToggleService));
			this.floatingMenuGeometryService = floatingMenuGeometryService ?? throw new ArgumentNullException(nameof(floatingMenuGeometryService));
			this.windowPlacementService = windowPlacementService ?? throw new ArgumentNullException(nameof(windowPlacementService));
			this.taskbarOrbitIconService = taskbarOrbitIconService ?? throw new ArgumentNullException(nameof(taskbarOrbitIconService));

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

			Activated += OnWindowActivated;
			Deactivated += OnWindowDeactivated;

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

			//// Forward location/state events to window placement persistence
			LocationChanged += MainWindow_LocationChanged;
			StateChanged += MainWindow_StateChanged;


			floatingMenuInactivityTimer = new DispatcherTimer();
			UpdateFloatingMenuInactivityInterval();
			floatingMenuInactivityTimer.Tick += FloatingMenuInactivityTimer_Tick;
			// Only track mouse movement on the floating menu itself; avoid resetting timers for any mouse move inside app
			// PreviewMouseMove += HandleWindowMouseMove;
			ResetFloatingMenuInactivityTimer();

			floatingMenuWakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
			floatingMenuWakeTimer.Tick += FloatingMenuWakeTimer_Tick;
			floatingMenuWakeTimer.Start();

			windowPlacementPersistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
			windowPlacementPersistTimer.Tick += WindowPlacementPersistTimer_Tick;

		this.viewModel.UpdateHostViewport(ActualWidth, ActualHeight);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		if (!windowPlacementRestored)
		{
			RestoreWindowPlacementFromSettingsIfPrimary();
			windowPlacementRestored = true;
		}
		windowPlacementPersistenceReady = true;

		hwndSource = PresentationSource.FromVisual(this) as HwndSource;
		if (hwndSource != null)
		{
			messageHook = WndProcHook;
			hwndSource.AddHook(messageHook);
		}
	}

		protected override async void OnClosing(CancelEventArgs e)
		{
			if (forceAppClose || viewModel == null || (System.Windows.Application.Current as App)?.IsShutdownPrepared == true)
			{
				base.OnClosing(e);
				return;
			}

			if (transferringWindowContents) { e.Cancel = true; return; }
			if ((System.Windows.Application.Current as App)?.IsShuttingDown == true)
			{
				e.Cancel = true;
				return;
			}

			// If the user closes the "primary" window while other Orbit windows are still open (e.g. after
			// tearing tabs out), do not treat it as an app shutdown. This avoids closing sessions/tools
			// that are currently hosted elsewhere.
			var remainingShell = System.Windows.Application.Current?.Windows?
				.OfType<MainWindow>()
				.FirstOrDefault(w => !ReferenceEquals(w, this) && w.IsVisible);
			if (remainingShell != null)
			{
				e.Cancel = true;
				transferringWindowContents = true;
				try
				{
					if (ReferenceEquals(System.Windows.Application.Current.MainWindow, this))
						System.Windows.Application.Current.MainWindow = remainingShell;
					var target = (MainWindowViewModel)remainingShell.DataContext;
					var controls = Utilities.VisualTreeUtil.FindVisualChildren<TabablzControl>(this)
						.Where(control => Utilities.VisualTreeUtil.FindVisualAncestor<Views.OrbitGridLayoutView>(control) == null).ToArray();
					var items = viewModel.Tabs.Concat(controls.SelectMany(control => control.Items.Cast<object>())).Distinct().ToArray();
					((App)System.Windows.Application.Current).Services.GetRequiredService<Services.ShellWindowTransferService>().Transfer(
						items,
						item =>
						{
							viewModel.Tabs.Remove(item);
							foreach (var control in controls.Where(control => control.Items.Contains(item)))
								if (control.ItemsSource is IList list && !list.IsReadOnly) list.Remove(item);
								else if (control.ItemsSource == null) control.Items.Remove(item);
						},
						item => { if (!viewModel.Tabs.Contains(item)) viewModel.Tabs.Add(item); },
						target.Tabs, viewModel, target, () => target.OpenOrbitViewCommand.Execute(null),
						ReferenceEquals(System.Windows.Application.Current.MainWindow, remainingShell)
							? Services.SessionPlacementKind.MainTabs : Services.SessionPlacementKind.TearOffWindow);
					forceAppClose = true;
					_ = Dispatcher.BeginInvoke(new Action(Close));
					}
				catch (Exception ex)
				{
					System.Windows.MessageBox.Show($"Could not move this window's contents: {ex.Message}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Error);
				}
				finally { transferringWindowContents = false; }
				return;
			}

			e.Cancel = true;
			if (shutdownInProgress) return;
			shutdownInProgress = true;
			try
			{
				await ((App)System.Windows.Application.Current).PrepareForShutdownAsync();
				forceAppClose = true;
				_ = Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
			}
			catch (Exception ex)
			{
				shutdownInProgress = false;
				System.Windows.MessageBox.Show($"Orbit could not finish shutdown: {ex.Message}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

	protected override void OnClosed(EventArgs e)
	{
		if (isPrimaryShellWindow)
		{
			SaveWindowPlacementToSettings();
		}

		if (hwndSource != null && messageHook != null)
		{
			hwndSource.RemoveHook(messageHook);
			hwndSource = null;
			messageHook = null;
		}

		ThemeManager.Current.ThemeChanged -= OnThemeChanged;
		Loaded -= MainWindow_Loaded;
		Activated -= OnWindowActivated;
		Deactivated -= OnWindowDeactivated;
		LocationChanged -= MainWindow_LocationChanged;
		StateChanged -= MainWindow_StateChanged;
		SessionTabControl.ClosingItemCallback -= this.viewModel.TabControl_ClosingItemHandler;
		foreach (var control in branchItemHandlers.Keys.ToArray()) DetachBranchTabControl(control);
		floatingMenuInactivityTimer.Tick -= FloatingMenuInactivityTimer_Tick;
		floatingMenuWakeTimer.Tick -= FloatingMenuWakeTimer_Tick;
		windowPlacementPersistTimer.Tick -= WindowPlacementPersistTimer_Tick;

		if (TaskbarInfo != null)
		{
			TaskbarInfo.Overlay = null;
			}

			floatingMenuInactivityTimer.Stop();
			floatingMenuWakeTimer.Stop();
			windowPlacementPersistTimer.Stop();
			if (viewModel != null)
			{
				viewModel.PropertyChanged -= ViewModel_PropertyChanged;
				viewModel.Dispose();
			}

			base.OnClosed(e);
		}

		private void RestoreWindowPlacementFromSettingsIfPrimary()
		{
			var referencePrimaryWindow = System.Windows.Application.Current?.MainWindow;
			var sourceBounds = default(Rect?);
			var sizeSourceWindow = !ReferenceEquals(referencePrimaryWindow, this)
				? referencePrimaryWindow
				: null;
			if (sizeSourceWindow is { IsLoaded: true } &&
				sizeSourceWindow.WindowState != WindowState.Minimized)
			{
				sourceBounds = sizeSourceWindow.WindowState == WindowState.Normal
					? new Rect(sizeSourceWindow.Left, sizeSourceWindow.Top, sizeSourceWindow.Width, sizeSourceWindow.Height)
					: sizeSourceWindow.RestoreBounds;
			}

			var plan = windowPlacementService.BuildRestorePlan(
				windowPlacementService.ReadFromSettings(),
				sourceBounds,
				isPrimaryShellWindow,
				GetVirtualScreenBounds());

			if (plan.Width.HasValue)
			{
				Width = plan.Width.Value;
			}

			if (plan.Height.HasValue)
			{
				Height = plan.Height.Value;
			}

			if (plan.Left.HasValue && plan.Top.HasValue)
			{
				WindowStartupLocation = WindowStartupLocation.Manual;
				Left = plan.Left.Value;
				Top = plan.Top.Value;
			}

			if (plan.Maximize)
			{
				WindowState = WindowState.Maximized;
			}
		}

		private static Rect GetVirtualScreenBounds()
		{
			return new Rect(
				SystemParameters.VirtualScreenLeft,
				SystemParameters.VirtualScreenTop,
				SystemParameters.VirtualScreenWidth,
				SystemParameters.VirtualScreenHeight);
		}

		private void MainShellBranchTabControl_Loaded(object sender, RoutedEventArgs e)
		{
			if (sender is not TabablzControl tabControl || viewModel == null)
			{
				return;
			}

			// Branch tabs need the same close routing so session shutdown logic stays centralized.
			tabControl.ClosingItemCallback -= viewModel.TabControl_ClosingItemHandler;
			tabControl.ClosingItemCallback += viewModel.TabControl_ClosingItemHandler;

			tabControl.InterTabController ??= new InterTabController();
			tabControl.InterTabController.InterTabClient = viewModel.InterTabClient;
			tabControl.InterTabController.Partition = "OrbitMainShell";
			if (!branchItemHandlers.ContainsKey(tabControl))
			{
				NotifyCollectionChangedEventHandler handler = (_, args) => viewModel.ReconcileBranchTabItemsChanged(tabControl, args);
				((INotifyCollectionChanged)tabControl.Items).CollectionChanged += handler;
				branchItemHandlers.Add(tabControl, handler);
				tabControl.Unloaded += MainShellBranchTabControl_Unloaded;
			}
			if (tabControl.Items.Count > 0)
				viewModel.ReconcileBranchTabItemsChanged(tabControl, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, tabControl.Items.Cast<object>().ToList()));
		}

		private void MainShellBranchTabControl_Unloaded(object sender, RoutedEventArgs e)
		{
			if (sender is TabablzControl control) DetachBranchTabControl(control);
		}

		private void DetachBranchTabControl(TabablzControl control)
		{
			if (branchItemHandlers.Remove(control, out var handler)) ((INotifyCollectionChanged)control.Items).CollectionChanged -= handler;
			control.Unloaded -= MainShellBranchTabControl_Unloaded;
			control.ClosingItemCallback -= viewModel.TabControl_ClosingItemHandler;
		}

		private void MainShellTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
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

			var host = session.HostControl;
			host.RefreshDockedContentActivation(requestFocus: IsActive);

			if (IsActive)
			{
				_ = Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						host.FocusEmbeddedClient();
					}
					catch
					{
						// Best effort only.
					}
				}), DispatcherPriority.Input);
			}
		}

		private void SaveWindowPlacementToSettings()
		{
			if (!isPrimaryShellWindow || !windowPlacementPersistenceReady)
			{
				return;
			}

			try
			{
				if (WindowState == WindowState.Minimized)
				{
					return;
				}

				var bounds = WindowState == WindowState.Normal
					? new Rect(Left, Top, Width, Height)
					: RestoreBounds;

				windowPlacementService.SaveToSettings(bounds, WindowState == WindowState.Maximized);
			}
			catch
			{
				// best effort persistence only
			}
		}

		private void ScheduleWindowPlacementSave()
		{
			if (!isPrimaryShellWindow || !windowPlacementPersistenceReady || !IsLoaded)
			{
				return;
			}

			windowPlacementPersistTimer.Stop();
			windowPlacementPersistTimer.Start();
		}

		private void WindowPlacementPersistTimer_Tick(object? sender, EventArgs e)
		{
			windowPlacementPersistTimer.Stop();
			SaveWindowPlacementToSettings();
		}

		private void MainWindow_LocationChanged(object? sender, EventArgs e)
		{
			ScheduleWindowPlacementSave();
		}

		private void MainWindow_StateChanged(object? sender, EventArgs e)
		{
			if (WindowState == WindowState.Minimized)
			{
				return;
			}

			ScheduleWindowPlacementSave();
		}

		// Method to handle window size changes
		public void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			ScheduleWindowPlacementSave();
			// use overlay anchor for accurate content bounds
			var hostWidth = FloatingMenuOverlayAnchor?.ActualWidth > 0 ? FloatingMenuOverlayAnchor.ActualWidth : ActualWidth;
			var hostHeight = FloatingMenuOverlayAnchor?.ActualHeight > 0 ? FloatingMenuOverlayAnchor.ActualHeight : ActualHeight;
			viewModel.UpdateHostViewport(hostWidth, hostHeight);
		}


		private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
		{
			if (Dispatcher.CheckAccess())
			{
				UpdateTaskbarOverlay();
				return;
			}

			Dispatcher.BeginInvoke((Action)UpdateTaskbarOverlay);
		}

		private void UpdateTaskbarOverlay()
		{
			if (TaskbarInfo == null)
			{
				return;
			}

			// Use white for taskbar icon for best visibility on both light and dark taskbars
			Icon = taskbarOrbitIconService.CreateMainIcon();

			// No overlay needed anymore - main icon handles it
			TaskbarInfo.Overlay = null;
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
				viewModel.IsFloatingMenuDragging = true;
				((UIElement)sender).CaptureMouse();
			}

			var newLeft = floatingMenuOrigin.X + (current.X - floatingMenuDragStart.X);
			var newTop = floatingMenuOrigin.Y + (current.Y - floatingMenuDragStart.Y);

			newLeft = Math.Clamp(newLeft, 0, Math.Max(0, ActualWidth - FloatingMenuHandle.ActualWidth - 16));
			newTop = Math.Clamp(newTop, 0, Math.Max(0, ActualHeight - FloatingMenuHandle.ActualHeight - 16));

			// Detect snap zones and clipping
			var overlayWidth = SnapZoneOverlay.ActualWidth;
			var overlayHeight = SnapZoneOverlay.ActualHeight;
			var detection = floatingMenuGeometryService.DetectSnapZone(
				newLeft,
				newTop,
				FloatingMenuHandle.ActualWidth,
				FloatingMenuHandle.ActualHeight,
				overlayWidth > 0 ? overlayWidth : ActualWidth,
				overlayHeight > 0 ? overlayHeight : ActualHeight,
				Settings.Default.FloatingMenuDockEdgeThreshold,
				Settings.Default.FloatingMenuDockCornerThreshold,
				Settings.Default.FloatingMenuDockCornerHeight,
				Settings.Default.FloatingMenuDockEdgeCoverage);
			viewModel.FloatingMenuDockCandidate = detection.Region;
			viewModel.SetFloatingMenuClipping(detection.Clipped);
			viewModel.IsFloatingMenuDockOverlayVisible =
				detection.Region != FloatingMenuDockRegion.None ||
				detection.Clipped ||
				(viewModel.ShowAllSnapZonesOnDrag && viewModel.IsFloatingMenuDragging);

			viewModel.UpdateFloatingMenuPosition(newLeft, newTop, ActualWidth, ActualHeight);
			e.Handled = true;
		}

		private void FloatingMenuHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (floatingMenuDragging)
			{
				((UIElement)sender).ReleaseMouseCapture();
				floatingMenuDragging = false;
				viewModel.IsFloatingMenuDragging = false;

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

			// Never wake or show floating menu popups while this window is inactive.
			if (!IsActive)
			{
				if (viewModel.IsFloatingMenuVisible)
				{
					viewModel.HideFloatingMenu();
				}

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

			if (!IsActive)
			{
				floatingMenuInactivityTimer.Stop();
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
			TryHandleMouseQuickToggle(e);
		}

		private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
		{
			TryHandleMouseQuickToggle(e);
		}

		private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
		{
			if (viewModel == null)
			{
				return;
			}

			if (TryHandleMesharpDebugMenuHotkey(e))
			{
				return;
			}

			if (floatingMenuQuickToggleService.ShouldToggleFromKeyboard(
				viewModel.FloatingMenuQuickToggleMode,
				e.Key,
				Keyboard.Modifiers,
				IsTextInputContext(e.OriginalSource)))
			{
				e.Handled = true;
				ToggleFloatingMenuQuickAccess();
			}
		}

		private bool TryHandleMesharpDebugMenuHotkey(KeyEventArgs e)
		{
			if (e.Handled || viewModel == null || !viewModel.IsMesharpDebugMenuHotkeyEnabled)
			{
				return false;
			}

			if (IsTextInputContext(e.OriginalSource))
			{
				return false;
			}

			var key = e.Key == Key.System ? e.SystemKey : e.Key;
			if (!viewModel.MatchesMesharpDebugMenuHotkey(key, Keyboard.Modifiers))
			{
				return false;
			}

			e.Handled = true;
			_ = viewModel.ToggleNativeDebugMenuAsync();
			return true;
		}

		private void TryHandleMouseQuickToggle(MouseButtonEventArgs e)
		{
			if (e.Handled || viewModel == null)
			{
				return;
			}

			if (!floatingMenuQuickToggleService.ShouldToggleFromMouse(
				viewModel.FloatingMenuQuickToggleMode,
				e.ChangedButton,
				e.ClickCount,
				Keyboard.Modifiers,
				IsTextInputContext(e.OriginalSource)))
			{
				return;
			}

			e.Handled = true;
			ToggleFloatingMenuQuickAccess();
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
			_ = viewModel?.ReassertInputPassthroughAsync(orbitActive: true);

			// Auto-open Orbit View if enabled
			if (Settings.Default.AutoOpenOrbitViewOnStartup)
			{
				viewModel?.OpenOrbitViewCommand?.Execute(null);
			}
		}

		private void OnWindowActivated(object? sender, EventArgs e)
		{
			hasCursorBaseline = false;
			_ = this.viewModel.ReassertInputPassthroughAsync(orbitActive: true);
		}

		private void OnWindowDeactivated(object? sender, EventArgs e)
		{
			floatingMenuDragCandidate = false;
			floatingMenuDragging = false;
			this.viewModel.IsFloatingMenuDragging = false;
			this.viewModel.IsFloatingMenuDockOverlayVisible = false;
			this.viewModel.SetFloatingMenuClipping(false);
			this.viewModel.HideFloatingMenu();
			_ = this.viewModel.ReassertInputPassthroughAsync(orbitActive: false);
		}
	}
}
