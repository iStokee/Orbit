using System;
using Dragablz;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using Size = System.Windows.Size;
using WF = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Windows.Threading;

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
        #endregion

		internal RSForm rsForm;
        internal bool hasStarted = false;
		private bool loadRequested;
		private readonly TaskCompletionSource<RSForm> sessionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly SessionParkingHost parkingHost = new();
		private Size lastViewportSize = new Size(800, 600);

        public ChildClientView()
        {
            InitializeComponent();
            // FIXED: Don't call LoadNewSession() until visual tree is ready
            // This was causing race conditions with WindowsFormsHost initialization
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void RSPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure initial sizing uses the actual host size, not the whole TabControl
            if (RSPanel?.ActualWidth > 0 && RSPanel?.ActualHeight > 0)
            {
                _ = ResizeWindowAsync((int)RSPanel.ActualWidth, (int)RSPanel.ActualHeight);
            }
        }

        private void RSPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Resize docked client when the WindowsFormsHost layout changes
			lastViewportSize = new Size(Math.Max(1, e.NewSize.Width), Math.Max(1, e.NewSize.Height));
            _ = ResizeWindowAsync((int)Math.Max(0, e.NewSize.Width), (int)Math.Max(0, e.NewSize.Height));
        }

        private void RSPanel_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Forward focus to the embedded game window when clicked
            FocusEmbeddedClient();
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
                    _ = ResizeWindowAsync((int)RSPanel.ActualWidth, (int)RSPanel.ActualHeight);
                }
                else
                {
                    _ = ResizeWindowAsync((int)tabControl.ActualWidth, (int)tabControl.ActualHeight);
                }
            }
        }

		private void OnUnloaded(object sender, RoutedEventArgs e)
		{
			if (Parent is TabItem parentTab && parentTab.Parent is TabablzControl tabControl)
			{
				tabControl.SizeChanged -= OnTabControlSizeChanged;
			}

			ParkSessionIntoOffscreenHost();
		}

        private async void OnTabControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Parent is TabItem parentTab && parentTab.Parent is TabablzControl tabControl)
            {
                // Prefer host size when available
                if (RSPanel?.ActualWidth > 0 && RSPanel?.ActualHeight > 0)
                {
                    await ResizeWindowAsync((int)RSPanel.ActualWidth, (int)RSPanel.ActualHeight);
                }
                else
                {
                    await ResizeWindowAsync((int)tabControl.ActualWidth, (int)tabControl.ActualHeight);
                }
            }
        }

		public async Task ResizeWindowAsync(int width, int height)
		{
			if (rsForm != null)
			{
				await rsForm.ResizeWindowOvl(width, height);
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
						lastViewportSize = viewport;
						_ = ResizeWindowAsync((int)Math.Max(0, viewport.Width), (int)Math.Max(0, viewport.Height));
					}
				}
				else
				{
					var viewport = GetHostViewportSize();
					if (viewport.Width > 0 && viewport.Height > 0)
					{
						lastViewportSize = viewport;
					}
					ParkSessionIntoOffscreenHost();
				}
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

			parkingHost.Park(rsForm, lastViewportSize);
		}

		internal void DetachSession(bool restoreParent = true)
		{
			try
			{
				Dispatcher.Invoke(() =>
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
				});
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
					rsForm.Undock(restoreParent, restoreStyles: restoreParent);
					rsForm.Close();
					rsForm.Dispose();
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
