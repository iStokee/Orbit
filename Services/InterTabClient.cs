using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Dragablz;
using Dragablz.Dockablz;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Services;
using Orbit.Views;
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
		private readonly TearOffHostRegistry tearOffRegistry;

		public InterTabClient(IServiceProvider serviceProvider, TearOffHostRegistry tearOffRegistry)
		{
			this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			this.tearOffRegistry = tearOffRegistry ?? throw new ArgumentNullException(nameof(tearOffRegistry));
		}

		/// <summary>
		/// Creates a new host window when tabs are torn off.
		/// Returns a MainWindow instance, enabling drag-and-drop back to the main shell.
		/// </summary>
		public INewTabHost<Window> GetNewHost(IInterTabClient interTabClient, object partition, TabablzControl source)
		{
			var view = serviceProvider.GetRequiredService<MainWindow>();
			var partitionKey = partition?.ToString() ?? string.Empty;
			var origin = ResolveOrigin(source, partitionKey);
			OrbitInteractionLogger.Log($"[OrbitView][Drag] New tear-off host requested partition='{partitionKey}' origin={origin}.");

			tearOffRegistry.Register(view, view.SessionTabControl, partitionKey, origin);
			return new NewTabHost<Window>(view, view.SessionTabControl);
		}

		private TearOffHostRegistry.HostOrigin ResolveOrigin(TabablzControl? source, string partitionKey)
		{
			if (IsOrbitViewSource(source))
			{
				return TearOffHostRegistry.HostOrigin.OrbitView;
			}

			if (source != null && tearOffRegistry.TryGetOrigin(source, partitionKey, out var sourceOrigin))
			{
				return sourceOrigin;
			}

			var hostWindow = source == null ? null : Window.GetWindow(source);
			if (hostWindow != null && tearOffRegistry.TryGetOrigin(hostWindow, partitionKey, out var windowOrigin))
			{
				return windowOrigin;
			}

			return TearOffHostRegistry.HostOrigin.MainTabs;
		}

		private static bool IsOrbitViewSource(TabablzControl? source)
		{
			// Main shell tabs can also live under a Dockablz Layout; only the dedicated Orbit
			// workspace should be treated as Orbit-origin for tear-off cleanup/rehome behavior.
			return source != null && FindVisualAncestor<OrbitGridLayoutView>(source) != null;
		}

		private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
		{
			var current = child;
			while (current != null)
			{
				if (current is T typed)
				{
					return typed;
				}

				current = VisualTreeHelper.GetParent(current);
			}

			return null;
		}

		/// <summary>
		/// Handles empty TabablzControl scenarios.
		/// - Main window: Keep alive (prevents accidental close)
		/// - Tear-off windows: Close automatically
		/// - Layout branches: Collapse the branch (removes empty split)
		/// </summary>
		public TabEmptiedResponse TabEmptiedHandler(TabablzControl tabControl, Window window)
		{
			// Keep the primary shell window alive, but still collapse empty split branches inside it.
			if (ReferenceEquals(window, Application.Current.MainWindow))
			{
				if (ShouldCollapsePrimaryShellBranch(tabControl))
				{
					OrbitInteractionLogger.Log("[OrbitView][Drag] TabEmptied on primary shell branch; collapsing empty layout branch.");
					return TabEmptiedResponse.CloseWindowOrLayoutBranch;
				}

				OrbitInteractionLogger.Log("[OrbitView][Drag] TabEmptied on last primary shell tab control; keeping window alive.");
				return TabEmptiedResponse.DoNothing;
			}

			// For tear-off windows and layout branches: close/collapse when empty
			// CloseWindowOrLayoutBranch will:
			// - Close the window if it's a tear-off
			// - Collapse the branch if it's within a Layout control
			OrbitInteractionLogger.Log("[OrbitView][Drag] TabEmptied on secondary window/layout; closing host branch.");
			return TabEmptiedResponse.CloseWindowOrLayoutBranch;
		}

		private static bool ShouldCollapsePrimaryShellBranch(TabablzControl tabControl)
		{
			if (tabControl == null)
			{
				return false;
			}

			var layout = FindVisualAncestor<Layout>(tabControl);
			if (layout == null)
			{
				return false;
			}

			var tabControls = FindVisualChildren<TabablzControl>(layout).ToList();
			return tabControls.Count > 1;
		}

		private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
		{
			if (parent == null)
			{
				yield break;
			}

			var count = VisualTreeHelper.GetChildrenCount(parent);
			for (var index = 0; index < count; index++)
			{
				var child = VisualTreeHelper.GetChild(parent, index);
				if (child is T typed)
				{
					yield return typed;
				}

				foreach (var descendant in FindVisualChildren<T>(child))
				{
					yield return descendant;
				}
			}
		}
	}
}
