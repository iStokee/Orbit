using Orbit.Models;
using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

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
			_sessions.CollectionChanged += OnSessionsCollectionChanged;
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

		private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.OldItems != null)
			{
				foreach (SessionModel session in e.OldItems)
				{
					session.PropertyChanged -= OnSessionPropertyChanged;
					LogSessionCollection("removed", session);
					if (ReferenceEquals(GlobalSelectedSession, session))
					{
						GlobalSelectedSession = null;
					}

					if (ReferenceEquals(GlobalHotReloadTargetSession, session))
					{
						GlobalHotReloadTargetSession = null;
					}
				}
			}

			if (e.NewItems != null)
			{
				foreach (SessionModel session in e.NewItems)
				{
					session.PropertyChanged += OnSessionPropertyChanged;
					LogSessionCollection("added", session);
				}
			}

			ValidateSessionIdentity();
		}

		private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(SessionModel.RSProcess) or nameof(SessionModel.State) or nameof(SessionModel.ExternalHandle))
			{
				ValidateSessionIdentity();
			}
		}

		private void ValidateSessionIdentity()
		{
			var liveProcessGroups = Sessions
				.Select(session => (session, pid: TryGetLiveProcessId(session)))
				.Where(entry => entry.pid.HasValue)
				.GroupBy(entry => entry.pid!.Value)
				.Where(group => group.Count() > 1)
				.ToList();

			foreach (var group in liveProcessGroups)
			{
				var names = string.Join(", ", group.Select(entry => $"{entry.session.Name ?? entry.session.Id.ToString()}[{entry.session.Id:N}]"));
				Console.WriteLine($"[Orbit][SessionCollection][Warning] PID {group.Key} is attached to multiple live sessions: {names}");
			}
		}

		/// <summary>
		/// Safely resolves the PID of a session whose process is live. A stale, disposed, or
		/// foreign <see cref="System.Diagnostics.Process"/> handle throws on <c>HasExited</c>/
		/// <c>Id</c> (the state a ghost/duplicated session ends up in); such sessions are not
		/// identity-bearing and must never crash collection-change validation.
		/// </summary>
		private static int? TryGetLiveProcessId(SessionModel session)
		{
			var process = session.RSProcess;
			if (process == null || session.State == SessionState.Closed)
			{
				return null;
			}

			try
			{
				return process.HasExited ? null : process.Id;
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Renders a session's PID for logging without throwing. <c>RSProcess?.Id</c> null-guards
		/// the handle but not the stale/unstarted-handle exception, so logging on add/remove must
		/// not be the thing that crashes the app.
		/// </summary>
		private static string DescribeProcessId(SessionModel session)
		{
			try
			{
				return session.RSProcess?.Id.ToString() ?? "n/a";
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return "n/a";
			}
		}

		private static void LogSessionCollection(string action, SessionModel session)
		{
			var pid = DescribeProcessId(session);
			var handle = session.ExternalHandle == nint.Zero ? "n/a" : $"0x{session.ExternalHandle:X}";
			Console.WriteLine($"[Orbit][SessionCollection] {action} session='{session.Name ?? session.Id.ToString()}' id={session.Id:N} type={session.SessionType} state={session.State} injection={session.InjectionState} pid={pid} hwnd={handle}");
		}
	}
}
