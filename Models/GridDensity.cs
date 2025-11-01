using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines the grid layout density (number of cells)
	/// </summary>
	public enum GridDensity
	{
		/// <summary>
		/// Single fullscreen cell (1x1)
		/// </summary>
		[Description("Fullscreen (1x1)")]
		Single = 1,

		/// <summary>
		/// Standard 2x2 grid (4 cells)
		/// </summary>
		[Description("Standard (2x2)")]
		Standard = 2,

		/// <summary>
		/// Dense 3x3 grid (9 cells)
		/// </summary>
		[Description("Dense (3x3)")]
		Dense = 3
	}
}
