using Orbit.Classes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;

namespace Orbit
{
	public partial class FloatingWindow : Window
	{
		public FloatingWindow()
		{
			InitializeComponent();
			SessionTabControl.ItemsSource = ((MainWindow)Application.Current.MainWindow).Sessions;
		}

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		private void SessionTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (SessionTabControl.SelectedItem is Session session)
			{
				session.SetFocus();
			}
		}

	}
}
