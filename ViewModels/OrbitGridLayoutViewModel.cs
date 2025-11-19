using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Dragablz;
using Dragablz.Dockablz;
using Orbit.Converters;
using Orbit.Models;
using Orbit.Services;
using Orbit;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

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
		private readonly OrbitViewCompactnessToMarginConverter _marginConverter = new();
		private int _customRows = 2;
		private int _customColumns = 2;
		private readonly OrbitViewCompactnessToCellMarginConverter _cellMarginConverter = new();
		private readonly OrbitViewBorderThicknessConverter _borderThicknessConverter = new();
		private int _currentRows = 1;
		private int _currentColumns = 1;
		private string _suggestedGridLabel = "Suggested: 1 x 1";
		private bool _isHeaderCollapsed;

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
			Items.CollectionChanged += (_, __) =>
			{
				RefreshCommandStates();
				UpdateSuggestedGridLabel();
			};

			TabClosingHandler = HandleTabClosing;
			UpdateSuggestedGridLabel();
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
		/// Suggested grid text (based on current session count).
		/// </summary>
		public string SuggestedGridLabel
		{
			get => _suggestedGridLabel;
			private set
			{
				if (_suggestedGridLabel == value)
					return;
				_suggestedGridLabel = value;
				OnPropertyChanged();
			}
		}

		public bool IsHeaderCollapsed
		{
			get => _isHeaderCollapsed;
			set
			{
				if (_isHeaderCollapsed == value)
					return;
				_isHeaderCollapsed = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Custom grid rows for user-defined layout.
		/// </summary>
		public int CustomRows
		{
			get => _customRows;
			set
			{
				var normalized = value < 1 ? 1 : value;
				if (_customRows == normalized)
					return;
				_customRows = normalized;
				OnPropertyChanged();
				RefreshCommandStates();
			}
		}

		/// <summary>
		/// Custom grid columns for user-defined layout.
		/// </summary>
		public int CustomColumns
		{
			get => _customColumns;
			set
			{
				var normalized = value < 1 ? 1 : value;
				if (_customColumns == normalized)
					return;
				_customColumns = normalized;
				OnPropertyChanged();
				RefreshCommandStates();
			}
		}

		/// <summary>
		/// Gets the InterTabClient for cross-window dragging
		/// </summary>
	public IInterTabClient InterTabClient => _interTabClient;

		private RelayCommand? _createCustomGridCommand;
		private RelayCommand? _createGrid2x2Command;
		private RelayCommand? _createGrid3x3Command;
		private RelayCommand? _resetLayoutCommand;
		private RelayCommand? _autoFitGridCommand;
		private RelayCommand? _balanceRowsCommand;
		private RelayCommand? _balanceColumnsCommand;
		private RelayCommand? _toggleHeaderCommand;

		/// <summary>
		/// Command to create a custom rows x columns grid layout
		/// </summary>
		public IRelayCommand CreateCustomGridCommand => _createCustomGridCommand ??= new RelayCommand(CreateCustomGrid, CanCreateCustomGrid);

		/// <summary>
		/// Command to create a 2x2 grid layout
		/// </summary>
	public IRelayCommand CreateGrid2x2Command => _createGrid2x2Command ??= new RelayCommand(() => CreateGrid(2, 2), CanCreateGrid);

		/// <summary>
		/// Command to create a 3x3 grid layout
		/// </summary>
		public IRelayCommand CreateGrid3x3Command => _createGrid3x3Command ??= new RelayCommand(() => CreateGrid(3, 3), CanCreateGrid);

		/// <summary>
		/// Command to reset layout to single view
		/// </summary>
		public IRelayCommand ResetLayoutCommand => _resetLayoutCommand ??= new RelayCommand(() => _ = ResetLayout(), CanResetLayout);

		/// <summary>
		/// Command to auto-fit the grid to the current session count (closest to square with minimal empty cells).
		/// </summary>
		public IRelayCommand AutoFitGridCommand => _autoFitGridCommand ??= new RelayCommand(AutoFitGrid, CanCreateGrid);

		/// <summary>
		/// Command to rebalance row heights evenly.
		/// </summary>
		public IRelayCommand BalanceRowsCommand => _balanceRowsCommand ??= new RelayCommand(() => ReapplyCurrentGrid(), CanBalanceGrid);

		/// <summary>
		/// Command to rebalance column widths evenly.
		/// </summary>
		public IRelayCommand BalanceColumnsCommand => _balanceColumnsCommand ??= new RelayCommand(() => ReapplyCurrentGrid(), CanBalanceGrid);

		/// <summary>
		/// Command to toggle the header density (compact vs full).
		/// </summary>
		public IRelayCommand ToggleHeaderCommand => _toggleHeaderCommand ??= new RelayCommand(() => IsHeaderCollapsed = !IsHeaderCollapsed);

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
			RefreshCommandStates();
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
				var allItems = Items.ToList();
				var rootTabControl = ResetLayout();

				if (rootTabControl == null)
					return;

				_sessionLayout.UpdateLayout();

				var rowControls = new List<TabablzControl> { rootTabControl };

				for (int row = 1; row < rows; row++)
				{
					var sourceControl = rowControls[rowControls.Count - 1];
					var branchResult = Layout.Branch(
						sourceControl,
						CreateTabControlShell(),
						Orientation.Vertical,
						false,
						0.5);

					var newControl = branchResult?.TabablzControl;
					if (newControl != null)
					{
						ConfigureTabControl(newControl);
						rowControls.Add(newControl);
					}
				}

				var allCellControls = new List<TabablzControl>();

				foreach (var rowControl in rowControls.ToList())
				{
					var cellsInRow = new List<TabablzControl> { rowControl };

					for (int col = 1; col < cols; col++)
					{
						var sourceControl = cellsInRow[cellsInRow.Count - 1];
						var branchResult = Layout.Branch(
							sourceControl,
							CreateTabControlShell(),
							Orientation.Horizontal,
							false,
							0.5);

						var newControl = branchResult?.TabablzControl;
						if (newControl != null)
						{
							ConfigureTabControl(newControl);
							cellsInRow.Add(newControl);
						}
					}

					allCellControls.AddRange(cellsInRow);
				}

				// Clear any existing items from each cell before redistribution to avoid duplicates sticking to (1,1)
				foreach (var control in allCellControls)
				{
					control.Items.Clear();
				}

				if (allCellControls.Count > 0)
				{
					int cellIndex = 0;
					foreach (var item in allItems)
					{
						var targetControl = allCellControls[cellIndex % allCellControls.Count];
						if (!targetControl.Items.Contains(item))
						{
							targetControl.Items.Add(item);
						}

						if (targetControl.SelectedItem == null)
						{
							targetControl.SelectedItem = item;
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

				foreach (var session in allItems.OfType<SessionModel>())
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

				_currentRows = rows;
				_currentColumns = cols;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Failed to create {rows}x{cols} grid: {ex.Message}");
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Stack trace: {ex.StackTrace}");
				// TODO: Show user-friendly error message via ViewModel property
			}
			finally
			{
				RefreshCommandStates();
				UpdateSuggestedGridLabel();
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
				var allItems = Items.ToList();

				var newRootControl = CreateTabControlShell();

				foreach (var item in allItems)
				{
					newRootControl.Items.Add(item);
				}

				if (newRootControl.Items.Count > 0)
				{
					newRootControl.SelectedIndex = 0;
				}

				_sessionLayout.Content = newRootControl;
				_sessionLayout.UpdateLayout();

				System.Diagnostics.Debug.WriteLine($"[OrbitView] Layout reset complete. Restored {allItems.Count} sessions to single view.");
				return newRootControl;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Failed to reset layout: {ex.Message}");
				System.Diagnostics.Debug.WriteLine($"[OrbitView] Stack trace: {ex.StackTrace}");
				return null;
			}
			finally
			{
				RefreshCommandStates();
				_currentRows = 1;
				_currentColumns = 1;
				UpdateSuggestedGridLabel();
			}
		}

		private TabablzControl CreateTabControlShell()
		{
			var tabControl = new TabablzControl
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				Margin = GetMarginFromSettings(),
				Padding = GetCellMarginFromSettings(),
				Background = System.Windows.Media.Brushes.Transparent,
				BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MahApps.Brushes.Accent"],
				BorderThickness = GetBorderThicknessFromSettings(),
				ShowDefaultAddButton = false,
				ShowDefaultCloseButton = true,
				ClosingItemCallback = TabClosingHandler
			};

			tabControl.ContentTemplate = (System.Windows.DataTemplate)System.Windows.Application.Current.Resources["SessionContentTemplate"];
			tabControl.ItemTemplate = (System.Windows.DataTemplate)System.Windows.Application.Current.Resources["OrbitSessionHeaderTemplate"];

			tabControl.InterTabController = new Dragablz.InterTabController
			{
				InterTabClient = InterTabClient,
				Partition = "OrbitMainShell"
			};

			return tabControl;
		}

		private void ConfigureTabControl(TabablzControl control)
		{
			control.HorizontalAlignment = HorizontalAlignment.Stretch;
			control.VerticalAlignment = VerticalAlignment.Stretch;
			control.Margin = GetMarginFromSettings();
			control.Padding = GetCellMarginFromSettings();
			control.BorderThickness = GetBorderThicknessFromSettings();
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

		private Thickness GetMarginFromSettings()
		{
			return (Thickness)_marginConverter.Convert(Settings.Default.OrbitViewCompactness, typeof(Thickness), null, CultureInfo.CurrentCulture);
		}

		private Thickness GetCellMarginFromSettings()
		{
			return (Thickness)_cellMarginConverter.Convert(Settings.Default.OrbitViewCompactness, typeof(Thickness), null, CultureInfo.CurrentCulture);
		}

		private Thickness GetBorderThicknessFromSettings()
		{
			return (Thickness)_borderThicknessConverter.Convert(Settings.Default.OrbitViewBorderThickness, typeof(Thickness), null, CultureInfo.CurrentCulture);
		}

		private void CreateCustomGrid()
		{
			CreateGrid(CustomRows, CustomColumns);
		}

		private bool CanCreateCustomGrid() => _sessionLayout != null && CustomRows > 0 && CustomColumns > 0;

		public void ApplyDefaultGridDensity()
		{
			if (_sessionLayout == null)
				return;

			var density = Settings.Default.OrbitViewGridDensity;
				if (density <= 1)
				{
					ResetLayout();
					return;
				}

				CreateGrid(density, density);
			RefreshCommandStates();
		}

	private bool CanCreateGrid() => _sessionLayout != null;

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
					RefreshCommandStates();
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
					AddItemToLayout(item);
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
						RemoveItemFromLayout(item);
					}
				}
				else if (e.Action == NotifyCollectionChangedAction.Reset)
				{
					for (int i = Items.Count - 1; i >= 0; i--)
					{
						if (Items[i] is SessionModel session && !_sessionCollectionService.Sessions.Contains(session))
						{
							Items.RemoveAt(i);
							RemoveItemFromLayout(session);
						}
					}
				}
			}
			RefreshCommandStates();
			UpdateSuggestedGridLabel();
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		private void AddItemToLayout(object item)
		{
			if (_sessionLayout == null)
				return;

			var targetControl = SelectTargetControlForNewItem();
			if (targetControl == null)
				return;

			if (!targetControl.Items.Contains(item))
			{
				targetControl.Items.Add(item);
				if (targetControl.SelectedItem == null)
				{
					targetControl.SelectedItem = item;
				}
			}
			RefreshCommandStates();
		}

		private TabablzControl? SelectTargetControlForNewItem()
		{
			if (_sessionLayout == null)
				return null;

			var tabControls = GetAllTabControls(_sessionLayout).ToList();
			if (tabControls.Count == 0)
				return null;

			return tabControls
				.OrderBy(c => c.Items.Count)
				.ThenBy(c => tabControls.IndexOf(c))
				.FirstOrDefault();
		}

		private void AutoFitGrid()
		{
			var (rows, columns) = CalculateRecommendedGrid(GetSessionCount());
			CreateGrid(rows, columns);
		}

		private void ReapplyCurrentGrid()
		{
			if (_currentRows < 1 || _currentColumns < 1)
				return;

			CreateGrid(_currentRows, _currentColumns);
		}

		private bool CanBalanceGrid() => _sessionLayout != null && (_currentRows > 1 || _currentColumns > 1);

		private (int rows, int columns) CalculateRecommendedGrid(int sessionCount)
		{
			if (sessionCount <= 1)
				return (1, 1);

			var columns = (int)Math.Ceiling(Math.Sqrt(sessionCount));
			if (columns < 1)
				columns = 1;

			var rows = (int)Math.Ceiling(sessionCount / (double)columns);
			if (rows < 1)
				rows = 1;

			return (rows, columns);
		}

		private int GetSessionCount() => Items.OfType<SessionModel>().Count();

		private void UpdateSuggestedGridLabel()
		{
			var sessionCount = GetSessionCount();
			var (rows, columns) = CalculateRecommendedGrid(sessionCount);
			SuggestedGridLabel = $"Suggested: {rows} x {columns} for {sessionCount} session{(sessionCount == 1 ? string.Empty : "s")}";
		}

		private void RemoveItemFromLayout(object item)
		{
			if (_sessionLayout == null)
				return;

			foreach (var control in GetAllTabControls(_sessionLayout))
			{
				if (control.Items.Contains(item))
				{
					control.Items.Remove(item);
					CollapseIfEmpty(control);
				}
			}
		}

		private void CollapseIfEmpty(TabablzControl control)
		{
			if (control == null || control.Items.Count > 0)
				return;

			// Attempt to collapse empty branches inside the Layout (matches Dragablz branch consolidation).
			var consolidate = typeof(Dragablz.Dockablz.Layout).GetMethod("ConsolidateBranch", BindingFlags.Static | BindingFlags.NonPublic);
			var targetNode = (DependencyObject)control;
			consolidate?.Invoke(null, new object[] { targetNode });

			// If this tab control lives in a tear-off window (not the main window) and is now empty, close that window.
			var hostWindow = System.Windows.Window.GetWindow(control);
			if (hostWindow != null && !ReferenceEquals(hostWindow, Application.Current?.MainWindow) && control.Items.Count == 0)
			{
				try { hostWindow.Close(); } catch { /* best effort */ }
			}

			RefreshCommandStates();
		}

		private void RefreshCommandStates()
		{
			_createCustomGridCommand?.NotifyCanExecuteChanged();
			_createGrid2x2Command?.NotifyCanExecuteChanged();
			_createGrid3x3Command?.NotifyCanExecuteChanged();
			_resetLayoutCommand?.NotifyCanExecuteChanged();
			_autoFitGridCommand?.NotifyCanExecuteChanged();
			_balanceRowsCommand?.NotifyCanExecuteChanged();
			_balanceColumnsCommand?.NotifyCanExecuteChanged();
			_toggleHeaderCommand?.NotifyCanExecuteChanged();
		}

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
