using System.Windows;
using Dragablz;
using Orbit.Views;

namespace Orbit;

public sealed class ConsoleInterTabClient : IInterTabClient
{
	public static ConsoleInterTabClient Instance { get; } = new();

	private ConsoleInterTabClient()
	{
	}

	public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
	{
		var window = new ConsoleHostWindow();
		return new NewTabHost<Window>(window, window.ConsoleTabControl);
	}

	public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
		=> TabEmptiedResponse.CloseWindowOrLayoutBranch;
}
