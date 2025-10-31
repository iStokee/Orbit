using Orbit.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views
{
	public partial class AccountManagerView : UserControl
	{
		public AccountManagerView(AccountManagerViewModel viewModel)
		{
			InitializeComponent();
			DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		}

		private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
		{
			if (sender is PasswordBox passwordBox && DataContext is AccountManagerViewModel vm)
			{
				vm.NewPassword = passwordBox.Password;
			}
		}
	}
}
