using Dragablz;
using MahApps.Metro.Controls;
using ControlzEx.Theming;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.Integration;
using Orbit.Views;
using MessageBox = System.Windows.Forms.MessageBox;
using Application = System.Windows.Application;
using System.Windows.Interop;
using System.Windows.Controls;
using Orbit.Classes;
using System.Windows.Forms;
using ControlzEx.Theming;

// Alias ThemeManagers to avoid conflicts
using MahAppsThemeManager = ControlzEx.Theming.ThemeManager;
using ControlzExThemeManager = ControlzEx.Theming.ThemeManager;
using MahApps.Metro.Theming;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Orbit
{
	public partial class MainWindow : MetroWindow
	{
		#region Dll Imports
		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);
		#endregion

		private const int HWND_TOP = 0;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_SHOWWINDOW = 0x0040;
		private const uint SWP_NOZORDER = 0x0004;
		private const int WS_CHILD = 0x40000000;
		private const int GWL_STYLE = -16;
		private const int WS_CAPTION = 0x00C00000;

		// Collection to track sessions
		public IInterTabClient InterTabClient { get; } = new CustomInterTabClient();

		public ObservableCollection<Session> Sessions { get; set; }

		public MainWindow()
		{
			InitializeComponent();
			Sessions = new ObservableCollection<Session>();
			DataContext = this;  // Set DataContext to make Sessions available for data binding


			PopulateThemesMenu(); // Populate the themes menu
			LoadSavedTheme();
		}

		private void PopulateThemesMenu()
		{
			var themesMenuItem = ThemesMenu;

			// Clear existing items if any
			themesMenuItem.Items.Clear();

			// Define theme types and accents
			var themeTypes = new[] { "Light", "Dark" };
			var accentColors = new[] { "Blue", "Red", "Green", "Purple", "Orange" };

			foreach (var theme in themeTypes)
			{
				var themeMenuItem = new MenuItem { Header = theme };
				foreach (var accent in accentColors)
				{
					var accentMenuItem = new MenuItem { Header = accent };
					accentMenuItem.Click += (s, e) =>
					{
						ApplyTheme(theme, accent);
						SaveTheme(theme, accent);
					};
					themeMenuItem.Items.Add(accentMenuItem);
				}
				themesMenuItem.Items.Add(themeMenuItem);
			}
		}

		private void ApplyTheme(string theme, string accent)
		{
			// Construct the full theme name as "BaseLight.Blue" or "BaseDark.Red"
			string themeName = $"{theme}.{accent}";

			// Retrieve the theme to ensure it exists
			var selectedTheme = ThemeManager.Current.GetTheme(themeName);

			if (selectedTheme != null)
			{
				// Apply the theme
				ThemeManager.Current.ChangeTheme(Application.Current, themeName);
			}
			else
			{
				// Handle the missing theme scenario
				MessageBox.Show($"The theme '{themeName}' was not found. Please select a valid theme.", "Theme Not Found", (MessageBoxButtons)MessageBoxButton.OK, (MessageBoxIcon)MessageBoxImage.Warning);
			}
		}




		private void SaveTheme(string theme, string accent)
		{
			// Save to user settings
			Settings.Default.Theme = theme;
			Settings.Default.Accent = accent;
			Settings.Default.Save();
		}

		private void LoadSavedTheme()
		{
			var savedTheme = Settings.Default.Theme;
			var savedAccent = Settings.Default.Accent;

			if (!string.IsNullOrEmpty(savedTheme) && !string.IsNullOrEmpty(savedAccent))
			{
				ApplyTheme(savedTheme, savedAccent);
			}
			else
			{
				// Apply a default theme if none is saved
				ApplyTheme("Light", "Blue");
			}
		}

		private async void AddSession_Click(object sender, RoutedEventArgs e)
		{
			// Create a new session
			var session = new Session
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now
			};

			// Create the WindowsFormsHost
			var windowsFormsHost = new WindowsFormsHost();
			session.HostControl = windowsFormsHost;

			// Create the RSForm
			session.RSForm = new RSForm();
			session.RSForm.TopLevel = false;

			// Add the RSForm to the WindowsFormsHost
			windowsFormsHost.Child = session.RSForm;

			await session.RSForm.BeginLoad();

			// Add the session to the collection
			Sessions.Add(session);

			// Start the new session logic
			await StartNewSession(session);

			// set the tab control to the new session
			SessionTabControl.SelectedItem = session;
		}

		private async Task StartNewSession(Session session)
		{
			if (session.RSForm == null)
			{
				Console.WriteLine("LoadNewSession");
				await session.RSForm.BeginLoad();
				await Task.Delay(5000);
				Console.WriteLine("Client has finished loading");
			}
			await Task.Delay(500);

			session.ClientLoaded = true;
			session.ClientStatus = "Loaded";
		}

		private void EnsureWindowTopMost(Session session)
		{
			SetWindowPos(session.ExternalHandle, (IntPtr)HWND_TOP, 0, 0, 0, 0,
				SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
		}

		private void SessionTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (SessionTabControl.SelectedItem is Session session && session.HostControl != null)
			{
				EnsureWindowTopMost(session);
			}
		}

		private void ShowSessions_Click(object sender, RoutedEventArgs e)
		{
			// Open the Sessions Window
			SessionsWindow sessionsWindow = new SessionsWindow(Sessions);
			sessionsWindow.Show();
		}
	}

	public class CustomInterTabClient : IInterTabClient
	{
		// This method returns the control container where new tabs will be placed in the new floating window.
		public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
		{
			// Create a new floating window
			var newWindow = new FloatingWindow();

			// Bind the new window's SessionTabControl to the same Sessions collection
			var newTabablzControl = newWindow.SessionTabControl;
			newTabablzControl.ItemsSource = ((MainWindow)Application.Current.MainWindow).Sessions;

			return new NewTabHost<Window>(newWindow, newTabablzControl);
		}

		// Defines what happens when a tab is emptied
		public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
		{
			return TabEmptiedResponse.CloseWindowOrLayoutBranch;
		}
	}

	// Your custom ThemeProvider class
	public class MyLibraryThemeProvider : MahAppsLibraryThemeProvider
	{
		/// <inheritdoc/>
		public static new readonly MyLibraryThemeProvider DefaultInstance = new MyLibraryThemeProvider();

		public override void FillColorSchemeValues(Dictionary<string, string> values, RuntimeThemeColorValues colorValues)
		{
			// Check if all needed parameters are not null
			if (values is null) throw new ArgumentNullException(nameof(values));
			if (colorValues is null) throw new ArgumentNullException(nameof(colorValues));

			bool isDarkMode = colorValues.Options.BaseColorScheme.Name == ThemeManager.BaseColorDark;
			Color baseColor = (Color)ColorConverter.ConvertFromString(colorValues.Options.BaseColorScheme.Values["MahApps.Colors.ThemeBackground"]);
			Color accent = colorValues.AccentBaseColor;
			double factor = isDarkMode ? 0.1 : 0.2;

			// Add the values you like to override
			values.Add("MahApps.Colors.AccentBase", accent.ToString(CultureInfo.InvariantCulture));
			values.Add("MahApps.Colors.Accent", AddColor(accent, baseColor, factor * 1).ToString(CultureInfo.InvariantCulture));
			values.Add("MahApps.Colors.Accent2", AddColor(accent, baseColor, factor * 2).ToString(CultureInfo.InvariantCulture));
			values.Add("MahApps.Colors.Accent3", AddColor(accent, baseColor, factor * 3).ToString(CultureInfo.InvariantCulture));
			values.Add("MahApps.Colors.Accent4", AddColor(accent, baseColor, factor * 4).ToString(CultureInfo.InvariantCulture));

			values.Add("MahApps.Colors.Highlight", AddColor(accent, isDarkMode ? Colors.White : Colors.Black, 0.8).ToString(CultureInfo.InvariantCulture));
			values.Add("MahApps.Colors.IdealForeground", colorValues.IdealForegroundColor.ToString(CultureInfo.InvariantCulture));

			// Gray Colors
			for (int i = 1; i <= 10; i++)
			{
				values.Add($"MahApps.Colors.Gray{i}", GetShadedGray(i / 11d, isDarkMode).ToString(CultureInfo.InvariantCulture));
			}
		}

		private static Color GetShadedGray(double percentage, bool inverse = false)
		{
			if (inverse)
			{
				percentage = 1 - percentage;
			}

			return Color.FromRgb((byte)(percentage * 255), (byte)(percentage * 255), (byte)(percentage * 255));
		}

		private static Color AddColor(Color baseColor, Color colorToAdd, double? factor)
		{
			byte firstColorAlpha = baseColor.A;
			byte secondColorAlpha = factor.HasValue ? (byte)(factor * 255) : colorToAdd.A;

			byte alpha = CompositeAlpha(firstColorAlpha, secondColorAlpha);

			byte r = CompositeColorComponent(baseColor.R, firstColorAlpha, colorToAdd.R, secondColorAlpha, alpha);
			byte g = CompositeColorComponent(baseColor.G, firstColorAlpha, colorToAdd.G, secondColorAlpha, alpha);
			byte b = CompositeColorComponent(baseColor.B, firstColorAlpha, colorToAdd.B, secondColorAlpha, alpha);

			return Color.FromArgb(255, r, g, b);
		}

		/// <summary>
		/// For a single R/G/B component. a = precomputed CompositeAlpha(a1, a2)
		/// </summary>
		private static byte CompositeColorComponent(byte c1, byte a1, byte c2, byte a2, byte a)
		{
			// Handle the singular case of both layers fully transparent.
			if (a == 0)
			{
				return 0;
			}

			return System.Convert.ToByte((((255 * c2 * a2) + (c1 * a1 * (255 - a2))) / a) / 255);
		}

		private static byte CompositeAlpha(byte a1, byte a2)
		{
			return System.Convert.ToByte(255 - ((255 - a2) * (255 - a1)) / 255);
		}
	}
}
