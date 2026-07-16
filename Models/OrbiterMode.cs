namespace Orbit.Models;

/// <summary>
/// Represents the mutually exclusive presentation modes of the Orbiter.
/// </summary>
public enum OrbiterMode
{
	/// <summary>
	/// The compact, quiet handle is available without an expanded surface.
	/// </summary>
	Dormant = 0,

	/// <summary>
	/// Application-wide destinations, launch actions, and recent work are shown.
	/// </summary>
	GlobalNavigation = 1,

	/// <summary>
	/// A searchable command and destination lens is active.
	/// </summary>
	CommandLens = 2,

	/// <summary>
	/// Global destinations are temporarily replaced by commands for the selected object.
	/// </summary>
	Context = 3
}
