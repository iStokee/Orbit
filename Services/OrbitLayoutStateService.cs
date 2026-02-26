using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Orbit.Models;
using Orbit.ViewModels;
using Application = System.Windows.Application;

namespace Orbit.Services
{
	/// <summary>
	/// Shared state for the Orbit layout surface. Maintains the collection fed into the
	/// Dragablz layout so that multiple views (or reopening the tool) reuse the same items.
	/// Items are added explicitly when they are moved into the Orbit workspace.
	///
	/// Important: sessions are NOT automatically synchronized into Orbit View. Otherwise, opening Orbit View
	/// would "steal" HostControls from any other session presentation (e.g. Individual Tabs),
	/// leaving behind orphaned tab headers that are still wired to close the underlying session.
	/// </summary>
	public sealed class OrbitLayoutStateService
	{
		public OrbitLayoutStateService(SessionCollectionService sessionCollectionService)
		{
			_ = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
		}

		/// <summary>
		/// Underlying collection bound to Dragablz layout.
		/// </summary>
		public ObservableCollection<object> Items { get; } = new();

		/// <summary>
		/// Adds an item to the shared layout state if it is not already present.
		/// Used for tool tabs or manually re-homing sessions.
		/// </summary>
		public void AddItem(object item)
		{
			if (item == null)
			{
				return;
			}

			AddItemInternal(item);
		}

		/// <summary>
		/// Removes an item from the shared layout state.
		/// </summary>
		public void RemoveItem(object item)
		{
			if (item == null)
			{
				return;
			}

			ExecuteOnUi(() => Items.Remove(item));
		}

	private void AddItemInternal(object item)
	{
		ExecuteOnUi(() =>
		{
			RemoveFromWindowTabs(item);

			if (!Items.Contains(item))
			{
				Items.Add(item);
			}
		});
	}

	private static void RemoveFromWindowTabs(object item)
	{
		if (item == null)
		{
			return;
		}

		// Orbit View is itself a shell tool; never purge it from tab strips automatically.
		if (item is ToolTabItem orbitTool && string.Equals(orbitTool.Key, "OrbitView", StringComparison.Ordinal))
		{
			return;
		}

		var windows = Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel vm)
			{
				continue;
			}

			if (vm.Tabs.Contains(item))
			{
				vm.Tabs.Remove(item);
			}
		}
	}

		private static void ExecuteOnUi(Action action)
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher == null || dispatcher.CheckAccess())
			{
				action();
			}
			else
			{
				dispatcher.Invoke(action);
			}
		}
	}
}
