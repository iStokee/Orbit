using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines the grid positions where sessions can be snapped
	/// </summary>
	public enum SessionGridPosition
	{
		/// <summary>
		/// Session is not snapped to grid (default tab behavior)
		/// </summary>
		[Description("Not snapped")]
		None = 0,

		/// <summary>
		/// Top-left corner of the grid
		/// </summary>
		[Description("Top left")]
		TopLeft = 1,

		/// <summary>
		/// Top-right corner of the grid
		/// </summary>
		[Description("Top right")]
		TopRight = 2,

		/// <summary>
		/// Bottom-left corner of the grid
		/// </summary>
		[Description("Bottom left")]
		BottomLeft = 3,

		/// <summary>
		/// Bottom-right corner of the grid
		/// </summary>
		[Description("Bottom right")]
		BottomRight = 4,

		/// <summary>
		/// Left half of the screen
		/// </summary>
		[Description("Left half")]
		Left = 5,

		/// <summary>
		/// Right half of the screen
		/// </summary>
		[Description("Right half")]
		Right = 6,

		/// <summary>
		/// Top half of the screen
		/// </summary>
		[Description("Top half")]
		Top = 7,

		/// <summary>
		/// Bottom half of the screen
		/// </summary>
		[Description("Bottom half")]
		Bottom = 8,

		/// <summary>
		/// Full screen (centered)
		/// </summary>
		[Description("Full screen")]
		Fullscreen = 9,

		/// <summary>
		/// Center cell of a 3x3 grid.
		/// </summary>
		[Description("Center (3x3)")]
		Center = 10,

		/// <summary>
		/// Top-center cell of a 3x3 grid.
		/// </summary>
		[Description("Top center (3x3)")]
		TopCenter = 11,

		/// <summary>
		/// Middle-left cell of a 3x3 grid.
		/// </summary>
		[Description("Middle left (3x3)")]
		MiddleLeft = 12,

		/// <summary>
		/// Middle-right cell of a 3x3 grid.
		/// </summary>
		[Description("Middle right (3x3)")]
		MiddleRight = 13,

		/// <summary>
		/// Bottom-center cell of a 3x3 grid.
		/// </summary>
		[Description("Bottom center (3x3)")]
		BottomCenter = 14
	}
}
