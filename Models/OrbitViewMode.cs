using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Determines how new sessions are displayed by default
	/// </summary>
	public enum OrbitViewMode
	{
		/// <summary>
		/// Sessions appear in the Orbit View grid layout (live embedded windows)
		/// </summary>
		[Description("Orbit View")]
		OrbitView,

		/// <summary>
		/// Sessions appear in traditional tabbed interface
		/// </summary>
		[Description("Tabs")]
		Tabs
	}
}
