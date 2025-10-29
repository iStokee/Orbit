using System;
using System.Diagnostics;
using System.Management;
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
		internal static string rs2clientWindowTitle;
		internal static Process RuneScapeProcess;
		internal static Process process;
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
		private const int SW_MAXIMIZE = 3;
		//internal static List<RuneScapeHandler> rsHandlerList = new List<RuneScapeHandler>();
		private bool hasStarted = false;
		Thread StartRS;
		internal static IntPtr rsWindow;
		internal static int rs2ClientID;
		internal static int runescapeProcessID;
		internal static IntPtr hWndDocked;
		internal static Process rs2client = null;
		internal static Process runescape = null;
		internal static IntPtr jagOpenGLViewWindowHandler;
		internal static IntPtr JagWindow;
		internal static IntPtr wxWindowNR;

		public int? ParentProcessId { get; private set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int DockedRSHwnd { get; set; }

        [DllImport("user32.dll")]
		internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
		[DllImport("user32.dll", SetLastError = true)]
		internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		internal static int GetParentProcess(int Id)
		{
			int parentPid = 0;
			using (ManagementObject mo = new ManagementObject("win32_process.handle='" + Id.ToString() + "'"))
			{
				try
				{
					mo.Get();
					parentPid = Convert.ToInt32(mo["ParentProcessId"]);
				}
				catch (ArgumentException e)
				{

				}
			}
			return parentPid;
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
			hWndDocked = IntPtr.Zero;
			ParentProcessId = null;

			await Task.Run(() => panel_DockPanel.Invoke((MethodInvoker)delegate
			{
				if (hWndDocked != IntPtr.Zero)
				{
					var previousParent = SetParent(hWndDocked, panel_DockPanel.Handle);
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
			Console.WriteLine($"Finished loading {hWndDocked}");
		}

		public async Task LoadRS()
		{
			if (hWndDocked != IntPtr.Zero)
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
					if (hWndDocked != IntPtr.Zero)
					{
						var previousParent = SetParent(hWndDocked, panel_DockPanel.Handle);
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

		internal void FocusGameWindow()
		{
			try
			{
				if (hWndDocked != IntPtr.Zero)
				{
					SetFocus(hWndDocked);
				}
			}
			catch { /* best effort */ }
		}

		internal void Undock()
		{
			void PerformUndock()
			{
				if (hWndDocked == IntPtr.Zero)
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
					SetWindowLong(hWndDocked, GWL_STYLE, (uint)((int)originalWindowStyle.Value));
					SetWindowPos(
						hWndDocked,
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
					SetParent(hWndDocked, hWndOriginalParent);
					hWndParent = IntPtr.Zero;
					isCurrentlyDocked = false;
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
				// best-effort; failures here should not prevent shutdown
			}
		}

		private async Task ExecuteRSLoadAsync()
		{
			if (hWndDocked != IntPtr.Zero)
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
			// Your logic to wait for the docking window and set hWndDocked
			while (hWndDocked == IntPtr.Zero)
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

				hWndDocked = pDocked.MainWindowHandle;  //cache the window handle
				ClientSettings.gameHandle = hWndDocked;
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
				var previousParent = SetParent(hWndDocked, panel_DockPanel.Handle);
				if (hWndOriginalParent == IntPtr.Zero)
				{
					hWndOriginalParent = previousParent;
				}
				hWndParent = panel_DockPanel.Handle;
				// ClientSettings.cameraHandle = panel1.Handle;
				DockedRSHwnd = (int)hWndDocked;
				dockedHandle = hWndDocked;

				// Ensure correct child window styles and refresh frame
				const int GWL_STYLE = -16;
				const uint WS_CHILD = 0x40000000;
				const uint WS_VISIBLE = 0x10000000;
				const uint WS_CLIPSIBLINGS = 0x04000000;
				const uint WS_CLIPCHILDREN = 0x02000000;
				const uint WS_POPUP = 0x80000000; // <- now valid as uint
				uint curStyle = (uint)GetWindowLong(hWndDocked, GWL_STYLE);
				if (!originalWindowStyle.HasValue)
				{
					originalWindowStyle = curStyle;
				}
				uint newStyle = (curStyle & ~WS_POPUP) |
								WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
				SetWindowLong(hWndDocked, GWL_STYLE, (uint)(int)newStyle);
				dockedStylesApplied = true;
				isCurrentlyDocked = true;

				// Apply style change without moving or resizing yet
				const int SWP_NOSIZE = 0x0001;
				const int SWP_NOMOVE = 0x0002;
				const int SWP_NOACTIVATE = 0x0010;
				const int SWP_FRAMECHANGED = 0x0020;
				SetWindowPos(hWndDocked, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
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
				IntPtr handle = hWndDocked;

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
				IntPtr handle = hWndDocked;
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
