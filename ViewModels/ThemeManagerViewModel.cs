using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using MahApps.Metro;
using Orbit.Classes;
using Newtonsoft.Json; // If using Newtonsoft.Json
// using System.Text.Json; // If using System.Text.Json

using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Orbit.ViewModels
{
    internal class ThemeManagerViewModel : DependencyObject
    {

        public List<AccentColorMenuData> AccentColors { get; set; }

        public List<AppThemeMenuData> AppThemes { get; set; }

        // Renamed from 'Colors' to 'AvailableColors'
        public List<AccentColorMenuData> AvailableColors
        {
            get => (List<AccentColorMenuData>)GetValue(AvailableColorsProperty);
            set => SetValue(AvailableColorsProperty, value);
        }

        public static readonly DependencyProperty AvailableColorsProperty =
            DependencyProperty.Register(
                nameof(AvailableColors),
                typeof(List<AccentColorMenuData>),
                typeof(ThemeManagerViewModel),
                new PropertyMetadata(default(List<AccentColorMenuData>)));

        internal ThemeManagerViewModel()
        {

            AccentColors = ThemeManager.Accents
                .OrderBy(a => a.Name)
                .Select(a => new AccentColorMenuData(
                    a.Name,
                    new SolidColorBrush(((SolidColorBrush)(a.Resources["AccentColorBrush"] ?? Brushes.Transparent)).Color),
                    Brushes.Gray
                ))
                .ToList();

            AvailableColors = typeof(Colors)
                .GetProperties()
                .Where(prop => typeof(Color).IsAssignableFrom(prop.PropertyType))
                .Select(prop => new AccentColorMenuData(
                    prop.Name,
                    new SolidColorBrush((Color)prop.GetValue(null)),
                    Brushes.Gray
                ))
                .ToList();

            AppThemes = ThemeManager.AppThemes
                .OrderBy(a => a.Name)
                .Select(a => new AppThemeMenuData(
                    a.Name,
                    a.Resources["WindowBackgroundBrush"] as Brush ?? Brushes.White,
                    a.Resources["HighlightColorBrush"] as Brush ?? Brushes.Gray
                ))
                .ToList();
        }
    }

    public class AccentColorMenuData : INotifyPropertyChanged
    {
        private string? _name;
        private Brush? _colorBrush;
        private Brush? _borderColorBrush;

        public string? Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public Brush? ColorBrush
        {
            get => _colorBrush;
            set
            {
                _colorBrush = value;
                OnPropertyChanged();
            }
        }

        public Brush? BorderColorBrush
        {
            get => _borderColorBrush;
            set
            {
                _borderColorBrush = value;
                OnPropertyChanged();
            }
        }

        public ICommand ChangeAccentCommand { get; }

        // Make ChangeThemeCommand settable in derived classes
        public ICommand ChangeThemeCommand { get; protected set; }

        public AccentColorMenuData(string name, Brush colorBrush, Brush borderColorBrush)
        {
            Name = name;
            ColorBrush = colorBrush;
            BorderColorBrush = borderColorBrush;
            ChangeAccentCommand = new SimpleCommand<string?>(o => true, DoChangeTheme);
            ChangeThemeCommand = new SimpleCommand<string?>(o => true, DoChangeTheme); // Initialize to prevent null reference
        }

        protected virtual void DoChangeTheme(string? name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var app = Application.Current;
                var accent = ThemeManager.GetAccent(name) ?? new Accent(name, null);

                if (accent != null)
                {
                    var currentTheme = ThemeManager.DetectAppStyle(app);
                    if (currentTheme != null)
                    {
                        ThemeManager.ChangeAppStyle(app, accent, currentTheme.Item1);
                        Settings.Default.Theme = currentTheme.Item1.Name;
                        Settings.Default.Accent = accent.Name;
                        Settings.Default.Save();
                    }
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AppThemeMenuData : AccentColorMenuData
    {
        public AppThemeMenuData(string name, Brush colorBrush, Brush borderColorBrush)
            : base(name, colorBrush, borderColorBrush)
        {
            // Assign ChangeThemeCommand specifically for themes
            ChangeThemeCommand = new SimpleCommand<string?>(o => true, DoChangeTheme);
        }

        protected override void DoChangeTheme(string? name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var app = Application.Current;

                // Retrieve the app theme from ThemeManager
                var theme = ThemeManager.AppThemes.FirstOrDefault(t => t.Name == name);

                if (theme != null)
                {
                    var currentAccent = ThemeManager.DetectAppStyle(app)?.Item2; // Get current accent

                    if (currentAccent != null)
                    {
                        // Apply the selected theme while retaining the existing accent
                        ThemeManager.ChangeAppStyle(app, currentAccent, theme);
                        Settings.Default.Theme = name;
                        Settings.Default.Accent = currentAccent.Name;
                        Settings.Default.Save();
                    }
                    else
                    {
                        // Handle case where currentAccent is null
                        Console.WriteLine("Current accent not detected.");
                    }
                }
                else
                {
                    // Handle case where theme is null
                    Console.WriteLine("Theme not found.");
                }
            }
        }
    }
}
