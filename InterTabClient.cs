using System;
using System.Windows;
using Dragablz;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace Orbit
{
	public class InterTabClient : IInterTabClient
	{
		private readonly IServiceProvider serviceProvider;

		public InterTabClient(IServiceProvider serviceProvider)
		{
			this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		}

		public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
		{
			var view = serviceProvider.GetRequiredService<MainWindow>();
			return new NewTabHost<Window>(view, view.SessionTabControl);
		}

		public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
		{
			// Keep the primary shell alive even when all tabs close; allow tear-off shells to close normally.
			if (ReferenceEquals(window, Application.Current.MainWindow))
			{
				return TabEmptiedResponse.DoNothing;
			}

			return TabEmptiedResponse.CloseWindowOrLayoutBranch;
		}
	}
}
