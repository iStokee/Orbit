using System;

namespace Orbit
{
	public static class AppVersion
	{
		public const string Current = "1.0.4";
		public const string AssemblyVersion = Current + ".0";
		public const string FileVersion = Current + ".0";
		public const string InformationalVersion = Current;

		public static Version Parsed =>
			Version.TryParse(Current, out var parsed) ? parsed : new Version(0, 0, 0, 0);

		public static string Display => Current;
	}
}
