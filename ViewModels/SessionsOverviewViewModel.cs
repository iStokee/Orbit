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

			SetActiveCommand = new RelayCommand<SessionModel?>(session =>
			{
				if (TryResolveSession(session, out var resolved))
				{
					activateSession?.Invoke(resolved);
				}
			}, session => session is SessionModel);

			FocusCommand = new RelayCommand<SessionModel?>(session =>
			{
				if (TryResolveSession(session, out var resolved))
				{
					focusSession?.Invoke(resolved);
				}
			}, session => session is SessionModel);

			CloseCommand = new RelayCommand<SessionModel?>(session =>
			{
				if (TryResolveSession(session, out var resolved))
				{
					closeSession?.Invoke(resolved);
				}
			}, session => session is SessionModel);
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

		public IRelayCommand<SessionModel?> SetActiveCommand { get; }
		public IRelayCommand<SessionModel?> FocusCommand { get; }
		public IRelayCommand<SessionModel?> CloseCommand { get; }

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		private bool TryResolveSession(SessionModel? parameter, out SessionModel session)
		{
			if (parameter is SessionModel model)
			{
				SelectedSession = model;
				session = model;
				return true;
			}

			session = null;
			return false;
		}
	}
}
