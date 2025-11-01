using System.ComponentModel;

namespace Orbit.Models
{
	/// <summary>
	/// Defines tab header height for Orbit View session tabs
	/// </summary>
	public enum OrbitViewTabHeaderSize
	{
		/// <summary>
		/// Compact headers - minimum vertical space (24px)
		/// </summary>
		[Description("Compact")]
		Compact = 0,

		/// <summary>
		/// Standard headers - balanced size (32px)
		/// </summary>
		[Description("Standard")]
		Standard = 1,

		/// <summary>
		/// Comfortable headers - easy to grab (40px)
		/// </summary>
		[Description("Comfortable")]
		Comfortable = 2
	}
}
