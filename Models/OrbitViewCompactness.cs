using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines UI spacing density for Orbit View
	/// </summary>
	public enum OrbitViewCompactness
	{
		/// <summary>
		/// Minimal spacing - maximum game space (1pt margins)
		/// </summary>
		[Description("Minimal")]
		Minimal = 0,

		/// <summary>
		/// Moderate spacing - balanced (5pt margins)
		/// </summary>
		[Description("Moderate")]
		Moderate = 1,

		/// <summary>
		/// Maximum spacing - comfortable (10pt margins)
		/// </summary>
		[Description("Maximum")]
		Maximum = 2
	}
}
