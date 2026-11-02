using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Orbit.Models;
using Orbit.Services;
using static Orbit.Classes.Win32;
using System.ComponentModel;
using System.Runtime.Versioning;

namespace Orbit
{
	[SupportedOSPlatform("windows")]
	public partial class RSForm : Form
	{
		private enum ClientLaunchMode
		{
			Legacy,
			Launcher
		}

		internal Process pDocked = null!;
		private readonly TaskCompletionSource<bool> windowReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal Action<Process?>? ProcessChanged { get; set; }
		private Process? activationProcess;
		private ClientLaunchObservation? launchObservation;
		private ClientLaunchMode attemptLaunchMode;
		private HostedWindowIdentity? dockedWindowIdentity;
		private readonly object nativeWindowSync = new();
		private readonly TaskCompletionSource<bool> injectionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private static readonly TimeSpan ProcessDetectionTimeout = TimeSpan.FromSeconds(45);
		private static readonly TimeSpan ProcessPollInterval = TimeSpan.FromMilliseconds(250);
		private static readonly TimeSpan WindowHandleTimeout = TimeSpan.FromSeconds(25);
		private static readonly TimeSpan WindowHandlePollInterval = TimeSpan.FromMilliseconds(200);
		private static readonly TimeSpan LauncherRefreshDelay = TimeSpan.FromMilliseconds(500);
		private static readonly string LauncherUri = "rs-launch://www.runescape.com/k=5/l=$(Language:0)/jav_config.ws";
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string? LauncherCharacterId { get; set; }
		private static readonly SemaphoreSlim LauncherStartGate = new(1, 1);
		private Guid processOwner = Guid.NewGuid();
		private bool sessionOwnsProcess;
		internal void SetProcessOwner(Guid sessionId) { processOwner = sessionId; sessionOwnsProcess = true; }
		private CancellationToken loadCancellation;
		private IntPtr hWndOriginalParent;
		private IntPtr hWndParent;
		private uint? originalWindowStyle;
		private IntPtr dockedHandleCache;
		//internal static List<RuneScapeHandler> rsHandlerList = new List<RuneScapeHandler>();
		private bool hasStarted = false;
		private IntPtr rsWindow;
		private int rs2ClientID;
		private int runescapeProcessID;
		private IntPtr _dockedHandle;

		public int? ParentProcessId { get; private set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public IntPtr DockedRSHwnd { get; private set; }

		public IntPtr DockedClientHandle => _dockedHandle;

		[DllImport("user32.dll", SetLastError = true)]
		internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
		[DllImport("user32.dll")]
		private static extern IntPtr GetParent(IntPtr hwnd);
		[DllImport("user32.dll", SetLastError = true)]
		internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr hObject);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct PROCESSENTRY32
		{
			public uint dwSize;
			public uint cntUsage;
			public uint th32ProcessID;
			public IntPtr th32DefaultHeapID;
			public uint th32ModuleID;
			public uint cntThreads;
			public uint th32ParentProcessID;
			public int pcPriClassBase;
			public uint dwFlags;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string szExeFile;
		}

		private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
		private const uint TH32CS_SNAPPROCESS = 0x00000002;

		internal static int GetParentProcess(int Id)
		{
			var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
			if (snapshot == InvalidHandleValue)
			{
				return 0;
			}

			try
			{
				var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
				if (!Process32First(snapshot, ref entry))
				{
					return 0;
				}

				do
				{
					if (entry.th32ProcessID == (uint)Id)
					{
						return (int)entry.th32ParentProcessID;
					}
				}
				while (Process32Next(snapshot, ref entry));
			}
			finally
			{
				CloseHandle(snapshot);
			}

			return 0;
		}

		internal Task WindowReadyTask => windowReadyTcs.Task;

		[DefaultValue(true)]
		internal bool WaitForInjectionBeforeDock { get; set; } = true;

		public event EventHandler<DockedEventArgs>? Docked;

		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);

		internal void SignalInjectionReady()
		{
			injectionReadyTcs.TrySetResult(true);
		}

		public RSForm()
		{
			InitializeComponent();
			AutoSize = false;
			panel_DockPanel.AutoSize = false;
			panel_DockPanel.ClientSizeChanged += (_, e) => DockHostSizeChanged?.Invoke(this, e);
			Disposed += (_, _) => { activationProcess?.Dispose(); dockedWindowIdentity?.Dispose(); if (!sessionOwnsProcess) SessionProcessOwnership.Instance.ReleaseOwner(processOwner); };
			// set back color to DarkSlate from dynamic resources
			//this.BackColor = System.Drawing.ColorTranslator.FromHtml("#2f2f2f");
		}

		private void RSForm_Load(object sender, EventArgs e) { }
		internal event EventHandler? DockHostSizeChanged;

		public Task BeginLoad() => BeginLoad(CancellationToken.None);

		internal void EnsureDockHostHandle() { _ = Handle; _ = panel_DockPanel.Handle; }

		public async Task BeginLoad(CancellationToken cancellationToken)
		{
			if (hasStarted)
			{
				return;
			}

			loadCancellation = cancellationToken;
			loadCancellation.ThrowIfCancellationRequested();
			this.FormBorderStyle = FormBorderStyle.None;
			_dockedHandle = IntPtr.Zero;
			ParentProcessId = null;

			Console.WriteLine("LoadRS");
			await LoadRS();

			hasStarted = true;
			Console.WriteLine($"Finished loading {_dockedHandle}");
		}

		public async Task LoadRS()
		{
				if (_dockedHandle != IntPtr.Zero)
				{
					return;
				}

			try
			{
				// this is what triggers the actual loading of the client
				await ExecuteRSLoadAsync();

				loadCancellation.ThrowIfCancellationRequested();
			}
			catch (Exception ex)
				{
					Console.WriteLine($"An error occurred: {ex}");
					windowReadyTcs.TrySetException(ex);
					throw;
				}

			}

		public IntPtr GetRenderSurfaceHandle()
		{
			var dockedHandle = dockedHandleCache;
			if (dockedHandle == IntPtr.Zero)
			{
				dockedHandle = DockedRSHwnd;
			}

			if (dockedHandle == IntPtr.Zero)
			{
				dockedHandle = _dockedHandle;
			}

			if (dockedHandle == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}

			var renderView = FindWindowEx(dockedHandle, IntPtr.Zero, "JagRenderView", null);
			if (renderView != IntPtr.Zero)
			{
				return renderView;
			}

			// Legacy clients sometimes host rendering inside a Sun AWT canvas.
			var sunCanvas = FindWindowEx(dockedHandle, IntPtr.Zero, "SunAwtCanvas", null);
			if (sunCanvas != IntPtr.Zero)
			{
				return sunCanvas;
			}

			return dockedHandle;
		}

		internal void FocusGameWindow()
		{
			try
			{
				if (dockedWindowIdentity?.IsCurrent == true && hWndParent != 0 && NativeWindowGeometry.GetParent(_dockedHandle) == hWndParent)
				{
					SetFocus(_dockedHandle);
				}
			}
			catch { /* best effort */ }
		}

		internal void Undock(bool restoreParent = true, bool restoreStyles = true)
		{
			void PerformUndock()
			{
				lock (nativeWindowSync)
				{
				if (_dockedHandle == IntPtr.Zero)
				{
					return;
				}
				if (dockedWindowIdentity?.IsCurrent != true) return;

				if (restoreParent && originalWindowStyle.HasValue)
				{
					NativeWindowRestorer.Restore(_dockedHandle, hWndOriginalParent, (int)originalWindowStyle.Value);
					hWndParent = IntPtr.Zero;
				}

				dockedHandleCache = IntPtr.Zero;
				}
			}

			try
			{
				if (IsDisposed)
				{
					return;
				}

				if (InvokeRequired)
				{
					Invoke((MethodInvoker)PerformUndock);
				}
				else
				{
					PerformUndock();
				}
			}
			catch
			{
				throw; // A failed restoration must retain the host.
			}
		}

		private async Task ExecuteRSLoadAsync()
		{
			if (_dockedHandle != IntPtr.Zero) return;
			try
			{
				await StartAndDetectClientAsync(ResolveLaunchMode());
				await WaitForAndSetDockingWindowAsync();
				await DockWindowToPanelAsync();
			}
			catch (Exception ex) { windowReadyTcs.TrySetException(ex); throw; }
		}

		#region RSForm Loading

		private async Task StartAndDetectClientAsync(ClientLaunchMode mode)
		{
			// URI launches do not return the game process. Keep discovery inside the same
			// gate as launch so concurrent Orbit requests cannot exchange account/PID bindings.
			if (mode == ClientLaunchMode.Launcher) await LauncherStartGate.WaitAsync(loadCancellation).ConfigureAwait(false);
			try
			{
				attemptLaunchMode = mode;
				await StartAndRefreshRSProcessAsync();
				await FindAndSetRsClientAsync();
			}
			finally { if (mode == ClientLaunchMode.Launcher) LauncherStartGate.Release(); }
		}

		private async Task StartAndRefreshRSProcessAsync()
		{
			loadCancellation.ThrowIfCancellationRequested();
			var launchTarget = ResolveLaunchTarget(attemptLaunchMode);
			var existingIds = new System.Collections.Generic.List<int>();
			foreach (var existing in Process.GetProcessesByName("rs2client"))
				using (existing) existingIds.Add(existing.Id);
			launchObservation = new ClientLaunchObservation(existingIds, DateTime.UtcNow);
			using var environment = attemptLaunchMode == ClientLaunchMode.Launcher ? new LauncherEnvironmentScope() : null;
			ApplyConfiguredLauncherAccountEnvironment(attemptLaunchMode);
			activationProcess?.Dispose();
			activationProcess = new Process { StartInfo = new ProcessStartInfo { FileName = launchTarget, UseShellExecute = true } };
			if (!activationProcess.Start())
			{
				activationProcess.Dispose();
				activationProcess = null;
			}
			Console.WriteLine($"[Orbit] Launch request accepted via {attemptLaunchMode}; waiting for a distinct game client.");
			await Task.Delay(LauncherRefreshDelay, loadCancellation).ConfigureAwait(false);
		}
		private void ApplyConfiguredLauncherAccountEnvironment(ClientLaunchMode launchMode)
		{
			// JX_* values are only meaningful when launching through the Jagex launcher URI flow.
			if (launchMode != ClientLaunchMode.Launcher)
			{
				return;
			}

			LauncherAccountModel? selected = null;
			if (!string.IsNullOrWhiteSpace(LauncherCharacterId))
			{
				selected = LauncherAccountStore.Load().FirstOrDefault(a => a.CharacterId == LauncherCharacterId)
					?? throw new InvalidOperationException("The original launcher account is no longer configured.");
			}
			var selectedDisplayName = Settings.Default.LauncherSelectedDisplayName;

			// Preferred: rotate through all accounts checked in launcher config (multi-account support).
			if (selected == null && LauncherAccountStore.TryGetNextSelected(out var roundRobinAccount) && roundRobinAccount != null)
			{
				selected = roundRobinAccount;
				Console.WriteLine($"[Orbit][Launcher] Multi-account round-robin selected '{selected.DisplayName}'.");
			}
			else if (selected == null && LauncherAccountStore.TryGetByDisplayName(selectedDisplayName, out var configuredSingle) && configuredSingle != null)
			{
				// Backward-compatible fallback: single selected display name.
				selected = configuredSingle;
				Console.WriteLine($"[Orbit][Launcher] Falling back to single selected account '{selected.DisplayName}'.");
			}

			if (selected == null)
			{
				Console.WriteLine("[Orbit][Launcher] No configured launcher account found (SELECTED list empty and no fallback display name). Launching with current launcher context.");
				return;
			}

			LauncherCharacterId = selected.CharacterId;
			Environment.SetEnvironmentVariable("JX_ACCESS_TOKEN", string.Empty);
			Environment.SetEnvironmentVariable("JX_DISPLAY_NAME", selected.DisplayName ?? string.Empty);
			Environment.SetEnvironmentVariable("JX_CHARACTER_ID", selected.CharacterId ?? string.Empty);
			Environment.SetEnvironmentVariable("JX_REFRESH_TOKEN", string.Empty);
			Environment.SetEnvironmentVariable("JX_SESSION_ID", selected.SessionId ?? string.Empty);

			Console.WriteLine($"[Orbit][Launcher] Applied account environment for '{selected.DisplayName}'.");
		}

		private async Task FindAndSetRsClientAsync()
		{
			var observation = launchObservation ?? throw new InvalidOperationException("No launch observation exists.");
			int? launcherPid = activationProcess?.Id;
			var stopwatch = Stopwatch.StartNew();
			while (stopwatch.Elapsed < ProcessDetectionTimeout)
			{
				loadCancellation.ThrowIfCancellationRequested();
				var candidates = new System.Collections.Generic.List<(int Pid, bool IsLauncherChild)>();
				foreach (var candidate in Process.GetProcessesByName("rs2client"))
				{
					using (candidate)
					{
						try
						{
							if (candidate.HasExited || !observation.IsFresh(candidate.Id, candidate.StartTime.ToUniversalTime())) continue;
							var direct = attemptLaunchMode == ClientLaunchMode.Legacy && candidate.Id == launcherPid;
							var child = launcherPid.HasValue && GetParentProcess(candidate.Id) == launcherPid.Value;
							if (direct || child || !launcherPid.HasValue || stopwatch.Elapsed > TimeSpan.FromSeconds(8))
								candidates.Add((candidate.Id, direct || child));
						}
						catch (InvalidOperationException) { }
						catch (Win32Exception) { }
					}
				}
				var selectedPid = ClientLaunchObservation.SelectCandidate(candidates);
				if (selectedPid.HasValue)
				{
					Process? client = null;
					try
					{
						client = Process.GetProcessById(selectedPid.Value);
						if (!client.HasExited && observation.IsFresh(client.Id, client.StartTime.ToUniversalTime()) && !IsProcessAlreadyBoundToSession(client.Id))
						{
							ParentProcessId = GetParentProcess(client.Id) is var parent && parent > 0 ? parent : null;
							pDocked = client;
							rs2ClientID = client.Id;
							runescapeProcessID = launcherPid ?? 0;
							ProcessChanged?.Invoke(client);
							client = null; // Session now owns the retained process reference.
							activationProcess?.Dispose();
							activationProcess = null;
							return;
						}
					}
					catch (ArgumentException) { }
					catch (InvalidOperationException) { }
					catch (Win32Exception) { }
					finally { client?.Dispose(); }
				}
				await Task.Delay(ProcessPollInterval, loadCancellation).ConfigureAwait(false);
			}
			throw new TimeoutException("No unambiguous new RuneScape client could be associated with this launch request.");
		}
		private static ClientLaunchMode ResolveLaunchMode()
		{
			var configured = Settings.Default.ClientLaunchMode;
			if (string.Equals(configured, "Launcher", StringComparison.OrdinalIgnoreCase))
			{
				return ClientLaunchMode.Launcher;
			}

			return ClientLaunchMode.Legacy;
		}

		private static string ResolveLaunchTarget(ClientLaunchMode launchMode)
		{
			if (launchMode == ClientLaunchMode.Launcher)
			{
				return LauncherUri;
			}

			var legacyPath = ResolveLegacyClientPath();
			if (!string.IsNullOrWhiteSpace(legacyPath))
			{
				return legacyPath;
			}

			throw new FileNotFoundException(
				"[Orbit] Legacy launch mode is enabled but no RuneScape executable could be resolved. " +
				"Switch Client Launch Mode to 'Launcher' in Settings or install a supported client path.");
		}

		private bool IsProcessAlreadyBoundToSession(int processId)
		{
			try
			{
				using var process = Process.GetProcessById(processId);
				return !SessionProcessOwnership.Instance.TryClaim(processId, process.StartTime.ToUniversalTime(), processOwner);
			}
			catch { return true; } // Unknown identity is not permission to bind.
		}

		private static string ResolveLegacyClientPath()
		{
			var candidates = new[]
			{
				@"C:\Program Files (x86)\Jagex Launcher\Games\RuneScape\RuneScape.exe",
				@"C:\Program Files\Jagex\RuneScape Launcher\RuneScape.exe",
				@"C:\ProgramData\Jagex\launcher\rs2client.exe",
				@"C:\Program Files (x86)\Steam\steamapps\common\RuneScape\bin\win64\RuneScape.exe",
				@"C:\Program Files (x86)\Steam\steamapps\common\RuneScape\launcher\rs2client.exe"
			};

			foreach (var candidate in candidates)
			{
				if (System.IO.File.Exists(candidate))
				{
					return candidate;
				}
			}

			return string.Empty;
		}

		private async Task WaitForAndSetDockingWindowAsync()
		{
			var stopwatch = Stopwatch.StartNew();
			while (_dockedHandle == IntPtr.Zero && stopwatch.Elapsed < WindowHandleTimeout)
			{
				try
				{
					loadCancellation.ThrowIfCancellationRequested();
						 pDocked.Refresh();
				}
				catch (InvalidOperationException)
				{
					// Process may have exited or not yet exposed a window; continue polling.
				}

				if (pDocked.HasExited)
				{
					Console.WriteLine("[Orbit] RuneScape process exited before a main window handle became available.");
					break;
				}

				_dockedHandle = pDocked.MainWindowHandle;
				if (_dockedHandle != IntPtr.Zero)
				{
						try
						{
							using var clientProcess = Process.GetProcessById(rs2ClientID);
							rsWindow = FindWindowEx(clientProcess.MainWindowHandle, IntPtr.Zero, "JagRenderView", null);
						}
					catch (Exception)
					{
					}

					Console.WriteLine($"[Orbit] Obtained RuneScape window handle 0x{_dockedHandle.ToInt64():X} after {stopwatch.ElapsedMilliseconds}ms.");
					break;
				}

				await Task.Delay(WindowHandlePollInterval, loadCancellation).ConfigureAwait(false);
			}

			if (_dockedHandle == IntPtr.Zero)
			{
				var message = $"[Orbit] Timed out waiting for RuneScape window handle after {WindowHandleTimeout.TotalSeconds:N1}s.";
				Console.WriteLine(message);
				var timeoutException = new TimeoutException(message);
				throw timeoutException;
			}

			try
			{
				// Best-effort: ensure the process has finished creating its input queue.
				pDocked?.WaitForInputIdle(500);
			}
			catch (InvalidOperationException)
			{
				// Ignore if the process doesn't have a message loop yet.
			}

			dockedWindowIdentity = new HostedWindowIdentity(_dockedHandle, pDocked);
			windowReadyTcs.TrySetResult(true);
			await WaitForInjectionGateAsync(loadCancellation).ConfigureAwait(false);

			IntPtr dockedHandle = IntPtr.Zero;
			try
			{
				panel_DockPanel.Invoke((MethodInvoker)delegate
				{
					loadCancellation.ThrowIfCancellationRequested();
					const int GWL_STYLE = -16;
					const uint WS_CHILD = 0x40000000;
					const uint WS_VISIBLE = 0x10000000;
					const uint WS_CLIPSIBLINGS = 0x04000000;
					const uint WS_CLIPCHILDREN = 0x02000000;
					const uint WS_POPUP = 0x80000000;

					uint curStyle = (uint)GetWindowLong(_dockedHandle, GWL_STYLE);
					if (!originalWindowStyle.HasValue)
					{
						hWndOriginalParent = GetParent(_dockedHandle);
						originalWindowStyle = curStyle;
					}

					uint newStyle = (curStyle & ~WS_POPUP) | WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
					SetWindowLong(_dockedHandle, GWL_STYLE, (uint)(int)newStyle);
					Marshal.SetLastPInvokeError(0);
					var previousParent = SetParent(_dockedHandle, panel_DockPanel.Handle);
					var parentError = Marshal.GetLastPInvokeError();
					if (previousParent == IntPtr.Zero && parentError != 0)
					{
						SetWindowLong(_dockedHandle, GWL_STYLE, originalWindowStyle.Value);
						throw new Win32Exception(parentError, "Unable to dock the game client.");
					}
					hWndParent = panel_DockPanel.Handle;
					DockedRSHwnd = _dockedHandle;
					dockedHandleCache = _dockedHandle;
					dockedHandle = _dockedHandle;

						const int SWP_NOSIZE = 0x0001;
						const int SWP_NOMOVE = 0x0002;
						const int SWP_NOACTIVATE = 0x0010;
						const int SWP_FRAMECHANGED = 0x0020;
						const int SWP_SHOWWINDOW = 0x0040;
						SetWindowPos(_dockedHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
					});
				}
			catch (ObjectDisposedException)
			{
				Console.WriteLine("[Orbit] Dock panel disposed before docking could complete.");
				throw;
			}

			if (dockedHandle != IntPtr.Zero)
			{
				Docked?.Invoke(this, new DockedEventArgs(dockedHandle));
			}
		}

		private async Task DockWindowToPanelAsync()
		{
			if (_dockedHandle != IntPtr.Zero)
			{
				// Invoke your existing ResizeWindow method
				await ResizeWindow();
				Console.WriteLine("Game client docked successfully");
			}
		}

		internal async Task WaitForInjectionGateAsync(CancellationToken cancellationToken = default)
		{
			if (!WaitForInjectionBeforeDock) return;
			await injectionReadyTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		#endregion

		internal System.Drawing.Size DockHostPixelSize => NativeWindowGeometry.ClientSize(hWndParent);

		internal Task<bool> ResizeToHostAsync(CancellationToken token = default)
		{
			var handle = _dockedHandle;
			var parent = hWndParent;
			var identity = dockedWindowIdentity;
			bool Resize()
			{
				lock (nativeWindowSync)
				{
					token.ThrowIfCancellationRequested();
					if (hWndParent != parent || parent == 0 || identity?.IsCurrent != true) return false;
					return NativeWindowGeometry.FitToParent(handle, parent);
				}
			}
			return NativeWindowGeometry.IsCurrentThreadWindow(handle) ? Task.FromResult(Resize()) : Task.Run(Resize, token);
		}

		internal async Task ResizeWindow() => await ResizeToHostAsync(loadCancellation).ConfigureAwait(false);
	}

	[SupportedOSPlatform("windows")]
	public sealed class DockedEventArgs : EventArgs
	{
		public DockedEventArgs(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}
}
