using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using Orbit.Models;
using Application = System.Windows.Application;

namespace Orbit.Services
{
	/// <summary>
	/// Shared state for the Orbit layout surface. Maintains the collection fed into the
	/// Dragablz layout so that multiple views (or reopening the tool) reuse the same items.
	/// Sessions are synchronized with <see cref="SessionCollectionService"/>; tool tabs are added
	/// explicitly when they are moved into the Orbit workspace.
	/// </summary>
	public sealed class OrbitLayoutStateService
	{
		private readonly SessionCollectionService sessionCollectionService;

		public OrbitLayoutStateService(SessionCollectionService sessionCollectionService)
		{
			this.sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));

			foreach (var session in this.sessionCollectionService.Sessions)
			{
				AddItemInternal(session);
			}

			this.sessionCollectionService.Sessions.CollectionChanged += OnSessionsCollectionChanged;
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

		private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				ExecuteOnUi(() =>
				{
					for (var i = Items.Count - 1; i >= 0; i--)
					{
						if (Items[i] is SessionModel)
						{
							Items.RemoveAt(i);
						}
					}
				});

				foreach (var session in sessionCollectionService.Sessions)
				{
					AddItemInternal(session);
				}

				return;
			}

			if (e.NewItems != null && (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace))
			{
				foreach (var session in e.NewItems.OfType<SessionModel>())
				{
					AddItemInternal(session);
				}
			}

			if (e.OldItems != null && (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Replace))
			{
				foreach (var session in e.OldItems.OfType<SessionModel>())
				{
					RemoveItem(session);
				}
			}
		}

		private void AddItemInternal(object item)
		{
			ExecuteOnUi(() =>
			{
				if (!Items.Contains(item))
				{
					Items.Add(item);
				}
			});
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
