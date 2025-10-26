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
		private IntPtr hWndOriginalParent;
		private IntPtr hWndParent;
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

		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);

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

			await Task.Run(() => panel_DockPanel.Invoke((MethodInvoker)delegate
			{
				hWndOriginalParent = SetParent(hWndDocked, panel_DockPanel.Handle);
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
					hWndOriginalParent = SetParent(hWndDocked, panel_DockPanel.Handle);
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
					pDocked.WaitForInputIdle(1000);
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

			// dock the client to the panel
			panel_DockPanel.Invoke((MethodInvoker)delegate
			{
				hWndOriginalParent = SetParent(hWndDocked, panel_DockPanel.Handle);
				// ClientSettings.cameraHandle = panel1.Handle;
				DockedRSHwnd = (int)hWndDocked;

				// Ensure correct child window styles and refresh frame
				const int GWL_STYLE = -16;
				const uint WS_CHILD = 0x40000000;
				const uint WS_VISIBLE = 0x10000000;
				const uint WS_CLIPSIBLINGS = 0x04000000;
				const uint WS_CLIPCHILDREN = 0x02000000;
				const uint WS_POPUP = 0x80000000; // <- now valid as uint
				uint curStyle = (uint)GetWindowLong(hWndDocked, GWL_STYLE);
				uint newStyle = (curStyle & ~WS_POPUP) |
								WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
				SetWindowLong(hWndDocked, GWL_STYLE, (uint)(int)newStyle);

				// Apply style change without moving or resizing yet
				const int SWP_NOSIZE = 0x0001;
				const int SWP_NOMOVE = 0x0002;
				const int SWP_NOACTIVATE = 0x0010;
				const int SWP_FRAMECHANGED = 0x0020;
				SetWindowPos(hWndDocked, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
			});
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
}
