using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Orbit.Classes;
using Orbit.Models;
using Orbit.Views;
using Orbit.ViewModels;
using Application = System.Windows.Application;

namespace Orbit.Services
{
	/// <summary>
	/// Service for integrating external script windows into Orbit's tab ecosystem
	/// </summary>
	public class ScriptIntegrationService
	{
		private readonly SessionCollectionService _sessionCollection;
		private readonly SessionPlacementService _sessionPlacement;
		private readonly OrbitLayoutStateService _orbitLayoutState;
		internal bool IsShuttingDown { get; set; }

		public ScriptIntegrationService(
			SessionCollectionService sessionCollection,
			SessionPlacementService sessionPlacement,
			OrbitLayoutStateService orbitLayoutState)
		{
			_sessionCollection = sessionCollection ?? throw new ArgumentNullException(nameof(sessionCollection));
			_sessionPlacement = sessionPlacement ?? throw new ArgumentNullException(nameof(sessionPlacement));
			_orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));
		}

		/// <summary>
		/// Registers an external script window to be embedded as a tab in Orbit
		/// </summary>
		/// <param name="windowHandle">The Win32 window handle (HWND) of the script's window</param>
		/// <param name="tabName">Display name for the tab</param>
		/// <param name="processId">Process ID of the script (optional, will be detected if not provided)</param>
		/// <returns>The session ID for the registered script window</returns>
		public Guid RegisterScriptWindow(IntPtr windowHandle, string tabName, int? processId = null)
			=> ExecuteOnUi(() => RegisterScriptWindowOnUi(windowHandle, tabName, processId));

		public Task<Guid> RegisterScriptWindowAsync(IntPtr handle, string name, int? processId, CancellationToken token, DateTimeOffset? deadline = null)
			=> UiRequestDispatcher.RunAsync(Application.Current?.Dispatcher, () => RegisterScriptWindowOnUi(handle, name, processId), token, deadline);

		private Guid RegisterScriptWindowOnUi(IntPtr windowHandle, string tabName, int? processId)
		{
			if (IsShuttingDown) throw new InvalidOperationException("Orbit is shutting down.");
			if (!Orbit.Settings.Default.ScriptWindowEmbeddingEnabled)
			{
				throw new InvalidOperationException("Script window embedding is disabled.");
			}

			if (windowHandle == IntPtr.Zero)
			{
				throw new ArgumentException("Window handle cannot be zero", nameof(windowHandle));
			}

			if (string.IsNullOrWhiteSpace(tabName))
			{
				throw new ArgumentException("Tab name cannot be empty", nameof(tabName));
			}

			// Validate the window handle exists
			if (!Win32.IsWindow(windowHandle))
			{
				throw new ArgumentException("Invalid window handle - window does not exist", nameof(windowHandle));
			}
			if (_sessionCollection.Sessions.Any(s => s.IsRuneScapeClient &&
				(s.ExternalHandle == windowHandle || s.RenderSurfaceHandle == windowHandle || s.RSForm?.DockedClientHandle == windowHandle)))
				throw new InvalidOperationException("A managed game window cannot also be registered as an external script window.");

			// Get process ID if not provided
			int pid = GetProcessIdFromWindow(windowHandle);
			if (pid <= 0 || (processId.HasValue && processId.Value != pid))
				throw new ArgumentException("The supplied process ID does not own the script window.", nameof(processId));

			// Get process for tracking
			Process scriptProcess = null;
			try
			{
				scriptProcess = Process.GetProcessById(pid);
			}
			catch (ArgumentException)
			{
				throw new ArgumentException($"Process with ID {pid} does not exist", nameof(processId));
			}

			// Reuse existing external session when the same HWND is already registered.
			var existingSession = _sessionCollection.Sessions.FirstOrDefault(s =>
				s.IsExternalScript && s.ExternalHandle == (nint)windowHandle);
			if (existingSession != null)
			{
				if (existingSession.ExternalWindowIdentity?.IsCurrent != true)
				{
					UnregisterScriptWindowOnUi(existingSession.Id);
				}
				else
				{
					existingSession.Name = tabName;
					scriptProcess.Dispose();
					return existingSession.Id;
				}
			}

			// Create a host control for the embedded window
			var hostControl = new ChildClientView();

			// Create session model
			var session = new SessionModel
			{
				Id = Guid.NewGuid(),
				Name = tabName,
				CreatedAt = DateTime.UtcNow,
				SessionType = SessionType.ExternalScript,
				ExternalHandle = (nint)windowHandle,
				RSProcess = scriptProcess,
				HostControl = hostControl,
				RequireInjectionBeforeDock = false
			};
			try { session.ExternalWindowIdentity = new HostedWindowIdentity(windowHandle, scriptProcess); }
			catch { scriptProcess.Dispose(); hostControl.DetachSession(); throw; }

			hostControl.DataContext = session;

			// Set initial states using public methods
			session.UpdateState(SessionState.ClientReady);
			session.UpdateInjectionState(InjectionState.NotReady); // Scripts don't need injection

			// Add to session collection (this will trigger UI updates)
			_sessionCollection.Sessions.Add(session);

			// Surface the registered window into the active session presentation.
			// Orbit View no longer auto-synchronizes sessions, so without this the tab could be "registered but invisible".
				try
				{
					var launchBehavior = SessionLaunchCoordinatorService.NormalizeSessionLaunchBehavior(Orbit.Settings.Default.SessionLaunchBehavior);
					var mainVm = Application.Current?.Windows
						.OfType<Orbit.MainWindow>()
						.FirstOrDefault()
						?.DataContext as MainWindowViewModel;

					if (string.Equals(launchBehavior, "OrbitView", StringComparison.Ordinal))
					{
						using (_sessionPlacement.BeginMove(session, SessionPlacementKind.OrbitWorkspace))
						{
							_orbitLayoutState.AddItem(session);
						}
						mainVm?.OpenOrbitViewCommand?.Execute(null);
						mainVm?.ActivateSession(session);
				}
				else // IndividualTabs/Ask/etc
				{
					if (mainVm != null && !mainVm.Tabs.Contains(session))
					{
						_sessionPlacement.SetPlacement(session, SessionPlacementKind.MainTabs);
						mainVm.Tabs.Add(session);
						mainVm.SelectedSession = session;
						mainVm.SelectedTab = session;
					}
				}
			}
			catch
			{
				// Best-effort only; registration should succeed even if the shell cannot be surfaced.
				}

				return session.Id;
			}

		/// <summary>
		/// Unregisters a script window from Orbit
		/// </summary>
		/// <param name="sessionId">The session ID returned from RegisterScriptWindow</param>
		/// <returns>True if the session was found and removed, false otherwise</returns>
		public bool UnregisterScriptWindow(Guid sessionId)
			=> ExecuteOnUi(() => UnregisterScriptWindowOnUi(sessionId));

		public Task<bool> UnregisterScriptWindowAsync(Guid sessionId, CancellationToken token, DateTimeOffset? deadline = null)
			=> UiRequestDispatcher.RunAsync(Application.Current?.Dispatcher, () => UnregisterScriptWindowOnUi(sessionId), token, deadline);

		private bool UnregisterScriptWindowOnUi(Guid sessionId)
		{
			var session = _sessionCollection.Sessions.FirstOrDefault(s => s.Id == sessionId);
			if (session == null || !session.IsExternalScript)
			{
				return false;
			}

			_orbitLayoutState.RemoveItem(session);

			var mainWindows = Application.Current?.Windows
				.OfType<Orbit.MainWindow>()
				.ToList();

			if (mainWindows != null)
			{
				foreach (var window in mainWindows)
				{
					if (window.DataContext is MainWindowViewModel vm)
					{
						vm.Tabs.Remove(session);
					}
				}
			}

			session.HostControl?.DetachSession(restoreParent: true);
			session.ExternalWindowIdentity?.Dispose();
			session.ExternalWindowIdentity = null;
			session.RSProcess?.Dispose();
			session.RSProcess = null;
			session.ExternalHandle = nint.Zero;
			session.UpdateState(SessionState.Closed, clearError: false);
			_sessionPlacement.Remove(session);
			_sessionCollection.Sessions.Remove(session);

			return true;
		}

		/// <summary>
		/// Gets the process ID that owns the specified window
		/// </summary>
			private int GetProcessIdFromWindow(IntPtr hwnd)
			{
				Win32.GetWindowThreadProcessId(hwnd, out uint processId);
				return (int)processId;
			}

			private static T ExecuteOnUi<T>(Func<T> action)
			{
				var dispatcher = Application.Current?.Dispatcher;
				if (dispatcher == null || dispatcher.CheckAccess())
				{
					return action();
				}

				return dispatcher.Invoke(action);
			}
		}
	}
