using System;
using Dragablz;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

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
		private readonly TaskCompletionSource<RSForm> sessionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ChildClientView()
        {
            InitializeComponent();
            _ = LoadNewSession();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
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
            _ = ResizeWindowAsync((int)Math.Max(0, e.NewSize.Width), (int)Math.Max(0, e.NewSize.Height));
        }

        private void RSPanel_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Forward focus to the embedded game window when clicked
            FocusEmbeddedClient();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
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

		public Task<RSForm> WaitForSessionAsync() => sessionReadyTcs.Task;

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
				sessionReadyTcs.TrySetResult(rsForm);
            }
            catch (Exception ex)
            {
                // Log the full exception details
                Console.WriteLine(ex.ToString());
				sessionReadyTcs.TrySetException(ex);
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
    }
}
