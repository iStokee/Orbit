using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines where new sessions should dock when launched
	/// </summary>
	public enum SessionLaunchBehavior
	{
		/// <summary>
		/// Launch sessions as individual tabs alongside tools (classic behavior)
		/// </summary>
		[Description("Individual Tabs")]
		IndividualTabs,

		/// <summary>
		/// Launch sessions directly into Orbit View grid
		/// </summary>
		[Description("Orbit View")]
		OrbitView,

		/// <summary>
		/// Ask user each time where to launch
		/// </summary>
		[Description("Ask Each Time")]
		Ask
	}
}
