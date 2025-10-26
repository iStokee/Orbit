using MahApps.Metro.Controls;
using Orbit.ViewModels;
using System.Windows;

namespace Orbit.Views;

public partial class ScriptManagerView : MetroWindow
{
	public ScriptManagerViewModel ViewModel { get; }

	public ScriptManagerView()
	{
		InitializeComponent();
		ViewModel = new ScriptManagerViewModel(this);
		DataContext = ViewModel;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
