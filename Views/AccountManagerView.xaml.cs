using Orbit.Services;
using Orbit.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Orbit.Views
{
	public partial class AccountManagerView
	{
		private readonly AccountManagerViewModel _viewModel;

		public AccountManagerView(AccountService accountService)
		{
			InitializeComponent();
			_viewModel = new AccountManagerViewModel(accountService);
			DataContext = _viewModel;
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
