using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dragablz;
using Dragablz.Dockablz;
using Orbit.Models;
using Orbit.Services;
using Application = System.Windows.Application;
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
			CreateGrid2x2Command = new RelayCommand(() => CreateGrid(2, 2), CanCreateGrid);
			CreateGrid3x3Command = new RelayCommand(() => CreateGrid(3, 3), CanCreateGrid);
			ResetLayoutCommand = new RelayCommand(() => _ = ResetLayout(), CanResetLayout);

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
		public IRelayCommand CreateGrid2x2Command { get; }

		/// <summary>
		/// Command to create a 3x3 grid layout
		/// </summary>
		public IRelayCommand CreateGrid3x3Command { get; }

		/// <summary>
		/// Command to reset layout to single view
		/// </summary>
		public IRelayCommand ResetLayoutCommand { get; }

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
				var controlCount = GetAllTabControls(_sessionLayout).Take(2).Count();
				var rootTabControl = controlCount > 1
					? ResetLayout()
					: GetFirstTabControl(_sessionLayout) ?? ResetLayout();

				if (rootTabControl == null)
					return;

				if (rootTabControl.Items.Count > 0 && rootTabControl.SelectedItem == null)
				{
					rootTabControl.SelectedIndex = 0;
				}

				var allSessions = rootTabControl.Items.Cast<object>().ToList();
				rootTabControl.Items.Clear();

				var knownControls = new System.Collections.Generic.HashSet<TabablzControl> { rootTabControl };
				var rowControls = new System.Collections.Generic.List<TabablzControl> { rootTabControl };

				TabablzControl? CaptureNewControl()
				{
					foreach (var control in GetAllTabControls(_sessionLayout))
					{
						if (knownControls.Add(control))
						{
							return control;
						}
					}

					return null;
				}

				for (int row = 1; row < rows; row++)
				{
					var sourceControl = rowControls[rowControls.Count - 1];
					Layout.Branch(
						sourceControl,
						Orientation.Vertical,
						false,
						0.5);

					var newControl = CaptureNewControl();
					if (newControl != null)
					{
						rowControls.Add(newControl);
					}
				}

				var allCellControls = new System.Collections.Generic.List<TabablzControl>();

				foreach (var rowControl in rowControls.ToList())
				{
					var cellsInRow = new System.Collections.Generic.List<TabablzControl> { rowControl };

					for (int col = 1; col < cols; col++)
					{
						var sourceControl = cellsInRow[cellsInRow.Count - 1];
						Layout.Branch(
							sourceControl,
							Orientation.Horizontal,
							false,
							0.5);

						var newControl = CaptureNewControl();
						if (newControl != null)
						{
							cellsInRow.Add(newControl);
						}
					}

					allCellControls.AddRange(cellsInRow);
				}

				if (allSessions.Count > 0 && allCellControls.Count > 0)
				{
					int cellIndex = 0;
					foreach (var session in allSessions)
					{
						var targetControl = allCellControls[cellIndex % allCellControls.Count];
						targetControl.Items.Add(session);
						if (targetControl.SelectedItem == null)
						{
							targetControl.SelectedItem = session;
						}

						cellIndex++;
					}
				}

				foreach (var control in allCellControls)
				{
					if (control.Items.Count > 0 && control.SelectedItem == null)
					{
						control.SelectedIndex = 0;
					}
				}

				foreach (var session in allSessions.OfType<SessionModel>())
				{
					var host = session.HostControl;
					if (host == null)
						continue;

					var dispatcher = host.Dispatcher ?? Application.Current?.Dispatcher;
					dispatcher?.InvokeAsync(() =>
					{
						host.EnsureActiveAfterLayout();
					}, DispatcherPriority.Background);
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
		private TabablzControl? ResetLayout()
		{
			if (_sessionLayout == null)
				return null;

			try
			{
				var allControls = GetAllTabControls(_sessionLayout).ToList();
				if (allControls.Count <= 1)
				{
					var existing = allControls.FirstOrDefault();
					if (existing != null && existing.Items.Count > 0 && existing.SelectedItem == null)
					{
						existing.SelectedIndex = 0;
					}

					return existing;
				}

				var allItems = new System.Collections.Generic.List<object>();
				foreach (var control in allControls)
				{
					if (control.Items == null)
						continue;

					foreach (var item in control.Items.Cast<object>().ToList())
					{
						if (!allItems.Contains(item))
						{
							allItems.Add(item);
						}
					}
				}

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

				newRootControl.ContentTemplate = (System.Windows.DataTemplate)System.Windows.Application.Current.Resources["SessionContentTemplate"];
				newRootControl.ItemTemplate = (System.Windows.DataTemplate)System.Windows.Application.Current.Resources["OrbitSessionHeaderTemplate"];

				newRootControl.InterTabController = new Dragablz.InterTabController
				{
					InterTabClient = InterTabClient,
					Partition = "OrbitMainShell"
				};

				foreach (var item in allItems)
				{
					newRootControl.Items.Add(item);
				}

				if (newRootControl.Items.Count > 0)
				{
					newRootControl.SelectedIndex = 0;
				}

				_sessionLayout.Content = newRootControl;

				System.Diagnostics.Debug.WriteLine($"[OrbitView] Layout reset complete. Restored {allItems.Count} sessions to single view.");
				return newRootControl;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Failed to reset layout: {ex.Message}");
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Stack trace: {ex.StackTrace}");
				return null;
			}
		}

		private bool CanResetLayout()
		{
			if (_sessionLayout == null)
				return false;

			// Can reset if we have more than one TabablzControl (i.e., branches exist)
			var controlCount = GetAllTabControls(_sessionLayout).Take(2).Count();
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

			var item = args.DragablzItem?.DataContext;
			if (item == null)
				return;

			// Handle session close
			if (item is SessionModel session)
			{
				args.Cancel();
				_closeSession(session);
				return;
			}

			// Handle tool/other tab close - remove from Items collection
			// This prevents empty tab shells from being left behind
			if (Items.Contains(item))
			{
				Application.Current.Dispatcher.BeginInvoke(() =>
				{
					Items.Remove(item);
				}, DispatcherPriority.Background);
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
