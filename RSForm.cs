using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Linq;
using Orbit.Models;
using Orbit.Services;
using static Orbit.Classes.Win32;
using System.ComponentModel;

namespace Orbit
{
	public partial class RSForm : Form
	{
		private enum ClientLaunchMode
		{
			Legacy,
			Launcher
		}

		private string rs2clientWindowTitle;
		private Process RuneScapeProcess;
		private Process process;
		internal IntPtr rsMainWindowHandle;
		internal Process pDocked;
		private readonly TaskCompletionSource<Process> processReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> injectionReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> injectionStartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private static readonly TimeSpan InjectionWaitTimeout = TimeSpan.FromSeconds(6);
		private static readonly TimeSpan InjectionStartGracePeriod = TimeSpan.FromSeconds(2);
		private static readonly TimeSpan ProcessDetectionTimeout = TimeSpan.FromSeconds(45);
		private static readonly TimeSpan ProcessPollInterval = TimeSpan.FromMilliseconds(250);
		private static readonly TimeSpan WindowHandleTimeout = TimeSpan.FromSeconds(25);
		private static readonly TimeSpan WindowHandlePollInterval = TimeSpan.FromMilliseconds(200);
		private static readonly TimeSpan LauncherRefreshDelay = TimeSpan.FromMilliseconds(500);
		private static readonly TimeSpan LauncherStartSettleDelay = TimeSpan.FromMilliseconds(1200);
		private static readonly string LauncherUri = "rs-launch://www.runescape.com/k=5/l=$(Language:0)/jav_config.ws";
		private static readonly SemaphoreSlim LauncherStartSync = new(1, 1);
		private IntPtr hWndOriginalParent;
		private IntPtr hWndParent;
		private uint? originalWindowStyle;
		private bool dockedStylesApplied;
		private bool isCurrentlyDocked;
		private IntPtr dockedHandleCache;
		private const int SW_MAXIMIZE = 3;
		//internal static List<RuneScapeHandler> rsHandlerList = new List<RuneScapeHandler>();
		private bool hasStarted = false;
		Thread StartRS;
		private IntPtr rsWindow;
		private int rs2ClientID;
		private int runescapeProcessID;
		private IntPtr _dockedHandle;
		private Process rs2client = null;
		private Process runescape = null;
		private IntPtr jagOpenGLViewWindowHandler;
		private IntPtr JagWindow;
		private IntPtr wxWindowNR;
		private static readonly object ClientClaimSync = new();
		private static readonly Dictionary<int, WeakReference<RSForm>> ClaimedClientPids = new();
		private int? _claimedClientPid;

		public int? ParentProcessId { get; private set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public IntPtr DockedRSHwnd { get; private set; }

		public IntPtr DockedClientHandle => _dockedHandle;

		[DllImport("user32.dll")]
		internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
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

		internal Task<Process> ProcessReadyTask => processReadyTcs.Task;

		[DefaultValue(true)]
		internal bool WaitForInjectionBeforeDock { get; set; } = true;

		public event EventHandler<DockedEventArgs> Docked;

		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);

		internal void SignalInjectionStarting()
		{
			injectionStartedTcs.TrySetResult(true);
		}

		internal void SignalInjectionReady()
		{
			injectionReadyTcs.TrySetResult(true);
		}

		public RSForm()
		{
			InitializeComponent();
			FormClosed += (_, _) => ReleaseClaimedClientPid();
			// set back color to DarkSlate from dynamic resources
			//this.BackColor = System.Drawing.ColorTranslator.FromHtml("#2f2f2f");
		}

		private void RSForm_Load(object sender, EventArgs e) { }

		public async Task BeginLoad()
		{
			if (hasStarted)
			{
				return;
			}

			this.FormBorderStyle = FormBorderStyle.None;
			_dockedHandle = IntPtr.Zero;
			ParentProcessId = null;

			await Task.Run(() => panel_DockPanel.Invoke((MethodInvoker)delegate
			{
				if (_dockedHandle != IntPtr.Zero)
				{
					var previousParent = SetParent(_dockedHandle, panel_DockPanel.Handle);
					if (hWndOriginalParent == IntPtr.Zero)
					{
						hWndOriginalParent = previousParent;
					}
					isCurrentlyDocked = true;
				}
			}));

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

				// Dock the client to the panel
				await Task.Run(() => panel_DockPanel.Invoke((MethodInvoker)delegate
				{
					if (_dockedHandle != IntPtr.Zero)
					{
						var previousParent = SetParent(_dockedHandle, panel_DockPanel.Handle);
						if (hWndOriginalParent == IntPtr.Zero)
						{
							hWndOriginalParent = previousParent;
						}
						isCurrentlyDocked = true;
					}
				}));

				if (ClientSettings.rs2cPID > 0)
				{
						ResizeWindow();
						Console.WriteLine("Game client resized successfully");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"An error occurred: {ex}");
					processReadyTcs.TrySetException(ex);
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
				if (_dockedHandle != IntPtr.Zero)
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
				if (_dockedHandle == IntPtr.Zero)
				{
					return;
				}

				const int GWL_STYLE = -16;
				const int SWP_NOSIZE = 0x0001;
				const int SWP_NOMOVE = 0x0002;
				const int SWP_NOZORDER = 0x0004;
				const int SWP_NOACTIVATE = 0x0010;
				const int SWP_FRAMECHANGED = 0x0020;
				const int SWP_HIDEWINDOW = 0x0080;

				if (restoreStyles && dockedStylesApplied && originalWindowStyle.HasValue)
				{
					SetWindowLong(_dockedHandle, GWL_STYLE, (uint)((int)originalWindowStyle.Value));
					var restoreFlags = (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
					SetWindowPos(_dockedHandle, IntPtr.Zero, 0, 0, 0, 0, (int)restoreFlags);
					dockedStylesApplied = false;
				}

				if (isCurrentlyDocked)
				{
					if (restoreParent)
					{
						SetParent(_dockedHandle, hWndOriginalParent);
					}
					else
					{
						var hideFlags = (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
						SetWindowPos(_dockedHandle, IntPtr.Zero, 0, 0, 0, 0, (int)hideFlags);
					}

					hWndParent = IntPtr.Zero;
					isCurrentlyDocked = false;
				}

				dockedHandleCache = IntPtr.Zero;
				ReleaseClaimedClientPid();
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
				// best-effort; failures here should not prevent shutdown
			}
		}

		private async Task ExecuteRSLoadAsync()
		{
			if (_dockedHandle != IntPtr.Zero)
			{
				return;
			}

			try
			{
				Console.WriteLine("Starting RS process");
				await StartAndRefreshRSProcessAsync();

				Console.WriteLine("Finding RS client");
				await FindAndSetRsClientAsync();

				Console.WriteLine("Waiting for RS client to be ready");
				await WaitForAndSetDockingWindowAsync();

				Console.WriteLine("Docking RS client");
				await DockWindowToPanelAsync();
			}
			catch (Exception ex)
			{
				// Log or handle the exception
				Console.WriteLine(ex.Message);
				processReadyTcs.TrySetException(ex);
				throw;
			}
		}

		#region RSForm Loading

		private async Task StartAndRefreshRSProcessAsync()
		{
			try
			{
				var launchMode = ResolveLaunchMode();
				var launchTarget = ResolveLaunchTarget(launchMode);
				async Task StartProcessCoreAsync()
				{
					ApplyConfiguredLauncherAccountEnvironment(launchMode);
					pDocked = new Process
					{
						StartInfo = new ProcessStartInfo
						{
							FileName = launchTarget,
							UseShellExecute = true
						}
					};
					pDocked.Start();
					Console.WriteLine($"[Orbit] Session launch started via {launchMode} target '{launchTarget}' (PID {pDocked.Id}). Waiting for bootstrap...");
					await Task.Delay(LauncherRefreshDelay).ConfigureAwait(false);
					try
					{
						pDocked.Refresh();
					}
					catch (InvalidOperationException)
					{
						// Launcher may already have exited once the client starts.
					}
				}

				if (launchMode == ClientLaunchMode.Launcher)
				{
					await LauncherStartSync.WaitAsync().ConfigureAwait(false);
					try
					{
						await StartProcessCoreAsync().ConfigureAwait(false);
						// Keep launches serialized briefly so each process snapshots its own JX_* values.
						await Task.Delay(LauncherStartSettleDelay).ConfigureAwait(false);
					}
					finally
					{
						LauncherStartSync.Release();
					}
				}
				else
				{
					await StartProcessCoreAsync().ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"An exception occurred: {ex}");
				processReadyTcs.TrySetException(ex);
				throw;
			}
		}

		private static void ApplyConfiguredLauncherAccountEnvironment(ClientLaunchMode launchMode)
		{
			// JX_* values are only meaningful when launching through the Jagex launcher URI flow.
			if (launchMode != ClientLaunchMode.Launcher)
			{
				return;
			}

			LauncherAccountModel? selected = null;
			var selectedDisplayName = Settings.Default.LauncherSelectedDisplayName;

			// Preferred: rotate through all accounts checked in launcher config (multi-account support).
			if (LauncherAccountStore.TryGetNextSelected(out var roundRobinAccount) && roundRobinAccount != null)
			{
				selected = roundRobinAccount;
				Console.WriteLine($"[Orbit][Launcher] Multi-account round-robin selected '{selected.DisplayName}'.");
			}
			else if (LauncherAccountStore.TryGetByDisplayName(selectedDisplayName, out var configuredSingle) && configuredSingle != null)
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

			Environment.SetEnvironmentVariable("JX_ACCESS_TOKEN", string.Empty);
			Environment.SetEnvironmentVariable("JX_DISPLAY_NAME", selected.DisplayName ?? string.Empty);
			Environment.SetEnvironmentVariable("JX_CHARACTER_ID", selected.CharacterId ?? string.Empty);
			Environment.SetEnvironmentVariable("JX_REFRESH_TOKEN", string.Empty);
			Environment.SetEnvironmentVariable("JX_SESSION_ID", selected.SessionId ?? string.Empty);

			Console.WriteLine($"[Orbit][Launcher] Applied JX env vars for '{selected.DisplayName}' (CharacterId='{selected.CharacterId}', SessionId='{selected.SessionId}').");
		}

		private async Task FindAndSetRsClientAsync()
		{
			if (pDocked == null)
			{
				throw new InvalidOperationException("Launcher process not initialized.");
			}

			ReleaseClaimedClientPid();

			var launchMode = ResolveLaunchMode();
			var launcherProcess = pDocked;
			var launcherPid = launcherProcess.Id;
			DateTime launcherStartTime;
			try
			{
				launcherStartTime = launcherProcess.StartTime;
			}
			catch (Exception)
			{
				launcherStartTime = DateTime.Now;
			}

			Console.WriteLine($"[Orbit] Waiting for RuneScape client process (mode={launchMode}, started PID={launcherPid})...");
			var stopwatch = Stopwatch.StartNew();
			var fallbackEnabled = false;

			while (stopwatch.Elapsed < ProcessDetectionTimeout)
			{
				try
				{
					launcherProcess.Refresh();
				}
				catch (InvalidOperationException)
				{
					// Launcher may have exited once the game client bootstraps.
				}

				Process? confirmedClient = null;
				if (launchMode == ClientLaunchMode.Legacy && string.Equals(launcherProcess.ProcessName, "rs2client", StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						var candidate = Process.GetProcessById(launcherPid);
						if (!IsProcessAlreadyBoundToAnotherSession(candidate.Id) && TryClaimClientPid(candidate.Id))
						{
							confirmedClient = candidate;
						}
						else
						{
							candidate.Dispose();
						}
					}
					catch (Exception)
					{
						confirmedClient = null;
					}
				}

				if (confirmedClient == null)
				{
					var candidates = Process.GetProcessesByName("rs2client")
						.OrderByDescending(GetProcessStartTimeOrMinValue)
						.ThenByDescending(p => p.Id)
						.ToList();

					foreach (var candidate in candidates)
					{
						try
						{
							if (IsProcessAlreadyBoundToAnotherSession(candidate.Id))
							{
								continue;
							}

							if (!TryClaimClientPid(candidate.Id))
							{
								continue;
							}

							var candidateParent = GetParentProcess(candidate.Id);
							if (candidateParent == launcherPid)
							{
								try
								{
									confirmedClient = Process.GetProcessById(candidate.Id);
								}
								catch
								{
									ReleaseClaimedClientPid();
									continue;
								}

								break;
							}

							if (candidateParent == 0 && fallbackEnabled)
							{
								try
								{
									if (candidate.StartTime >= launcherStartTime.AddSeconds(-2))
									{
										try
										{
											confirmedClient = Process.GetProcessById(candidate.Id);
										}
										catch
										{
											ReleaseClaimedClientPid();
											continue;
										}

										break;
									}
								}
								catch (Exception)
								{
									// Ignore StartTime access failures; continue scanning.
								}
							}

							ReleaseClaimedClientPid();
						}
						finally
						{
							candidate.Dispose();
						}
					}
				}

				if (confirmedClient != null)
				{
					rs2client = confirmedClient;
					ClientSettings.rs2client = confirmedClient;
					runescape = launcherProcess;
					ParentProcessId = confirmedClient.Id == launcherPid ? null : launcherProcess?.Id;
					try
					{
						JagWindow = FindWindowEx(confirmedClient.MainWindowHandle, IntPtr.Zero, "JagWindow", null);
					}
					catch (Exception)
					{
						JagWindow = IntPtr.Zero;
					}

					try
					{
						wxWindowNR = launcherProcess?.MainWindowHandle != IntPtr.Zero
							? FindWindowEx(launcherProcess.MainWindowHandle, IntPtr.Zero, "wxWindowNR", null)
							: IntPtr.Zero;
					}
					catch (Exception)
					{
						wxWindowNR = IntPtr.Zero;
					}

					rs2ClientID = confirmedClient.Id;
					ClientSettings.rs2cPID = confirmedClient.Id;
					runescapeProcessID = launcherPid;
					ClientSettings.runescapePID = launcherPid;
					pDocked = confirmedClient;
					processReadyTcs.TrySetResult(confirmedClient);
					Console.WriteLine($"[Orbit] RuneScape client detected (PID {confirmedClient.Id})");
					return;
				}

				if (!fallbackEnabled && stopwatch.Elapsed > TimeSpan.FromSeconds(8))
				{
					Console.WriteLine("[Orbit] Falling back to StartTime matching while waiting for client parent linkage...");
					fallbackEnabled = true;
				}

				await Task.Delay(ProcessPollInterval).ConfigureAwait(false);
			}

			if (launchMode == ClientLaunchMode.Legacy && !launcherProcess.HasExited)
			{
				Console.WriteLine("[Orbit] Falling back to started process as dock target for legacy mode.");
				if (!TryClaimClientPid(launcherProcess.Id))
				{
					var claimMessage = $"[Orbit] Legacy fallback process PID {launcherProcess.Id} is already claimed by another session.";
					Console.WriteLine(claimMessage);
					var claimException = new InvalidOperationException(claimMessage);
					processReadyTcs.TrySetException(claimException);
					throw claimException;
				}

				rs2client = launcherProcess;
				ClientSettings.rs2client = launcherProcess;
				runescape = launcherProcess;
				ParentProcessId = null;
				rs2ClientID = launcherProcess.Id;
				ClientSettings.rs2cPID = launcherProcess.Id;
				runescapeProcessID = launcherPid;
				ClientSettings.runescapePID = launcherPid;
				pDocked = launcherProcess;
				processReadyTcs.TrySetResult(launcherProcess);
				return;
			}

			var message = "[Orbit] RuneScape client process could not be located within the allotted timeout.";
			Console.WriteLine(message);
			var timeoutException = new TimeoutException(message);
			ReleaseClaimedClientPid();
			processReadyTcs.TrySetException(timeoutException);
			throw timeoutException;
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

		private static DateTime GetProcessStartTimeOrMinValue(Process process)
		{
			try
			{
				return process.StartTime;
			}
			catch
			{
				return DateTime.MinValue;
			}
		}

		private static bool IsProcessAlreadyBoundToAnotherSession(int pid)
		{
			try
			{
				var app = System.Windows.Application.Current;
				if (app?.Dispatcher == null)
				{
					return false;
				}

				if (app.Dispatcher.CheckAccess())
				{
					return SessionCollectionService.Instance.Sessions.Any(s =>
						s?.RSProcess != null &&
						!s.RSProcess.HasExited &&
						s.RSProcess.Id == pid &&
						s.State != SessionState.Closed);
				}

				return app.Dispatcher.Invoke(() => SessionCollectionService.Instance.Sessions.Any(s =>
					s?.RSProcess != null &&
					!s.RSProcess.HasExited &&
					s.RSProcess.Id == pid &&
					s.State != SessionState.Closed));
			}
			catch
			{
				return false;
			}
		}

		private bool TryClaimClientPid(int pid)
		{
			lock (ClientClaimSync)
			{
				CleanupStaleClaims_NoLock();

				if (ClaimedClientPids.TryGetValue(pid, out var existingRef))
				{
					if (existingRef.TryGetTarget(out var existingOwner))
					{
						if (ReferenceEquals(existingOwner, this))
						{
							_claimedClientPid = pid;
							return true;
						}

						Console.WriteLine($"[Orbit][Launch] PID {pid} is already claimed by another pending session.");
						return false;
					}

					ClaimedClientPids.Remove(pid);
				}

				ClaimedClientPids[pid] = new WeakReference<RSForm>(this);
				_claimedClientPid = pid;
				return true;
			}
		}

		private void ReleaseClaimedClientPid()
		{
			lock (ClientClaimSync)
			{
				if (_claimedClientPid.HasValue &&
					ClaimedClientPids.TryGetValue(_claimedClientPid.Value, out var ownerRef) &&
					ownerRef.TryGetTarget(out var owner) &&
					ReferenceEquals(owner, this))
				{
					ClaimedClientPids.Remove(_claimedClientPid.Value);
				}

				_claimedClientPid = null;
				CleanupStaleClaims_NoLock();
			}
		}

		private static void CleanupStaleClaims_NoLock()
		{
			var stalePids = ClaimedClientPids
				.Where(kvp => !kvp.Value.TryGetTarget(out _))
				.Select(kvp => kvp.Key)
				.ToArray();

			foreach (var pid in stalePids)
			{
				ClaimedClientPids.Remove(pid);
			}
		}

		private async Task WaitForAndSetDockingWindowAsync()
		{
			var stopwatch = Stopwatch.StartNew();
			while (_dockedHandle == IntPtr.Zero && stopwatch.Elapsed < WindowHandleTimeout)
			{
				try
				{
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
					ClientSettings.gameHandle = _dockedHandle;
						try
						{
							using var clientProcess = Process.GetProcessById(rs2ClientID);
							rsWindow = FindWindowEx(clientProcess.MainWindowHandle, IntPtr.Zero, "JagRenderView", null);
							ClientSettings.jagOpenGL = rsWindow != IntPtr.Zero ? rsWindow : _dockedHandle;
						}
					catch (Exception)
					{
						ClientSettings.jagOpenGL = _dockedHandle;
					}

					Console.WriteLine($"[Orbit] Obtained RuneScape window handle 0x{_dockedHandle.ToInt64():X} after {stopwatch.ElapsedMilliseconds}ms.");
					break;
				}

				await Task.Delay(WindowHandlePollInterval).ConfigureAwait(false);
			}

			if (_dockedHandle == IntPtr.Zero)
			{
				var message = $"[Orbit] Timed out waiting for RuneScape window handle after {WindowHandleTimeout.TotalSeconds:N1}s.";
				Console.WriteLine(message);
				var timeoutException = new TimeoutException(message);
				processReadyTcs.TrySetException(timeoutException);
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

			await WaitForInjectionGateAsync().ConfigureAwait(false);

			IntPtr dockedHandle = IntPtr.Zero;
			try
			{
				panel_DockPanel.Invoke((MethodInvoker)delegate
				{
					var previousParent = SetParent(_dockedHandle, panel_DockPanel.Handle);
					if (hWndOriginalParent == IntPtr.Zero)
					{
						hWndOriginalParent = previousParent;
					}
					hWndParent = panel_DockPanel.Handle;
					DockedRSHwnd = _dockedHandle;
					dockedHandleCache = _dockedHandle;
					dockedHandle = _dockedHandle;

					const int GWL_STYLE = -16;
					const uint WS_CHILD = 0x40000000;
					const uint WS_VISIBLE = 0x10000000;
					const uint WS_CLIPSIBLINGS = 0x04000000;
					const uint WS_CLIPCHILDREN = 0x02000000;
					const uint WS_POPUP = 0x80000000;

					uint curStyle = (uint)GetWindowLong(_dockedHandle, GWL_STYLE);
					if (!originalWindowStyle.HasValue)
					{
						originalWindowStyle = curStyle;
					}

					uint newStyle = (curStyle & ~WS_POPUP) | WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
					SetWindowLong(_dockedHandle, GWL_STYLE, (uint)(int)newStyle);
					dockedStylesApplied = true;
					isCurrentlyDocked = true;

					const int SWP_NOSIZE = 0x0001;
					const int SWP_NOMOVE = 0x0002;
					const int SWP_NOACTIVATE = 0x0010;
					const int SWP_FRAMECHANGED = 0x0020;
					SetWindowPos(_dockedHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
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
			if (ClientSettings.rs2cPID > 0)
			{
				// Invoke your existing ResizeWindow method
				await ResizeWindow();
				Console.WriteLine("Game client docked successfully");
			}
		}

		private async Task WaitForInjectionGateAsync()
		{
			if (!WaitForInjectionBeforeDock)
			{
				Console.WriteLine("[Orbit] Injection-before-dock disabled; proceeding to dock immediately.");
				return;
			}

			if (injectionReadyTcs.Task.IsCompleted)
			{
				Console.WriteLine("[Orbit] Injection already signalled ready prior to docking.");
				return;
			}

			// Only defer docking if the injection pipeline has actually started.
			if (!injectionStartedTcs.Task.IsCompleted)
			{
				Console.WriteLine("[Orbit] Injection has not started yet; docking immediately to avoid idle wait.");
				return;
			}

			Console.WriteLine($"[Orbit] Injection started; waiting up to {InjectionWaitTimeout.TotalSeconds:N1}s before docking...");
			var injectionCompleted = await Task.WhenAny(injectionReadyTcs.Task, Task.Delay(InjectionWaitTimeout)).ConfigureAwait(false);
			if (injectionCompleted == injectionReadyTcs.Task)
			{
				Console.WriteLine("[Orbit] Injection completed before docking.");
				return;
			}

			Console.WriteLine("[Orbit] Injection still running after grace window; docking now and allowing injection to finish in the background.");
		}

		#endregion

		internal async Task ResizeWindowOvl(int width, int height)
		{
			try
			{
				IntPtr handle = _dockedHandle;

				await Task.Run(() =>
				{
					const int SWP_SHOWWINDOW = 0x0040;
					const int SWP_FRAMECHANGED = 0x0020;
					SetWindowPos(handle, IntPtr.Zero, -8, -32, width + 16, height + 40, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
					Debug.WriteLine("Resized RSForm window");
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"An error occurred while resizing the window: {ex}");
			}
		}

		internal async Task ResizeWindow()
		{
			try
			{
				IntPtr handle = _dockedHandle;
				int width = panel_DockPanel.Width + 16;
				int height = panel_DockPanel.Height + 40;

					await Task.Run(() =>
					{
						// Force the docked window to top within the panel and refresh frame
						const int SWP_SHOWWINDOW = 0x0040;
						const int SWP_FRAMECHANGED = 0x0020;
						SetWindowPos(handle, IntPtr.Zero, -8, -32, width, height, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
					Console.WriteLine("Moved window");
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"An error occurred while resizing the window: {ex}");
			}
		}
	}

	public sealed class DockedEventArgs : EventArgs
	{
		public DockedEventArgs(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}
}
