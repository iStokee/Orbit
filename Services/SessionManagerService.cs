using Orbit;
using Orbit.Models;
using Orbit.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services
{
	public class SessionManagerService
	{
		private const string DefaultInjectorDll = "XInput1_4_inject.dll";
		private const uint WM_CLOSE = 0x0010;
		private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(8);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

        public async Task InitializeSessionAsync(SessionModel session)
        {
            if (session.HostControl is not ChildClientView clientView)
                throw new InvalidOperationException("Session host control is missing or invalid.");

			try
			{
				var rsForm = await clientView.WaitForSessionAsync();
				rsForm.WaitForInjectionBeforeDock = session.RequireInjectionBeforeDock;
				EventHandler<DockedEventArgs> dockedHandler = null;
				dockedHandler = async (_, args) =>
				{
					session.ExternalHandle = args.Handle;
					try { clientView.FocusEmbeddedClient(); } catch { }

					if (session.InjectionState != InjectionState.Injected)
					{
						rsForm.Docked -= dockedHandler;
						return;
					}

					// Re-apply input passthrough after docking to ensure keyboard focus is restored.
					if (session.RSProcess != null)
					{
						var applied = await OrbitCommandClient
							.SendInputModeWithRetryAsync(1, session.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
							.ConfigureAwait(false);
						if (!applied)
						{
							Console.WriteLine("[Orbit] Warning: Unable to persist MemoryError input passthrough after docking.");
						}

						var focusApplied = await OrbitCommandClient
							.SendFocusSpoofWithRetryAsync(false, session.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
							.ConfigureAwait(false);
						if (!focusApplied)
						{
							Console.WriteLine("[Orbit] Warning: Unable to persist MemoryError focus spoof state after docking.");
						}
					}
					rsForm.Docked -= dockedHandler;
				};
				rsForm.Docked += dockedHandler;

				session.RSForm = rsForm;
				session.RSProcess = rsForm?.pDocked ?? throw new InvalidOperationException("RuneScape process handle is unavailable.");
				session.ParentProcessId = rsForm?.ParentProcessId;
				session.ExternalHandle = rsForm.DockedRSHwnd;
				if (session.RSProcess != null)
				{
					var parentInfo = session.ParentProcessId.HasValue ? session.ParentProcessId.Value.ToString() : "n/a";
					Console.WriteLine($"[Orbit] Session '{session.Name}' attached to PID {session.RSProcess.Id} (Parent {parentInfo}) (MainWindow 0x{session.RSProcess.MainWindowHandle.ToInt64():X}) DockedHWND=0x{session.ExternalHandle:X}");
				}
                session.UpdateState(SessionState.ClientReady);
                session.UpdateInjectionState(InjectionState.Ready);

				// Give input focus to the embedded client once ready
				clientView.FocusEmbeddedClient();

				if (!session.RequireInjectionBeforeDock)
				{
					rsForm.SignalInjectionReady();
				}
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
				Console.WriteLine($"[Orbit] Failed to resolve injector path: {ex}");
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

				// Ensure ImGui does not capture keyboard input by default
				var inputModeApplied = await OrbitCommandClient
					.SendInputModeWithRetryAsync(1, process.Id, maxAttempts: 5, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!inputModeApplied)
				{
					Console.WriteLine("[Orbit] Warning: Unable to set MemoryError input mode to passthrough (keyboard capture may persist).");
				}

				var focusSpoofApplied = await OrbitCommandClient
					.SendFocusSpoofWithRetryAsync(false, process.Id, maxAttempts: 5, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!focusSpoofApplied)
				{
					Console.WriteLine("[Orbit] Warning: Unable to disable MemoryError focus spoof (focus handling may remain hijacked).");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Injection pipeline threw: {ex}");
				session.RecordInjectionFailure(ex);
				throw;
			}
			finally
			{
				if (session.RSForm != null)
				{
					Console.WriteLine($"[Orbit] Injection cleanup signaled ready for session '{session.Name}'.");
					session.RSForm.SignalInjectionReady();
				}
			}

			// Final safeguard: reassert passthrough shortly after MESharp finishes wiring hooks.
			_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
					if (!process.HasExited)
					{
						await OrbitCommandClient
							.SendInputModeWithRetryAsync(1, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
							.ConfigureAwait(false);

						await OrbitCommandClient
							.SendFocusSpoofWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
							.ConfigureAwait(false);

						// Attempt to refocus the embedded client on the UI thread.
						_ = session.HostControl?.Dispatcher?.InvokeAsync(() =>
						{
							try { session.HostControl.FocusEmbeddedClient(); } catch { }
						});
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Orbit] Deferred input passthrough request failed: {ex}");
				}
			});
	    }

		public async Task ShutdownSessionAsync(SessionModel session, TimeSpan? timeout = null, CancellationToken cancellationToken = default, bool forceKillOnTimeout = false)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));

			var shutdownTimeout = timeout ?? DefaultShutdownTimeout;
			var process = session.RSProcess;
			var externalHandle = session.ExternalHandle;

			session.UpdateState(SessionState.ShuttingDown, clearError: false);
			session.UpdateInjectionState(InjectionState.NotReady);

			// Detach embedded host to release Win32 parenting
			if (session.HostControl is ChildClientView childHost)
			{
				childHost.DetachSession();
			}

			var rsForm = session.RSForm;
			if (rsForm != null)
			{
				try
				{
					rsForm.Undock();
					if (rsForm.InvokeRequired)
					{
						rsForm.Invoke(new Action(() =>
						{
							try
							{
								if (!rsForm.IsDisposed)
								{
									rsForm.Close();
								}
							}
							catch
							{
								// best effort
							}
						}));
					}
					else if (!rsForm.IsDisposed)
					{
						rsForm.Close();
					}

					if (!rsForm.IsDisposed)
					{
						rsForm.Dispose();
					}
				}
				catch
				{
					// WinForms teardown can fail if the form is already closing; ignore.
				}
			}

			Process parentProcess = null;
			if (session.ParentProcessId is int parentPid)
			{
				if (process == null || process.Id != parentPid)
				{
					try
					{
						parentProcess = Process.GetProcessById(parentPid);
					}
					catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
					{
						parentProcess = null;
					}
				}
			}

			await TryShutdownProcessAsync(process, externalHandle, "client", session.Name, shutdownTimeout, cancellationToken, forceKillOnTimeout).ConfigureAwait(true);

			if (parentProcess != null)
			{
				await TryShutdownProcessAsync(parentProcess, nint.Zero, "launcher", session.Name, shutdownTimeout, cancellationToken, forceKillOnTimeout).ConfigureAwait(true);
			}

			session.RSForm = null;
			session.RSProcess = null;
			session.ParentProcessId = null;
			session.ExternalHandle = nint.Zero;
			session.UpdateState(SessionState.Closed, clearError: false);
		}

		private async Task<bool> TryShutdownProcessAsync(Process process, nint fallbackHandle, string processLabel, string sessionName, TimeSpan timeout, CancellationToken cancellationToken, bool forceKillOnTimeout)
		{
			if (process == null)
				return true;

			try
			{
				if (process.HasExited)
					return true;

				var closeSignalled = TrySignalProcessWindow(process);

				if (!closeSignalled && fallbackHandle != nint.Zero)
				{
					closeSignalled = PostMessage(fallbackHandle, WM_CLOSE, nint.Zero, nint.Zero);
				}

				if (closeSignalled && !process.HasExited)
				{
					using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					linkedCts.CancelAfter(timeout);

					try
					{
						await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(true);
					}
					catch (OperationCanceledException)
					{
						// Timed out while waiting for graceful exit.
					}
				}

				if (!process.HasExited && forceKillOnTimeout)
				{
					process.Kill();
					await process.WaitForExitAsync(cancellationToken).ConfigureAwait(true);
				}
				else if (!process.HasExited)
				{
					Console.WriteLine($"[Orbit] Session '{sessionName}' ({processLabel} PID {process.Id}) did not exit within {timeout.TotalSeconds:F1}s; leaving process running.");
				}

				return process.HasExited;
			}
			catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
			{
				// Process might have already exited or the handle may be invalid.
				return true;
			}
			finally
			{
				process.Dispose();
			}
		}

		private static bool TrySignalProcessWindow(Process process)
		{
			try
			{
				if (process == null || process.HasExited)
					return false;

				var signalled = false;

				try
				{
					signalled = process.CloseMainWindow();
				}
				catch (InvalidOperationException)
				{
					// Process exited between checks; treat as handled.
					return true;
				}

				if (!signalled)
				{
					var mainHandle = process.MainWindowHandle;
					if (mainHandle != nint.Zero)
					{
						signalled = PostMessage(mainHandle, WM_CLOSE, nint.Zero, nint.Zero);
					}
				}

				return signalled;
			}
			catch (InvalidOperationException)
			{
				// Process exited between checks; treat as handled.
				return true;
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
