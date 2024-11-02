using Orbit.Classes;
using Orbit.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms.Integration;
using System.Windows.Input;

namespace Orbit.ViewModels
{
	public class MainWindowViewModel : INotifyPropertyChanged
	{
		public ObservableCollection<Session> Sessions { get; }
		public ICommand AddSessionCommand { get; }
		public ICommand CloseTabCommand { get; }



		// Constructor
		public MainWindowViewModel()
		{
			Sessions = new ObservableCollection<Session>();
			AddSessionCommand = new RelayCommand(async _ => await AddSession());
			CloseTabCommand = new RelayCommand(CloseTab);
		}

		// Add Session
		private async Task AddSession()
		{
			var session = new Session
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now
			};

			var windowsFormsHost = new ChildClientView();
			session.HostControl = windowsFormsHost;

			// Pass the session to RSForm to store the process
			await session.HostControl.LoadNewSession();

			Sessions.Add(session);

			await StartNewSession(session);
		}

		private async Task StartNewSession(Session session)
		{
			//if (session.RSForm == null)
			//{
			//	await session.RSForm.BeginLoad();
			//	await Task.Delay(5000);
			//}
			//await Task.Delay(500);

			session.ClientLoaded = true;
			session.ClientStatus = "Loaded";
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

		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged(string propertyName)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
