using Orbit;
using Orbit.Models;
using Orbit.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services
{
	public class SessionManagerService
	{
		private const string DefaultInjectorDll = "XInput1_4_inject.dll";
		private const uint WM_CLOSE = 0x0010;
		private const int SW_HIDE = 0;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_HIDEWINDOW = 0x0080;
		private static readonly nint HWND_TOP = nint.Zero;
		private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(3);

		private enum ProcessRole
		{
			Client,
			Launcher
		}

		private readonly ConcurrentDictionary<int, ManagedProcessRecord> _managedProcesses = new();

		private record struct ManagedProcessRecord(
			int ProcessId,
			DateTime? StartTime,
			ProcessRole Role,
			string SessionName,
			nint FallbackHandle,
			int? ParentProcessId);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool ShowWindow(nint hWnd, int nCmdShow);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public async Task InitializeSessionAsync(SessionModel session)
        {
            if (session.HostControl is not ChildClientView clientView)
                throw new InvalidOperationException("Session host control is missing or invalid.");

            try
            {
				var rsForm = await clientView.WaitForSessionAsync();
				// Window exists; give it a brief moment to finish initializing before we grab handles.
				await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
				rsForm.WaitForInjectionBeforeDock = session.RequireInjectionBeforeDock;
				EventHandler<DockedEventArgs> dockedHandler = null;
				dockedHandler = async (_, args) =>
				{
					session.ExternalHandle = args.Handle;
					session.RenderSurfaceHandle = rsForm.GetRenderSurfaceHandle();
					if (session.RSProcess != null)
					{
						UpdateProcessFallbackHandle(session.RSProcess.Id, session.ExternalHandle);
					}
					try { clientView.FocusEmbeddedClient(); } catch { }

					// IMPROVED: Always try to re-apply commands if injected, even if injection completed earlier
					// Window re-parenting (SetParent) can reset hooks, so we re-apply to restore state
					if (session.InjectionState == InjectionState.Injected && session.RSProcess != null)
					{
						Console.WriteLine($"[Orbit] Re-applying MESharp commands after docking for session '{session.Name}'...");

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

						Console.WriteLine($"[Orbit] Post-dock command re-application complete for session '{session.Name}'.");
					}
					else if (session.InjectionState != InjectionState.Injected)
					{
						Console.WriteLine($"[Orbit] Docked event fired for '{session.Name}' but injection state is {session.InjectionState}. Skipping command re-application.");
					}

					rsForm.Docked -= dockedHandler;
				};
				rsForm.Docked += dockedHandler;

				session.RSForm = rsForm;
				session.RSProcess = rsForm?.pDocked ?? throw new InvalidOperationException("RuneScape process handle is unavailable.");
				session.ParentProcessId = rsForm?.ParentProcessId;
				session.ExternalHandle = rsForm.DockedClientHandle;
				session.RenderSurfaceHandle = rsForm.GetRenderSurfaceHandle();
				RegisterSessionProcesses(session);
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
				session.RSForm?.SignalInjectionStarting();
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

				var runtimeStarted = await OrbitCommandClient
					.SendStartRuntimeWithRetryAsync(process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!runtimeStarted)
				{
					Console.WriteLine("[Orbit] Warning: Unable to start MemoryError .NET runtime after injection.");
				}

				// Ensure ImGui does not capture keyboard input by default
				var inputModeApplied = await OrbitCommandClient
					.SendInputModeWithRetryAsync(1, process.Id, maxAttempts: 5, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!inputModeApplied)
				{
					Console.WriteLine("[Orbit] Warning: Unable to set MemoryError input mode to passthrough (keyboard capture may persist).");
				}

				var debugMenuHidden = await OrbitCommandClient
					.SendDebugMenuVisibleWithRetryAsync(false, process.Id, maxAttempts: 5, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!debugMenuHidden)
				{
					Console.WriteLine("[Orbit] Warning: Unable to hide MemoryError debug menu (input capture may persist).");
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
							.SendDebugMenuVisibleWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
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

			if (session.InjectionState == InjectionState.Injected && session.RSProcess != null && !session.RSProcess.HasExited)
			{
				try
				{
					await OrbitCommandClient
						.SendUnloadScriptWithRetryAsync(session.RSProcess.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: cancellationToken)
						.ConfigureAwait(true);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Orbit] Failed to request managed script unload for session '{session.Name}': {ex.Message}");
				}
			}

			session.UpdateInjectionState(InjectionState.NotReady);

			HideWindowSilently(externalHandle);
			if (process?.MainWindowHandle is nint mainHandle and not 0 && mainHandle != externalHandle)
			{
				HideWindowSilently(mainHandle);
			}

			// Detach embedded host to release Win32 parenting
			if (session.HostControl is ChildClientView childHost)
			{
				childHost.DetachSession(restoreParent: false);
			}
			else if (session.RSForm is RSForm standaloneForm)
			{
				try
				{
					standaloneForm.Undock(restoreParent: false, restoreStyles: false);
					if (standaloneForm.InvokeRequired)
					{
						standaloneForm.Invoke(new Action(() =>
						{
							try
							{
								if (!standaloneForm.IsDisposed)
								{
									standaloneForm.Close();
								}
							}
							catch
							{
								// best effort
							}
						}));
					}
					else if (!standaloneForm.IsDisposed)
					{
						standaloneForm.Close();
					}

					if (!standaloneForm.IsDisposed)
					{
						standaloneForm.Dispose();
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

			var processId = process?.Id;
			var parentProcessIdValue = parentProcess?.Id;

			// Close client first, then parent/launcher to avoid orphaning the child process.
			// We avoid force-killing the launcher to prevent respawn loops/crash flashes.
			var shutdownTargets = new List<(Process process, string label, nint fallbackHandle, bool allowKill)>
			{
				(process, "client", externalHandle, forceKillOnTimeout),
				(parentProcess, "launcher", nint.Zero, false)
			}.Where(p => p.process != null).ToList();

			foreach (var target in shutdownTargets)
			{
				var exited = await TryShutdownProcessAsync(target.process, target.fallbackHandle, target.label, session.Name, shutdownTimeout, cancellationToken, target.allowKill).ConfigureAwait(true);
				if (exited)
				{
					TryUnregisterProcess(target.process.Id);
				}
			}

			session.RSForm = null;
			session.RSProcess = null;
			session.ParentProcessId = null;
			session.ExternalHandle = nint.Zero;
			session.RenderSurfaceHandle = nint.Zero;
			session.UpdateState(SessionState.Closed, clearError: false);
		}

		public async Task ShutdownManagedProcessesAsync(bool forceKillOnTimeout = false, CancellationToken cancellationToken = default)
		{
			var tracked = _managedProcesses.Values.ToList();
			if (tracked.Count == 0)
			{
				return;
			}

			foreach (var record in tracked)
			{
				cancellationToken.ThrowIfCancellationRequested();

				Process process;
				try
				{
					process = Process.GetProcessById(record.ProcessId);
				}
				catch (ArgumentException)
				{
					TryUnregisterProcess(record.ProcessId);
					continue;
				}
				catch (InvalidOperationException)
				{
					TryUnregisterProcess(record.ProcessId);
					continue;
				}

				if (process.HasExited)
				{
					TryUnregisterProcess(record.ProcessId);
					process.Dispose();
					continue;
				}

				if (record.StartTime.HasValue)
				{
					var currentStart = TryGetProcessStartTime(process);
					if (currentStart.HasValue && Math.Abs((currentStart.Value - record.StartTime.Value).TotalSeconds) > 1)
					{
						// PID has been reused for a different process; skip to avoid touching unrelated instances.
						process.Dispose();
						TryUnregisterProcess(record.ProcessId);
						continue;
					}
				}

				var label = record.Role == ProcessRole.Client ? "client" : "launcher";
				try
				{
					await TryShutdownProcessAsync(
						process,
						record.FallbackHandle,
						label,
						record.SessionName,
						DefaultShutdownTimeout,
						cancellationToken,
						forceKillOnTimeout: forceKillOnTimeout).ConfigureAwait(true);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
				{
					Console.WriteLine($"[Orbit] Failed to gracefully close tracked {label} PID {record.ProcessId}: {ex.Message}");
				}
				finally
				{
					TryUnregisterProcess(record.ProcessId);
				}
			}
		}

		private void RegisterSessionProcesses(SessionModel session)
		{
			if (session == null)
				return;

			if (session.RSProcess != null)
			{
				RegisterManagedProcess(
					session.RSProcess,
					ProcessRole.Client,
					session.Name ?? $"Session {session.Id}",
					session.ExternalHandle,
					session.ParentProcessId);
			}

			if (session.ParentProcessId is int parentPid && session.RSProcess?.Id != parentPid)
			{
				try
				{
					var parentProcess = Process.GetProcessById(parentPid);
					RegisterManagedProcess(
						parentProcess,
						ProcessRole.Launcher,
						session.Name ?? $"Session {session.Id}",
						nint.Zero,
						null);
					if (!ReferenceEquals(parentProcess, session.RSProcess))
					{
						parentProcess.Dispose();
					}
				}
				catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
				{
					// Parent process may have already exited; nothing to track.
				}
			}
		}

		private void RegisterManagedProcess(Process process, ProcessRole role, string sessionName, nint fallbackHandle, int? parentProcessId)
		{
			if (process == null)
				return;

			try
			{
				var record = new ManagedProcessRecord(
					process.Id,
					TryGetProcessStartTime(process),
					role,
					sessionName,
					fallbackHandle,
					parentProcessId);

				_managedProcesses[process.Id] = record;
			}
			catch (InvalidOperationException)
			{
				// Process exited between creation and tracking; ignore.
			}
		}

		private void UpdateProcessFallbackHandle(int processId, nint fallbackHandle)
		{
			if (processId <= 0)
				return;

			if (_managedProcesses.TryGetValue(processId, out var record))
			{
				_managedProcesses[processId] = record with { FallbackHandle = fallbackHandle };
			}
		}

		private void TryUnregisterProcess(int processId)
		{
			if (processId <= 0)
				return;

			_managedProcesses.TryRemove(processId, out _);
		}

		private static DateTime? TryGetProcessStartTime(Process process)
		{
			if (process == null)
				return null;

			try
			{
				return process.StartTime;
			}
			catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
			{
				return null;
			}
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

		private static void HideWindowSilently(nint handle)
		{
			if (handle == nint.Zero)
				return;

			try
			{
				ShowWindow(handle, SW_HIDE);
				SetWindowPos(handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
			}
			catch
			{
				// Window may already be closing; ignore failures.
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
				if (File.Exists(expandedPath) && IsValidInjectorDirectory(Path.GetDirectoryName(expandedPath)))
					return expandedPath;

				throw new FileNotFoundException($"Injector DLL not found or missing ME runtime assets at configured path '{expandedPath}'.", expandedPath);
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
				if (File.Exists(candidate) && IsValidInjectorDirectory(Path.GetDirectoryName(candidate)))
					return candidate;

				if (seenPaths.Add(candidate))
				{
					attempted.Add(candidate);
				}
			}

			var message = $"Injector DLL '{DefaultInjectorDll}' could not be located. Probed locations:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", attempted)}";
			throw new FileNotFoundException(message, DefaultInjectorDll);
		}

		private static bool IsValidInjectorDirectory(string? directory)
		{
			if (string.IsNullOrWhiteSpace(directory))
			{
				return false;
			}

			var runtimeConfig = Path.Combine(directory, "ME.runtimeconfig.json");
			var interop = Path.Combine(directory, "csharp_interop.dll");
			if (File.Exists(runtimeConfig) && File.Exists(interop))
			{
				return true;
			}

			return false;
		}
	}
}
