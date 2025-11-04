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

		viewModel.PasswordReset += OnPasswordReset;
		Unloaded += (_, _) => viewModel.PasswordReset -= OnPasswordReset;
	}

	private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox passwordBox && DataContext is AccountManagerViewModel vm)
		{
			vm.UpdateNewPassword(passwordBox.SecurePassword);
		}
	}

	private void OnPasswordReset(object? sender, EventArgs e)
	{
		NewPasswordBox.PasswordChanged -= NewPasswordBox_PasswordChanged;
		NewPasswordBox.Clear();
		NewPasswordBox.PasswordChanged += NewPasswordBox_PasswordChanged;
	}
	}
}
