namespace Orbit.Models
{
	/// <summary>
	/// Defines the type of session being managed by Orbit
	/// </summary>
	public enum SessionType
	{
		/// <summary>
		/// RuneScape 3 client window
		/// </summary>
		RuneScape,

		/// <summary>
		/// External script UI window (docked from another process)
		/// </summary>
		ExternalScript
	}
}
