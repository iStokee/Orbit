using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.Integration;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Orbit.Classes
{
	public class Session
	{

		public Guid Id { get; set; }
		public string Name { get; set; }
		public DateTime CreatedAt { get; set; }
		public WindowsFormsHost HostControl { get; set; }
		public RSForm RSForm { get; set; }
		public Process ExternalProcess { get; set; }
		public IntPtr ExternalHandle { get; set; }

		// New properties
		public bool IsClientLoaded { get; set; }
		public bool ClientLoaded { get; set; }
		public string ClientStatus { get; set; }
		public Guid BotId { get; set; } = Guid.NewGuid();
		


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
