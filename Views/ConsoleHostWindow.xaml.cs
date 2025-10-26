using Dragablz;
using MahApps.Metro.Controls;

namespace Orbit.Views;

public partial class ConsoleHostWindow : MetroWindow
{
	public ConsoleHostWindow()
	{
		InitializeComponent();
	}

	public TabablzControl ConsoleTabControl => ConsoleTabHost;
}
