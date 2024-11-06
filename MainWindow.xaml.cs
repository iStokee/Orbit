using Dragablz;
using MahApps.Metro.Controls;

using Orbit.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;


namespace Orbit
{
	public partial class MainWindow : MetroWindow
	{
		private DispatcherTimer resizeTimer;
		ViewModels.MainWindowViewModel viewModel;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		public MainWindow()
		{
			InitializeComponent();

			viewModel = new MainWindowViewModel();
			this.DataContext = viewModel;
			this.SessionTabControl.InterTabController = new InterTabController();
			this.SessionTabControl.ClosingItemCallback += viewModel.TabControl_ClosingItemHandler;

			//// Forward SizeChanged event to the ViewModel
			this.SizeChanged += MetroWindow_SizeChanged;
			this.viewModel.Sessions.CollectionChanged += (s,e) => ResizeWindows();

			resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
			resizeTimer.Tick += ResizeTimer_Tick;
		}

		// Method to handle window size changes
		public void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		{
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
			int width = (int)SessionTabControl.ActualWidth + 16;
			int height = (int)SessionTabControl.ActualHeight + 40;

			foreach (var session in viewModel.Sessions)
			{
				MoveWindow(session.ExternalHandle, -8, -32, width, height, true);
			}
		}
	}
}
