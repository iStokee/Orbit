using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using static Orbit.Classes.Win32;
using System.ComponentModel;

namespace Orbit
{
	public partial class RSForm : Form
	{
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
				pDocked = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = "rs-launch://www.runescape.com/k=5/l=$(Language:0)/jav_config.ws",
						UseShellExecute = true
					}
				};
				pDocked.Start();
				Console.WriteLine($"[Orbit] Launcher process started (PID {pDocked.Id}). Waiting for bootstrap...");
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
			catch (Exception ex)
			{
				Console.WriteLine($"An exception occurred: {ex}");
				processReadyTcs.TrySetException(ex);
				throw;
			}
		}

		private async Task FindAndSetRsClientAsync()
		{
			if (pDocked == null)
			{
				throw new InvalidOperationException("Launcher process not initialized.");
			}

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

			Console.WriteLine($"[Orbit] Waiting for RuneScape client process spawned by launcher PID {launcherPid}...");
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
				foreach (var candidate in Process.GetProcessesByName("rs2client"))
				{
					try
					{
						var candidateParent = GetParentProcess(candidate.Id);
						if (candidateParent == launcherPid)
						{
							confirmedClient = Process.GetProcessById(candidate.Id);
							break;
						}

						if (candidateParent == 0 && fallbackEnabled)
						{
							try
							{
								if (candidate.StartTime >= launcherStartTime.AddSeconds(-2))
								{
									confirmedClient = Process.GetProcessById(candidate.Id);
									break;
								}
							}
							catch (Exception)
							{
								// Ignore StartTime access failures; continue scanning.
							}
						}
					}
					finally
					{
						candidate.Dispose();
					}
				}

				if (confirmedClient != null)
				{
					rs2client = confirmedClient;
					ClientSettings.rs2client = confirmedClient;
					runescape = launcherProcess;
					ParentProcessId = launcherProcess?.Id;
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

			var message = "[Orbit] RuneScape client process could not be located within the allotted timeout.";
			Console.WriteLine(message);
			var timeoutException = new TimeoutException(message);
			processReadyTcs.TrySetException(timeoutException);
			throw timeoutException;
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
						var clientProcess = Process.GetProcessById(rs2ClientID);
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

				await Task.Factory.StartNew(() =>
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
