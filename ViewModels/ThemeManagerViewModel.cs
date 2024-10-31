using MahApps.Metro.Controls.Dialogs;
using MahApps.Metro.Controls;
using MahApps.Metro;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Orbit.Classes;
using Newtonsoft.Json.Linq;

namespace Orbit.ViewModels
{

	internal class ThemeManagerViewModel
	{
		public List<AccentColorMenuData> AccentColors { get; set; }

		public List<AppThemeMenuData> AppThemes { get; set; }

		internal ThemeManagerViewModel()
		{
			// Create accent color menu items
			this.AccentColors = ThemeManager.Accents
				.OrderBy(a => a.Name)
				.Select(a => new AccentColorMenuData(
					a.Name,
					a.Resources["AccentColorBrush"] as Brush ?? Brushes.Transparent,
					Brushes.Gray // Set a default border color, or adjust as needed
				))
				.ToList();

			// Create metro theme color menu items
			this.AppThemes = ThemeManager.AppThemes
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

		public Brush? BorderColorBrush { get; set; } = Brushes.Gray; // Default color for border

		public Brush? ColorBrush { get; set; } // Main fill color for the accent

		public AccentColorMenuData(string name, Brush colorBrush, Brush borderColorBrush)
		{
			Name = name;
			ColorBrush = colorBrush;
			BorderColorBrush = borderColorBrush;
			ChangeAccentCommand = new SimpleCommand<string?>(o => true, DoChangeTheme);
		}

		public ICommand ChangeAccentCommand { get; }

		protected virtual void DoChangeTheme(string? name)
		{
			if (name is not null)
			{
				var app = System.Windows.Application.Current;
				var accent = ThemeManager.GetAccent(name);

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

		protected override void DoChangeTheme(string? name)
		{
			if (name is not null)
			{
				var app = System.Windows.Application.Current;
				var appTheme = ThemeManager.AppThemes.FirstOrDefault(x => x.Name == name);

				if (appTheme != null)
				{
					var currentStyle = ThemeManager.DetectAppStyle(app);
					var accent = currentStyle?.Item2; // Existing accent color

					if (accent != null)
					{
						ThemeManager.ChangeAppStyle(app, accent, appTheme);
					}
				}
			}
		}
	}

}
