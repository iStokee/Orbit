using ControlzEx.Theming;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using ColorConverter = System.Windows.Media.ColorConverter;
using Color = System.Windows.Media.Color;

namespace Orbit.Services
{
	public class CustomThemeDefinition
	{
		public string Name { get; set; } = string.Empty;
		public string BaseTheme { get; set; } = "Dark";
		public string AccentHex { get; set; } = "#FF1BA1E2";
	}

	/// <summary>
	/// Theme descriptor for UI display
	/// </summary>
	public class ThemeDescriptor
	{
		public string Name { get; set; } = string.Empty;
		public string DisplayName { get; set; } = string.Empty;
		public string BaseColorScheme { get; set; } = string.Empty;
		public string ColorScheme { get; set; } = string.Empty;

		public ThemeDescriptor(string name, string displayName, string baseColorScheme, string colorScheme)
		{
			Name = name;
			DisplayName = displayName;
			BaseColorScheme = baseColorScheme;
			ColorScheme = colorScheme;
		}
	}

	internal class ThemeService
	{
		private const string CustomAccentPrefix = "custom:";
		private const string CustomAccentDictionaryMarkerKey = "__Orbit.CustomAccentDictionary";

		/// <summary>
		/// Get available base themes (Light, Dark)
		/// </summary>
		public ObservableCollection<string> GetAvailableBaseThemes()
		{
			var baseThemes = new HashSet<string>();
			foreach (var theme in ThemeManager.Current.Themes)
			{
				if (!string.IsNullOrEmpty(theme.BaseColorScheme))
					baseThemes.Add(theme.BaseColorScheme);
			}
			return new ObservableCollection<string>(baseThemes.OrderBy(x => x));
		}

		/// <summary>
		/// Get available color schemes (accent colors)
		/// </summary>
		public ObservableCollection<string> GetAvailableColorSchemes()
		{
			var schemes = new HashSet<string>();
			foreach (var theme in ThemeManager.Current.Themes)
			{
				if (!string.IsNullOrEmpty(theme.ColorScheme))
					schemes.Add(theme.ColorScheme);
			}
			return new ObservableCollection<string>(schemes.OrderBy(x => x));
		}

		/// <summary>
		/// Get all available themes as descriptors
		/// </summary>
		public ObservableCollection<ThemeDescriptor> GetAvailableThemes()
		{
			var themes = new List<ThemeDescriptor>();
			foreach (var theme in ThemeManager.Current.Themes)
			{
				themes.Add(new ThemeDescriptor(
					theme.Name,
					theme.DisplayName ?? theme.Name,
					theme.BaseColorScheme ?? "Unknown",
					theme.ColorScheme ?? "Unknown"
				));
			}
			return new ObservableCollection<ThemeDescriptor>(themes.OrderBy(t => t.DisplayName));
		}

		public ObservableCollection<CustomThemeDefinition> LoadCustomThemes()
		{
			var raw = Settings.Default.CustomThemes;
			if (string.IsNullOrWhiteSpace(raw))
				return new ObservableCollection<CustomThemeDefinition>();

			try
			{
				var items = JsonConvert.DeserializeObject<List<CustomThemeDefinition>>(raw);
				if (items == null)
					return new ObservableCollection<CustomThemeDefinition>();

				return new ObservableCollection<CustomThemeDefinition>(items);
			}
			catch
			{
				// Corrupt settings; reset entry.
				Settings.Default.CustomThemes = string.Empty;
				Settings.Default.Save();
				return new ObservableCollection<CustomThemeDefinition>();
			}
		}

		public void SaveCustomThemes(IEnumerable<CustomThemeDefinition> themes)
		{
			var serialized = JsonConvert.SerializeObject(themes, Formatting.Indented);
			Settings.Default.CustomThemes = serialized;
			Settings.Default.Save();
		}

		public void ApplySavedTheme()
		{
			var savedThemeName = Settings.Default.Theme;
			var savedAccentKey = Settings.Default.Accent;

			// Handle custom themes
			if (!string.IsNullOrWhiteSpace(savedAccentKey) &&
			    savedAccentKey.StartsWith(CustomAccentPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var customName = savedAccentKey.Substring(CustomAccentPrefix.Length);
				var custom = LoadCustomThemes().FirstOrDefault(t =>
					string.Equals(t.Name, customName, StringComparison.OrdinalIgnoreCase));

				if (custom != null)
				{
					ApplyCustomTheme(custom);
					return;
				}
			}

			// Handle built-in themes
			// v1 saved "BaseDark" + "Cyan", need to convert to v2 format "Dark.Cyan"
			var baseTheme = ConvertLegacyBaseTheme(savedThemeName);
			var colorScheme = savedAccentKey ?? "Cyan";

			// Remove "custom:" prefix if it exists
			if (colorScheme.StartsWith(CustomAccentPrefix, StringComparison.OrdinalIgnoreCase))
			{
				colorScheme = "Cyan"; // Fallback
			}

			ApplyBuiltInTheme(baseTheme, colorScheme);
		}

		/// <summary>
		/// Convert v1.6 theme names to v2.0 format
		/// </summary>
		private string ConvertLegacyBaseTheme(string? oldThemeName)
		{
			if (string.IsNullOrWhiteSpace(oldThemeName))
				return "Dark";

			// v1.6 used "BaseDark" and "BaseLight"
			// v2.0 uses "Dark" and "Light"
			if (oldThemeName.Equals("BaseDark", StringComparison.OrdinalIgnoreCase))
				return "Dark";
			if (oldThemeName.Equals("BaseLight", StringComparison.OrdinalIgnoreCase))
				return "Light";

			// If already in new format or unknown, return as-is
			return oldThemeName;
		}

		public void ApplyBuiltInTheme(string baseTheme, string colorScheme)
		{
			ThemeLogger.LogThemeChange("Built-in", baseTheme, colorScheme);
			RemoveCustomAccentOverrides();

			// v2 format: "Dark.Cyan", "Light.Blue", etc.
			var themeName = $"{baseTheme}.{colorScheme}";
			var theme = ThemeManager.Current.GetTheme(themeName);

			if (theme != null)
			{
				ThemeLogger.Log($"Applying theme: {themeName}");
				ThemeManager.Current.ChangeTheme(Application.Current, theme);
				SaveCurrentTheme(baseTheme, colorScheme);
				ThemeLogger.Log($"Theme applied successfully");
			}
			else
			{
				// Fallback to Dark.Steel
				ThemeLogger.Log($"Theme {themeName} not found, falling back to Dark.Steel");
				var fallback = ThemeManager.Current.GetTheme("Dark.Steel");
				if (fallback != null)
				{
					ThemeManager.Current.ChangeTheme(Application.Current, fallback);
					SaveCurrentTheme("Dark", "Steel");
				}
			}
		}

		public void ApplyCustomTheme(CustomThemeDefinition customTheme)
		{
			ThemeLogger.LogThemeChange("Custom", customTheme.BaseTheme, customTheme.AccentHex);

			if (!TryParseColor(customTheme.AccentHex, out var accentColor))
			{
				ThemeLogger.Log($"Failed to parse color {customTheme.AccentHex}, falling back to SteelBlue");
				accentColor = Colors.SteelBlue;
			}

			// Get base theme name and ensure it's in v2 format
			var baseTheme = ConvertLegacyBaseTheme(customTheme.BaseTheme);
			ThemeLogger.Log($"Using base theme: {baseTheme}");

			// Remove any existing custom accent overrides first
			RemoveCustomAccentOverrides();

			// Apply base theme first (using a standard color scheme)
			var baseThemeObj = ThemeManager.Current.GetTheme($"{baseTheme}.Steel");
			if (baseThemeObj == null)
			{
				// Fallback to any theme with matching base
				baseThemeObj = ThemeManager.Current.Themes.FirstOrDefault(t =>
					t.BaseColorScheme?.Equals(baseTheme, StringComparison.OrdinalIgnoreCase) == true);
			}

			if (baseThemeObj == null)
			{
				// Cannot create custom theme without base
				ApplyBuiltInTheme("Dark", "Steel");
				return;
			}

			// Apply the base theme
			ThemeLogger.Log($"Applying base theme: {baseTheme}.Steel");
			ThemeManager.Current.ChangeTheme(Application.Current, baseThemeObj);

			// IMPORTANT: After ChangeTheme, MahApps adds dictionaries to the collection.
			// We need to apply our custom accents AFTER to ensure they override Steel colors
			ThemeLogger.Log("Applying custom accent colors");
			ApplyAccentResources(accentColor);

			ThemeLogger.Log("<<< Custom theme applied successfully");

			// Save the custom theme reference
			SaveCurrentTheme(baseTheme, $"{CustomAccentPrefix}{customTheme.Name}");
		}

		private static void SaveCurrentTheme(string baseTheme, string accentKey)
		{
			Settings.Default.Theme = baseTheme;
			Settings.Default.Accent = accentKey;
			Settings.Default.Save();
		}

		private static void ApplyAccentResources(Color accentColor)
		{
			var accentColor2 = ChangeColorBrightness(accentColor, 0.2);
			var accentColor3 = ChangeColorBrightness(accentColor, -0.2);
			var accentColor4 = ChangeColorBrightness(accentColor, -0.4);
			var highlightColor = ChangeColorBrightness(accentColor, 0.35);
			var idealForeground = GetIdealForegroundColor(accentColor);

			ThemeLogger.Log($"ApplyAccentResources: Primary={accentColor}, IdealForeground={idealForeground}");

			// Instead of using a custom dictionary in MergedDictionaries,
			// set the resources directly on Application.Current.Resources
			// This ensures they override all theme resources
			var resources = Application.Current.Resources;

			// MahApps v2 uses MahApps.* prefixed keys
			resources["MahApps.Colors.Accent"] = accentColor;
			ThemeLogger.LogResourceSet("MahApps.Colors.Accent", accentColor);
			resources["MahApps.Colors.Accent2"] = accentColor2;
			ThemeLogger.LogResourceSet("MahApps.Colors.Accent2", accentColor2);
			resources["MahApps.Colors.Accent3"] = accentColor3;
			ThemeLogger.LogResourceSet("MahApps.Colors.Accent3", accentColor3);
			resources["MahApps.Colors.Accent4"] = accentColor4;
			ThemeLogger.LogResourceSet("MahApps.Colors.Accent4", accentColor4);
			resources["MahApps.Colors.Highlight"] = highlightColor;
			ThemeLogger.LogResourceSet("MahApps.Colors.Highlight", highlightColor);
			resources["MahApps.Colors.IdealForeground"] = idealForeground;
			ThemeLogger.LogResourceSet("MahApps.Colors.IdealForeground", idealForeground);

			resources["MahApps.Brushes.Accent"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("MahApps.Brushes.Accent", resources["MahApps.Brushes.Accent"]);
			resources["MahApps.Brushes.Accent2"] = CreateFrozenBrush(accentColor2);
			ThemeLogger.LogResourceSet("MahApps.Brushes.Accent2", resources["MahApps.Brushes.Accent2"]);
			resources["MahApps.Brushes.Accent3"] = CreateFrozenBrush(accentColor3);
			ThemeLogger.LogResourceSet("MahApps.Brushes.Accent3", resources["MahApps.Brushes.Accent3"]);
			resources["MahApps.Brushes.Accent4"] = CreateFrozenBrush(accentColor4);
			ThemeLogger.LogResourceSet("MahApps.Brushes.Accent4", resources["MahApps.Brushes.Accent4"]);
			resources["MahApps.Brushes.Highlight"] = CreateFrozenBrush(highlightColor);
			ThemeLogger.LogResourceSet("MahApps.Brushes.Highlight", resources["MahApps.Brushes.Highlight"]);
			resources["MahApps.Brushes.IdealForeground"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("MahApps.Brushes.IdealForeground", resources["MahApps.Brushes.IdealForeground"]);

			// Legacy aliases for backward compatibility
			resources["AccentColor"] = accentColor;
			ThemeLogger.LogResourceSet("AccentColor", accentColor);
			resources["AccentColor2"] = accentColor2;
			ThemeLogger.LogResourceSet("AccentColor2", accentColor2);
			resources["AccentColor3"] = accentColor3;
			ThemeLogger.LogResourceSet("AccentColor3", accentColor3);
			resources["AccentColor4"] = accentColor4;
			ThemeLogger.LogResourceSet("AccentColor4", accentColor4);
			resources["HighlightColor"] = highlightColor;
			ThemeLogger.LogResourceSet("HighlightColor", highlightColor);
			resources["IdealForegroundColor"] = idealForeground;
			ThemeLogger.LogResourceSet("IdealForegroundColor", idealForeground);

			resources["AccentColorBrush"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("AccentColorBrush", resources["AccentColorBrush"]);
			resources["AccentColorBrush2"] = CreateFrozenBrush(accentColor2);
			ThemeLogger.LogResourceSet("AccentColorBrush2", resources["AccentColorBrush2"]);
			resources["AccentColorBrush3"] = CreateFrozenBrush(accentColor3);
			ThemeLogger.LogResourceSet("AccentColorBrush3", resources["AccentColorBrush3"]);
			resources["AccentColorBrush4"] = CreateFrozenBrush(accentColor4);
			ThemeLogger.LogResourceSet("AccentColorBrush4", resources["AccentColorBrush4"]);
			resources["HighlightBrush"] = CreateFrozenBrush(highlightColor);
			ThemeLogger.LogResourceSet("HighlightBrush", resources["HighlightBrush"]);
			resources["IdealForegroundColorBrush"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("IdealForegroundColorBrush", resources["IdealForegroundColorBrush"]);

			resources["AccentSelectedColorBrush"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("AccentSelectedColorBrush", resources["AccentSelectedColorBrush"]);
			resources["WindowTitleColorBrush"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("WindowTitleColorBrush", resources["WindowTitleColorBrush"]);
			resources["ProgressBrush"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("ProgressBrush", resources["ProgressBrush"]);
			resources["CheckmarkFill"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("CheckmarkFill", resources["CheckmarkFill"]);
			resources["RightArrowFill"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("RightArrowFill", resources["RightArrowFill"]);
		}

		private static ResourceDictionary EnsureCustomAccentDictionary()
		{
			var resources = Application.Current.Resources;

			// Check if custom dictionary already exists
			foreach (var dictionary in resources.MergedDictionaries)
			{
				if (dictionary.Contains(CustomAccentDictionaryMarkerKey))
				{
					// Move it to the end for highest priority
					resources.MergedDictionaries.Remove(dictionary);
					resources.MergedDictionaries.Add(dictionary);
					return dictionary;
				}
			}

			// Create new custom dictionary at the end (highest priority)
			var customDictionary = new ResourceDictionary
			{
				{ CustomAccentDictionaryMarkerKey, true }
			};
			resources.MergedDictionaries.Add(customDictionary);
			return customDictionary;
		}

		private static void RemoveCustomAccentOverrides()
		{
			ThemeLogger.Log("RemoveCustomAccentOverrides: Cleaning up custom accent resources");
			var resources = Application.Current.Resources;

			// Remove custom resource keys from Application.Current.Resources
			// This allows the base theme resources to show through
			var keysToRemove = new[]
			{
				"MahApps.Colors.Accent", "MahApps.Colors.Accent2", "MahApps.Colors.Accent3", "MahApps.Colors.Accent4",
				"MahApps.Colors.Highlight", "MahApps.Colors.IdealForeground",
				"MahApps.Brushes.Accent", "MahApps.Brushes.Accent2", "MahApps.Brushes.Accent3", "MahApps.Brushes.Accent4",
				"MahApps.Brushes.Highlight", "MahApps.Brushes.IdealForeground",
				"AccentColor", "AccentColor2", "AccentColor3", "AccentColor4", "HighlightColor", "IdealForegroundColor",
				"AccentColorBrush", "AccentColorBrush2", "AccentColorBrush3", "AccentColorBrush4",
				"HighlightBrush", "IdealForegroundColorBrush",
				"AccentSelectedColorBrush", "WindowTitleColorBrush", "ProgressBrush", "CheckmarkFill", "RightArrowFill"
			};

			int removedCount = 0;
			foreach (var key in keysToRemove)
			{
				if (resources.Contains(key))
				{
					resources.Remove(key);
					ThemeLogger.LogResourceRemove(key);
					removedCount++;
				}
			}
			ThemeLogger.Log($"Removed {removedCount} custom accent resources");
		}

		private static bool TryParseColor(string value, out Color color)
		{
			try
			{
				var converted = ColorConverter.ConvertFromString(value);
				if (converted != null)
				{
					color = (Color)converted;
					return true;
				}
			}
			catch
			{
				// Ignore and fallback below.
			}

			color = Colors.SteelBlue;
			return false;
		}

		private static SolidColorBrush CreateFrozenBrush(Color color)
		{
			var brush = new SolidColorBrush(color);
			brush.Freeze();
			return brush;
		}

		private static Color ChangeColorBrightness(Color color, double correctionFactor)
		{
			double red = color.R;
			double green = color.G;
			double blue = color.B;

			if (correctionFactor < 0)
			{
				correctionFactor = 1 + correctionFactor;
				red *= correctionFactor;
				green *= correctionFactor;
				blue *= correctionFactor;
			}
			else
			{
				red = (255 - red) * correctionFactor + red;
				green = (255 - green) * correctionFactor + green;
				blue = (255 - blue) * correctionFactor + blue;
			}

			return Color.FromArgb(color.A, (byte)red, (byte)green, (byte)blue);
		}

		private static Color GetIdealForegroundColor(Color color)
		{
			double luma = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
			return luma > 0.5 ? Colors.Black : Colors.White;
		}
	}
}
