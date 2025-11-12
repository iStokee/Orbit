using Orbit.Logging;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaColors = System.Windows.Media.Colors;

namespace Orbit.ViewModels
{
	public enum ThemeColorEditorMode
	{
		Accent,
		Foreground
	}

	public class ThemeManagerViewModel : ObservableObject
	{
		private readonly ThemeService themeService;
		private string customThemeName = string.Empty;
		private MediaColor selectedCustomColor = MediaColors.SteelBlue;
		private CustomThemeDefinition? selectedCustomTheme;
		private string? selectedBaseTheme;
		private string? selectedColorScheme;
		private bool useCustomForeground;
		private MediaColor selectedCustomForeground = MediaColors.White;
		private ThemeColorEditorMode activeColorEditor = ThemeColorEditorMode.Accent;

		public ThemeManagerViewModel() : this(new ThemeService())
		{
		}

		public ThemeManagerViewModel(ThemeService themeService)
		{
			this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

			BaseThemes = themeService.GetAvailableBaseThemes();
			ColorSchemes = themeService.GetAvailableColorSchemes();
			CustomThemes = themeService.LoadCustomThemes();

			ApplyThemeCommand = new RelayCommand(ApplySelectedTheme, CanApplyTheme);
			SaveCustomThemeCommand = new RelayCommand(SaveCustomTheme, CanSaveCustomTheme);
			ApplyCustomThemeCommand = new RelayCommand(ApplyCustomTheme, () => SelectedCustomTheme != null);
			DeleteCustomThemeCommand = new RelayCommand(DeleteCustomTheme, () => SelectedCustomTheme != null);

			// Detect current theme
			DetectCurrentTheme();
		}

		public ObservableCollection<string> BaseThemes { get; }
			public ObservableCollection<string> ColorSchemes { get; }
			public ObservableCollection<CustomThemeDefinition> CustomThemes { get; }

			public string? SelectedBaseTheme
			{
				get => selectedBaseTheme;
				set
				{
					if (SetProperty(ref selectedBaseTheme, value))
					{
						ApplyThemeCommand.NotifyCanExecuteChanged();
						SaveCustomThemeCommand.NotifyCanExecuteChanged();
					}
				}
			}

			public string? SelectedColorScheme
			{
				get => selectedColorScheme;
				set
				{
					if (SetProperty(ref selectedColorScheme, value))
					{
						ApplyThemeCommand.NotifyCanExecuteChanged();
						SaveCustomThemeCommand.NotifyCanExecuteChanged();
					}
				}
			}

			public string CustomThemeName
			{
				get => customThemeName;
				set
				{
					if (SetProperty(ref customThemeName, value))
					{
						SaveCustomThemeCommand.NotifyCanExecuteChanged();
					}
				}
			}

		public MediaColor SelectedCustomColor
		{
			get => selectedCustomColor;
			set
			{
				if (!SetProperty(ref selectedCustomColor, value))
					return;

				if (ActiveColorEditor == ThemeColorEditorMode.Accent)
				{
					OnPropertyChanged(nameof(ActiveColorSelection));
				}
			}
		}

		public bool UseCustomForeground
		{
			get => useCustomForeground;
			set
			{
				if (!SetProperty(ref useCustomForeground, value))
					return;

				OnPropertyChanged(nameof(IsActiveColorEditable));

				if (SelectedCustomTheme != null)
				{
					SelectedCustomTheme.OverrideForeground = value;
					SelectedCustomTheme.ForegroundHex = value ? SelectedCustomForeground.ToString() : null;
				}

				if (!useCustomForeground && ActiveColorEditor == ThemeColorEditorMode.Foreground)
				{
					ActiveColorEditor = ThemeColorEditorMode.Accent;
				}
			}
		}

		public MediaColor SelectedCustomForeground
		{
			get => selectedCustomForeground;
			set
			{
				if (!SetProperty(ref selectedCustomForeground, value))
					return;

				if (ActiveColorEditor == ThemeColorEditorMode.Foreground)
				{
					OnPropertyChanged(nameof(ActiveColorSelection));
				}

				if (UseCustomForeground && SelectedCustomTheme != null)
				{
					SelectedCustomTheme.ForegroundHex = selectedCustomForeground.ToString();
				}
			}
		}

		public CustomThemeDefinition? SelectedCustomTheme
		{
			get => selectedCustomTheme;
			set
			{
				if (selectedCustomTheme == value)
					return;
				if (!SetProperty(ref selectedCustomTheme, value))
					return;
				if (selectedCustomTheme != null)
				{
					CustomThemeName = selectedCustomTheme.Name;

					// Update base theme selection
					if (!string.IsNullOrEmpty(selectedCustomTheme.BaseTheme))
					{
						selectedBaseTheme = selectedCustomTheme.BaseTheme;
						OnPropertyChanged(nameof(SelectedBaseTheme));
					}

					try
					{
						var converted = MediaColorConverter.ConvertFromString(selectedCustomTheme.AccentHex);
						if (converted is MediaColor color)
						{
							SelectedCustomColor = color;
						}
						else
						{
							ConsoleLogService.Instance.Append(
								$"[ThemeManager] Invalid accent color '{selectedCustomTheme.AccentHex}' in theme '{selectedCustomTheme.Name}'. Color parsing returned null.",
								ConsoleLogSource.Orbit,
								ConsoleLogLevel.Warning);
						}
					}
					catch (Exception ex)
					{
						ConsoleLogService.Instance.Append(
							$"[ThemeManager] Failed to parse accent color in theme '{selectedCustomTheme.Name}': {ex.Message}",
							ConsoleLogSource.Orbit,
							ConsoleLogLevel.Warning);
					}

					if (selectedCustomTheme.OverrideForeground && !string.IsNullOrWhiteSpace(selectedCustomTheme.ForegroundHex))
					{
						try
						{
							var convertedForeground = MediaColorConverter.ConvertFromString(selectedCustomTheme.ForegroundHex);
							if (convertedForeground is MediaColor foregroundColor)
							{
								SelectedCustomForeground = foregroundColor;
							}
							else
							{
								ConsoleLogService.Instance.Append(
									$"[ThemeManager] Invalid foreground color '{selectedCustomTheme.ForegroundHex}' in theme '{selectedCustomTheme.Name}'. Color parsing returned null.",
									ConsoleLogSource.Orbit,
									ConsoleLogLevel.Warning);
							}
						}
						catch (Exception ex)
						{
							ConsoleLogService.Instance.Append(
								$"[ThemeManager] Failed to parse foreground color in theme '{selectedCustomTheme.Name}': {ex.Message}",
								ConsoleLogSource.Orbit,
								ConsoleLogLevel.Warning);
						}
						UseCustomForeground = true;
					}
					else
					{
						UseCustomForeground = false;
						SelectedCustomForeground = themeService.GetCurrentForegroundColor();
					}
				}
				else
				{
					UseCustomForeground = false;
					SelectedCustomForeground = themeService.GetCurrentForegroundColor();
				}

				ApplyCustomThemeCommand.NotifyCanExecuteChanged();
				DeleteCustomThemeCommand.NotifyCanExecuteChanged();

				OnPropertyChanged(nameof(ActiveColorSelection));
			}
		}

		public ThemeColorEditorMode ActiveColorEditor
		{
			get => activeColorEditor;
			set
			{
				if (activeColorEditor == value)
					return;
				activeColorEditor = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsAccentEditorSelected));
				OnPropertyChanged(nameof(IsForegroundEditorSelected));
				OnPropertyChanged(nameof(ActiveColorSelection));
				OnPropertyChanged(nameof(IsActiveColorEditable));
			}
		}

		public bool IsAccentEditorSelected
		{
			get => ActiveColorEditor == ThemeColorEditorMode.Accent;
			set
			{
				if (value)
					ActiveColorEditor = ThemeColorEditorMode.Accent;
			}
		}

		public bool IsForegroundEditorSelected
		{
			get => ActiveColorEditor == ThemeColorEditorMode.Foreground;
			set
			{
				if (value)
				{
					ActiveColorEditor = ThemeColorEditorMode.Foreground;
					// Automatically enable custom foreground when foreground editor is selected
					if (!UseCustomForeground)
					{
						UseCustomForeground = true;
					}
				}
			}
		}

		public MediaColor ActiveColorSelection
		{
			get => ActiveColorEditor == ThemeColorEditorMode.Accent ? SelectedCustomColor : SelectedCustomForeground;
			set
			{
				if (ActiveColorEditor == ThemeColorEditorMode.Accent)
				{
					SelectedCustomColor = value;
				}
				else
				{
					SelectedCustomForeground = value;
				}
			}
		}

		public bool IsActiveColorEditable => ActiveColorEditor == ThemeColorEditorMode.Accent || UseCustomForeground;

		public IRelayCommand ApplyThemeCommand { get; }
		public IRelayCommand SaveCustomThemeCommand { get; }
		public IRelayCommand ApplyCustomThemeCommand { get; }
		public IRelayCommand DeleteCustomThemeCommand { get; }

		private void DetectCurrentTheme()
		{
			// Try to detect current theme from settings
			var savedTheme = Settings.Default.Theme;
			var savedAccent = Settings.Default.Accent;

			// Convert legacy theme names
			if (savedTheme == "BaseDark")
				savedTheme = "Dark";
			else if (savedTheme == "BaseLight")
				savedTheme = "Light";

			// Set defaults if not found
			SelectedBaseTheme = BaseThemes.Contains(savedTheme ?? "Dark") ? savedTheme : BaseThemes.FirstOrDefault() ?? "Dark";

			// Handle accent/color scheme
			if (!string.IsNullOrEmpty(savedAccent) && !savedAccent.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
			{
				SelectedColorScheme = ColorSchemes.Contains(savedAccent) ? savedAccent : ColorSchemes.FirstOrDefault() ?? "Steel";
			}
			else
			{
				SelectedColorScheme = ColorSchemes.FirstOrDefault() ?? "Steel";
			}

			SelectedCustomColor = themeService.GetCurrentAccentColor();
			SelectedCustomForeground = themeService.GetCurrentForegroundColor();
			UseCustomForeground = false;
		}

		private bool CanApplyTheme()
			=> !string.IsNullOrEmpty(SelectedBaseTheme) && !string.IsNullOrEmpty(SelectedColorScheme);

		private void ApplySelectedTheme()
		{
			if (string.IsNullOrEmpty(SelectedBaseTheme) || string.IsNullOrEmpty(SelectedColorScheme))
				return;

			// Clear the selected custom theme to avoid visual confusion in the UI
			// (built-in themes don't use custom themes)
			selectedCustomTheme = null;
			OnPropertyChanged(nameof(SelectedCustomTheme));

			themeService.ApplyBuiltInTheme(SelectedBaseTheme, SelectedColorScheme);
			SelectedCustomColor = themeService.GetCurrentAccentColor();
			SelectedCustomForeground = themeService.GetCurrentForegroundColor();
			UseCustomForeground = false;
		}

		private bool CanSaveCustomTheme()
			=> !string.IsNullOrWhiteSpace(CustomThemeName) && !string.IsNullOrEmpty(SelectedBaseTheme);

		private void SaveCustomTheme()
		{
			if (string.IsNullOrEmpty(SelectedBaseTheme))
				return;

			var definition = new CustomThemeDefinition
			{
				Name = CustomThemeName.Trim(),
				BaseTheme = SelectedBaseTheme,
				AccentHex = SelectedCustomColor.ToString(),
				OverrideForeground = UseCustomForeground,
				ForegroundHex = UseCustomForeground ? SelectedCustomForeground.ToString() : null
			};

			var existing = CustomThemes.FirstOrDefault(t => string.Equals(t.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
			if (existing != null)
			{
				existing.BaseTheme = definition.BaseTheme;
				existing.AccentHex = definition.AccentHex;
				existing.OverrideForeground = definition.OverrideForeground;
				existing.ForegroundHex = definition.ForegroundHex;
			}
			else
			{
				CustomThemes.Add(definition);
			}

			themeService.SaveCustomThemes(CustomThemes);
			SelectedCustomTheme = definition;
			ApplyCustomTheme();
		}

		private void ApplyCustomTheme()
		{
			if (SelectedCustomTheme == null)
				return;

			// Ensure the custom theme honors the current base selection
			if (!string.IsNullOrEmpty(SelectedBaseTheme) &&
				!string.Equals(SelectedCustomTheme.BaseTheme, SelectedBaseTheme, StringComparison.OrdinalIgnoreCase))
			{
				SelectedCustomTheme.BaseTheme = SelectedBaseTheme;
				themeService.SaveCustomThemes(CustomThemes);
				OnPropertyChanged(nameof(SelectedCustomTheme));
			}

			// Clear the selected color scheme to avoid visual confusion in the UI
			// (custom themes don't use the built-in color schemes)
			selectedColorScheme = null;
			OnPropertyChanged(nameof(SelectedColorScheme));

			SelectedCustomTheme.OverrideForeground = UseCustomForeground;
			SelectedCustomTheme.ForegroundHex = UseCustomForeground ? SelectedCustomForeground.ToString() : null;

			themeService.ApplyCustomTheme(SelectedCustomTheme);
			SelectedCustomColor = themeService.GetCurrentAccentColor();
			SelectedCustomForeground = themeService.GetCurrentForegroundColor();
		}

		private void DeleteCustomTheme()
		{
			if (SelectedCustomTheme == null)
				return;

			var toRemove = SelectedCustomTheme;
			CustomThemes.Remove(toRemove);
			themeService.SaveCustomThemes(CustomThemes);
			SelectedCustomTheme = CustomThemes.FirstOrDefault();
		}
	}
}
