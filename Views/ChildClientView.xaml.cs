using Orbit.Classes;
using System;
using Dragablz;
using System.Collections.Generic;
using System.Diagnostics;
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
        //internal RSForm_Deam rsForm_D;

        internal List<RSClient> rsHandlerList = new List<RSClient>();
        internal bool hasStarted = false;
        internal RSClient client;

        public ChildClientView()
        {
            InitializeComponent();
            LoadNewSession();
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			if (Parent is TabItem parentTab && parentTab.Parent is TabablzControl tabControl)
			{
				tabControl.SizeChanged += OnTabControlSizeChanged;
				ResizeWindowAsync((int)tabControl.ActualWidth, (int)tabControl.ActualHeight);
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
				await ResizeWindowAsync((int)tabControl.ActualWidth, (int)tabControl.ActualHeight);
			}
		}

		public async Task ResizeWindowAsync(int width, int height)
		{
			if (rsForm != null)
			{
				await rsForm.ResizeWindowOvl(width, height);
			}
		}

		public async Task LoadNewSession()
        {
            // If the session has already started, just return
            if (hasStarted)
            {
                return;
            }

            try
            {
                // Use InvokeAsync to run this asynchronously
                await Dispatcher.InvokeAsync(async () =>
                {
                    client = new RSClient();
                    rsForm = new RSForm();
                    rsForm.TopLevel = false;

                    // Add the form to the panel
                    RSPanel.Child = rsForm;

                    // Start loading the form
                    Console.WriteLine("BeginLoad");
                    await rsForm.BeginLoad();

                    // Link the process
                    client.rs2Process = rsForm.pDocked;

                    // Add to the handler list
                    rsHandlerList.Add(client);
                });

                // Mark the session as started
                hasStarted = true;
            }
            catch (Exception ex)
            {
                // Log the full exception details
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
