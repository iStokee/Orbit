using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using Orbit.Services;
using Orbit.ViewModels;

namespace Orbit.API
{
	/// <summary>
	/// Public API for external scripts to integrate with Orbit
	/// </summary>
	public static class OrbitAPI
	{
		private const string BridgePipeName = "OrbitApiBridge";
		private const int BridgeConnectTimeoutMs = 500;
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
		private static ScriptIntegrationService? TryGetScriptIntegration()
		{
			if (_scriptIntegration != null)
			{
				return _scriptIntegration;
			}

			try
			{
				var mainWindow = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
				if (mainWindow?.DataContext is MainWindowViewModel viewModel)
				{
					_scriptIntegration = viewModel.ScriptIntegration;
					return _scriptIntegration;
				}
			}
			catch
			{
				// Out-of-process callers can still use IPC bridge.
			}

			return null;
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
		public static Guid RegisterScriptWindow(nint windowHandle, string tabName, int? processId = null)
		{
			var integration = TryGetScriptIntegration();
			if (integration != null)
			{
				return integration.RegisterScriptWindow(windowHandle, tabName, processId);
			}

			var response = SendBridgeRequest(new
			{
				action = "register",
				windowHandle = windowHandle.ToInt64().ToString(),
				tabName,
				processId
			});

			if (!response.ok || !Guid.TryParse(response.sessionId, out var sessionId))
			{
				throw new InvalidOperationException(
					$"OrbitAPI registration failed via IPC bridge: {response.message ?? "Unknown error"}");
			}

			return sessionId;
		}

		/// <summary>
		/// Unregisters a script window from Orbit
		/// </summary>
		/// <param name="sessionId">The session ID returned from RegisterScriptWindow</param>
		/// <returns>True if the session was found and removed, false otherwise</returns>
		public static bool UnregisterScriptWindow(Guid sessionId)
		{
			var integration = TryGetScriptIntegration();
			if (integration != null)
			{
				return integration.UnregisterScriptWindow(sessionId);
			}

			var response = SendBridgeRequest(new
			{
				action = "unregister",
				sessionId = sessionId.ToString()
			});

			return response.ok;
		}

		/// <summary>
		/// Checks if Orbit is currently running and the API is available
		/// </summary>
		public static bool IsOrbitAvailable()
		{
			try
			{
				if (_scriptIntegration != null || System.Windows.Application.Current?.Windows.OfType<MainWindow>().Any() == true)
				{
					return true;
				}

				var response = SendBridgeRequest(new { action = "isavailable" });
				return response.ok && response.available == true;
			}
			catch
			{
				return false;
			}
		}

		private static BridgeResponse SendBridgeRequest(object payload)
		{
			using var pipe = new NamedPipeClientStream(".", BridgePipeName, PipeDirection.InOut);
			pipe.Connect(BridgeConnectTimeoutMs);

			using var writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
			using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

			var json = JsonSerializer.Serialize(payload);
			writer.WriteLine(json);
			var responseLine = reader.ReadLine();
			if (string.IsNullOrWhiteSpace(responseLine))
			{
				return new BridgeResponse { ok = false, message = "No IPC response from Orbit." };
			}

			var response = JsonSerializer.Deserialize<BridgeResponse>(responseLine);
			return response ?? new BridgeResponse { ok = false, message = "Invalid IPC response from Orbit." };
		}

		private sealed class BridgeResponse
		{
			public bool ok { get; set; }
			public string? message { get; set; }
			public bool? available { get; set; }
			public string? sessionId { get; set; }
		}
	}
}
