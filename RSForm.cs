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
		private static readonly TimeSpan InjectionWaitTimeout = TimeSpan.FromSeconds(15);
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

		internal void Undock()
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

					if (dockedStylesApplied && originalWindowStyle.HasValue)
					{
						SetWindowLong(_dockedHandle, GWL_STYLE, (uint)((int)originalWindowStyle.Value));
						SetWindowPos(
							_dockedHandle,
						IntPtr.Zero,
						0,
						0,
						0,
						0,
						SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
					dockedStylesApplied = false;
				}

				if (isCurrentlyDocked)
				{
						SetParent(_dockedHandle, hWndOriginalParent);
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
				Console.WriteLine("Process started, waiting for just a moment...");
				Thread.Sleep(9000);
				pDocked.Refresh();
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
			//Your logic to find the RS client and set associated properties
			Process runescapeProcess = pDocked;
			bool found = false;
			int counter = 0;

			while (!found && !pDocked.HasExited)
			{
				pDocked.Refresh();
				counter++;

				Process[] processes = Process.GetProcessesByName("rs2client");
				foreach (Process p in processes)
				{
					if (GetParentProcess(p.Id) == pDocked.Id)
					{
						rs2client = p;
						ClientSettings.rs2client = p;
						runescape = runescapeProcess;
						ParentProcessId = runescapeProcess?.Id;
						JagWindow = FindWindowEx(p.MainWindowHandle, IntPtr.Zero, "JagWindow", null);
						wxWindowNR = FindWindowEx(runescapeProcess.MainWindowHandle, IntPtr.Zero, "wxWindowNR", null);

						rs2ClientID = p.Id;
						ClientSettings.rs2cPID = p.Id;
						runescapeProcessID = pDocked.Id;
						ClientSettings.runescapePID = pDocked.Id;
						pDocked = p;
						found = true;
						processReadyTcs.TrySetResult(pDocked);

						break;
					}
				}
			}

			if (!found)
			{
				throw new InvalidOperationException("RuneScape client process could not be located.");
			}
		}

		private async Task WaitForAndSetDockingWindowAsync()
		{
			// Your logic to wait for the docking window and set _dockedHandle
			while (_dockedHandle == IntPtr.Zero)
			{
				//wait for the window to be ready for input;

				try
				{
					pDocked.WaitForInputIdle(500);
				}
				catch (InvalidOperationException ex)
				{
					//  ConsoleRespond($"Input idle: {ex}");
				}

				pDocked.Refresh(); //update process info
				if (pDocked.HasExited)
				{

					break; //abort if the process finished before we got a handle.
				}

				_dockedHandle = pDocked.MainWindowHandle;  //cache the window handle
				ClientSettings.gameHandle = _dockedHandle;
				Process q = Process.GetProcessById(rs2ClientID);
				rsWindow = FindWindowEx(q.MainWindowHandle, IntPtr.Zero, "JagRenderView", null);
				ClientSettings.jagOpenGL = rsWindow;
			}

			// ensure MESharp injection has been attempted before we re-parent the window
			if (WaitForInjectionBeforeDock && !injectionReadyTcs.Task.IsCompleted)
			{
				Console.WriteLine("[Orbit] Waiting for MESharp injection signal before docking RuneScape window...");
				var completed = await Task.WhenAny(injectionReadyTcs.Task, Task.Delay(InjectionWaitTimeout)).ConfigureAwait(true);
				if (completed != injectionReadyTcs.Task)
				{
					Console.WriteLine("[Orbit] Injection signal timed out; proceeding to dock the client anyway.");
				}
			}

			// dock the client to the panel
			IntPtr dockedHandle = IntPtr.Zero;
			panel_DockPanel.Invoke((MethodInvoker)delegate
			{
				var previousParent = SetParent(_dockedHandle, panel_DockPanel.Handle);
				if (hWndOriginalParent == IntPtr.Zero)
				{
					hWndOriginalParent = previousParent;
				}
				hWndParent = panel_DockPanel.Handle;
					// ClientSettings.cameraHandle = panel1.Handle;
					DockedRSHwnd = _dockedHandle;
					dockedHandleCache = _dockedHandle;
					dockedHandle = _dockedHandle;

				// Ensure correct child window styles and refresh frame
				const int GWL_STYLE = -16;
				const uint WS_CHILD = 0x40000000;
				const uint WS_VISIBLE = 0x10000000;
				const uint WS_CLIPSIBLINGS = 0x04000000;
				const uint WS_CLIPCHILDREN = 0x02000000;
				const uint WS_POPUP = 0x80000000; // <- now valid as uint
				uint curStyle = (uint)GetWindowLong(_dockedHandle, GWL_STYLE);
				if (!originalWindowStyle.HasValue)
				{
					originalWindowStyle = curStyle;
				}
				uint newStyle = (curStyle & ~WS_POPUP) |
								WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
				SetWindowLong(_dockedHandle, GWL_STYLE, (uint)(int)newStyle);
				dockedStylesApplied = true;
				isCurrentlyDocked = true;

				// Apply style change without moving or resizing yet
				const int SWP_NOSIZE = 0x0001;
				const int SWP_NOMOVE = 0x0002;
				const int SWP_NOACTIVATE = 0x0010;
				const int SWP_FRAMECHANGED = 0x0020;
				SetWindowPos(_dockedHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
			});

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
					Console.WriteLine("Resized RSForm window");
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
