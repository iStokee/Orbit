using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Determines which assignment wins when the saved layout conflicts with a user drop.
	/// </summary>
	public enum SessionGridConflictResolution
	{
		/// <summary>
		/// Prioritise the most recent user drop, updating the persisted layout.
		/// </summary>
		[Description("Prefer the most recent drop")]
		PreferUserDrop = 0,

		/// <summary>
		/// Keep the persisted layout and bounce the tab back to its saved location.
		/// </summary>
		[Description("Respect the saved layout")]
		PreferSavedLayout = 1,

		/// <summary>
		/// Ask the user which layout to keep (implemented as a toast/notification in the UI).
		/// </summary>
		[Description("Ask before overwriting")]
		Prompt = 2
	}
}
