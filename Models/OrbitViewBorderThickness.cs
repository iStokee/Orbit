using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines border thickness for Orbit View grid cells
	/// </summary>
	public enum OrbitViewBorderThickness
	{
		/// <summary>
		/// No borders - maximum space (0px)
		/// </summary>
		[Description("None")]
		None = 0,

		/// <summary>
		/// Minimal borders - subtle separation (1px)
		/// </summary>
		[Description("Minimal")]
		Minimal = 1,

		/// <summary>
		/// Standard borders - clear definition (2px)
		/// </summary>
		[Description("Standard")]
		Standard = 2
	}
}
