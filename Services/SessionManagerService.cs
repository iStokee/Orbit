using Orbit.Models;
using Orbit.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Orbit.Services
{
	internal class SessionManagerService
	{
		private const string DefaultInjectorDll = "XInput1_4_inject.dll";

        public async Task InitializeSessionAsync(SessionModel session)
        {
            if (session.HostControl is not ChildClientView clientView)
                throw new InvalidOperationException("Session host control is missing or invalid.");

			try
			{
				var rsForm = await clientView.WaitForSessionAsync();
				session.RSForm = rsForm;
				session.RSProcess = rsForm?.pDocked ?? throw new InvalidOperationException("RuneScape process handle is unavailable.");
				session.ExternalHandle = rsForm.DockedRSHwnd;
				if (session.RSProcess != null)
				{
					Console.WriteLine($"[Orbit] Session '{session.Name}' attached to PID {session.RSProcess.Id} (MainWindow 0x{session.RSProcess.MainWindowHandle.ToInt64():X}) DockedHWND=0x{session.ExternalHandle:X}");
				}
                session.UpdateState(SessionState.ClientReady);
                session.UpdateInjectionState(InjectionState.Ready);

				// Give input focus to the embedded client once ready
				clientView.FocusEmbeddedClient();
            }
            catch (Exception ex)
            {
                session.Fail(ex);
                throw;
            }
        }

		public async Task InjectAsync(SessionModel session)
		{
			var process = session.RSProcess ?? throw new InvalidOperationException("Cannot inject before the RuneScape client is ready.");

			string dllPath;
			try
			{
				dllPath = ResolveInjectorPath();
			}
			catch (Exception ex)
			{
				session.RecordInjectionFailure(ex);
				throw;
			}

			session.UpdateInjectionState(InjectionState.Injecting);
			session.UpdateState(SessionState.Injecting);

            try
            {
                Console.WriteLine($"[Orbit] Injecting '{dllPath}' into PID {process.Id}...");
                var injected = await Task.Run(() => Orbit.ME.DllInjector.Inject(process.Id, dllPath));
                Console.WriteLine(injected
                    ? "[Orbit] Injection completed successfully."
                    : "[Orbit] Injection call returned false.");
                session.UpdateInjectionState(InjectionState.Injected);
                session.UpdateState(SessionState.Injected);

                // Give the injected DLL a moment to initialize
                await Task.Delay(500);

                // Restore focus to the game window so input works
                Console.WriteLine($"[Orbit] Restoring focus to game window (0x{session.ExternalHandle:X})");
                try { session.HostControl?.FocusEmbeddedClient(); } catch { }
            }
            catch (Exception ex)
            {
                session.RecordInjectionFailure(ex);
                throw;
            }
        }

		private static string ResolveInjectorPath()
		{
			var configuredPath = Environment.GetEnvironmentVariable("ORBIT_INJECTOR_PATH");
			if (!string.IsNullOrWhiteSpace(configuredPath))
			{
				var expandedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
				if (File.Exists(expandedPath))
					return expandedPath;

				throw new FileNotFoundException($"Injector DLL not found at configured path '{expandedPath}'.", expandedPath);
			}

			var baseDirectory = AppContext.BaseDirectory;
			var probeRoots = new[]
			{
				".",
				"..",
				"../..",
				"../../..",
				"../../../..",
				"../../../../ME/x64/Build_DLL",
				"../../../../ME/MemoryError/x64/Build_DLL"
			};

			var attempted = new List<string>();
			var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var root in probeRoots)
			{
				var candidate = Path.GetFullPath(Path.Combine(baseDirectory, root, DefaultInjectorDll));
				if (File.Exists(candidate))
					return candidate;

				if (seenPaths.Add(candidate))
				{
					attempted.Add(candidate);
				}
			}

			var message = $"Injector DLL '{DefaultInjectorDll}' could not be located. Probed locations:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", attempted)}";
			throw new FileNotFoundException(message, DefaultInjectorDll);
		}
	}
}
