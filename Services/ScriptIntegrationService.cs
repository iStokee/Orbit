using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Orbit.Classes;
using Orbit.Models;
using Orbit.Views;

namespace Orbit.Services
{
	/// <summary>
	/// Service for integrating external script windows into Orbit's tab ecosystem
	/// </summary>
	public class ScriptIntegrationService
	{
		private readonly SessionCollectionService _sessionCollection;

		public ScriptIntegrationService(SessionCollectionService sessionCollection)
		{
			_sessionCollection = sessionCollection ?? throw new ArgumentNullException(nameof(sessionCollection));
		}

		/// <summary>
		/// Registers an external script window to be embedded as a tab in Orbit
		/// </summary>
		/// <param name="windowHandle">The Win32 window handle (HWND) of the script's window</param>
		/// <param name="tabName">Display name for the tab</param>
		/// <param name="processId">Process ID of the script (optional, will be detected if not provided)</param>
		/// <returns>The session ID for the registered script window</returns>
		public Guid RegisterScriptWindow(IntPtr windowHandle, string tabName, int? processId = null)
		{
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

			// Get process ID if not provided
			int pid = processId ?? GetProcessIdFromWindow(windowHandle);

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
				HostControl = hostControl
			};

			hostControl.DataContext = session;

			// Set initial states using public methods
			session.UpdateState(SessionState.ClientReady);
			session.UpdateInjectionState(InjectionState.NotReady); // Scripts don't need injection

			// Add to session collection (this will trigger UI updates)
			System.Windows.Application.Current.Dispatcher.Invoke(() =>
			{
				_sessionCollection.Sessions.Add(session);
			});

			return session.Id;
		}

		/// <summary>
		/// Unregisters a script window from Orbit
		/// </summary>
		/// <param name="sessionId">The session ID returned from RegisterScriptWindow</param>
		/// <returns>True if the session was found and removed, false otherwise</returns>
		public bool UnregisterScriptWindow(Guid sessionId)
		{
			var session = _sessionCollection.Sessions.FirstOrDefault(s => s.Id == sessionId);
			if (session == null || !session.IsExternalScript)
			{
				return false;
			}

			System.Windows.Application.Current.Dispatcher.Invoke(() =>
			{
				_sessionCollection.Sessions.Remove(session);
			});

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
	}
}
