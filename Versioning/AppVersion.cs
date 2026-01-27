using System;

namespace Orbit
{
	public static class AppVersion
	{
		public const string Current = "1.0.4";
		public const string InformationalVersion = Current;

		private static readonly Version _parsed;
		public static readonly string AssemblyVersion;
		public static readonly string FileVersion;

		static AppVersion()
		{
			if (!Version.TryParse(Current, out var parsed))
			{
				parsed = new Version(0, 0, 0, 0);
			}

			_parsed = parsed;

			// Ensure AssemblyVersion and FileVersion use a four-component version.
			var assemblyVersion = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
			AssemblyVersion = assemblyVersion.ToString();
			FileVersion = AssemblyVersion;
		}

		public static Version Parsed => _parsed;
		public static string Display => Current;
	}
}
