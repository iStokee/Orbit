using System;
using System.IO;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Orbit.Services
{
	public static class ThemeLogger
	{
		private static bool _isEnabled;
		private static readonly object _lock = new object();
		private static string _logFilePath;

		static ThemeLogger()
		{
			var appDataPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"Orbit"
			);
			Directory.CreateDirectory(appDataPath);
			_logFilePath = Path.Combine(appDataPath, "theme-debug.log");
		}

		public static bool IsEnabled
		{
			get => _isEnabled;
			set
			{
				_isEnabled = value;
				if (value)
				{
					Log("=== Theme logging enabled ===");
					Log($"Log file location: {_logFilePath}");
				}
			}
		}

		public static string LogFilePath => _logFilePath;

		public static void Log(string message)
		{
			if (!_isEnabled) return;

			lock (_lock)
			{
				try
				{
					var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
					var logEntry = $"[{timestamp}] {message}";
					File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
				}
				catch
				{
					// Silently fail to avoid breaking the app
				}
			}
		}

		public static void LogThemeChange(string themeType, string baseTheme, string accent)
		{
			Log($">>> Applying {themeType} theme: Base={baseTheme}, Accent={accent}");
		}

		public static void LogResourceSet(string key, object value)
		{
			if (!_isEnabled) return;

			var valueStr = value switch
			{
				Color c => $"Color({c.R},{c.G},{c.B},{c.A})",
				SolidColorBrush b => $"Brush({b.Color.R},{b.Color.G},{b.Color.B},{b.Color.A})",
				_ => value?.ToString() ?? "null"
			};

			Log($"  Set Resource: {key} = {valueStr}");
		}

		public static void LogResourceRemove(string key)
		{
			Log($"  Remove Resource: {key}");
		}

		public static void LogDictionaryOperation(string operation, int count)
		{
			Log($"  {operation}: {count} dictionaries");
		}

		public static void ClearLog()
		{
			lock (_lock)
			{
				try
				{
					File.WriteAllText(_logFilePath, string.Empty);
					Log("=== Log cleared ===");
				}
				catch
				{
					// Silently fail
				}
			}
		}
	}
}
