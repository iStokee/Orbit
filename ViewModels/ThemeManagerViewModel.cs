using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using Application = System.Windows.Application;


namespace Orbit.ViewModels
{
	public class ThemeManagerViewModel : INotifyPropertyChanged
	{
		private readonly ThemeService themeService;
		private string customThemeName = string.Empty;
		private MediaColor selectedCustomColor = MediaColors.SteelBlue;
		private CustomThemeDefinition? selectedCustomTheme;
		private string? selectedBaseTheme;
		private string? selectedColorScheme;
		private bool useCustomForeground;
		private MediaColor selectedCustomForeground = MediaColors.White;

		public ThemeManagerViewModel() : this(new ThemeService())
		{
		}

		public ThemeManagerViewModel(ThemeService themeService)
		{
			this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

			BaseThemes = themeService.GetAvailableBaseThemes();
			ColorSchemes = themeService.GetAvailableColorSchemes();
			CustomThemes = themeService.LoadCustomThemes();

			// Detect current theme
			DetectCurrentTheme();

			ApplyThemeCommand = new RelayCommand(_ => ApplySelectedTheme(), _ => CanApplyTheme());
			SaveCustomThemeCommand = new RelayCommand(_ => SaveCustomTheme(), _ => CanSaveCustomTheme());
			ApplyCustomThemeCommand = new RelayCommand(_ => ApplyCustomTheme(), _ => SelectedCustomTheme != null);
			DeleteCustomThemeCommand = new RelayCommand(_ => DeleteCustomTheme(), _ => SelectedCustomTheme != null);
		}

		public ObservableCollection<string> BaseThemes { get; }
		public ObservableCollection<string> ColorSchemes { get; }
		public ObservableCollection<CustomThemeDefinition> CustomThemes { get; }

		public string? SelectedBaseTheme
		{
			get => selectedBaseTheme;
			set
			{
				if (selectedBaseTheme == value)
					return;
				selectedBaseTheme = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public string? SelectedColorScheme
		{
			get => selectedColorScheme;
			set
			{
				if (selectedColorScheme == value)
					return;
				selectedColorScheme = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public string CustomThemeName
		{
			get => customThemeName;
			set
			{
				if (customThemeName == value)
					return;
				customThemeName = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public MediaColor SelectedCustomColor
		{
			get => selectedCustomColor;
			set
			{
				if (selectedCustomColor == value)
					return;
				selectedCustomColor = value;
				OnPropertyChanged();
			}
		}

		public bool UseCustomForeground
		{
			get => useCustomForeground;
			set
			{
				if (useCustomForeground == value)
					return;
				useCustomForeground = value;
				OnPropertyChanged();

				if (SelectedCustomTheme != null)
				{
					SelectedCustomTheme.OverrideForeground = value;
					SelectedCustomTheme.ForegroundHex = value ? SelectedCustomForeground.ToString() : null;
				}
			}
		}

		public MediaColor SelectedCustomForeground
		{
			get => selectedCustomForeground;
			set
			{
				if (selectedCustomForeground == value)
					return;
				selectedCustomForeground = value;
				OnPropertyChanged();

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
				selectedCustomTheme = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
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
					}
					catch
					{
						// Ignore malformed colors and keep current picker selection.
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
						}
						catch
						{
							// Ignore parse failures and fall back below.
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
				}
			}
		}

		public ICommand ApplyThemeCommand { get; }
		public ICommand SaveCustomThemeCommand { get; }
		public ICommand ApplyCustomThemeCommand { get; }
		public ICommand DeleteCustomThemeCommand { get; }

		public event PropertyChangedEventHandler? PropertyChanged;

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

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
