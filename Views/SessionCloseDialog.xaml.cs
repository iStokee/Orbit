using System.Windows;
using Orbit.ViewModels;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Orbit.Views;

/// <summary>
/// Interaction logic for SessionCloseDialog.xaml
/// </summary>
public partial class SessionCloseDialog : FluentWindow
{
	public SessionCloseDialog(SessionCloseDialogViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
		Close();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
