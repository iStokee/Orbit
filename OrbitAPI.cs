using System;
using System.Linq;
using System.Windows;
using Orbit.Services;
using Orbit.ViewModels;

namespace Orbit
{
	/// <summary>
	/// Public API for external scripts to integrate with Orbit
	/// </summary>
	public static class OrbitAPI
	{
		private static ScriptIntegrationService _scriptIntegration;

		/// <summary>
		/// Initializes the Orbit API. This is called automatically by Orbit on startup.
		/// </summary>
		internal static void Initialize(ScriptIntegrationService scriptIntegration)
		{
			_scriptIntegration = scriptIntegration ?? throw new ArgumentNullException(nameof(scriptIntegration));
		}

		/// <summary>
		/// Gets the Script Integration service (auto-initialized)
		/// </summary>
		private static ScriptIntegrationService ScriptIntegration
		{
			get
			{
				if (_scriptIntegration == null)
				{
					// Auto-discover from the first MainWindow if not explicitly initialized
					var mainWindow = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
					if (mainWindow?.DataContext is MainWindowViewModel viewModel)
					{
						_scriptIntegration = viewModel.ScriptIntegration;
					}
					else
					{
						throw new InvalidOperationException(
							"OrbitAPI is not initialized. Ensure Orbit is running and your script is loaded.");
					}
				}
				return _scriptIntegration;
			}
		}

		/// <summary>
		/// Registers an external script window to be embedded as a tab in Orbit
		/// </summary>
		/// <param name="windowHandle">The Win32 window handle (HWND) of the script's window</param>
		/// <param name="tabName">Display name for the tab</param>
		/// <param name="processId">Process ID of the script (optional, will be detected if not provided)</param>
		/// <returns>The session ID for the registered script window</returns>
		/// <exception cref="ArgumentException">Thrown if windowHandle is invalid or tabName is empty</exception>
		/// <exception cref="InvalidOperationException">Thrown if Orbit is not running</exception>
		public static Guid RegisterScriptWindow(IntPtr windowHandle, string tabName, int? processId = null)
		{
			return ScriptIntegration.RegisterScriptWindow(windowHandle, tabName, processId);
		}

		/// <summary>
		/// Unregisters a script window from Orbit
		/// </summary>
		/// <param name="sessionId">The session ID returned from RegisterScriptWindow</param>
		/// <returns>True if the session was found and removed, false otherwise</returns>
		public static bool UnregisterScriptWindow(Guid sessionId)
		{
			return ScriptIntegration.UnregisterScriptWindow(sessionId);
		}

		/// <summary>
		/// Checks if Orbit is currently running and the API is available
		/// </summary>
		public static bool IsOrbitAvailable()
		{
			try
			{
				return _scriptIntegration != null || System.Windows.Application.Current?.Windows.OfType<MainWindow>().Any() == true;
			}
			catch
			{
				return false;
			}
		}
	}
}
