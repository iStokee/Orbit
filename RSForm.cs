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

namespace Orbit
{
	public partial class RSForm : Form
	{
		internal static string rs2clientWindowTitle;
		internal static Process RuneScapeProcess;
		internal static Process process;
		internal IntPtr rsMainWindowHandle;
		internal Process pDocked;
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

		[DllImport("user32.dll")]
		internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
		[DllImport("user32.dll")]
		internal static extern int SetWindowText(IntPtr hWnd, string text);
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
			}

		}

		private async Task ExecuteRSLoadAsync_1()
		{
			if (hWndDocked != IntPtr.Zero) //don't do anything if there's already a window docked.
			{
				return;
			};
			hWndParent = IntPtr.Zero;
			pDocked = Process.Start("rs-launch://www.runescape.com/k=5/l=$(Language:0)/jav_config.ws");
			await Task.Delay(3000);
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
						//process = p;
						pDocked = p;
						found = true;

						break;
					}
				}
			}

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
			});

			if (ClientSettings.rs2cPID > 0)
			{
				await ResizeWindow();
				Console.WriteLine("Game client docked succesfully");
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
						//process = p;
						pDocked = p;
						//Console.WriteLine($"pDocked: {pDocked}");
						found = true;

						break;
					}
				}
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
					MoveWindow(handle, -8, -32, width + 16, height + 40, true);
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
					MoveWindow(handle, -8, -32, width, height, true);
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
