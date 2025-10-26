using Orbit.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Orbit.ViewModels
{
	public class SessionsOverviewViewModel : INotifyPropertyChanged
	{
		private readonly Action<SessionModel> activateSession;
		private readonly Action<SessionModel> focusSession;
		private readonly Action<SessionModel> closeSession;
		private SessionModel selectedSession;

		public SessionsOverviewViewModel(ObservableCollection<SessionModel> sessions,
			Action<SessionModel> activateSession,
			Action<SessionModel> focusSession,
			Action<SessionModel> closeSession)
		{
			Sessions = sessions;
			this.activateSession = activateSession;
			this.focusSession = focusSession;
			this.closeSession = closeSession;

			SetActiveCommand = new RelayCommand(_ => activateSession?.Invoke(SelectedSession), _ => SelectedSession != null);
			FocusCommand = new RelayCommand(_ => focusSession?.Invoke(SelectedSession), _ => SelectedSession != null);
			CloseCommand = new RelayCommand(_ => closeSession?.Invoke(SelectedSession), _ => SelectedSession != null);
		}

		public ObservableCollection<SessionModel> Sessions { get; }

		public SessionModel SelectedSession
		{
			get => selectedSession;
			set
			{
				if (selectedSession == value)
					return;
				selectedSession = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public ICommand SetActiveCommand { get; }
		public ICommand FocusCommand { get; }
		public ICommand CloseCommand { get; }

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
