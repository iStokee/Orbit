using System;

namespace Orbit
{
	public static class AppVersion
	{
		public const string Current = "1.0.6";
		public const string InformationalVersion = Current;
		public const string AssemblyVersion = Current + ".1";
		public const string FileVersion = AssemblyVersion;

		private static readonly Version _parsed;

		static AppVersion()
		{
			if (!Version.TryParse(AssemblyVersion, out var parsed))
			{
				parsed = new Version(0, 0, 0, 0);
			}

			_parsed = parsed;
		}

		public static Version Parsed => _parsed;
		public static string Display => AssemblyVersion;
	}
}
