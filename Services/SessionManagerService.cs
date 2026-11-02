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
	public class SessionManagerService : IDisposable
	{
		private const uint WM_CLOSE = 0x0010;
		private const int SW_HIDE = 0;
		private const int SW_MINIMIZE = 6;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_HIDEWINDOW = 0x0080;
		private static readonly nint HWND_TOP = nint.Zero;
		private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(3);
		private static bool IsMesharpIntegrationEnabled => Settings.Default.MesharpIntegrationEnabled;
		private static bool HideNativeDebugMenuOnInject => Settings.Default.HideNativeDebugMenuOnInject;
		private readonly InjectorPathResolver injectorPathResolver;
		private SessionCollectionService? trackedSessions;
		private readonly HashSet<SessionModel> observedSessions = new();

		public void StartTracking(SessionCollectionService sessions)
		{
			if (trackedSessions != null) throw new InvalidOperationException("Session tracking is already active.");
			trackedSessions = sessions;
			sessions.Sessions.CollectionChanged += OnSessionCollectionChanged;
			ObserveSessions();
		}

		private void OnSessionCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ObserveSessions();
		private void ObserveSessions()
		{
			foreach (var session in observedSessions.Where(s => !trackedSessions!.Sessions.Contains(s)).ToArray())
			{
				session.PropertyChanged -= OnTrackedSessionChanged;
				observedSessions.Remove(session);
				SessionProcessOwnership.Instance.ReleaseOwner(session.Id);
			}
			foreach (var session in trackedSessions!.Sessions)
			{
				if (observedSessions.Add(session)) session.PropertyChanged += OnTrackedSessionChanged;
				if (session.IsRuneScapeClient) RegisterSessionProcesses(session);
			}
		}

		private void OnTrackedSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (sender is not SessionModel session || !session.IsRuneScapeClient) return;
			if (e.PropertyName is nameof(SessionModel.RSProcess) or nameof(SessionModel.ExternalHandle) or nameof(SessionModel.Name)) RegisterSessionProcesses(session);
			if (e.PropertyName == nameof(SessionModel.State) && session.State == SessionState.Closed)
				SessionProcessOwnership.Instance.ReleaseOwner(session.Id);
		}

		public void Dispose()
		{
			if (trackedSessions != null) trackedSessions.Sessions.CollectionChanged -= OnSessionCollectionChanged;
			foreach (var session in observedSessions) session.PropertyChanged -= OnTrackedSessionChanged;
			observedSessions.Clear();
		}

		internal void DetachPresentation(SessionModel session)
		{
			DisposeSessionHost(session);
			session.ExternalWindowIdentity?.Dispose();
			session.ExternalWindowIdentity = null;
			if (session.RSProcess != null)
			{
				TryUnregisterProcess(session.RSProcess.Id);
				session.RSProcess.Dispose();
				session.RSProcess = null;
			}
			session.ExternalHandle = nint.Zero;
			session.RenderSurfaceHandle = nint.Zero;
			session.UpdateState(SessionState.Closed, clearError: false);
		}

		private enum ProcessRole
		{
			Client,
			Launcher
		}

		private readonly ConcurrentDictionary<int, ManagedProcessRecord> _managedProcesses = new();

		public SessionManagerService()
			: this(new InjectorPathResolver())
		{
		}

		public SessionManagerService(InjectorPathResolver injectorPathResolver)
		{
			this.injectorPathResolver = injectorPathResolver ?? throw new ArgumentNullException(nameof(injectorPathResolver));
		}

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
				if (session.StartupTask == null)
				{
					session.LifetimeCancellation.ThrowIfCancellationRequested();
					var form = new RSForm
					{
						LauncherCharacterId = session.LauncherCharacterId,
						WaitForInjectionBeforeDock = session.RequireInjectionBeforeDock
					};
					TrackSessionProcess(form, session, clientView.Dispatcher);
					clientView.AttachSessionHost(form);
					session.StartupTask = form.BeginLoad(session.LifetimeCancellation);
					_ = clientView.ObserveStartupAsync(session.StartupTask);
				}
				var rsForm = await clientView.WaitForSessionAsync().WaitAsync(session.LifetimeCancellation);
				// Window exists; give it a brief moment to finish initializing before we grab handles.
				await Task.Delay(TimeSpan.FromSeconds(2), session.LifetimeCancellation).ConfigureAwait(true);
				if (session.State is SessionState.Closed or SessionState.ShuttingDown)
					throw new OperationCanceledException("Session closed during initialization.");
				session.RSForm = rsForm;
				session.LauncherCharacterId = rsForm.LauncherCharacterId;
				session.RSProcess = rsForm?.pDocked ?? throw new InvalidOperationException("RuneScape process handle is unavailable.");
				if (SessionCollectionService.Instance.Sessions.Any(s =>
					!ReferenceEquals(s, session) &&
					s.RSProcess != null &&
					!s.RSProcess.HasExited &&
					s.RSProcess.Id == session.RSProcess.Id))
				{
					Console.WriteLine($"[Orbit][Warning] Session '{session.Name}' resolved to PID {session.RSProcess.Id}, which is already bound to another session. Injection targeting may be incorrect.");
				}
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

				// Ensure host content is measured/activated even if tab selection didn't change.
				clientView.RefreshDockedContentActivation(requestFocus: true);

				if (!session.RequireInjectionBeforeDock)
				{
					rsForm.SignalInjectionReady();
				}
            }
            catch (Exception ex)
            {
                if (session.State is not (SessionState.Closed or SessionState.ShuttingDown)) session.Fail(ex);
                throw;
            }
        }

		internal static void TrackSessionProcess(RSForm form, SessionModel session, System.Windows.Threading.Dispatcher dispatcher)
		{
			form.SetProcessOwner(session.Id);
			session.RSForm = form;
			form.ProcessChanged = process => dispatcher.Invoke(() =>
			{
				if (session.State == SessionState.Closed || (process == null && session.State == SessionState.ShuttingDown)) return;
				session.RSProcess = process;
				session.ParentProcessId = process == null ? null : form.ParentProcessId;
				session.LauncherCharacterId = form.LauncherCharacterId;
			});
		}

		internal async Task CompleteDockingAsync(SessionModel session)
		{
			var token = session.LifetimeCancellation;
			await session.HostControl.WaitForDockAsync().WaitAsync(token).ConfigureAwait(true);
			token.ThrowIfCancellationRequested();
			session.ExternalHandle = session.RSForm.DockedClientHandle;
			session.RenderSurfaceHandle = session.RSForm.GetRenderSurfaceHandle();
			if (session.RSProcess != null) UpdateProcessFallbackHandle(session.RSProcess.Id, session.ExternalHandle);
			session.HostControl.RefreshDockedContentActivation(requestFocus: true);
			if (IsMesharpIntegrationEnabled)
				await new MesharpSessionCommandService(new OrbitCommandClientAdapter())
					.ReassertInputPassthroughAsync(new[] { session }).ConfigureAwait(true);
			token.ThrowIfCancellationRequested();
		}

		public async Task InjectAsync(SessionModel session)
		{
			if (!session.IsInjectable)
				throw new InvalidOperationException("Session is not ready for injection. An unknown injection outcome requires restarting the client.");
			var process = session.RSProcess ?? throw new InvalidOperationException("Cannot inject before the RuneScape client is ready.");
			var processId = process.Id;
			var workToken = session.WorkCancellation;
			var form = session.RSForm;
			workToken.ThrowIfCancellationRequested();
			var duplicateOwner = SessionCollectionService.Instance.Sessions.FirstOrDefault(s =>
				!ReferenceEquals(s, session) &&
				s.RSProcess != null &&
				!s.RSProcess.HasExited &&
				s.RSProcess.Id == processId &&
				s.State != SessionState.Closed);
			if (duplicateOwner != null)
			{
				var message = $"Cannot inject session '{session.Name}' because PID {processId} is already attached to session '{duplicateOwner.Name}'.";
				Console.WriteLine($"[Orbit][Inject] {message}");
				session.RecordInjectionFailure(new InvalidOperationException(message));
				throw new InvalidOperationException(message);
			}

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
				Console.WriteLine($"[Orbit] Injecting '{dllPath}' into PID {processId}...");
				var injected = await Task.Run(() =>
				{
					workToken.ThrowIfCancellationRequested();
					return Orbit.ME.DllInjector.Inject(processId, dllPath);
				});
				workToken.ThrowIfCancellationRequested();
				if (session.State is SessionState.Closed or SessionState.ShuttingDown)
					throw new OperationCanceledException("Session closed during injection.");
				Console.WriteLine(injected
					? "[Orbit] Injection completed successfully."
					: "[Orbit] Injection call returned false.");
				if (!injected)
				{
					var message =
						$"Injection failed for PID {processId}. Verify '{Path.GetFileName(dllPath)}' is the correct Orbit/ME injector binary and includes required runtime assets.";
					var failure = new InvalidOperationException(message);
					session.RecordInjectionFailure(failure);
					throw failure;
				}

				session.UpdateInjectionState(InjectionState.Injected);
				session.UpdateState(SessionState.Injected);

				// Give the injected DLL a moment to initialize
				await Task.Delay(500, workToken);

				// Restore focus to the game window so input works
				Console.WriteLine($"[Orbit] Restoring focus to game window (0x{session.ExternalHandle:X})");
				try { session.HostControl?.FocusEmbeddedClient(); } catch { }

				if (IsMesharpIntegrationEnabled)
				{
					var autoStartRuntime = new McpPreferencesStore().Load().AutoStartRuntimeOnInject;
					if (autoStartRuntime)
					{
						var runtimeStarted = await OrbitCommandClient
							.SendStartRuntimeWithRetryAsync(processId, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: workToken)
							.ConfigureAwait(true);
						if (!runtimeStarted)
						{
							Console.WriteLine("[Orbit] Warning: Unable to start MemoryError .NET runtime after injection.");
						}
					}
					else
					{
						Console.WriteLine("[Orbit] MCP policy: runtime auto-start on injection is disabled.");
					}

					await new MesharpSessionCommandService(new OrbitCommandClientAdapter())
						.ApplyInjectionPreferenceAsync(session, HideNativeDebugMenuOnInject).ConfigureAwait(true);
					workToken.ThrowIfCancellationRequested();
				}
				else
				{
					Console.WriteLine($"[Orbit] MESharp integration disabled. Skipping runtime/script/input commands for session '{session.Name}'.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Injection pipeline threw: {ex}");
				if (!workToken.IsCancellationRequested && ReferenceEquals(session.RSProcess, process) &&
					session.State is not (SessionState.Closed or SessionState.ShuttingDown) && session.InjectionState != InjectionState.Injected)
					session.RecordInjectionFailure(ex);
				throw;
			}
			finally
			{
				if (form != null)
				{
					Console.WriteLine($"[Orbit] Injection cleanup signaled ready for session '{session.Name}'.");
					form.SignalInjectionReady();
				}
			}

			if (IsMesharpIntegrationEnabled)
			{
				_ = ReassertAfterInjectionAsync();
			}

			async Task ReassertAfterInjectionAsync()
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(2), workToken).ConfigureAwait(true);
					if (!ReferenceEquals(session.RSProcess, process)) return;
					await new MesharpSessionCommandService(new OrbitCommandClientAdapter())
						.ReassertInputPassthroughAsync(new[] { session }).ConfigureAwait(true);
				}
				catch (OperationCanceledException) when (workToken.IsCancellationRequested) { }
				catch (Exception ex) { Console.WriteLine($"[Orbit] Deferred input passthrough request failed: {ex.Message}"); }
			}
		}

		public async Task ShutdownSessionAsync(SessionModel session, TimeSpan? timeout = null, CancellationToken cancellationToken = default, bool forceKillOnTimeout = false)
		{
			ArgumentNullException.ThrowIfNull(session);
			await session.ShutdownGate.WaitAsync(cancellationToken).ConfigureAwait(true);
			try
			{
				if (session.State == SessionState.Closed) return;
				session.UpdateState(SessionState.ShuttingDown, clearError: false);
				if (session.StartupTask != null)
				{
					try { await session.StartupTask.WaitAsync(cancellationToken).ConfigureAwait(true); }
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
					catch (Exception ex) when (ex is not OperationCanceledException) { Console.WriteLine($"[Orbit] Startup finished with an error during close: {ex.Message}"); }
				}
				await ShutdownSessionCoreAsync(session, timeout, cancellationToken, forceKillOnTimeout).ConfigureAwait(true);
			}
			finally { session.ShutdownGate.Release(); }
		}

		private async Task ShutdownSessionCoreAsync(SessionModel session, TimeSpan? timeout, CancellationToken cancellationToken, bool forceKillOnTimeout)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));

			// Registration grants ownership of the presentation, never of the caller's process.
			// A script window can share its process with an injected game and other windows.
			if (session.IsExternalScript)
			{
				DisposeSessionHost(session);
				session.ExternalWindowIdentity?.Dispose();
				session.ExternalWindowIdentity = null;
				session.RSProcess?.Dispose();
				session.RSProcess = null;
				session.ExternalHandle = nint.Zero;
				session.RenderSurfaceHandle = nint.Zero;
				session.UpdateState(SessionState.Closed, clearError: false);
				return;
			}

			var shutdownTimeout = timeout ?? DefaultShutdownTimeout;
			var process = session.RSProcess;
			var externalHandle = session.ExternalHandle;

			session.UpdateState(SessionState.ShuttingDown, clearError: false);
			await session.ScriptCommandGate.WaitAsync(cancellationToken).ConfigureAwait(true);
			try
			{

			if (IsMesharpIntegrationEnabled && session.InjectionState == InjectionState.Injected && session.RSProcess != null && !session.RSProcess.HasExited)
			{
				try
				{
					var scriptIdToUnload = !string.IsNullOrWhiteSpace(session.ActiveScriptId)
						? session.ActiveScriptId
						: (!string.IsNullOrWhiteSpace(session.ActiveScriptPath)
							? ScriptManagerService.DeriveScriptIdFromPath(session.ActiveScriptPath)
							: null);

					if (!string.IsNullOrWhiteSpace(scriptIdToUnload))
					{
						var unloaded = await OrbitCommandClient
							.SendUnloadScriptWithRetryAsync(scriptIdToUnload, session.RSProcess.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: cancellationToken)
							.ConfigureAwait(true);
						if (unloaded) session.SetScriptStopped();
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Orbit] Failed to request managed script unload for session '{session.Name}': {ex.Message}");
				}
			}


			HideWindowSilently(externalHandle);
			if (process?.MainWindowHandle is nint mainHandle and not 0 && mainHandle != externalHandle)
			{
				HideWindowSilently(mainHandle);
			}
			HideOrMinimizeProcessWindowSilently(process);

			var processId = process?.Id;
			var clientExited = await TryShutdownProcessAsync(process, externalHandle, "client", session.Name,
				shutdownTimeout, cancellationToken, forceKillOnTimeout, disposeProcess: false).ConfigureAwait(true);
			if (clientExited && processId.HasValue) TryUnregisterProcess(processId.Value);

			if (!clientExited)
			{
				var recoveryState = session.InjectionState == InjectionState.Injected
					? SessionState.Injected
					: SessionState.ClientReady;
				session.UpdateState(recoveryState, clearError: false);
				throw new TimeoutException($"Session '{session.Name}' client did not exit within {shutdownTimeout.TotalSeconds:F1}s.");
			}

			session.SetScriptStopped();
			session.UpdateInjectionState(InjectionState.NotReady);
			DisposeSessionHost(session);

			session.RSForm = null;
			session.RSProcess = null;
			process?.Dispose();
			session.ParentProcessId = null;
			session.ExternalHandle = nint.Zero;
			session.RenderSurfaceHandle = nint.Zero;
			session.UpdateState(SessionState.Closed, clearError: false);
			}
			finally { session.ScriptCommandGate.Release(); }
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
				if (record.Role == ProcessRole.Launcher) { TryUnregisterProcess(record.ProcessId); continue; }
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
					if (!currentStart.HasValue || currentStart.Value != record.StartTime.Value)
					{
						// PID has been reused for a different process; skip to avoid touching unrelated instances.
						process.Dispose();
						TryUnregisterProcess(record.ProcessId);
						continue;
					}
				}
				else
				{
					process.Dispose();
					continue; // Unknown creation identity is not authority to close a PID.
				}

				var label = record.Role == ProcessRole.Client ? "client" : "launcher";
				try
				{
					var exited = await TryShutdownProcessAsync(
						process,
						record.FallbackHandle,
						label,
						record.SessionName,
						DefaultShutdownTimeout,
						cancellationToken,
						forceKillOnTimeout: forceKillOnTimeout).ConfigureAwait(true);
					if (exited) TryUnregisterProcess(record.ProcessId);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
				{
					Console.WriteLine($"[Orbit] Failed to gracefully close tracked {label} PID {record.ProcessId}: {ex.Message}");
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

			// Keep tracked labels in sync with the latest session name.
			RefreshTrackedSessionNames(session);
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

		private void RefreshTrackedSessionNames(SessionModel session)
		{
			if (session == null)
			{
				return;
			}

			var sessionName = session.Name ?? $"Session {session.Id}";
			var candidateIds = new[] { session.RSProcess?.Id ?? 0 }
				.Where(id => id > 0)
				.Distinct()
				.ToArray();

			foreach (var pid in candidateIds)
			{
				if (_managedProcesses.TryGetValue(pid, out var record))
				{
					_managedProcesses[pid] = record with { SessionName = sessionName };
				}
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

		private async Task<bool> TryShutdownProcessAsync(
			Process process,
			nint fallbackHandle,
			string processLabel,
			string sessionName,
			TimeSpan timeout,
			CancellationToken cancellationToken,
			bool forceKillOnTimeout,
			bool disposeProcess = true)
		{
			if (process == null)
				return true;

			try
			{
				if (process.HasExited)
					return true;

				HideOrMinimizeProcessWindowSilently(process);

				var closeSignalled = TrySignalProcessWindow(process);

				if (!closeSignalled && fallbackHandle != nint.Zero && WindowBelongsToProcess(fallbackHandle, process.Id))
				{
					closeSignalled = PostMessage(fallbackHandle, WM_CLOSE, nint.Zero, nint.Zero);
				}

				if (closeSignalled && !process.HasExited)
				{
					using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					linkedCts.CancelAfter(timeout);

					try
					{
						await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
					}
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
					{
						// Timed out while waiting for graceful exit.
					}
				}

				if (!process.HasExited && forceKillOnTimeout)
				{
					process.Kill();
					await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
				}
				else if (!process.HasExited)
				{
					Console.WriteLine($"[Orbit] Session '{sessionName}' ({processLabel} PID {process.Id}) did not exit within {timeout.TotalSeconds:F1}s; leaving process running.");
				}

				return process.HasExited;
			}
			catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
			{
				// Access failures do not establish that the process has exited.
				Console.WriteLine($"[Orbit] Could not confirm shutdown for '{sessionName}': {ex.Message}");
				return false;
			}
			finally
			{
				if (disposeProcess)
				{
					process.Dispose();
				}
			}
		}

		private static bool WindowBelongsToProcess(nint hwnd, int processId)
		{
			GetWindowThreadProcessId(hwnd, out var owner);
			return owner == processId;
		}
		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

		private static void DisposeSessionHost(SessionModel session)
		{
			if (session?.HostControl is ChildClientView childHost)
			{
				childHost.DetachSession(restoreParent: true);
				return;
			}

			if (session?.RSForm is not RSForm standaloneForm)
			{
				return;
			}

			try
			{
				standaloneForm.Undock(restoreParent: true, restoreStyles: true);
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
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Session host retained because restoration or teardown failed: {ex.Message}");
				throw;
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

		private static void HideOrMinimizeProcessWindowSilently(Process process)
		{
			if (process == null)
			{
				return;
			}

			try
			{
				if (process.HasExited)
				{
					return;
				}

				process.Refresh();
				var handle = process.MainWindowHandle;
				if (handle == nint.Zero)
				{
					return;
				}

				var hidden = ShowWindow(handle, SW_HIDE);
				SetWindowPos(handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
				if (!hidden)
				{
					// Some windows ignore hide while changing parent/style; minimize as fallback.
					ShowWindow(handle, SW_MINIMIZE);
				}
			}
			catch
			{
				// best effort
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

		private string ResolveInjectorPath()
			=> injectorPathResolver.Resolve(AppContext.BaseDirectory, Settings.Default.InjectorDllPath, IsMesharpIntegrationEnabled);
	}
}
