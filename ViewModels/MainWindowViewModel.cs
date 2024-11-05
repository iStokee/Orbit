using Dragablz;
using MahApps.Metro.Controls;
using Orbit.Classes;
using Orbit.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Orbit.ViewModels
{
	public class MainWindowViewModel : INotifyPropertyChanged
	{
		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		public ObservableCollection<Session> Sessions { get; }
		public Session SelectedSession { get; set; }
		public IInterTabClient InterTabClient { get; }

		public ICommand AddSessionCommand { get; }
		public ICommand CloseTabCommand { get; }
		public ICommand ShowSessionsCommand { get; }
		public ICommand OpenThemeManagerCommand { get; }
		public ICommand HWNDTestCommand { get; }

		//private DispatcherTimer resizeTimer;

		// Constructor
		public MainWindowViewModel()
		{
			Sessions = new ObservableCollection<Session>();
			AddSessionCommand = new RelayCommand(_ => AddSession());
			CloseTabCommand = new RelayCommand(CloseTab);
			ShowSessionsCommand = new RelayCommand(_ => ShowSessions());
			OpenThemeManagerCommand = new RelayCommand(_ => OpenThemeManager());
			HWNDTestCommand = new RelayCommand(_ => HWNDTest());
			InterTabClient = new InterTabClient();

			//resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
			//resizeTimer.Tick += ResizeTimer_Tick;
		}

		// Add Session
		private async void AddSession()
		{
			var session = new Session
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now
			};

			var windowsFormsHost = new ChildClientView();
			session.HostControl = windowsFormsHost;

			// Start the new session logic
			//windowsFormsHost.LoadNewSession();
			//windowsFormsHost.LoadNewSession();

			await Task.Delay(2000);

			// Assign the RSProcess property to the process of the RSForm
			session.RSProcess = windowsFormsHost.rsForm.pDocked;
			session.ExternalHandle = (int)windowsFormsHost.rsForm.DockedRSHwnd;
			session.RSForm = windowsFormsHost.rsForm;

			// Mark host start completion
			windowsFormsHost.hasStarted = true;

			session.ClientLoaded = true;
			session.ClientStatus = "Loaded";

			Sessions.Add(session);
			SelectedSession = session;

			OnPropertyChanged(nameof(SelectedSession));
		}

		// Close Tab
		private void CloseTab(object parameter)
		{
			if (parameter is Session session)
			{
				session.KillProcess();
				Sessions.Remove(session);
			}
		}

		// Show Sessions
		private void ShowSessions()
		{
			var sessionsWindow = new SessionsView(Sessions);
			sessionsWindow.Show();
		}

		// Open Theme Manager
		private void OpenThemeManager()
		{
			var themeManagerWindow = new ThemeManagerView();
			themeManagerWindow.Show();
		}

		// HWND Test
		private void HWNDTest()
		{
			var manipulatorView = new WindowManipulatorView
			{
				DataContext = new WindowManipulatorViewModel()
			};
			manipulatorView.Show();
		}



		//public void UpdateSessionTabDimensions(double width, double height)
		//{
		//	if (Sessions.Count == 0) return;

		//	int adjustedWidth = (int)width + 16;
		//	int adjustedHeight = (int)height + 40;

		//	foreach (var session in Sessions)
		//	{
		//		MoveWindow(session.ExternalHandle, -8, -32, adjustedWidth, adjustedHeight, true);
		//	}
		//}

		//// Method to handle window size changes
		//public void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
		//{
		//	if (Sessions.Count == 0) return;
		//	resizeTimer.Stop();
		//	resizeTimer.Start();
		//}


		// Timer tick method to handle resize logic
		//private void ResizeTimer_Tick(object sender, EventArgs e)
		//{
		//	// If the left mouse button is pressed, don't proceed with resize
		//	if (System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed) return;

		//	resizeTimer.Stop();

		//	foreach (var session in Sessions)
		//	{
		//		int width = (int)SessionTabControl.ActualWidth + 16;
		//		int height = (int)SessionTabControl.ActualHeight + 40;

		//		MoveWindow(session.ExternalHandle, -8, -32, width, height, true);
		//	}
		//}

		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged(string propertyName)
			 => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
