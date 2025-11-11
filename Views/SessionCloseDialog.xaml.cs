using System.Windows;
using MahApps.Metro.Controls;
using Orbit.ViewModels;

namespace Orbit.Views;

/// <summary>
/// Interaction logic for SessionCloseDialog.xaml
/// </summary>
public partial class SessionCloseDialog : MetroWindow
{
	public bool DialogResult { get; private set; }

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
