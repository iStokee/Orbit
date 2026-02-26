using Orbit.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views
{
	public partial class AccountManagerView : UserControl
	{
	private readonly AccountManagerViewModel _viewModel;
	private bool _passwordResetSubscribed;

	public AccountManagerView(AccountManagerViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		DataContext = _viewModel;
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (_passwordResetSubscribed)
		{
			return;
		}

		_viewModel.PasswordReset += OnPasswordReset;
		_passwordResetSubscribed = true;
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		if (!_passwordResetSubscribed)
		{
			return;
		}

		_viewModel.PasswordReset -= OnPasswordReset;
		_passwordResetSubscribed = false;
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
