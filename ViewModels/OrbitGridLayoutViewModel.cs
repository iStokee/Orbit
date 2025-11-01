using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Dragablz;
using Dragablz.Dockablz;
using Orbit.Models;
using Orbit.Services;
using Orientation = System.Windows.Controls.Orientation;

namespace Orbit.ViewModels
{
	/// <summary>
	/// ViewModel for Orbit View - Unified live session grid layout using Dragablz Layout.Branch()
	/// Replaces the legacy GridLayoutBuilder approach.
	/// </summary>
	public class OrbitGridLayoutViewModel : INotifyPropertyChanged
	{
	private readonly SessionCollectionService _sessionCollectionService;
	private readonly OrbitLayoutStateService _layoutStateService;
	private readonly IInterTabClient _interTabClient;
	private readonly Action<SessionModel> _closeSession;
	private Layout? _sessionLayout;

	public OrbitGridLayoutViewModel(
		SessionCollectionService sessionCollectionService,
		OrbitLayoutStateService layoutStateService,
		IInterTabClient interTabClient,
		Action<SessionModel> closeSession)
	{
		_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
		_layoutStateService = layoutStateService ?? throw new ArgumentNullException(nameof(layoutStateService));
		_interTabClient = interTabClient ?? throw new ArgumentNullException(nameof(interTabClient));
		_closeSession = closeSession ?? throw new ArgumentNullException(nameof(closeSession));

		foreach (var session in _sessionCollectionService.Sessions)
		{
			if (!Items.Contains(session))
			{
				Items.Add(session);
			}
		}

		_sessionCollectionService.Sessions.CollectionChanged += OnSessionsCollectionChanged;
		Items.CollectionChanged += (_, __) => CommandManager.InvalidateRequerySuggested();

			// Commands for creating pre-defined grid layouts
			CreateGrid2x2Command = new RelayCommand(_ => CreateGrid(2, 2), _ => CanCreateGrid());
			CreateGrid3x3Command = new RelayCommand(_ => CreateGrid(3, 3), _ => CanCreateGrid());
			ResetLayoutCommand = new RelayCommand(_ => ResetLayout(), _ => CanResetLayout());

			TabClosingHandler = HandleTabClosing;
		}

		/// <summary>
		/// Gets the shared session collection from SessionCollectionService
		/// This is the SAME collection used by MainWindow - no duplication!
		/// </summary>
		public ObservableCollection<SessionModel> Sessions => _sessionCollectionService.Sessions;

		/// <summary>
		/// Items backing the Dragablz layout. Supports both sessions and tool tabs.
		/// </summary>
	public ObservableCollection<object> Items => _layoutStateService.Items;

		/// <summary>
		/// Gets the InterTabClient for cross-window dragging
		/// </summary>
		public IInterTabClient InterTabClient => _interTabClient;

		/// <summary>
		/// Command to create a 2x2 grid layout
		/// </summary>
		public ICommand CreateGrid2x2Command { get; }

		/// <summary>
		/// Command to create a 3x3 grid layout
		/// </summary>
		public ICommand CreateGrid3x3Command { get; }

		/// <summary>
		/// Command to reset layout to single view
		/// </summary>
		public ICommand ResetLayoutCommand { get; }

		/// <summary>
		/// Dragablz callback invoked when a tab close is requested.
		/// Ensures sessions are shut down via MainWindowViewModel instead of simply removing them from the collection.
		/// </summary>
		public ItemActionCallback TabClosingHandler { get; }

		/// <summary>
		/// Sets the Layout control reference from the view (set via code-behind after Loaded)
		/// </summary>
		public void SetLayoutControl(Layout layout)
		{
			_sessionLayout = layout;
		}

		/// <summary>
		/// Creates an NxN grid by programmatically branching the layout using Dragablz's native Layout.Branch() API
		/// FIXED: No longer re-queries controls by index, which was causing infinite nesting
		/// </summary>
		private void CreateGrid(int rows, int cols)
		{
			if (_sessionLayout == null || rows < 1 || cols < 1)
				return;

			try
			{
				// First, reset to single view if we're already branched
				if (GetAllTabControls(_sessionLayout).Count() > 1)
				{
					ResetLayout();
				}

				// Get the root TabablzControl - this is our starting point
				var rootTabControl = GetFirstTabControl(_sessionLayout);
				if (rootTabControl == null)
					return;

				// Collect all sessions from root control to redistribute later
				var allSessions = new System.Collections.Generic.List<object>();
				foreach (var item in rootTabControl.Items.Cast<object>().ToList())
				{
					allSessions.Add(item);
				}
				rootTabControl.Items.Clear();

				// Step 1: Create vertical splits to make rows (horizontal lines)
				// We use a simpler approach: create all row splits from the root control
				var rowControls = new System.Collections.Generic.List<TabablzControl> { rootTabControl };

				for (int row = 1; row < rows; row++)
				{
					// Branch from the LAST created row control
					var sourceControl = rowControls[rowControls.Count - 1];
					Layout.Branch(
						sourceControl,
						Orientation.Vertical,      // Creates horizontal split line
						false,                      // false = add below, true = add above
						0.5);                       // 50/50 split - Dragablz will adjust proportions

					// The newly created control is always added after branching
					// Find it by getting all controls and taking the one we don't have yet
					var allControls = GetAllTabControls(_sessionLayout).ToList();
					var newControl = allControls.FirstOrDefault(c => !rowControls.Contains(c));
					if (newControl != null)
					{
						rowControls.Add(newControl);
					}
				}

				// Step 2: For each row, create horizontal splits to make columns (vertical lines)
				var allCellControls = new System.Collections.Generic.List<TabablzControl>();

				foreach (var rowControl in rowControls.ToList()) // ToList() to avoid modification during iteration
				{
					var cellsInRow = new System.Collections.Generic.List<TabablzControl> { rowControl };

					for (int col = 1; col < cols; col++)
					{
						var sourceControl = cellsInRow[cellsInRow.Count - 1];
						Layout.Branch(
							sourceControl,
							Orientation.Horizontal,    // Creates vertical split line
							false,                      // false = add to right, true = add to left
							0.5);                       // 50/50 split

						// Find the newly created control
						var allControls = GetAllTabControls(_sessionLayout).ToList();
						var newControl = allControls.FirstOrDefault(c => !allCellControls.Contains(c) && !cellsInRow.Contains(c));
						if (newControl != null)
						{
							cellsInRow.Add(newControl);
						}
					}

					allCellControls.AddRange(cellsInRow);
				}

				// Step 3: Distribute sessions evenly across all cells
				if (allSessions.Count > 0 && allCellControls.Count > 0)
				{
					int cellIndex = 0;
					foreach (var session in allSessions)
					{
						// Round-robin distribution
						allCellControls[cellIndex % allCellControls.Count].Items.Add(session);
						cellIndex++;
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Failed to create {rows}x{cols} grid: {ex.Message}");
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Stack trace: {ex.StackTrace}");
				// TODO: Show user-friendly error message via ViewModel property
			}
		}

		/// <summary>
		/// Resets the layout to a single TabablzControl by moving all items to the first control
		/// FIXED: Now properly collapses branch structure instead of leaving empty branches
		/// </summary>
		private void ResetLayout()
		{
			if (_sessionLayout == null)
				return;

			try
			{
				// Get all TabablzControls in the layout
				var allControls = GetAllTabControls(_sessionLayout).ToList();
				if (allControls.Count <= 1)
					return; // Already in single view

				// Collect all items from all controls (sessions)
				var allItems = new System.Collections.Generic.List<object>();
				foreach (var control in allControls)
				{
					if (control.Items != null)
					{
						foreach (var item in control.Items.Cast<object>().ToList())
						{
							if (!allItems.Contains(item))
							{
								allItems.Add(item);
							}
						}
					}
				}

				// CRITICAL FIX: Replace the entire Layout.Content with a fresh TabablzControl
				// This is the proper way to collapse all branches - recreate the root control
				var newRootControl = new TabablzControl
				{
					Margin = new System.Windows.Thickness(4),
					Background = System.Windows.Media.Brushes.Transparent,
					BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MahApps.Brushes.Accent"],
					BorderThickness = new System.Windows.Thickness(2),
					ShowDefaultAddButton = false,
					ShowDefaultCloseButton = true,
					ClosingItemCallback = TabClosingHandler
				};

				// Set templates from resources
				newRootControl.ContentTemplate = (System.Windows.DataTemplate)System.Windows.Application.Current.Resources["SessionContentTemplate"];
				newRootControl.ItemTemplate = (System.Windows.DataTemplate)System.Windows.Application.Current.Resources["OrbitSessionHeaderTemplate"];

				// Configure InterTabController
				newRootControl.InterTabController = new Dragablz.InterTabController
				{
					InterTabClient = InterTabClient,
					Partition = "OrbitMainShell"
				};

				// Add all collected items to the new control
				foreach (var item in allItems)
				{
					newRootControl.Items.Add(item);
				}

				// Replace the Layout's content - this collapses all branches
				_sessionLayout.Content = newRootControl;

				System.Diagnostics.Debug.WriteLine($"[OrbitView] Layout reset complete. Restored {allItems.Count} sessions to single view.");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Failed to reset layout: {ex.Message}");
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Stack trace: {ex.StackTrace}");
			}
		}

		private bool CanResetLayout()
		{
			if (_sessionLayout == null)
				return false;

			// Can reset if we have more than one TabablzControl (i.e., branches exist)
			var controlCount = GetAllTabControls(_sessionLayout).Count();
			return controlCount > 1;
		}

		/// <summary>
		/// Gets the first TabablzControl in the layout
		/// </summary>
		private TabablzControl? GetFirstTabControl(Layout layout)
		{
			// Search the visual tree for the first TabablzControl
			return FindVisualChild<TabablzControl>(layout);
		}

		/// <summary>
		/// Gets all TabablzControls in the layout
		/// </summary>
		private System.Collections.Generic.IEnumerable<TabablzControl> GetAllTabControls(Layout layout)
		{
			// Search the visual tree for all TabablzControls
			return FindVisualChildren<TabablzControl>(layout);
		}

		/// <summary>
		/// Finds the first visual child of type T in the visual tree
		/// </summary>
		private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
		{
			if (parent == null)
				return null;

			for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
			{
				var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
				if (child is T typedChild)
					return typedChild;

				var result = FindVisualChild<T>(child);
				if (result != null)
					return result;
			}

			return null;
		}

		/// <summary>
		/// Finds all visual children of type T in the visual tree
		/// </summary>
		private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
		{
			if (parent == null)
				yield break;

			for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
			{
				var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
				if (child is T typedChild)
					yield return typedChild;

				foreach (var descendant in FindVisualChildren<T>(child))
				{
					yield return descendant;
				}
			}
		}

	private bool CanCreateGrid() => _sessionLayout != null && Items.Count > 0;

		private void HandleTabClosing(ItemActionCallbackArgs<TabablzControl> args)
		{
			if (args == null)
				return;

			if (args.DragablzItem?.DataContext is SessionModel session)
			{
				args.Cancel();
				_closeSession(session);
			}
		}

		private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
		if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
		{
			if (e.NewItems != null)
			{
				foreach (var item in e.NewItems.OfType<SessionModel>())
				{
					if (!Items.Contains(item))
					{
						Items.Add(item);
					}
				}
			}
		}

		if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset)
		{
			if (e.OldItems != null)
			{
				foreach (var item in e.OldItems.OfType<SessionModel>())
				{
					Items.Remove(item);
				}
			}
			else if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				for (int i = Items.Count - 1; i >= 0; i--)
				{
					if (Items[i] is SessionModel session && !_sessionCollectionService.Sessions.Contains(session))
					{
						Items.RemoveAt(i);
					}
				}
			}
		}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
