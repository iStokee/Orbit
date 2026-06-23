using Orbit.Services;
using Xunit;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using FontWeights = System.Windows.FontWeights;

namespace Orbit.Tests;

/// <summary>
/// Characterization tests for the pure color/typography helpers in <see cref="ThemeService"/>.
/// These pin the CURRENT behavior so the Cluster 2 theming refactor (collapsing the
/// static-XAML vs runtime-C# token duplication) can proceed without silently shifting
/// computed colors. They intentionally exercise only the Application-free code paths.
/// </summary>
public sealed class ThemeServiceColorMathTests
{
	private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

	[Fact]
	public void GetRelativeLuminance_BlackIsZero_WhiteIsOne()
	{
		Assert.Equal(0.0, ThemeService.GetRelativeLuminance(Colors.Black), 5);
		Assert.Equal(1.0, ThemeService.GetRelativeLuminance(Colors.White), 5);
	}

	[Fact]
	public void GetContrastRatio_BlackOnWhite_IsMaximum21()
	{
		// WCAG maximum contrast ratio is 21:1 and is symmetric.
		Assert.Equal(21.0, ThemeService.GetContrastRatio(Colors.Black, Colors.White), 3);
		Assert.Equal(21.0, ThemeService.GetContrastRatio(Colors.White, Colors.Black), 3);
	}

	[Fact]
	public void GetContrastRatio_SameColor_IsOne()
	{
		Assert.Equal(1.0, ThemeService.GetContrastRatio(Colors.SteelBlue, Colors.SteelBlue), 5);
	}

	[Theory]
	[InlineData(255, 255, 255, false)] // white background -> black text
	[InlineData(0, 0, 0, true)]        // black background -> white text
	[InlineData(255, 200, 0, false)]   // bright amber -> black text
	[InlineData(20, 20, 60, true)]     // dark navy -> white text
	public void GetBestContrastText_PicksReadableForeground(byte r, byte g, byte b, bool expectWhite)
	{
		var foreground = ThemeService.GetBestContrastText(Argb(255, r, g, b));

		Assert.Equal(expectWhite ? Colors.White : Colors.Black, foreground);
	}

	[Fact]
	public void ChangeColorBrightness_NegativeFactor_DarkensTowardBlack_PreservingAlpha()
	{
		// factor < 0 => component *= (1 + factor); white * 0.5 -> 127 (truncated)
		var result = ThemeService.ChangeColorBrightness(Argb(200, 255, 255, 255), -0.5);

		Assert.Equal(Argb(200, 127, 127, 127), result);
	}

	[Fact]
	public void ChangeColorBrightness_PositiveFactor_LightensTowardWhite_PreservingAlpha()
	{
		// factor > 0 => component += (255 - component) * factor; black + 0.5 -> 127 (truncated)
		var result = ThemeService.ChangeColorBrightness(Argb(200, 0, 0, 0), 0.5);

		Assert.Equal(Argb(200, 127, 127, 127), result);
	}

	[Fact]
	public void CreateSecondaryFrom_ScalesAlphaOnly()
	{
		// alpha = round(source.A * clamp(scale)); 255 * 0.82 = 209.1 -> 209; RGB preserved.
		var result = ThemeService.CreateSecondaryFrom(Argb(255, 10, 20, 30), 0.82);

		Assert.Equal(Argb(209, 10, 20, 30), result);
	}

	[Fact]
	public void CreateSecondaryFrom_ClampsScaleToUnitRange()
	{
		Assert.Equal((byte)255, ThemeService.CreateSecondaryFrom(Colors.Red, 5.0).A);
		Assert.Equal((byte)0, ThemeService.CreateSecondaryFrom(Colors.Red, -1.0).A);
	}

	[Fact]
	public void GetSharedAccentForeground_EmptyOrNull_DefaultsToWhite()
	{
		Assert.Equal(Colors.White, ThemeService.GetSharedAccentForeground());
		Assert.Equal(Colors.White, ThemeService.GetSharedAccentForeground((Color[])null!));
	}

	[Fact]
	public void GetSharedAccentForeground_PicksStrongestWorstCaseContrast()
	{
		// A bright surface set reads best with black; a dark set reads best with white.
		Assert.Equal(Colors.Black, ThemeService.GetSharedAccentForeground(Argb(255, 250, 230, 120)));
		Assert.Equal(Colors.White, ThemeService.GetSharedAccentForeground(Argb(255, 20, 20, 50)));
	}

	[Theory]
	[InlineData("BaseDark", "Dark")]
	[InlineData("BaseLight", "Light")]
	[InlineData("basedark", "Dark")] // case-insensitive
	[InlineData("Dark", "Dark")]     // already v2 -> passthrough
	[InlineData("Light", "Light")]
	[InlineData("Unknown", "Unknown")] // unknown -> passthrough
	[InlineData(null, "Dark")]
	[InlineData("", "Dark")]
	[InlineData("   ", "Dark")]
	public void ConvertLegacyBaseTheme_NormalizesToV2Names(string? input, string expected)
	{
		Assert.Equal(expected, new ThemeService().ConvertLegacyBaseTheme(input));
	}

	[Theory]
	[InlineData("Normal")]
	[InlineData("garbage")]
	[InlineData("")]
	public void ResolveFontWeight_UnknownOrNormal_ReturnsNormal(string input)
	{
		Assert.Equal(FontWeights.Normal, ThemeService.ResolveFontWeight(input));
	}

	[Fact]
	public void ResolveFontWeight_KnownWeights_AreCaseInsensitive()
	{
		Assert.Equal(FontWeights.SemiBold, ThemeService.ResolveFontWeight("semibold"));
		Assert.Equal(FontWeights.SemiBold, ThemeService.ResolveFontWeight("SemiBold"));
		Assert.Equal(FontWeights.Bold, ThemeService.ResolveFontWeight("BOLD"));
	}

	[Fact]
	public void SanitizeTypographySettings_Null_ReturnsDefaults()
	{
		var result = ThemeService.SanitizeTypographySettings(null);

		Assert.Equal("Segoe UI", result.FontFamily);
		Assert.Equal(12, result.BaseFontSize);
		Assert.Equal("Normal", result.FontWeight);
	}

	[Theory]
	[InlineData(5.0, 10.0)]   // below floor -> clamped to 10
	[InlineData(30.0, 22.0)]  // above ceiling -> clamped to 22
	[InlineData(13.46, 13.5)] // rounded to one decimal
	[InlineData(13.45, 13.4)] // 13.45 is 13.4499.. in double -> rounds down
	public void SanitizeTypographySettings_ClampsAndRoundsSize(double input, double expected)
	{
		var result = ThemeService.SanitizeTypographySettings(new ThemeService.TypographySettings { BaseFontSize = input });

		Assert.Equal(expected, result.BaseFontSize);
	}

	[Fact]
	public void SanitizeTypographySettings_NormalizesFamilyAndWeight()
	{
		var result = ThemeService.SanitizeTypographySettings(new ThemeService.TypographySettings
		{
			FontFamily = "   ",
			FontWeight = "ultralight",
			BaseFontSize = 14
		});

		Assert.Equal("Segoe UI", result.FontFamily); // blank -> default
		Assert.Equal("Normal", result.FontWeight);   // unsupported -> Normal
		Assert.Equal(14, result.BaseFontSize);

		var trimmed = ThemeService.SanitizeTypographySettings(new ThemeService.TypographySettings
		{
			FontFamily = "  Consolas  ",
			FontWeight = "SemiBold",
			BaseFontSize = 16
		});

		Assert.Equal("Consolas", trimmed.FontFamily); // trimmed
		Assert.Equal("SemiBold", trimmed.FontWeight); // supported weight preserved
	}

	[Fact]
	public void TryParseColor_ValidHex_Succeeds()
	{
		Assert.True(ThemeService.TryParseColor("#FF1BA1E2", out var color));
		Assert.Equal(Argb(255, 0x1B, 0xA1, 0xE2), color);
	}

	[Fact]
	public void TryParseColor_Invalid_FailsAndFallsBackToSteelBlue()
	{
		Assert.False(ThemeService.TryParseColor("not-a-color", out var color));
		Assert.Equal(Colors.SteelBlue, color);
	}
}
