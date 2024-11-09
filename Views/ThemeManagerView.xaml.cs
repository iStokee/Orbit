using Orbit.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Orbit.Views
{
	/// <summary>
	/// Interaction logic for ThemeManagerView.xaml
	/// </summary>
	public partial class ThemeManagerView : MahApps.Metro.Controls.MetroWindow
	{
		public ThemeManagerView()
		{
			InitializeComponent();
			this.DataContext = new ThemeManagerViewModel();
		}

		private void ListBox_AccentSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.AddedItems.Count > 0 && e.AddedItems[0] is AccentColorMenuData accent)
			{
				// Execute the command to change the accent color
				accent.ChangeAccentCommand.Execute(accent.Name);
			}
		}

		private void ListBox_ThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.AddedItems.Count > 0 && e.AddedItems[0] is AppThemeMenuData theme && theme != null)
			{
				// Check if theme name is valid and the command is available
				if (theme.ChangeThemeCommand.CanExecute(theme.Name))
				{
					theme.ChangeThemeCommand.Execute(theme.Name);
				}
			}
		}

		private void ListBox_CustomAccentSelectionChanged(object sender, SelectionChangedEventArgs e)
		{

		}
		private void ListBox_CustomThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
		{

		}
	}
}
