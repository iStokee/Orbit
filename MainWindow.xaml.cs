using Dragablz;
using MahApps.Metro.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms.Integration;
using Orbit.Views;
using Orbit.Classes;
using Orbit.ViewModels;
using System.Windows.Controls;
using Application = System.Windows.Application;

namespace Orbit
{
	public partial class MainWindow : MetroWindow
	{
		#region Dll Imports and Constants
		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

		// Consts
		private const int HWND_TOP = 0;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_SHOWWINDOW = 0x0040;
		private const uint SWP_NOZORDER = 0x0004;
		private const int WS_CHILD = 0x40000000;
		private const int GWL_STYLE = -16;
		private const int WS_CAPTION = 0x00C00000;

		#endregion


		public ObservableCollection<Session> Sessions { get; set; }
		//public ObservableCollection<CustomTheme> CustomThemes { get; set; }

		bool SessionWindowActive = false;


		// Command for closing tabs
		public ICommand CloseTabCommand { get; }
		public ICommand OpenTabCommand { get; }


		public MainWindow()
		{
			InitializeComponent();
			Sessions = new ObservableCollection<Session>();
			this.DataContext = this;  // Set DataContext to make Sessions available for data binding

			// Initialize the CloseTabCommand
			CloseTabCommand = new RelayCommand(CloseTab);
			OpenTabCommand = new RelayCommand(_ => LoadSession());
			SessionTabControl.ClosingItemCallback += TabControl_ClosingItemHandler;

			// Assign the NewItemFactory
			SessionTabControl.NewItemFactory = CreateNewSession;
		}

		// Event handlers
		#region Event Handlers

		private void SessionTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (SessionTabControl.SelectedItem is Session session && session.HostControl != null)
			{
				EnsureWindowTopMost(session);
			}
		}

		private void ShowSessions_Click(object sender, RoutedEventArgs e)
		{
			if (!SessionWindowActive)
			{
				var sessionsWindow = new SessionsView(Sessions);
				sessionsWindow.Show();
				SessionWindowActive = true;
				sessionsWindow.Closed += (s, e) => SessionWindowActive = false;
			}
			// else if there is an active session window, bring it to the front
			else
			{
				var sessionsWindow = Application.Current.Windows.OfType<SessionsView>().FirstOrDefault();
				sessionsWindow?.Activate();
			}
		}

		private async void AddSession_Click(object sender, RoutedEventArgs e)
		{
			LoadSession();
		}

		private async void LoadSession()
		{
			// Create a new session
			var session = new Session
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now
			};

			// Create the WindowsFormsHost
			var windowsFormsHost = new WindowsFormsHost();
			session.HostControl = windowsFormsHost;

			// Create the RSForm
			session.RSForm = new RSForm();
			session.RSForm.TopLevel = false;

			// Add the RSForm to the WindowsFormsHost
			windowsFormsHost.Child = session.RSForm;

			await session.RSForm.BeginLoad();

			// Add the session to the collection
			Sessions.Add(session);

			// Start the new session logic
			StartNewSession(session);

			// set the tab control to the new session
			SessionTabControl.SelectedItem = session;

			// set the RSProcess property to the process of the RSForm
			session.RSProcess = session.RSForm.pDocked;
		}

		// New method to create a new session synchronously
		private object CreateNewSession()
		{
			// Create a new session
			var session = new Session
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now
			};

			// Create the WindowsFormsHost
			var windowsFormsHost = new WindowsFormsHost();
			session.HostControl = windowsFormsHost;

			// Create the RSForm
			session.RSForm = new RSForm();
			session.RSForm.TopLevel = false;

			// Add the RSForm to the WindowsFormsHost
			windowsFormsHost.Child = session.RSForm;

			// Start the new session logic asynchronously
			session.RSForm.BeginLoad();

			// Any other initialization
			session.ClientLoaded = true;
			session.ClientStatus = "Loaded";

			// set the RSProcess property to the process of the RSForm
			session.RSProcess = session.RSForm.pDocked;

			return session;
		}

		#endregion

		// Command execution for closing a tab
		private void CloseTab(object parameter)
		{
			if (parameter is Session session)
			{
				// Remove the session from the collection
				try 
				{
					session.RSProcess.Kill();
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.Message);
				}

				// if kill successful, remove session
				if (session.RSProcess.HasExited)
				{
					Sessions.Remove(session);
				}
			}
		}

		private void TabControl_ClosingItemHandler(ItemActionCallbackArgs<TabablzControl> args)
		{
			if (System.Windows.MessageBox.Show("Sure", "", MessageBoxButton.YesNo, MessageBoxImage.Stop) == MessageBoxResult.No)
			{
				
				args.Cancel();

			}
			else
			{
				CloseTab(args.DragablzItem.DataContext);
			}
		}

		private void TabControl_NewItemHandler(ItemActionCallbackArgs<TabablzControl> args)
		{
			LoadSession();
		}


		// Adjusted StartNewSession method
		private async void StartNewSession(Session session)
		{
			await session.RSForm.BeginLoad();

			// Any other initialization
			session.ClientLoaded = true;
			session.ClientStatus = "Loaded";
		}

		private void EnsureWindowTopMost(Session session)
		{
			SetWindowPos(session.ExternalHandle, (IntPtr)HWND_TOP, 0, 0, 0, 0,
				SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
		}

		private void OpenThemeManager_Click(object sender, RoutedEventArgs e)
		{
			//var themeManagerWindow = new ThemeManagerView();
			//themeManagerWindow.Show();
		}
	}
}
