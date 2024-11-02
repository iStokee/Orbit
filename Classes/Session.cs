using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.Integration;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Orbit.Views;

namespace Orbit.Classes
{
	public class Session
	{

		public Guid Id { get; set; }
		public string Name { get; set; }
		public DateTime CreatedAt { get; set; }
		public ChildClientView HostControl { get; set; }
		public RSForm RSForm { get; set; }
		public Process ExternalProcess { get; set; }
		public IntPtr ExternalHandle { get; set; }

		// New properties
		public bool IsClientLoaded { get; set; }
		public bool ClientLoaded { get; set; }
		public string ClientStatus { get; set; }
		public Guid BotId { get; set; } = Guid.NewGuid();

		public Process RSProcess { get; set; } // Add this property

		public void Resize(double width, double height)
		{
			if (HostControl is ChildClientView clientView)
			{
				// Pass new dimensions to ChildClientView to resize properly
				clientView.ResizeWindowAsync((int)width, (int)height);
			}
		}


		public void KillProcess()
		{
			try
			{
				if (RSProcess != null && !RSProcess.HasExited)
				{
					RSProcess.Kill();
					RSProcess.Dispose();
				}
			}
			catch (Exception ex)
			{
				// Handle exception (e.g., log it)
			}
		}



		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		public void SetFocus()
		{
			if (ExternalHandle != IntPtr.Zero)
			{
				SetForegroundWindow(ExternalHandle);
			}
		}
	}
}
