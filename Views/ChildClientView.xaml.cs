using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
		[DllImport("user32.dll")]
		private static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        #endregion

		internal RSForm rsForm;
        internal bool hasStarted = false;
		private bool loadRequested;
		private bool detached;
		private bool activationPending;
		private bool activationScheduled;
		private bool activationFocusRequested;
		private readonly TaskCompletionSource<RSForm> sessionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> dockingCompleteTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly object externalWindowSync = new();
		private readonly object resizeSync = new();
		private CancellationTokenSource? resizeThrottleCts;
		private Task resizeTask = Task.CompletedTask;
		private Drawing.Size lastRequestedPixelSize;
		private Drawing.Size lastAppliedPixelSize;
		private Size lastMeasuredViewportSize = Size.Empty;
		
		private Size lastAppliedViewportSize = Size.Empty;
			private SessionModel? boundSession;

		// When SessionType=ExternalScript we embed an existing HWND inside a WinForms panel.
		private WF.Panel? externalHostPanel;
		private IntPtr externalOriginalParent = IntPtr.Zero;
		private int? externalOriginalStyle;

		private static readonly TimeSpan ResizeThrottleInterval = TimeSpan.FromMilliseconds(50);
		private const int SW_SHOW = 5;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_FRAMECHANGED = 0x0020;

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
			// Visual events only maintain presentation; the session manager starts clients.
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
            // Collapsed/unloaded hosts must not resize a live game to a 1px viewport.
            if (!IsVisible || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
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
			if (e.NewValue is not SessionModel session) return;
			if (boundSession != null && !ReferenceEquals(boundSession, session))
				throw new InvalidOperationException("A native session host cannot be rebound to a different session.");
			boundSession = session;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			EnsureSessionLoading();
			EnsureActiveAfterLayout();
			if (activationPending) RefreshDockedContentActivation(activationFocusRequested);
		}

		private void CancelPendingResize()
		{
			lock (resizeSync)
			{
				resizeThrottleCts?.Cancel();
				resizeThrottleCts = null;
				lastAppliedPixelSize = Drawing.Size.Empty;
			}
		}

		private void OnUnloaded(object sender, RoutedEventArgs e) => CancelPendingResize();

		private void OnNativeHostSizeChanged(object? sender, EventArgs e)
		{
			if (detached) return;
			var viewport = GetHostViewportSize();
			_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
		}

		/// <summary>Requests a layout resize; dimensions are WPF units, native bounds come from the host.</summary>
		public Task ResizeWindowAsync(int width, int height)
		{
			if (!Dispatcher.CheckAccess())
				return Dispatcher.InvokeAsync(() => ResizeWindowAsync(width, height)).Task.Unwrap();
			if (detached || width <= 0 || height <= 0 || !IsLoaded || !IsVisible) return Task.CompletedTask;
			var external = boundSession?.IsExternalScript == true;
			var parent = externalHostPanel?.IsHandleCreated == true ? externalHostPanel.Handle : IntPtr.Zero;
			var pixels = external ? Orbit.Services.NativeWindowGeometry.ClientSize(parent) : rsForm?.DockHostPixelSize ?? Drawing.Size.Empty;
			if (pixels.Width <= 0 || pixels.Height <= 0) return Task.CompletedTask;
			lock (resizeSync)
			{
				if (resizeThrottleCts != null && !resizeThrottleCts.IsCancellationRequested && pixels == lastRequestedPixelSize) return resizeTask;
				if (resizeThrottleCts == null && pixels == lastAppliedPixelSize) return Task.CompletedTask;
				resizeThrottleCts?.Cancel();
				var request = new CancellationTokenSource();
				resizeThrottleCts = request;
				lastRequestedPixelSize = pixels;
				resizeTask = PerformResizeAsync(new Size(width, height), pixels, parent, boundSession, rsForm, request);
				return resizeTask;
			}
		}

		private async Task PerformResizeAsync(Size logicalSize, Drawing.Size pixels, IntPtr parent, SessionModel? session, RSForm? form, CancellationTokenSource request)
		{
			try
			{
				await Task.Delay(ResizeThrottleInterval, request.Token).ConfigureAwait(false);
				bool applied;
				if (session?.IsExternalScript == true)
				{
					bool ResizeExternal()
					{
						lock (externalWindowSync)
						{
							request.Token.ThrowIfCancellationRequested();
							if (session.ExternalWindowIdentity?.IsCurrent != true) return false;
							return Orbit.Services.NativeWindowGeometry.FitToParent(session.ExternalHandle, parent);
						}
					}
					applied = await Dispatcher.InvokeAsync(() =>
						Orbit.Services.NativeWindowGeometry.IsCurrentThreadWindow(session.ExternalHandle)
							? Task.FromResult(ResizeExternal()) : Task.Run(ResizeExternal, request.Token),
						DispatcherPriority.Render, request.Token).Task.Unwrap().ConfigureAwait(false);
				}
				else
				{
					if (form == null) return;
					applied = await Dispatcher.InvokeAsync(() => form.ResizeToHostAsync(request.Token),
						DispatcherPriority.Render, request.Token).Task.Unwrap().ConfigureAwait(false);
				}
				lock (resizeSync)
				{
					if (applied && !request.IsCancellationRequested && !detached)
					{
						lastAppliedViewportSize = logicalSize;
						lastAppliedPixelSize = pixels;
					}
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex) { Debug.WriteLine($"[Orbit] Resize failed: {ex}"); }
			finally
			{
				lock (resizeSync)
				{
					if (ReferenceEquals(resizeThrottleCts, request)) resizeThrottleCts = null;
					request.Dispose();
				}
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

		internal Task WaitForDockAsync() => dockingCompleteTcs.Task;

		private void EnsureSessionLoading()
		{
			if (detached || hasStarted || loadRequested)
			{
				return;
			}

			// External script windows: embed the provided HWND instead of launching a client.
			if (boundSession?.IsExternalScript == true)
			{
				var externalSession = boundSession;
				loadRequested = true;

				_ = Dispatcher.InvokeAsync(() =>
				{
					try
					{
						if (detached) return;
						AttachExternalWindow(externalSession);
						hasStarted = true;
					}
					catch (Exception ex)
					{
						externalSession.Fail(ex);
						Console.WriteLine($"[Orbit] Failed to attach external script window: {ex}");
					}
					finally
					{
						loadRequested = false;
					}
				}, DispatcherPriority.Loaded);

				return;
			}

			// Client startup is explicitly owned by SessionManagerService.
		}

		internal void AttachExternalWindow(SessionModel session)
		{
			if (session == null || !session.IsExternalScript)
			{
				return;
			}
			if (session.ExternalWindowIdentity?.IsCurrent != true)
				throw new InvalidOperationException("The registered script window has been destroyed or replaced.");

			var hwnd = (IntPtr)session.ExternalHandle;
			if (hwnd == IntPtr.Zero)
			{
				Console.WriteLine($"[OrbitAPI] External script session '{session.Name}' has no HWND to attach.");
				return;
			}

			if (externalHostPanel == null || externalHostPanel.IsDisposed)
			{
				externalHostPanel = new WF.Panel { Dock = WF.DockStyle.Fill };
				externalHostPanel.ClientSizeChanged += OnNativeHostSizeChanged;
			}

			RSPanel.Child = externalHostPanel;
			var hostHandle = externalHostPanel.Handle;
			Console.WriteLine($"[OrbitAPI] Attaching script window '{session.Name}' HWND=0x{hwnd.ToInt64():X} to host HWND=0x{hostHandle.ToInt64():X}.");

			const int GWL_STYLE = -16;
			const int WS_CHILD = unchecked((int)0x40000000);
			const int WS_VISIBLE = unchecked((int)0x10000000);
			const int WS_CLIPSIBLINGS = 0x04000000;
			const int WS_CLIPCHILDREN = 0x02000000;
			const int WS_POPUP = unchecked((int)0x80000000);

			var curStyle = GetWindowLong(hwnd, GWL_STYLE);
			if (!externalOriginalStyle.HasValue) externalOriginalParent = GetParent(hwnd);
			externalOriginalStyle ??= curStyle;

			var newStyle = (curStyle & ~WS_POPUP) | WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
			SetWindowLong(hwnd, GWL_STYLE, newStyle);
			Marshal.SetLastPInvokeError(0);
			var previousParent = SetParent(hwnd, hostHandle);
			var parentError = Marshal.GetLastPInvokeError();
			if (previousParent == IntPtr.Zero && parentError != 0)
			{
				SetWindowLong(hwnd, GWL_STYLE, externalOriginalStyle.Value);
				throw new Win32Exception(parentError, "Unable to embed the script window.");
			}
			SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
			ShowWindow(hwnd, SW_SHOW);

			var viewport = GetHostViewportSize();
			if (viewport.Width > 0 && viewport.Height > 0)
			{
				_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
			}
			else
			{
				_ = ResizeWindowAsync(800, 600);
			}

			Console.WriteLine($"[OrbitAPI] Script window '{session.Name}' attached.");
		}

		private void DetachExternalWindow(bool restoreParent)
		{
			var session = boundSession;
			if (session?.IsExternalScript != true || session.ExternalHandle == 0)
			{
				return;
			}

			var hwnd = (IntPtr)session.ExternalHandle;
			if (restoreParent && session.ExternalWindowIdentity?.IsCurrent == true && externalOriginalStyle.HasValue)
			{
				lock (externalWindowSync)
				Orbit.Services.NativeWindowRestorer.Restore(hwnd, externalOriginalParent, externalOriginalStyle.Value);
			}
			try
			{
				if (RSPanel != null)
				{
					RSPanel.Child = null;
				}
			}
			catch { }

			try
			{
				if (externalHostPanel != null) externalHostPanel.ClientSizeChanged -= OnNativeHostSizeChanged;
				externalHostPanel?.Dispose();
			}
			catch { }

			externalHostPanel = null;
			externalOriginalParent = IntPtr.Zero;
			externalOriginalStyle = null;
			hasStarted = false;
			loadRequested = false;
		}

		internal void AttachSessionHost(RSForm form)
		{
			Dispatcher.VerifyAccess();
			if (detached || rsForm != null) throw new InvalidOperationException("Session host cannot be attached twice.");
			rsForm = form;
			form.DockHostSizeChanged += OnNativeHostSizeChanged;
			form.TopLevel = false;
			RSPanel.Child = form;
			form.EnsureDockHostHandle();
			loadRequested = true;
			_ = ObserveWindowReadyAsync(form);
		}

		internal async Task ObserveStartupAsync(Task startup)
		{
			try
			{
				await startup.ConfigureAwait(true);
				if (detached) return;
				hasStarted = true;
				EnsureHostAttached();
				var viewport = GetHostViewportSize();
				if (viewport.Width <= 0 || viewport.Height <= 0) viewport = new Size(800, 600);
				_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
				sessionReadyTcs.TrySetResult(rsForm);
				dockingCompleteTcs.TrySetResult(true);
			}
			catch (Exception ex)
			{
				sessionReadyTcs.TrySetException(ex);
				dockingCompleteTcs.TrySetException(ex);
				loadRequested = false;
			}
		}
		internal void TrackSessionProcess(RSForm form, SessionModel session)
			=> Orbit.Services.SessionManagerService.TrackSessionProcess(form, session, Dispatcher);
		private async Task ObserveWindowReadyAsync(RSForm form)
		{
			try
			{
				await form.WindowReadyTask.ConfigureAwait(true);
				if (!detached) sessionReadyTcs.TrySetResult(form);
			}
			catch (Exception ex) { sessionReadyTcs.TrySetException(ex); }
		}

		public void FocusEmbeddedClient()
		{
			if (detached || !IsLoaded || !IsVisible || Window.GetWindow(this)?.IsActive != true) return;
			try
			{
				// Give focus to the host then the docked window for reliable keyboard input
				RSPanel?.Focus();
				if (boundSession?.IsExternalScript == true && boundSession.ExternalHandle != 0)
				{
					if (boundSession.ExternalWindowIdentity?.IsCurrent != true) return;
					if (externalHostPanel?.IsHandleCreated != true || GetParent(boundSession.ExternalHandle) != externalHostPanel.Handle) return;
					try { SetFocus((IntPtr)boundSession.ExternalHandle); } catch { /* best effort */ }
					return;
				}
				rsForm?.FocusGameWindow();
			}
			catch { /* best effort only */ }
		}

		private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (e.NewValue is true) EnsureActiveAfterLayout();
			else CancelPendingResize();
		}

		private static bool AreClose(Size left, Size right)
		{
			return AreClose(left.Width, right.Width) && AreClose(left.Height, right.Height);
		}

		private static bool AreClose(double left, double right)
		{
			return Math.Abs(left - right) < 0.01;
		}

		internal void EnsureActiveAfterLayout()
		{
			if (detached) return;
			EnsureSessionLoading();

			if (!IsLoaded)
			{
				_ = Dispatcher.BeginInvoke(new Action(() =>
				{
					if (IsLoaded)
					{
						EnsureActiveAfterLayout();
					}
				}), DispatcherPriority.Loaded);
				return;
			}

			EnsureHostAttached();

			var viewport = GetHostViewportSize();
			if (viewport.Width > 0 && viewport.Height > 0)
			{
				lastMeasuredViewportSize = viewport;
				_ = ResizeWindowAsync((int)Math.Round(viewport.Width), (int)Math.Round(viewport.Height));
			}
		}

		internal void RefreshDockedContentActivation(bool requestFocus)
		{
			if (!Dispatcher.CheckAccess())
			{
				_ = Dispatcher.BeginInvoke(new Action(() => RefreshDockedContentActivation(requestFocus)), DispatcherPriority.Loaded);
				return;
			}
			if (detached) return;
			activationPending = true;
			activationFocusRequested |= requestFocus;
			// Loaded/visibility events resume this request. Never poll an unloaded visual.
			if (!IsLoaded || !IsVisible || activationScheduled) return;
			activationScheduled = true;
			_ = Dispatcher.BeginInvoke(new Action(() =>
			{
				activationScheduled = false;
				if (detached || !IsLoaded || !IsVisible) return;
				var focus = activationFocusRequested;
				activationPending = false;
				activationFocusRequested = false;
				EnsureActiveAfterLayout();
				RSPanel?.InvalidateMeasure();
				RSPanel?.InvalidateArrange();
				// WPF completes the invalidated layout normally; no synchronous UpdateLayout.
				if (focus && Window.GetWindow(this)?.IsActive == true) FocusEmbeddedClient();
			}), DispatcherPriority.Render);
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

		private void EnsureHostAttached()
		{
			if (rsForm != null && !rsForm.IsDisposed && RSPanel.Child != rsForm) RSPanel.Child = rsForm;
		}

		internal void DetachSession(bool restoreParent = true)
		{
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.Invoke(() => DetachSession(restoreParent));
				return;
			}
			if (detached) return;
			CancelPendingResize();
			sessionReadyTcs.TrySetCanceled();
			dockingCompleteTcs.TrySetCanceled();
			var form = rsForm;
			if (boundSession?.IsExternalScript == true) DetachExternalWindow(restoreParent);
			else if (form != null && !form.IsDisposed) form.Undock(restoreParent, restoreStyles: restoreParent);
			lock (resizeSync)
			{
				detached = true;
				resizeThrottleCts?.Cancel();
			}
			activationPending = false;
			activationFocusRequested = false;
			Loaded -= OnLoaded;
			Unloaded -= OnUnloaded;
			IsVisibleChanged -= OnIsVisibleChanged;
			DataContextChanged -= OnDataContextChanged;
			if (boundSession?.IsExternalScript == true)
			{
				boundSession = null;
				return;
			}

			// Complete restoration before disposing the host or stopping the dispatcher.
			if (form != null && !form.IsDisposed)
			{
				form.DockHostSizeChanged -= OnNativeHostSizeChanged;
				RSPanel.Child = null;
				form.Close();
				form.Dispose();
			}
			hasStarted = false;
			loadRequested = false;
			rsForm = null;
			IsVisibleChanged -= OnIsVisibleChanged;
		}

    }
}
