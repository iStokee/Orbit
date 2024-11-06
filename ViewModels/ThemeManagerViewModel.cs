using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using MahApps.Metro;
using Orbit.Classes;

using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace Orbit.ViewModels
{
	internal class ThemeManagerViewModel : DependencyObject
	{
		public List<AccentColorMenuData> AccentColors { get; set; }

		public List<AppThemeMenuData> AppThemes { get; set; }

		//public static readonly DependencyProperty ColorsProperty =
		//	DependencyProperty.Register(
		//		nameof(Colors),
		//		typeof(List<KeyValuePair<string, Color>>),
		//		typeof(ThemeManagerViewModel),
		//		new PropertyMetadata(default(List<KeyValuePair<string, Color>>)));

		public List<AccentColorMenuData> Colors
		{
			get => (List<AccentColorMenuData>)GetValue(ColorsProperty);
			set => SetValue(ColorsProperty, value);
		}

		public static readonly DependencyProperty ColorsProperty =
			DependencyProperty.Register(
				nameof(Colors),
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


			Colors = typeof(Colors)
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
			// Now assign ChangeThemeCommand here without error
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
