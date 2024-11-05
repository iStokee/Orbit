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
					a.Resources["AccentColorBrush"] as Brush ?? Brushes.Transparent,
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

	public class AccentColorMenuData
	{
		public string? Name { get; set; }
		public Brush? BorderColorBrush { get; set; }
		public Brush? ColorBrush { get; set; }
		public ICommand ChangeAccentCommand { get; }

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
	}


	public class AppThemeMenuData : AccentColorMenuData
	{
		public AppThemeMenuData(string name, Brush colorBrush, Brush borderColorBrush)
			: base(name, colorBrush, borderColorBrush)
		{
		}

		protected virtual void DoChangeTheme(string? name)
		{
			if (!string.IsNullOrEmpty(name))
			{
				var app = Application.Current;
				var accent = ThemeManager.GetAccent(name); // Retrieve accent from ThemeManager

				if (accent != null)
				{
					var currentTheme = ThemeManager.DetectAppStyle(app); // Get current theme
					if (currentTheme != null)
					{
						// Apply the selected accent while retaining the existing theme
						ThemeManager.ChangeAppStyle(app, accent, currentTheme.Item1);
					}
				}
			}
		}
	}
}
