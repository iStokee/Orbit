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
using SystemColors = System.Windows.SystemColors;

namespace Orbit.Services
{
	public class CustomThemeDefinition
	{
		public string Name { get; set; } = string.Empty;
		public string BaseTheme { get; set; } = "Dark";
		public string AccentHex { get; set; } = "#FF1BA1E2";
		public bool OverrideForeground { get; set; }
		public string? ForegroundHex { get; set; }
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

	public class ThemeService
	{
		private const string CustomAccentPrefix = "custom:";
		private const string CustomAccentDictionaryMarkerKey = "__Orbit.CustomAccentDictionary";
		private const string OrbitTextPrimaryKey = "Orbit.Brushes.Text.Primary";
		private const string OrbitTextSecondaryKey = "Orbit.Brushes.Text.Secondary";
		private const string OrbitTextOnAccentKey = "Orbit.Brushes.Text.OnAccent";
		private const string OrbitTextOnHeaderKey = "Orbit.Brushes.Text.OnHeader";
		private const string OrbitTextOnHeaderSecondaryKey = "Orbit.Brushes.Text.OnHeaderSecondary";
		private const string OrbitTextOnOverlayKey = "Orbit.Brushes.Text.OnOverlay";

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
			RemoveCustomForegroundOverrides();
			RemoveCustomAccentOverrides();

			// v2 format: "Dark.Cyan", "Light.Blue", etc.
			var themeName = $"{baseTheme}.{colorScheme}";
			var theme = ThemeManager.Current.GetTheme(themeName);

			if (theme != null)
			{
				ThemeLogger.Log($"Applying theme: {themeName}");
				ThemeManager.Current.ChangeTheme(Application.Current, theme);
				ApplyAccentResourcesFromTheme(theme);
				ApplyOrbitSemanticTextResources();
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
					ApplyAccentResourcesFromTheme(fallback);
					ApplyOrbitSemanticTextResources();
					SaveCurrentTheme("Dark", "Steel");
				}
			}
		}

		public Color GetCurrentAccentColor()
		{
			var resources = Application.Current.Resources;

			if (resources["MahApps.Brushes.Accent"] is SolidColorBrush accentBrush)
			{
				return accentBrush.Color;
			}

			if (resources["MahApps.Colors.Accent"] is Color accentColor)
			{
				return accentColor;
			}

			return Colors.SteelBlue;
		}

		public Color GetCurrentForegroundColor()
		{
			var resources = Application.Current.Resources;

			if (resources.Contains("MahApps.Brushes.ThemeForeground") &&
			    resources["MahApps.Brushes.ThemeForeground"] is SolidColorBrush brush)
			{
				return brush.Color;
			}

			if (resources.Contains("MahApps.Colors.ThemeForeground") &&
			    resources["MahApps.Colors.ThemeForeground"] is Color color)
			{
				return color;
			}

			return Colors.White;
		}

		private static void ApplyAccentResourcesFromTheme(Theme theme)
		{
			Color? ResolveColor(object? candidate)
				=> candidate switch
				{
					Color color => color,
					SolidColorBrush brush => brush.Color,
					_ => null
				};

			var resources = theme?.Resources;
			if (resources == null)
			{
				ApplyAccentResources(Colors.SteelBlue);
				return;
			}

			Color? accent = resources.Contains("MahApps.Colors.Accent")
				? ResolveColor(resources["MahApps.Colors.Accent"])
				: null;
			if (accent == null && resources.Contains("MahApps.Brushes.Accent"))
			{
				accent = ResolveColor(resources["MahApps.Brushes.Accent"]);
			}

			if (accent == null)
			{
				var fallback = Application.Current.TryFindResource("MahApps.Brushes.Accent");
				accent = ResolveColor(fallback) ?? Colors.SteelBlue;
			}

			ApplyAccentResources(accent.Value);
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
			RemoveCustomForegroundOverrides();
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

			if (customTheme.OverrideForeground && !string.IsNullOrWhiteSpace(customTheme.ForegroundHex))
			{
				if (TryParseColor(customTheme.ForegroundHex, out var foregroundColor))
				{
					ThemeLogger.Log($"Applying custom foreground color: {customTheme.ForegroundHex}");
					ApplyForegroundResources(foregroundColor);
				}
				else
				{
					ThemeLogger.Log($"Failed to parse custom foreground {customTheme.ForegroundHex}, keeping theme defaults");
					RemoveCustomForegroundOverrides();
				}
			}
			else
			{
				RemoveCustomForegroundOverrides();
			}

			ApplyOrbitSemanticTextResources();
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
			var idealForeground = GetSharedAccentForeground(accentColor, accentColor2, accentColor3, accentColor4, highlightColor);

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
			resources["MahApps.Colors.AccentForeground"] = idealForeground;
			ThemeLogger.LogResourceSet("MahApps.Colors.AccentForeground", idealForeground);
			resources["MahApps.Colors.AccentSelectedForeground"] = idealForeground;
			ThemeLogger.LogResourceSet("MahApps.Colors.AccentSelectedForeground", idealForeground);
			resources["MahApps.Colors.HighlightForeground"] = idealForeground;
			ThemeLogger.LogResourceSet("MahApps.Colors.HighlightForeground", idealForeground);
			resources["ControlzEx.Colors.AccentForeground"] = idealForeground;
			ThemeLogger.LogResourceSet("ControlzEx.Colors.AccentForeground", idealForeground);

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
			resources["MahApps.Brushes.AccentForeground"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("MahApps.Brushes.AccentForeground", resources["MahApps.Brushes.AccentForeground"]);
			resources["MahApps.Brushes.AccentSelectedForeground"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("MahApps.Brushes.AccentSelectedForeground", resources["MahApps.Brushes.AccentSelectedForeground"]);
			resources["MahApps.Brushes.HighlightForeground"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("MahApps.Brushes.HighlightForeground", resources["MahApps.Brushes.HighlightForeground"]);
			resources["ControlzEx.Brushes.AccentForeground"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("ControlzEx.Brushes.AccentForeground", resources["ControlzEx.Brushes.AccentForeground"]);

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
			resources["AccentForegroundColor"] = idealForeground;
			ThemeLogger.LogResourceSet("AccentForegroundColor", idealForeground);

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
			resources["AccentForegroundColorBrush"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("AccentForegroundColorBrush", resources["AccentForegroundColorBrush"]);
			resources["AccentSelectedForegroundColorBrush"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("AccentSelectedForegroundColorBrush", resources["AccentSelectedForegroundColorBrush"]);

			// MahApps v2 window title brushes
			resources["MahApps.Brushes.WindowTitle"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("MahApps.Brushes.WindowTitle", resources["MahApps.Brushes.WindowTitle"]);

			// Additional UI-specific brushes
			resources["AccentSelectedColorBrush"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("AccentSelectedColorBrush", resources["AccentSelectedColorBrush"]);
			resources["WindowTitleColorBrush"] = CreateFrozenBrush(accentColor); // Legacy alias
			ThemeLogger.LogResourceSet("WindowTitleColorBrush", resources["WindowTitleColorBrush"]);
			resources["ProgressBrush"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("ProgressBrush", resources["ProgressBrush"]);
			resources["CheckmarkFill"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("CheckmarkFill", resources["CheckmarkFill"]);
			resources["RightArrowFill"] = CreateFrozenBrush(idealForeground);
			ThemeLogger.LogResourceSet("RightArrowFill", resources["RightArrowFill"]);
		}

		private static void ApplyForegroundResources(Color foregroundColor)
		{
			ThemeLogger.Log($"ApplyForegroundResources: {foregroundColor}");
			var resources = Application.Current.Resources;

			resources["MahApps.Colors.ThemeForeground"] = foregroundColor;
			ThemeLogger.LogResourceSet("MahApps.Colors.ThemeForeground", foregroundColor);

			SetBrushResource("MahApps.Brushes.ThemeForeground", foregroundColor);
			SetBrushResource("MahApps.Brushes.Text", foregroundColor);
			SetBrushResource("MahApps.Brushes.Button.Border.Focus", foregroundColor);
			SetBrushResource("MahApps.Brushes.TextBox.Border.Focus", foregroundColor);
			SetBrushResource("MahApps.Brushes.ComboBox.Border.Focus", foregroundColor);
			SetBrushResource("MahApps.Brushes.Flyout.Foreground", foregroundColor);
			SetBrushResource("MahApps.Brushes.ContextMenu.Border", foregroundColor);
			SetBrushResource("MahApps.Brushes.SubMenu.Border", foregroundColor);
			SetBrushResource("MahApps.Brushes.Button.Square.Foreground.MouseOver", foregroundColor);
			SetBrushResource("MahApps.Brushes.DataGrid.Selection.Text.Inactive", foregroundColor);

			var overlayAlpha = (byte)Math.Clamp((int)(foregroundColor.A * 0.55), 0, 255);
			var overlayColor = Color.FromArgb(overlayAlpha, foregroundColor.R, foregroundColor.G, foregroundColor.B);
			SetBrushResource("MahApps.Brushes.Window.FlyoutOverlay", overlayColor);

			SetBrushResource(SystemColors.ControlTextBrushKey, foregroundColor);
			SetBrushResource(SystemColors.MenuTextBrushKey, foregroundColor);
			SetBrushResource(SystemColors.HighlightTextBrushKey, foregroundColor);
		}

		private static void RemoveCustomForegroundOverrides()
		{
			ThemeLogger.Log("RemoveCustomForegroundOverrides: Cleaning up custom foreground resources");
			var resources = Application.Current.Resources;

			object[] keysToRemove =
			{
				"MahApps.Colors.ThemeForeground",
				"MahApps.Brushes.ThemeForeground",
				"MahApps.Brushes.Text",
				"MahApps.Brushes.Button.Border.Focus",
				"MahApps.Brushes.TextBox.Border.Focus",
				"MahApps.Brushes.ComboBox.Border.Focus",
				"MahApps.Brushes.Flyout.Foreground",
				"MahApps.Brushes.ContextMenu.Border",
				"MahApps.Brushes.SubMenu.Border",
				"MahApps.Brushes.Button.Square.Foreground.MouseOver",
				"MahApps.Brushes.DataGrid.Selection.Text.Inactive",
				"MahApps.Brushes.Window.FlyoutOverlay",
				SystemColors.ControlTextBrushKey,
				SystemColors.MenuTextBrushKey,
				SystemColors.HighlightTextBrushKey
			};

			int removedCount = 0;
			foreach (var key in keysToRemove)
			{
				if (resources.Contains(key))
				{
					resources.Remove(key);
					ThemeLogger.LogResourceRemove(key.ToString() ?? key.GetType().Name);
					removedCount++;
				}
			}

			ThemeLogger.Log($"Removed {removedCount} custom foreground resources");
		}

		private static void ApplyOrbitSemanticTextResources()
		{
			var resources = Application.Current.Resources;
			var background = ResolveResourceColor(resources, "MahApps.Brushes.ThemeBackground", Colors.Black);
			var themeForeground = ResolveResourceColor(resources, "MahApps.Brushes.ThemeForeground", Colors.White);
			var accent = ResolveResourceColor(resources, "MahApps.Brushes.Accent", Colors.SteelBlue);
			var accent2 = ResolveResourceColor(resources, "MahApps.Brushes.Accent2", ChangeColorBrightness(accent, 0.2));
			var accent3 = ResolveResourceColor(resources, "MahApps.Brushes.Accent3", ChangeColorBrightness(accent, -0.2));
			var accent4 = ResolveResourceColor(resources, "MahApps.Brushes.Accent4", ChangeColorBrightness(accent, -0.4));
			var accentForeground = ResolveResourceColor(
				resources,
				"MahApps.Brushes.AccentForeground",
				GetSharedAccentForeground(accent, accent2, accent3, accent4));

			var primary = EnsureReadable(themeForeground, background, 4.5);
			var secondary = DeriveSecondaryText(primary, background);
			var onAccent = accentForeground;

			var headerSurface = AverageColor(accent, accent2, accent3);
			// Keep accent/header text decisions unified so controls on accent variants don't diverge.
			var onHeader = EnsureReadable(accentForeground, headerSurface, 4.5);
			var onHeaderSecondary = DeriveSecondaryText(onHeader, headerSurface);
			if (GetContrastRatio(onHeaderSecondary, headerSurface) < 3.0)
			{
				onHeaderSecondary = onHeader;
			}

			var overlaySurface = Blend(Colors.Black, background, 0.6);
			var onOverlay = EnsureReadable(themeForeground, overlaySurface, 4.5);

			SetBrushResource(OrbitTextPrimaryKey, primary);
			SetBrushResource(OrbitTextSecondaryKey, secondary);
			SetBrushResource(OrbitTextOnAccentKey, onAccent);
			SetBrushResource(OrbitTextOnHeaderKey, onHeader);
			SetBrushResource(OrbitTextOnHeaderSecondaryKey, onHeaderSecondary);
			SetBrushResource(OrbitTextOnOverlayKey, onOverlay);
		}

		private static Color ResolveResourceColor(ResourceDictionary resources, object key, Color fallback)
		{
			if (!resources.Contains(key))
			{
				return fallback;
			}

			var candidate = resources[key];
			return candidate switch
			{
				Color color => color,
				SolidColorBrush brush => brush.Color,
				_ => fallback
			};
		}

		private static Color EnsureReadable(Color preferred, Color background, double minContrastRatio)
		{
			if (GetContrastRatio(preferred, background) >= minContrastRatio)
			{
				return preferred;
			}

			var black = Colors.Black;
			var white = Colors.White;
			return GetContrastRatio(black, background) >= GetContrastRatio(white, background)
				? black
				: white;
		}

		private static Color DeriveSecondaryText(Color primary, Color background)
		{
			var candidate = Blend(primary, background, 0.72);
			return GetContrastRatio(candidate, background) >= 3.0
				? candidate
				: EnsureReadable(primary, background, 3.0);
		}

		private static Color AverageColor(params Color[] colors)
		{
			if (colors == null || colors.Length == 0)
			{
				return Colors.Black;
			}

			double a = 0;
			double r = 0;
			double g = 0;
			double b = 0;
			foreach (var color in colors)
			{
				a += color.A;
				r += color.R;
				g += color.G;
				b += color.B;
			}

			var count = colors.Length;
			return Color.FromArgb(
				(byte)Math.Round(a / count),
				(byte)Math.Round(r / count),
				(byte)Math.Round(g / count),
				(byte)Math.Round(b / count));
		}

		private static Color Blend(Color foreground, Color background, double foregroundWeight)
		{
			var clamped = Math.Clamp(foregroundWeight, 0.0, 1.0);
			var bgWeight = 1.0 - clamped;
			return Color.FromArgb(
				(byte)Math.Round((foreground.A * clamped) + (background.A * bgWeight)),
				(byte)Math.Round((foreground.R * clamped) + (background.R * bgWeight)),
				(byte)Math.Round((foreground.G * clamped) + (background.G * bgWeight)),
				(byte)Math.Round((foreground.B * clamped) + (background.B * bgWeight)));
		}

		private static double GetContrastRatio(Color colorA, Color colorB)
		{
			var luminanceA = GetRelativeLuminance(colorA);
			var luminanceB = GetRelativeLuminance(colorB);
			var lighter = Math.Max(luminanceA, luminanceB);
			var darker = Math.Min(luminanceA, luminanceB);
			return (lighter + 0.05) / (darker + 0.05);
		}

		private static double GetRelativeLuminance(Color color)
		{
			static double ToLinear(byte component)
			{
				var srgb = component / 255.0;
				return srgb <= 0.03928
					? srgb / 12.92
					: Math.Pow((srgb + 0.055) / 1.055, 2.4);
			}

			var r = ToLinear(color.R);
			var g = ToLinear(color.G);
			var b = ToLinear(color.B);
			return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
		}

		private static void SetBrushResource(object key, Color color)
		{
			var brush = CreateFrozenBrush(color);
			Application.Current.Resources[key] = brush;
			ThemeLogger.LogResourceSet(key.ToString() ?? key.GetType().Name, brush);
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
				"MahApps.Colors.Highlight", "MahApps.Colors.IdealForeground", "MahApps.Colors.AccentForeground",
				"MahApps.Colors.AccentSelectedForeground", "MahApps.Colors.HighlightForeground",
				"ControlzEx.Colors.AccentForeground",
				"MahApps.Brushes.Accent", "MahApps.Brushes.Accent2", "MahApps.Brushes.Accent3", "MahApps.Brushes.Accent4",
				"MahApps.Brushes.Highlight", "MahApps.Brushes.IdealForeground", "MahApps.Brushes.WindowTitle",
				"MahApps.Brushes.AccentForeground", "MahApps.Brushes.AccentSelectedForeground", "MahApps.Brushes.HighlightForeground",
				"ControlzEx.Brushes.AccentForeground",
				"AccentColor", "AccentColor2", "AccentColor3", "AccentColor4", "HighlightColor", "IdealForegroundColor",
				"AccentForegroundColor",
				"AccentColorBrush", "AccentColorBrush2", "AccentColorBrush3", "AccentColorBrush4",
				"HighlightBrush", "IdealForegroundColorBrush", "AccentForegroundColorBrush", "AccentSelectedForegroundColorBrush",
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

		private static Color GetSharedAccentForeground(params Color[] accentSurfaces)
		{
			if (accentSurfaces == null || accentSurfaces.Length == 0)
			{
				return Colors.White;
			}

			var black = Colors.Black;
			var white = Colors.White;
			var minBlackContrast = double.MaxValue;
			var minWhiteContrast = double.MaxValue;

			foreach (var surface in accentSurfaces)
			{
				minBlackContrast = Math.Min(minBlackContrast, GetContrastRatio(black, surface));
				minWhiteContrast = Math.Min(minWhiteContrast, GetContrastRatio(white, surface));
			}

			// Pick the color with the strongest worst-case contrast across all accent variants.
			return minBlackContrast >= minWhiteContrast ? black : white;
		}
	}
}
