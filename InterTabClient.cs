using System.Windows;
using Dragablz;
using Orbit.ViewModels;

namespace Orbit
{
	public class InterTabClient : IInterTabClient
	{
		public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
		{
			var view = new MainWindow();
			view.DataContext = new MainWindowViewModel();
			return new NewTabHost<Window>(view, view.SessionTabControl);
		}

		public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
		{
			return TabEmptiedResponse.CloseWindowOrLayoutBranch;
		}
	}
}
