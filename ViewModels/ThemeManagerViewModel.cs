using Orbit.Logging;
using Microsoft.Extensions.DependencyInjection;
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
		Foreground,
		UpdateBadge
	}

	public class ThemeManagerViewModel : ObservableObject, IDisposable
	{
	public string? StorageError => themeService.LastError;
	private void OnStorageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{ if (e.PropertyName == nameof(ThemeService.LastError)) OnPropertyChanged(nameof(StorageError)); }
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		if (_typographySaveTimer.IsEnabled) { _typographySaveTimer.Stop(); SaveTypography(); }
		_typographySaveTimer.Tick -= OnTypographySaveTick;
		themeService.PropertyChanged -= OnStorageChanged;
	}
	private bool _disposed;
	private string _themeStatusMessage = string.Empty;
	public string ThemeStatusMessage { get => _themeStatusMessage; private set => SetProperty(ref _themeStatusMessage, value); }
	private readonly System.Windows.Threading.DispatcherTimer _typographySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
	private readonly ThemeService themeService;
	private string customThemeName = string.Empty;
	private MediaColor selectedCustomColor = MediaColors.SteelBlue;
	private MediaColor updateBadgeColor = MediaColor.FromRgb(255, 196, 77);
	private string updateBadgeColorHex = "#FFFFC44D";
	private CustomThemeDefinition? selectedCustomTheme;
		private string? selectedBaseTheme;
		private string? selectedColorScheme;
	private bool useCustomForeground;
	private MediaColor selectedCustomForeground = MediaColors.White;
	private string selectedFontFamily = "Segoe UI";
	private double baseFontSize = 12;
	private string selectedFontWeight = "Normal";
	private bool suppressTypographyApply;
		private ThemeColorEditorMode activeColorEditor = ThemeColorEditorMode.Accent;
		private const string UpdateBadgeBrushKey = "Orbit.UpdateBadgeBrush";
		private const string DefaultUpdateBadgeColorHex = "#FFFFC44D";

		public ThemeManagerViewModel() : this(ResolveThemeService())
		{
		}

		private static ThemeService ResolveThemeService()
		{
			var app = Application.Current as App;
			var service = app?.Services.GetService<ThemeService>();
			if (service == null)
			{
				throw new InvalidOperationException("ThemeManagerViewModel requires ThemeService from DI.");
			}

			return service;
		}

		public ThemeManagerViewModel(ThemeService themeService)
		{
			this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
			themeService.PropertyChanged += OnStorageChanged;
			_typographySaveTimer.Tick += OnTypographySaveTick;

			BaseThemes = themeService.GetAvailableBaseThemes();
			ColorSchemes = themeService.GetAvailableColorSchemes();
			CustomThemes = themeService.LoadCustomThemes();
			AvailableFontFamilies = themeService.GetAvailableFontFamilies();
			AvailableFontWeights = themeService.GetAvailableFontWeights();

			ApplyThemeCommand = new RelayCommand(ApplySelectedTheme, CanApplyTheme);
			SaveCustomThemeCommand = new RelayCommand(SaveCustomTheme, CanSaveCustomTheme);
			ApplyCustomThemeCommand = new RelayCommand(ApplyCustomTheme, () => !string.IsNullOrEmpty(SelectedBaseTheme));
			DeleteCustomThemeCommand = new RelayCommand(DeleteCustomTheme, () => SelectedCustomTheme != null);
			ResetActiveColorCommand = new RelayCommand(ResetActiveColor);
			ResetTypographyCommand = new RelayCommand(ResetTypography);

			InitializeTypographySettings();

			// Detect current theme
			DetectCurrentTheme();
			InitializeUpdateBadgeColor();
		}

		public ObservableCollection<string> BaseThemes { get; }
			public ObservableCollection<string> ColorSchemes { get; }
			public ObservableCollection<CustomThemeDefinition> CustomThemes { get; }
			public ObservableCollection<string> AvailableFontFamilies { get; }
			public ObservableCollection<string> AvailableFontWeights { get; }

			public string? SelectedBaseTheme
			{
				get => selectedBaseTheme;
				set
				{
					if (SetProperty(ref selectedBaseTheme, value))
					{
						ApplyThemeCommand.NotifyCanExecuteChanged();
						SaveCustomThemeCommand.NotifyCanExecuteChanged();
						ApplyCustomThemeCommand.NotifyCanExecuteChanged();
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
					OnPropertyChanged(nameof(ActiveColorHex));
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
					OnPropertyChanged(nameof(ActiveColorHex));
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
				if (activeColorEditor == ThemeColorEditorMode.Foreground && !UseCustomForeground)
				{
					UseCustomForeground = true;
				}

				OnPropertyChanged();
				OnPropertyChanged(nameof(IsAccentEditorSelected));
				OnPropertyChanged(nameof(IsForegroundEditorSelected));
				OnPropertyChanged(nameof(IsUpdateBadgeEditorSelected));
				OnPropertyChanged(nameof(ActiveColorSelection));
				OnPropertyChanged(nameof(ActiveColorHex));
				OnPropertyChanged(nameof(IsActiveColorEditable));
				OnPropertyChanged(nameof(ActiveColorEditorLabel));
				OnPropertyChanged(nameof(ActiveColorEditorDescription));
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

		public bool IsUpdateBadgeEditorSelected
		{
			get => ActiveColorEditor == ThemeColorEditorMode.UpdateBadge;
			set
			{
				if (value)
				{
					ActiveColorEditor = ThemeColorEditorMode.UpdateBadge;
				}
			}
		}

		public MediaColor ActiveColorSelection
		{
			get => ActiveColorEditor switch
			{
				ThemeColorEditorMode.Accent => SelectedCustomColor,
				ThemeColorEditorMode.Foreground => SelectedCustomForeground,
				ThemeColorEditorMode.UpdateBadge => UpdateBadgeColor,
				_ => SelectedCustomColor
			};
			set
			{
				switch (ActiveColorEditor)
				{
					case ThemeColorEditorMode.Accent:
						SelectedCustomColor = value;
						break;
					case ThemeColorEditorMode.Foreground:
						SelectedCustomForeground = value;
						break;
					case ThemeColorEditorMode.UpdateBadge:
						UpdateBadgeColor = value;
						break;
				}
			}
		}

		public bool IsActiveColorEditable => true;

		public string ActiveColorHex
		{
			get => ActiveColorSelection.ToString();
			set
			{
				var candidate = value?.Trim() ?? string.Empty;
				if (!TryParseColor(candidate, out var color))
				{
					OnPropertyChanged();
					return;
				}

				ActiveColorSelection = color;
				OnPropertyChanged();
			}
		}

		public string ActiveColorEditorLabel => ActiveColorEditor switch
		{
			ThemeColorEditorMode.Accent => "Accent",
			ThemeColorEditorMode.Foreground => "Foreground",
			ThemeColorEditorMode.UpdateBadge => "Update badge",
			_ => "Color"
		};

		public string ActiveColorEditorDescription => ActiveColorEditor switch
		{
			ThemeColorEditorMode.Accent => "Used for highlights, selected controls, and accent surfaces.",
			ThemeColorEditorMode.Foreground => "Used for text and icon foreground. Selecting this enables custom foreground override.",
			ThemeColorEditorMode.UpdateBadge => "Used for update indicators in menu and settings.",
			_ => string.Empty
		};

		public MediaColor UpdateBadgeColor
		{
			get => updateBadgeColor;
			set
			{
				if (!SetProperty(ref updateBadgeColor, value))
				{
					return;
				}

				var normalized = value.ToString();
				if (!string.Equals(updateBadgeColorHex, normalized, StringComparison.OrdinalIgnoreCase))
				{
					updateBadgeColorHex = normalized;
					OnPropertyChanged(nameof(UpdateBadgeColorHex));
				}

				if (ActiveColorEditor == ThemeColorEditorMode.UpdateBadge)
				{
					OnPropertyChanged(nameof(ActiveColorSelection));
					OnPropertyChanged(nameof(ActiveColorHex));
				}

				Settings.Default.UpdateBadgeColorHex = normalized;
				Settings.Default.Save();
				ApplyUpdateBadgeBrush(value);
			}
		}

		public string UpdateBadgeColorHex
		{
			get => updateBadgeColorHex;
			set
			{
				var candidate = value?.Trim() ?? string.Empty;
				if (!TryParseColor(candidate, out var color))
				{
					OnPropertyChanged();
					return;
				}

				UpdateBadgeColor = color;
				OnPropertyChanged();
			}
		}

		public string SelectedFontFamily
		{
			get => selectedFontFamily;
			set
			{
				if (!SetProperty(ref selectedFontFamily, value))
				{
					return;
				}

				ApplyTypographyIfReady();
			}
		}

		public double BaseFontSize
		{
			get => baseFontSize;
			set
			{
				var clamped = Math.Clamp(value, 10.0, 22.0);
				if (!SetProperty(ref baseFontSize, clamped))
				{
					return;
				}

				ApplyTypographyIfReady();
			}
		}

		public string SelectedFontWeight
		{
			get => selectedFontWeight;
			set
			{
				if (!SetProperty(ref selectedFontWeight, value))
				{
					return;
				}

				ApplyTypographyIfReady();
			}
		}

		public IRelayCommand ApplyThemeCommand { get; }
		public IRelayCommand SaveCustomThemeCommand { get; }
		public IRelayCommand ApplyCustomThemeCommand { get; }
		public IRelayCommand DeleteCustomThemeCommand { get; }
		public IRelayCommand ResetActiveColorCommand { get; }
		public IRelayCommand ResetTypographyCommand { get; }
		public IRelayCommand ResetUpdateBadgeColorCommand => ResetActiveColorCommand;

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

		private void InitializeTypographySettings()
		{
			suppressTypographyApply = true;
			var settings = themeService.LoadTypographySettings();

			if (!AvailableFontFamilies.Contains(settings.FontFamily))
			{
				AvailableFontFamilies.Insert(0, settings.FontFamily);
			}

			SelectedFontFamily = settings.FontFamily;
			BaseFontSize = settings.BaseFontSize;
			SelectedFontWeight = settings.FontWeight;
			suppressTypographyApply = false;
			ApplyTypographyIfReady(persist: false);
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
			ThemeStatusMessage = $"Applied {SelectedBaseTheme} {SelectedColorScheme}.";
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
			var savedThemes = CustomThemes.ToList();
			if (existing == null) savedThemes.Add(definition);
			else savedThemes[savedThemes.IndexOf(existing)] = definition;
			if (!themeService.TrySaveCustomThemes(savedThemes)) return;
			if (existing != null)
			{
				CustomThemes[CustomThemes.IndexOf(existing)] = definition;
			}
			else
			{
				CustomThemes.Add(definition);
			}

			SelectedCustomTheme = definition;
			themeService.ApplyCustomTheme(definition);
			ThemeStatusMessage = $"Saved and applied '{definition.Name}'.";
		}

		private void ApplyCustomTheme()
		{
			// Clear the selected color scheme to avoid visual confusion in the UI
			// (custom themes don't use the built-in color schemes)
			selectedColorScheme = null;
			OnPropertyChanged(nameof(SelectedColorScheme));

			themeService.ApplyCustomTheme(new CustomThemeDefinition
			{
				Name = string.IsNullOrWhiteSpace(CustomThemeName) ? "Preview" : CustomThemeName.Trim(),
				BaseTheme = SelectedBaseTheme ?? "Dark",
				AccentHex = SelectedCustomColor.ToString(),
				OverrideForeground = UseCustomForeground,
				ForegroundHex = UseCustomForeground ? SelectedCustomForeground.ToString() : null
			}, persist: false);
			ThemeStatusMessage = "Palette preview applied. Save to keep these changes.";
			SelectedCustomColor = themeService.GetCurrentAccentColor();
			SelectedCustomForeground = themeService.GetCurrentForegroundColor();
		}

		private void DeleteCustomTheme()
		{
			if (SelectedCustomTheme == null)
				return;

			var toRemove = SelectedCustomTheme;
			if (!themeService.TrySaveCustomThemes(CustomThemes.Where(theme => !ReferenceEquals(theme, toRemove)))) return;
			CustomThemes.Remove(toRemove);
			SelectedCustomTheme = CustomThemes.FirstOrDefault();
		}

		private void InitializeUpdateBadgeColor()
		{
			var configured = Settings.Default.UpdateBadgeColorHex;
			if (!TryParseColor(configured, out var color))
			{
				TryParseColor(DefaultUpdateBadgeColorHex, out color);
				Settings.Default.UpdateBadgeColorHex = DefaultUpdateBadgeColorHex;
				Settings.Default.Save();
			}

			updateBadgeColor = color;
			updateBadgeColorHex = color.ToString();
			OnPropertyChanged(nameof(UpdateBadgeColor));
			OnPropertyChanged(nameof(UpdateBadgeColorHex));
			ApplyUpdateBadgeBrush(color);
		}

		private void ResetActiveColor()
		{
			switch (ActiveColorEditor)
			{
				case ThemeColorEditorMode.Accent:
					SelectedCustomColor = themeService.GetCurrentAccentColor();
					break;
				case ThemeColorEditorMode.Foreground:
					SelectedCustomForeground = themeService.GetCurrentForegroundColor();
					break;
				case ThemeColorEditorMode.UpdateBadge:
					UpdateBadgeColorHex = DefaultUpdateBadgeColorHex;
					break;
			}

			OnPropertyChanged(nameof(ActiveColorSelection));
			OnPropertyChanged(nameof(ActiveColorHex));
		}

		private void ApplyTypographyIfReady(bool persist = true)
		{
			if (suppressTypographyApply || _disposed)
			{
				return;
			}

			themeService.ApplyTypographySettings(new ThemeService.TypographySettings
			{
				FontFamily = SelectedFontFamily,
				BaseFontSize = BaseFontSize,
				FontWeight = SelectedFontWeight
			}, persist: false);
			if (persist) { _typographySaveTimer.Stop(); _typographySaveTimer.Start(); }
		}

		private void OnTypographySaveTick(object? sender, EventArgs e)
		{
			_typographySaveTimer.Stop();
			SaveTypography();
		}

		private void SaveTypography() => themeService.ApplyTypographySettings(new ThemeService.TypographySettings
		{
			FontFamily = SelectedFontFamily, BaseFontSize = BaseFontSize, FontWeight = SelectedFontWeight
		});

		private void ResetTypography()
		{
			suppressTypographyApply = true;
			SelectedFontFamily = "Segoe UI";
			BaseFontSize = 12;
			SelectedFontWeight = "Normal";
			suppressTypographyApply = false;
			ApplyTypographyIfReady();
		}

		private static bool TryParseColor(string? value, out MediaColor color)
		{
			color = default;
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}

			try
			{
				var parsed = MediaColorConverter.ConvertFromString(value);
				if (parsed is MediaColor resolved)
				{
					color = resolved;
					return true;
				}
			}
			catch
			{
				// invalid format
			}

			return false;
		}

		private static void ApplyUpdateBadgeBrush(MediaColor color)
		{
			var app = Application.Current;
			if (app == null)
			{
				return;
			}

			void UpdateResource()
			{
				if (app.Resources[UpdateBadgeBrushKey] is SolidColorBrush existing)
				{
					if (!existing.IsFrozen)
					{
						existing.Color = color;
						return;
					}

					app.Resources[UpdateBadgeBrushKey] = new SolidColorBrush(color);
					return;
				}

				app.Resources[UpdateBadgeBrushKey] = new SolidColorBrush(color);
			}

			if (app.Dispatcher.CheckAccess())
			{
				UpdateResource();
			}
			else
			{
				app.Dispatcher.Invoke(UpdateResource);
			}
		}
	}
}
