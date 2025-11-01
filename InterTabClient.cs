using System;
using System.Windows;
using Dragablz;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace Orbit
{
	/// <summary>
	/// InterTabClient implementation for unified drag-and-drop across MainWindow and Orbit View.
	/// Supports both TabablzControl (MainWindow) and Layout (Orbit View) scenarios.
	/// </summary>
	public class InterTabClient : IInterTabClient
	{
		private readonly IServiceProvider serviceProvider;

		public InterTabClient(IServiceProvider serviceProvider)
		{
			this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		}

		/// <summary>
		/// Creates a new host window when tabs are torn off.
		/// Returns a MainWindow instance, enabling drag-and-drop back to the main shell.
		/// </summary>
		public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
		{
			var view = serviceProvider.GetRequiredService<MainWindow>();
			return new NewTabHost<Window>(view, view.SessionTabControl);
		}

		/// <summary>
		/// Handles empty TabablzControl scenarios.
		/// - Main window: Keep alive (prevents accidental close)
		/// - Tear-off windows: Close automatically
		/// - Layout branches: Collapse the branch (removes empty split)
		/// </summary>
		public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
		{
			// Keep the primary shell alive even when all tabs close; allow tear-off shells to close normally.
			if (ReferenceEquals(window, Application.Current.MainWindow))
			{
				return TabEmptiedResponse.DoNothing;
			}

			// For tear-off windows and layout branches: close/collapse when empty
			// CloseWindowOrLayoutBranch will:
			// - Close the window if it's a tear-off
			// - Collapse the branch if it's within a Layout control
			return TabEmptiedResponse.CloseWindowOrLayoutBranch;
		}
	}
}
