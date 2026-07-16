namespace Orbit.Models;

/// <summary>
/// Identifies the application-shell presentation used to host Orbit's shared
/// sessions, tools, and runtime services.
/// </summary>
public enum OrbitShellMode
{
	/// <summary>
	/// The existing floating-first Orbit shell and compatibility experience.
	/// </summary>
	Classic = 0,

	/// <summary>
	/// The conventional Windows/PowerToys-inspired routed shell.
	/// </summary>
	Fluent = 1,

	/// <summary>
	/// The Orbiter-first dockable Spatial Workbench shell.
	/// </summary>
	Spatial = 2
}
