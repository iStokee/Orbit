using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Orbit.Logging;
using Orbit.ViewModels;
using ListBox = System.Windows.Controls.ListBox;
using UserControl = System.Windows.Controls.UserControl;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace Orbit.Views;

public partial class ConsoleView : UserControl
{
	private readonly ConsoleViewModel _viewModel;

	public ConsoleView()
	{
		InitializeComponent();
		_viewModel = ConsoleViewModel.Instance;
		DataContext = _viewModel;

		// Subscribe to collection changes for auto-scroll
		// ReadOnlyObservableCollection implements INotifyCollectionChanged
		if (ConsoleLogService.Instance.Entries is INotifyCollectionChanged notifyCollection)
		{
			notifyCollection.CollectionChanged += Entries_CollectionChanged;

			// Unsubscribe when unloaded to prevent memory leaks
			Unloaded += (s, e) =>
			{
				notifyCollection.CollectionChanged -= Entries_CollectionChanged;
			};
		}
	}

	private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Only auto-scroll when new items are added and auto-scroll is enabled
		if (e.Action == NotifyCollectionChangedAction.Add && _viewModel.AutoScrollEnabled)
		{
			// Defer scroll until after layout settles to avoid re-entrancy issues.
			Dispatcher.BeginInvoke(new Action(() =>
			{
				// Auto-scroll all visible listboxes based on current tab
				ScrollListBoxToEnd(AllSourcesListBox);
				ScrollListBoxToEnd(OrbitListBox);
				ScrollListBoxToEnd(MemoryErrorListBox);
				ScrollListBoxToEnd(ScriptsListBox);
			}), DispatcherPriority.ContextIdle);
		}
	}

	private void ScrollListBoxToEnd(ListBox? listBox)
	{
		if (listBox == null || !listBox.IsVisible || listBox.Items.Count == 0)
			return;

		var lastItem = listBox.Items[^1];
		listBox.ScrollIntoView(lastItem);
	}

	private void CopySelected_Click(object sender, RoutedEventArgs e)
	{
		var listBox = GetActiveListBox();
		if (listBox == null)
			return;

		var selectedItems = listBox.SelectedItems;
		if (selectedItems == null || selectedItems.Count == 0)
			return;

		if (_viewModel.CopySelectedCommand.CanExecute(selectedItems))
		{
			_viewModel.CopySelectedCommand.Execute(selectedItems);
		}
	}

	private ListBox? GetActiveListBox()
	{
		return _viewModel.SelectedTabIndex switch
		{
			1 => AllSourcesListBox,
			2 => OrbitListBox,
			3 => MemoryErrorListBox,
			4 => ScriptsListBox,
			_ => null
		};
	}
}
