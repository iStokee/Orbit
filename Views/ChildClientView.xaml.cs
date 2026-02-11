using System;
using Dragablz;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using Size = System.Windows.Size;
using WF = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Windows.Threading;
using Orbit.Models;

namespace Orbit.Views
{
    /// <summary>
    /// Interaction logic for ChildClientView.xaml
    /// </summary>
    /// 
    public partial class ChildClientView : UserControl
    {

        #region DLL Imports
        [DllImport("user32.dll")]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        internal static extern int SetWindowText(IntPtr hWnd, string text);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        #endregion

		internal RSForm rsForm;
        internal bool hasStarted = false;
		private bool loadRequested;
		private readonly TaskCompletionSource<RSForm> sessionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly SessionParkingHost parkingHost = new();
		private readonly object resizeSync = new();
		private CancellationTokenSource? resizeThrottleCts;
		private Size lastMeasuredViewportSize = Size.Empty;
		private Size lastRequestedViewportSize = Size.Empty;
		private Size lastAppliedViewportSize = Size.Empty;
		private SessionModel? boundSession;

		// When SessionType=ExternalScript we embed an existing HWND inside a WinForms panel.
		private WF.Panel? externalHostPanel;
		private IntPtr externalOriginalParent = IntPtr.Zero;
		private int? externalOriginalStyle;
		private bool externalStylesApplied;

		private static readonly TimeSpan ResizeThrottleInterval = TimeSpan.FromMilliseconds(50);

		internal Size LastAppliedViewportSize
		{
			get
			{
				lock (resizeSync)
				{
					return lastAppliedViewportSize;
				}
			}
		}

        public ChildClientView()
        {
            InitializeComponent();
            // FIXED: Don't call LoadNewSession() until visual tree is ready
			// This was causing race conditions with WindowsFormsHost initialization
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
			IsVisibleChanged += OnIsVisibleChanged;
			DataContextChanged += OnDataContextChanged;
        }

        private void RSPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure initial sizing uses the actual host size, not the whole TabControl
            if (RSPanel?.ActualWidth > 0 && RSPanel?.ActualHeight > 0)
            {
				var snapshot = new Size(RSPanel.ActualWidth, RSPanel.ActualHeight);
				if (!AreClose(snapshot, lastMeasuredViewportSize))
				{
					lastMeasuredViewportSize = snapshot;
				}
                _ = ResizeWindowAsync((int)Math.Round(snapshot.Width), (int)Math.Round(snapshot.Height));
            }
        }

        private void RSPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Resize docked client when the WindowsFormsHost layout changes
			var newSize = new Size(Math.Max(1, e.NewSize.Width), Math.Max(1, e.NewSize.Height));
			if (AreClose(newSize, lastMeasuredViewportSize))
			{
				return;
			}

			lastMeasuredViewportSize = newSize;
            _ = ResizeWindowAsync((int)Math.Round(newSize.Width), (int)Math.Round(newSize.Height));
        }

		private void RSPanel_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			// Forward focus to the embedded game window when clicked
			FocusEmbeddedClient();
		}

		private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (e.OldValue is SessionModel oldSession)
			{
			// Removed: overlay confirmation event unsubscription
			}

			if (e.NewValue is SessionModel newSession)
			{
				boundSession = newSession;
			// Removed: overlay confirmation event subscription and focus logic
			}
			else
			{
				boundSession = null;
			}
		}

		// Removed: Session_PropertyChanged method for overlay

		// Removed: ConfirmCloseButton_Click method for overlay

		// Removed: CancelCloseButton_Click method for overlay

		private void ChildClientView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
		// Removed: overlay confirmation PreviewKeyDown check

		// Removed: if (e.Key == Key.Escape)
		// Removed: {
		// Removed: boundSession.ResolveCloseConfirmation(false);
		// Removed: e.Handled = true;
		// Removed: }
		// Removed: else if (e.Key == Key.Enter)
		// Removed: {
		// Removed: boundSession.ResolveCloseConfirmation(true);
		// Removed: e.Handled = true;
		// Removed: }
		}

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
			RestoreSessionFromParking();

			// FIXED: Start session loading AFTER visual tree is ready
			// Only load if we haven't started yet (avoid re-loading on tab switch)
			EnsureSessionLoading();

            if (Parent is TabItem parentTab && parentTab.Parent is TabablzControl tabControl)
            {
                tabControl.SizeChanged += OnTabControlSizeChanged;
                // Prefer host size when available
                if (RSPanel?.ActualWidth > 0 && RSPanel?.ActualHeight > 0)
                {
					var snapshot = new Size(RSPanel.ActualWidth, RSPanel.ActualHeight);
					lastMeasuredViewportSize = snapshot;
                    _ = ResizeWindowAsync((int)Math.Round(snapshot.Width), (int)Math.Round(snapshot.Height));
                }
                else if (tabControl.ActualWidth > 0 && tabControl.ActualHeight > 0)
                {
					var snapshot = new Size(tabControl.ActualWidth, tabControl.ActualHeight);
					lastMeasuredViewportSize = snapshot;
                    _ = ResizeWindowAsync((int)Math.Round(snapshot.Width), (int)Math.Round(snapshot.Height));
                }
            }
        }

			private void OnUnloaded(object sender, RoutedEventArgs e)
			{
				if (Parent is TabItem parentTab && parentTab.Parent is TabablzControl tabControl)
				{
					tabControl.SizeChanged -= OnTabControlSizeChanged;
				}

				// External script windows: restore parent/styles so the window isn't orphaned.
				if (boundSession?.IsExternalScript == true && boundSession.ExternalHandle != 0)
				{
					try
					{
						var hwnd = (IntPtr)boundSession.ExternalHandle;

						if (externalStylesApplied && externalOriginalStyle.HasValue)
						{
							const int GWL_STYLE = -16;
							SetWindowLong(hwnd, GWL_STYLE, externalOriginalStyle.Value);
							externalStylesApplied = false;
						}

						if (externalOriginalParent != IntPtr.Zero)
						{
							SetParent(hwnd, externalOriginalParent);
						}
					}
					catch { /* best effort */ }

					try
					{
						RSPanel.Child = null;
					}
					catch { /* best effort */ }

					try
					{
						externalHostPanel?.Dispose();
					}
					catch { /* ignore */ }

					externalHostPanel = null;
					externalOriginalParent = IntPtr.Zero;
					externalOriginalStyle = null;
					hasStarted = false;
					loadRequested = false;
					boundSession = null;
					return;
				}

				ParkSessionIntoOffscreenHost();
				if (boundSession != null)
				{
				// Removed: boundSession.PropertyChanged -= Session_PropertyChanged;
					boundSession = null;
				}
			}

        private async void OnTabControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Parent is TabItem parentTab && parentTab.Parent is TabablzControl tabControl)
            {
                // Prefer host size when available
                if (RSPanel?.ActualWidth > 0 && RSPanel?.ActualHeight > 0)
                {
					var snapshot = new Size(RSPanel.ActualWidth, RSPanel.ActualHeight);
					if (!AreClose(snapshot, lastMeasuredViewportSize))
					{
						lastMeasuredViewportSize = snapshot;
					}
                    await ResizeWindowAsync((int)Math.Round(snapshot.Width), (int)Math.Round(snapshot.Height));
                }
                else if (tabControl.ActualWidth > 0 && tabControl.ActualHeight > 0)
                {
					var snapshot = new Size(tabControl.ActualWidth, tabControl.ActualHeight);
					if (!AreClose(snapshot, lastMeasuredViewportSize))
					{
						lastMeasuredViewportSize = snapshot;
					}
                    await ResizeWindowAsync((int)Math.Round(snapshot.Width), (int)Math.Round(snapshot.Height));
                }
            }
        }

		public Task ResizeWindowAsync(int width, int height)
		{
			if (boundSession?.IsExternalScript == true && boundSession.ExternalHandle != 0)
			{
				try
				{
					var hwnd = (IntPtr)boundSession.ExternalHandle;
					MoveWindow(hwnd, 0, 0, Math.Max(1, width), Math.Max(1, height), true);
					lock (resizeSync)
					{
						lastAppliedViewportSize = new Size(width, height);
					}
				}
				catch { /* best effort */ }

				return Task.CompletedTask;
			}

			return ScheduleResizeInternal(width, height);
		}

		private Task ScheduleResizeInternal(int width, int height)
		{
			if (width <= 0 || height <= 0)
			{
				return Task.CompletedTask;
			}

			var requested = new Size(width, height);
			CancellationTokenSource? nextCts = null;

			lock (resizeSync)
			{
				// Ignore duplicate requests when a resize is already pending for the same dimensions.
				if (AreClose(requested, lastRequestedViewportSize) && resizeThrottleCts != null)
				{
					return Task.CompletedTask;
				}

				lastRequestedViewportSize = requested;

				// Skip scheduling when the docked client is already at this size and nothing is pending.
				if (AreClose(requested, lastAppliedViewportSize) && resizeThrottleCts == null)
				{
					return Task.CompletedTask;
				}

				resizeThrottleCts?.Cancel();
				nextCts = new CancellationTokenSource();
				resizeThrottleCts = nextCts;
			}

			return PerformResizeAsync(width, height, nextCts);
		}

		private async Task PerformResizeAsync(int width, int height, CancellationTokenSource requestCts)
		{
			try
			{
				await Task.Delay(ResizeThrottleInterval, requestCts.Token).ConfigureAwait(false);

				var form = rsForm;
				if (form == null || form.IsDisposed)
				{
					return;
				}

				await form.ResizeWindowOvl(width, height).ConfigureAwait(false);

				lock (resizeSync)
				{
					lastAppliedViewportSize = new Size(width, height);
					if (ReferenceEquals(resizeThrottleCts, requestCts))
					{
						resizeThrottleCts = null;
					}
				}
			}
			catch (OperationCanceledException)
			{
				// A newer resize request superseded this one.
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Orbit] ResizeWindowAsync failed: {ex}");
			}
			finally
			{
				requestCts.Dispose();
			}
		}

		internal Size GetHostViewportSize()
		{
			if (RSPanel != null)
			{
				var renderSize = RSPanel.RenderSize;
				if (renderSize.Width > 0 && renderSize.Height > 0)
				{
					return renderSize;
				}

				var actual = new Size(RSPanel.ActualWidth, RSPanel.ActualHeight);
				if (actual.Width > 0 && actual.Height > 0)
				{
					return actual;
				}
			}

			if (lastMeasuredViewportSize.Width > 0 && lastMeasuredViewportSize.Height > 0)
			{
				return lastMeasuredViewportSize;
			}

			return new Size(ActualWidth, ActualHeight);
		}

		public Task<RSForm> WaitForSessionAsync()
		{
			EnsureSessionLoading();
			return sessionReadyTcs.Task;
		}

		private void EnsureSessionLoading()
		{
			if (hasStarted || loadRequested)
			{
				return;
			}

			// External script windows: embed the provided HWND instead of launching a client.
			if (boundSession?.IsExternalScript == true)
			{
				loadRequested = true;

				_ = Dispatcher.InvokeAsync(() =>
				{
					try
					{
						AttachExternalWindow(boundSession);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[Orbit] Failed to attach external script window: {ex}");
					}
					finally
					{
						hasStarted = true;
						loadRequested = false;
					}
				}, DispatcherPriority.Loaded);

				return;
			}

			loadRequested = true;

			if (!Dispatcher.CheckAccess())
			{
				_ = Dispatcher.InvokeAsync(() =>
				{
					if (!hasStarted)
					{
						_ = LoadNewSession();
					}
				}, DispatcherPriority.Loaded);
			}
			else
			{
				if (!hasStarted)
				{
					_ = LoadNewSession();
				}
			}
		}

		private void AttachExternalWindow(SessionModel session)
		{
			if (session == null || !session.IsExternalScript)
			{
				return;
			}

			var hwnd = (IntPtr)session.ExternalHandle;
			if (hwnd == IntPtr.Zero)
			{
				return;
			}

			if (externalHostPanel == null || externalHostPanel.IsDisposed)
			{
				externalHostPanel = new WF.Panel { Dock = WF.DockStyle.Fill };
			}

			RSPanel.Child = externalHostPanel;

			var previousParent = SetParent(hwnd, externalHostPanel.Handle);
			if (externalOriginalParent == IntPtr.Zero)
			{
				externalOriginalParent = previousParent;
			}

			const int GWL_STYLE = -16;
			const int WS_CHILD = unchecked((int)0x40000000);
			const int WS_VISIBLE = unchecked((int)0x10000000);
			const int WS_CLIPSIBLINGS = 0x04000000;
			const int WS_CLIPCHILDREN = 0x02000000;
			const int WS_POPUP = unchecked((int)0x80000000);

			var curStyle = GetWindowLong(hwnd, GWL_STYLE);
			externalOriginalStyle ??= curStyle;

			var newStyle = (curStyle & ~WS_POPUP) | WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
			SetWindowLong(hwnd, GWL_STYLE, newStyle);
			externalStylesApplied = true;

			var viewport = GetHostViewportSize();
			if (viewport.Width > 0 && viewport.Height > 0)
			{
				_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
			}
		}

		public async Task LoadNewSession()
        {
            // If the session has already started, just return
            if (hasStarted)
            {
				if (rsForm != null)
				{
					sessionReadyTcs.TrySetResult(rsForm);
				}
                return;
            }

            try
            {
                // Initialize the WinForms host on the dispatcher thread.
                await Dispatcher.InvokeAsync(() =>
                {
                    rsForm = new RSForm();
                    rsForm.TopLevel = false;

                    // Add the form to the panel
                    RSPanel.Child = rsForm;

                    // Start loading the form (fire-and-forget; readiness is tracked via ProcessReadyTask).
                    Console.WriteLine("BeginLoad");
                    _ = rsForm.BeginLoad();
                });

                // Wait until the RS process handle is available before signaling session readiness.
                await rsForm.ProcessReadyTask.ConfigureAwait(true);

				// Mark the session as started
				hasStarted = true;
				RestoreSessionFromParking();
				var viewportSnapshot = lastMeasuredViewportSize;
				if (viewportSnapshot.Width <= 0 || viewportSnapshot.Height <= 0)
				{
					viewportSnapshot = GetHostViewportSize();
				}
				if (viewportSnapshot.Width > 0 && viewportSnapshot.Height > 0)
				{
					_ = ResizeWindowAsync((int)Math.Round(viewportSnapshot.Width), (int)Math.Round(viewportSnapshot.Height));
				}
				sessionReadyTcs.TrySetResult(rsForm);
            }
            catch (Exception ex)
            {
                // Log the full exception details
                Console.WriteLine(ex.ToString());
				sessionReadyTcs.TrySetException(ex);
				loadRequested = false;
            }
        }

		public void FocusEmbeddedClient()
		{
			try
			{
				// Give focus to the host then the docked window for reliable keyboard input
				RSPanel?.Focus();
				if (boundSession?.IsExternalScript == true && boundSession.ExternalHandle != 0)
				{
					try { SetFocus((IntPtr)boundSession.ExternalHandle); } catch { /* best effort */ }
					return;
				}
				rsForm?.FocusGameWindow();
			}
			catch { /* best effort only */ }
		}

		private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (rsForm == null)
			{
				return;
			}

			if (e.NewValue is bool isVisible)
			{
				if (isVisible)
				{
					RestoreSessionFromParking();
					var viewport = GetHostViewportSize();
					if (viewport.Width > 0 && viewport.Height > 0)
					{
						lastMeasuredViewportSize = viewport;
						_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
					}
				}
				else
				{
					var viewport = GetHostViewportSize();
					if (viewport.Width > 0 && viewport.Height > 0)
					{
						lastMeasuredViewportSize = viewport;
					}
					ParkSessionIntoOffscreenHost();
				}
			}
		}

		private static bool AreClose(Size left, Size right)
		{
			return AreClose(left.Width, right.Width) && AreClose(left.Height, right.Height);
		}

		private static bool AreClose(double left, double right)
		{
			return Math.Abs(left - right) < 0.5;
		}

		internal void EnsureActiveAfterLayout()
		{
			if (!IsLoaded)
			{
				return;
			}

			RestoreSessionFromParking();

			var viewport = GetHostViewportSize();
			if (viewport.Width > 0 && viewport.Height > 0)
			{
				lastMeasuredViewportSize = viewport;
				_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
			}
		}

		/// <summary>
		/// Gets the window handle that should be used for thumbnail capture.
		/// Returns the docked RS client handle if available, otherwise IntPtr.Zero.
		/// </summary>
		public IntPtr GetCaptureHandle()
		{
			try
			{
				// Priority: RenderSurfaceHandle > DockedClientHandle > RSForm.Handle
				if (rsForm != null)
				{
					var renderSurface = rsForm.GetRenderSurfaceHandle();
					if (renderSurface != IntPtr.Zero)
					{
						return renderSurface;
					}

					var dockedHandle = rsForm.DockedClientHandle;
					if (dockedHandle != IntPtr.Zero)
					{
						return dockedHandle;
					}

					// Fallback to the RSForm's own handle
					if (rsForm.Handle != IntPtr.Zero)
					{
						return rsForm.Handle;
					}
				}

				return IntPtr.Zero;
			}
			catch
			{
				return IntPtr.Zero;
			}
		}

		private void RestoreSessionFromParking()
		{
			if (rsForm == null)
				return;

			try
			{
				parkingHost.Release(rsForm);
				if (RSPanel != null && RSPanel.Child != rsForm)
				{
					RSPanel.Child = rsForm;
				}
			}
			catch
			{
				// If the RSPanel is disposing, we'll reattach on next load.
			}
		}

		private void ParkSessionIntoOffscreenHost()
		{
			if (rsForm == null)
				return;

			try
			{
				if (RSPanel?.Child == rsForm)
				{
					RSPanel.Child = null;
				}
			}
			catch
			{
				// The panel may already be gone; continue parking regardless.
			}

			var snapshot = lastMeasuredViewportSize;
			if (snapshot.Width <= 0 || snapshot.Height <= 0)
			{
				snapshot = lastAppliedViewportSize;
			}
			if (snapshot.Width <= 0 || snapshot.Height <= 0)
			{
				snapshot = new Size(800, 600);
			}

			parkingHost.Park(rsForm, snapshot);
		}

		internal void DetachSession(bool restoreParent = true)
		{
			try
			{
				// Don't block the UI thread during teardown; best-effort detach.
				_ = Dispatcher.InvokeAsync(() =>
				{
					try
					{
						if (RSPanel != null)
						{
							RSPanel.Child = null;
						}
					}
					catch
					{
						// Ignore cleanup errors; we're detaching anyway.
					}
				}, DispatcherPriority.Background);
			}
			catch
			{
				// Dispatcher may already be shutting down; best effort only.
			}

			try
			{
				if (rsForm != null && !rsForm.IsDisposed)
				{
					if (rsForm.InvokeRequired)
					{
						rsForm.BeginInvoke(new Action(() =>
						{
							try { rsForm.Hide(); } catch { }
						}));
					}
					else
					{
						try { rsForm.Hide(); } catch { }
					}
				}
			}
			catch
			{
				// Hide best-effort only.
			}

			try
			{
				if (rsForm != null && !rsForm.IsDisposed)
				{
					// WinForms teardown can be expensive; keep it off the WPF UI thread if possible.
					if (rsForm.InvokeRequired)
					{
						rsForm.BeginInvoke(new Action(() =>
						{
							try
							{
								rsForm.Undock(restoreParent, restoreStyles: restoreParent);
								rsForm.Close();
								rsForm.Dispose();
							}
							catch { }
						}));
					}
					else
					{
						rsForm.Undock(restoreParent, restoreStyles: restoreParent);
						rsForm.Close();
						rsForm.Dispose();
					}
				}
			}
			catch
			{
				// WinForms teardown can fail if the form is already closing; ignore.
			}

			hasStarted = false;
			loadRequested = false;
			rsForm = null;
			parkingHost.Dispose();
			IsVisibleChanged -= OnIsVisibleChanged;
		}

		private sealed class SessionParkingHost : IDisposable
		{
			private readonly WF.Form hostForm;
			private readonly WF.Panel hostPanel;
			private bool disposed;

			public SessionParkingHost()
			{
				hostPanel = new WF.Panel { Dock = WF.DockStyle.Fill };
				hostForm = new WF.Form
				{
					FormBorderStyle = WF.FormBorderStyle.None,
					ShowInTaskbar = false,
					StartPosition = WF.FormStartPosition.Manual,
					Location = new Drawing.Point(-20000, -20000),
					Size = new Drawing.Size(2, 2),
					Opacity = 0.01,
					TopMost = false,
					Text = "SessionParkingHost"
				};
				hostForm.Controls.Add(hostPanel);
				hostForm.Load += (_, _) => hostForm.Hide();
				hostForm.CreateControl();
				hostForm.Show();
				hostForm.Hide();
			}

			public void Park(WF.Control control, Size viewportSize)
			{
				if (disposed || control == null)
					return;

				EnsureFormReady(viewportSize);

				if (control.Parent == hostPanel)
				{
					control.Dock = WF.DockStyle.Fill;
					return;
				}

				if (control.Parent != null)
				{
					control.Parent.Controls.Remove(control);
				}

				hostPanel.Controls.Add(control);
				control.Dock = WF.DockStyle.Fill;
			}

			public void Release(WF.Control control)
			{
				if (disposed || control == null)
					return;

				if (control.Parent == hostPanel)
				{
					hostPanel.Controls.Remove(control);
				}
			}

			private void EnsureFormReady(Size viewportSize)
			{
				if (disposed)
					return;

				var width = Math.Max(1, (int)Math.Ceiling(viewportSize.Width));
				var height = Math.Max(1, (int)Math.Ceiling(viewportSize.Height));

				hostForm.Location = new Drawing.Point(-20000, -20000);
				hostForm.Size = new Drawing.Size(width, height);
				if (!hostForm.Visible)
				{
					hostForm.Show();
				}
				hostForm.Refresh();
			}

			public void Dispose()
			{
				if (disposed)
					return;

				disposed = true;
				try
				{
					hostForm?.Close();
				}
				catch
				{
					// Ignore shutdown issues.
				}
				hostForm?.Dispose();
			}
		}
    }
}
