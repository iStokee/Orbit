using Orbit.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Orbit.Services
{
	/// <summary>
	/// Singleton service that manages a shared collection of sessions across all MainWindow instances.
	/// This ensures that tear-off windows can access the same session data for script loading.
	/// </summary>
	public sealed class SessionCollectionService : INotifyPropertyChanged
	{
		private static readonly Lazy<SessionCollectionService> _lazy = new(() => new SessionCollectionService());

		private readonly ObservableCollection<SessionModel> _sessions = new();
		private SessionModel _globalSelectedSession;

		private SessionCollectionService()
		{
		}

		public static SessionCollectionService Instance => _lazy.Value;

		public ObservableCollection<SessionModel> Sessions => _sessions;

		/// <summary>
		/// The globally selected session across all windows. This allows tear-off windows
		/// (like Script Controls) to target the correct session for script operations.
		/// </summary>
		public SessionModel GlobalSelectedSession
		{
			get => _globalSelectedSession;
			set
			{
				if (_globalSelectedSession != value)
				{
					_globalSelectedSession = value;
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlobalSelectedSession)));
				}
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}
}
