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
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

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
		private const string OrbitTextPrimaryKey = "Orbit.Brushes.Text.Primary";
		private const string OrbitTextSecondaryKey = "Orbit.Brushes.Text.Secondary";
		private const string OrbitTextOnAccentKey = "Orbit.Brushes.Text.OnAccent";
		private const string OrbitTextOnHeaderKey = "Orbit.Brushes.Text.OnHeader";
		private const string OrbitTextOnHeaderSecondaryKey = "Orbit.Brushes.Text.OnHeaderSecondary";
		private const string OrbitTextOnOverlayKey = "Orbit.Brushes.Text.OnOverlay";

		private static readonly IReadOnlyDictionary<string, Color> BuiltInSchemes =
			new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
			{
				["Steel"] = Color.FromRgb(0x47, 0x8C, 0xD1),
				["Cyan"] = Color.FromRgb(0x1B, 0xA1, 0xE2),
				["Blue"] = Color.FromRgb(0x00, 0x78, 0xD7),
				["Cobalt"] = Color.FromRgb(0x00, 0x50, 0xEF),
				["Emerald"] = Color.FromRgb(0x00, 0xA3, 0x00),
				["Green"] = Color.FromRgb(0x60, 0xA9, 0x17),
				["Lime"] = Color.FromRgb(0xA4, 0xC4, 0x00),
				["Teal"] = Color.FromRgb(0x00, 0xAB, 0xA9),
				["Sienna"] = Color.FromRgb(0xA0, 0x52, 0x2D),
				["Brown"] = Color.FromRgb(0x82, 0x52, 0x1B),
				["Orange"] = Color.FromRgb(0xFA, 0x68, 0x00),
				["Red"] = Color.FromRgb(0xE5, 0x14, 0x00),
				["Crimson"] = Color.FromRgb(0xA2, 0x00, 0x25),
				["Magenta"] = Color.FromRgb(0xD8, 0x00, 0x73),
				["Purple"] = Color.FromRgb(0xA2, 0x00, 0xFF)
			};

		public ObservableCollection<string> GetAvailableBaseThemes()
			=> new(new[] { "Dark", "Light" });

		public ObservableCollection<string> GetAvailableColorSchemes()
			=> new(BuiltInSchemes.Keys.OrderBy(x => x));

		public ObservableCollection<ThemeDescriptor> GetAvailableThemes()
		{
			var themes = new List<ThemeDescriptor>();
			foreach (var baseTheme in GetAvailableBaseThemes())
			{
				foreach (var scheme in GetAvailableColorSchemes())
				{
					themes.Add(new ThemeDescriptor(
						$"{baseTheme}.{scheme}",
						$"{baseTheme} {scheme}",
						baseTheme,
						scheme));
				}
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

			if (!string.IsNullOrWhiteSpace(savedAccentKey)
			    && savedAccentKey.StartsWith(CustomAccentPrefix, StringComparison.OrdinalIgnoreCase))
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

			var baseTheme = ConvertLegacyBaseTheme(savedThemeName);
			var colorScheme = savedAccentKey ?? "Steel";
			if (colorScheme.StartsWith(CustomAccentPrefix, StringComparison.OrdinalIgnoreCase))
			{
				colorScheme = "Steel";
			}

			ApplyBuiltInTheme(baseTheme, colorScheme);
		}

		private static string ConvertLegacyBaseTheme(string? oldThemeName)
		{
			if (string.IsNullOrWhiteSpace(oldThemeName))
				return "Dark";

			if (oldThemeName.Equals("BaseDark", StringComparison.OrdinalIgnoreCase))
				return "Dark";
			if (oldThemeName.Equals("BaseLight", StringComparison.OrdinalIgnoreCase))
				return "Light";

			return oldThemeName;
		}

		public void ApplyBuiltInTheme(string baseTheme, string colorScheme)
		{
			ThemeLogger.LogThemeChange("Built-in", baseTheme, colorScheme);
			RemoveCustomForegroundOverrides();
			RemoveCustomAccentOverrides();

			var normalizedBaseTheme = NormalizeBaseTheme(baseTheme);
			var normalizedScheme = BuiltInSchemes.ContainsKey(colorScheme) ? colorScheme : "Steel";

			ApplyBaseTheme(normalizedBaseTheme);
			ApplyAccentResources(BuiltInSchemes[normalizedScheme]);
			ApplyBaseForegroundForTheme(normalizedBaseTheme);
			ApplyOrbitSemanticTextResources();
			SaveCurrentTheme(normalizedBaseTheme, normalizedScheme);
			ThemeLogger.Log($"Theme applied successfully: {normalizedBaseTheme}.{normalizedScheme}");
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

			if (resources.Contains("MahApps.Brushes.ThemeForeground") && resources["MahApps.Brushes.ThemeForeground"] is SolidColorBrush brush)
			{
				return brush.Color;
			}

			if (resources.Contains("MahApps.Colors.ThemeForeground") && resources["MahApps.Colors.ThemeForeground"] is Color color)
			{
				return color;
			}

			return Colors.White;
		}

		public void ApplyCustomTheme(CustomThemeDefinition customTheme)
		{
			ThemeLogger.LogThemeChange("Custom", customTheme.BaseTheme, customTheme.AccentHex);

			if (!TryParseColor(customTheme.AccentHex, out var accentColor))
			{
				ThemeLogger.Log($"Failed to parse color {customTheme.AccentHex}, falling back to SteelBlue");
				accentColor = Colors.SteelBlue;
			}

			var baseTheme = ConvertLegacyBaseTheme(customTheme.BaseTheme);
			ThemeLogger.Log($"Using base theme: {baseTheme}");

			RemoveCustomForegroundOverrides();
			RemoveCustomAccentOverrides();
			ApplyBaseTheme(baseTheme);
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
					ThemeLogger.Log($"Failed to parse custom foreground {customTheme.ForegroundHex}, using theme default");
					ApplyBaseForegroundForTheme(baseTheme);
				}
			}
			else
			{
				ApplyBaseForegroundForTheme(baseTheme);
			}

			ApplyOrbitSemanticTextResources();
			ThemeLogger.Log("<<< Custom theme applied successfully");
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
			var resources = Application.Current.Resources;

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
			resources["MahApps.Brushes.WindowTitle"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("MahApps.Brushes.WindowTitle", resources["MahApps.Brushes.WindowTitle"]);

			resources["Orbit.Brushes.Accent.Primary"] = CreateFrozenBrush(accentColor);
			ThemeLogger.LogResourceSet("Orbit.Brushes.Accent.Primary", resources["Orbit.Brushes.Accent.Primary"]);
			resources["Orbit.Brushes.Accent.Strong"] = CreateFrozenBrush(accentColor2);
			ThemeLogger.LogResourceSet("Orbit.Brushes.Accent.Strong", resources["Orbit.Brushes.Accent.Strong"]);

			resources["AccentColor"] = accentColor;
			resources["AccentColor2"] = accentColor2;
			resources["AccentColor3"] = accentColor3;
			resources["AccentColor4"] = accentColor4;
			resources["HighlightColor"] = highlightColor;
			resources["IdealForegroundColor"] = idealForeground;
			resources["AccentForegroundColor"] = idealForeground;
			resources["AccentColorBrush"] = CreateFrozenBrush(accentColor);
			resources["AccentColorBrush2"] = CreateFrozenBrush(accentColor2);
			resources["AccentColorBrush3"] = CreateFrozenBrush(accentColor3);
			resources["AccentColorBrush4"] = CreateFrozenBrush(accentColor4);
			resources["HighlightBrush"] = CreateFrozenBrush(highlightColor);
			resources["IdealForegroundColorBrush"] = CreateFrozenBrush(idealForeground);
			resources["AccentForegroundColorBrush"] = CreateFrozenBrush(idealForeground);
			resources["AccentSelectedForegroundColorBrush"] = CreateFrozenBrush(idealForeground);
			resources["AccentSelectedColorBrush"] = CreateFrozenBrush(accentColor);
			resources["WindowTitleColorBrush"] = CreateFrozenBrush(accentColor);
			resources["ProgressBrush"] = CreateFrozenBrush(accentColor);
			resources["CheckmarkFill"] = CreateFrozenBrush(idealForeground);
			resources["RightArrowFill"] = CreateFrozenBrush(idealForeground);
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

		private static void ApplyBaseForegroundForTheme(string baseTheme)
		{
			var foreground = baseTheme.Equals("Light", StringComparison.OrdinalIgnoreCase)
				? Colors.Black
				: Colors.White;
			var background = baseTheme.Equals("Light", StringComparison.OrdinalIgnoreCase)
				? Color.FromRgb(0xF5, 0xF5, 0xF5)
				: Color.FromRgb(0x1F, 0x1F, 0x1F);

			ApplyForegroundResources(foreground);
			SetBrushResource("MahApps.Brushes.ThemeBackground", background);
			Application.Current.Resources["MahApps.Colors.ThemeBackground"] = background;
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
			var accentForeground = ResolveResourceColor(resources, "MahApps.Brushes.AccentForeground", GetSharedAccentForeground(accent, accent2, accent3, accent4));

			var primary = EnsureReadable(themeForeground, background, 4.5);
			var secondary = DeriveSecondaryText(primary, background);
			var onAccent = accentForeground;

			var headerSurface = AverageColor(accent, accent2, accent3);
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
			return GetContrastRatio(black, background) >= GetContrastRatio(white, background) ? black : white;
		}

		private static Color DeriveSecondaryText(Color primary, Color background)
		{
			var candidate = Blend(primary, background, 0.72);
			return GetContrastRatio(candidate, background) >= 3.0 ? candidate : EnsureReadable(primary, background, 3.0);
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
			return Color.FromArgb((byte)Math.Round(a / count), (byte)Math.Round(r / count), (byte)Math.Round(g / count), (byte)Math.Round(b / count));
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
				return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
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

		private static string NormalizeBaseTheme(string? baseTheme)
			=> string.Equals(baseTheme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

		private static void ApplyBaseTheme(string baseTheme)
		{
			var appTheme = NormalizeBaseTheme(baseTheme).Equals("Light", StringComparison.OrdinalIgnoreCase)
				? ApplicationTheme.Light
				: ApplicationTheme.Dark;
			ApplicationThemeManager.Apply(appTheme, WindowBackdropType.None, updateAccent: false);
		}

		private static void RemoveCustomAccentOverrides()
		{
			ThemeLogger.Log("RemoveCustomAccentOverrides: Cleaning up custom accent resources");
			var resources = Application.Current.Resources;

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
				"Orbit.Brushes.Accent.Primary", "Orbit.Brushes.Accent.Strong",
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

			return minBlackContrast >= minWhiteContrast ? black : white;
		}
	}
}
