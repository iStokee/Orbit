using Dragablz;
using MahApps.Metro;
using MahApps.Metro.Controls;
using Orbit.Classes;
using Orbit.Models;
using Orbit.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Orbit.ViewModels
{
	public class MainWindowViewModel : INotifyPropertyChanged
	{
		public ObservableCollection<SessionModel> Sessions { get; }
		public SessionModel SelectedSession { get; set; }
		public IInterTabClient InterTabClient { get; }
		public ICommand AddSessionCommand { get; }
		public ICommand ShowSessionsCommand { get; }
		public ICommand OpenThemeManagerCommand { get; }
		public ICommand HWNDTestCommand { get; }

		public ICommand METestCommand { get; }

		//private DispatcherTimer resizeTimer;

		// Constructor
		public MainWindowViewModel()
		{
			Sessions = new ObservableCollection<SessionModel>();
			AddSessionCommand = new RelayCommand(_ => AddSession());
			ShowSessionsCommand = new RelayCommand(_ => ShowSessions());
			OpenThemeManagerCommand = new RelayCommand(_ => OpenThemeManager());
			HWNDTestCommand = new RelayCommand(_ => HWNDTest());
			InterTabClient = new InterTabClient();
			METestCommand = new RelayCommand(_ => METest());

			// set the theme from the saved settings
			var accent = ThemeManager.GetAccent(Settings.Default.Accent);
			var theme = ThemeManager.GetAppTheme(Settings.Default.Theme);
			ThemeManager.ChangeAppStyle(Application.Current, accent, theme);


		}


		// Add Session
		private async void AddSession()
		{
			var session = new SessionModel
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now
			};

			var windowsFormsHost = new ChildClientView();
			session.HostControl = windowsFormsHost;

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
			if (parameter is SessionModel session)
			{
				session.KillProcess();
				Sessions.Remove(session);
			}
		}

		public void TabControl_ClosingItemHandler(ItemActionCallbackArgs<TabablzControl> args)
		{
			if (System.Windows.MessageBox.Show("Are you sure?", "", MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
			{
				args.Cancel();
			}
			else
			{
				CloseTab(args.DragablzItem.DataContext as SessionModel);
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

		private void METest()
		{

		}

		#region INotifyPropertyChanged
		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged(string propertyName)
			 => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		#endregion
	}
}
