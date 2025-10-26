using System.Collections.Specialized;
using System.Windows.Controls;
using Orbit.Logging;
using Orbit.ViewModels;
using ListBox = System.Windows.Controls.ListBox;
using UserControl = System.Windows.Controls.UserControl;

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
			// Get the ListBox by name
			var listBox = this.FindName("ConsoleListBox") as ListBox;

			// Scroll to the last item
			if (listBox?.Items.Count > 0)
			{
				listBox.ScrollIntoView(listBox.Items[^1]);
			}
		}
	}
}
