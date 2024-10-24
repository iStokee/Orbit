using System;
using System.Diagnostics;

namespace Orbit.Classes
{
	internal class RSClient
    {
        internal Process rs2Process { get; set; }
        internal IntPtr rs2hwndID { get; set; }
    }
}
