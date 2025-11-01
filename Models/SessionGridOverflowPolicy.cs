using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines how the grid layout should react when a host already contains a session.
	/// </summary>
	public enum SessionGridOverflowPolicy
	{
		/// <summary>
		/// Let Dragablz split the host, creating another branch where the tab is dropped.
		/// </summary>
		[Description("Auto split into a new cell")]
		AutoSplit = 0,

		/// <summary>
		/// Allow the host to behave like a stack, keeping the tab inside the same TabablzControl.
		/// </summary>
		[Description("Stack tabs in the same cell")]
		Stack = 1,

		/// <summary>
		/// Reject the drop and keep the tab in its original location.
		/// </summary>
		[Description("Bounce back to previous location")]
		Bounce = 2
	}
}
