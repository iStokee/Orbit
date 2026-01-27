using System.ComponentModel;

namespace Orbit.Models
{
	public enum SessionHeaderDensity
	{
		[Description("Full")]
		Full = 0,

		[Description("Compact")]
		Compact = 1,

		[Description("Minimal")]
		Minimal = 2
	}
}
