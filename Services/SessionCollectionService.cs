using Orbit.Models;
using System;
using System.Collections.ObjectModel;

namespace Orbit.Services
{
	/// <summary>
	/// Singleton service that manages a shared collection of sessions across all MainWindow instances.
	/// This ensures that tear-off windows can access the same session data for script loading.
	/// </summary>
	public sealed class SessionCollectionService : ObservableObject
	{
		private static readonly Lazy<SessionCollectionService> _lazy = new(() => new SessionCollectionService());

		private readonly ObservableCollection<SessionModel> _sessions = new();
		private SessionModel _globalSelectedSession;
		private SessionModel _globalHotReloadTargetSession;

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
			set => SetProperty(ref _globalSelectedSession, value);
		}

		/// <summary>
		/// Shared hot-reload target session. Script tooling can bind to this to keep the load target consistent.
		/// </summary>
		public SessionModel GlobalHotReloadTargetSession
		{
			get => _globalHotReloadTargetSession;
			set => SetProperty(ref _globalHotReloadTargetSession, value);
		}
	}
}
