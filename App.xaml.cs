using System;
using System.Windows;
using System.Windows.Media;

using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using MahApps.Metro;

namespace Orbit
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : System.Windows.Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			//LoadSavedTheme();

			//ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.SyncWithAppMode;
			//ThemeManager.Current.SyncTheme();

			//// Create runtime themes
			//ThemeManager.Current.AddTheme(new Theme("CustomDarkRed", "CustomDarkRed", "Dark", "Red", Colors.DarkRed, Brushes.DarkRed, true, false));
			//ThemeManager.Current.AddTheme(new Theme("CustomLightRed", "CustomLightRed", "Light", "Red", Colors.DarkRed, Brushes.DarkRed, true, false));

			//ThemeManager.Current.AddTheme(RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Dark", Colors.Red));
			//ThemeManager.Current.AddTheme(RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Light", Colors.Red));

			//ThemeManager.Current.AddTheme(RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Dark", Colors.GreenYellow));
			//ThemeManager.Current.AddTheme(RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Light", Colors.GreenYellow));

			//ThemeManager.Current.AddTheme(RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Dark", Colors.Indigo));
			//ThemeManager.Current.ChangeTheme(this, ThemeManager.Current.AddTheme(RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Light", Colors.Indigo)));

			//ThemeManager.Current.ChangeTheme(this, "Light.Red");
		}

		private void LoadSavedTheme()
		{
			//try
			//{
			//	// Load saved theme from settings
			//	var savedTheme = Settings.Default.Theme;
			//	var savedAccent = Settings.Default.Accent;

			//	// Check if settings are null or empty
			//	if (string.IsNullOrWhiteSpace(savedTheme) || string.IsNullOrWhiteSpace(savedAccent))
			//	{
			//		// Initialize with default theme
			//		ApplyDefaultTheme();
			//		return;
			//	}

			//	// Construct the theme name
			//	var themeName = $"{savedTheme}.{savedAccent}";

			//	// Validate and apply theme
			//	var theme = ThemeManager.Current.Themes.FirstOrDefault(t => t.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase));

			//	if (theme != null)
			//	{
			//		ThemeManager.Current.ChangeTheme(this, themeName);
			//		System.Diagnostics.Debug.WriteLine($"Applied saved theme: {themeName}");
			//	}
			//	else
			//	{
			//		// Handle the case where the saved theme does not exist
			//		System.Diagnostics.Debug.WriteLine($"Saved theme '{themeName}' not found. Applying default theme.");
			//		ApplyDefaultTheme();
			//	}
			//}
			//catch (Exception ex)
			//{
			//	// Log the exception and apply the default theme
			//	System.Diagnostics.Debug.WriteLine($"Error loading saved theme: {ex.Message}");
			//	ApplyDefaultTheme();
			//}
		}


		private void ApplyDefaultTheme()
		{
			// Define your default theme here
			//var defaultTheme = "Light.Blue"; // Example: "Light.Blue" or "Dark.Red"
			//var theme = ThemeManager.Current.Themes.FirstOrDefault(t => t.Name.Equals(defaultTheme, StringComparison.OrdinalIgnoreCase));

			//if (theme != null)
			//{
			//	ThemeManager.Current.ChangeTheme(this, defaultTheme);
			//	System.Diagnostics.Debug.WriteLine($"Applied default theme: {defaultTheme}");
			//}
			//else
			//{
			//	System.Diagnostics.Debug.WriteLine($"Default theme '{defaultTheme}' not found. Using system default.");
			//}
		}
	}
}
