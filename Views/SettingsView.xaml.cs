using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Tooling;
using Orbit.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Orbit.Views;

public partial class SettingsView : UserControl, Orbit.Services.IToolViewLifetime
{
	private void SnapSettingsLayout_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		var compact = e.NewSize.Width < 760;
		Grid.SetColumnSpan(SnapPreviewPane, compact ? 3 : 1);
		SnapPreviewPane.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
		Grid.SetRow(SnapSettingsControls, compact ? 1 : 0);
		Grid.SetColumn(SnapSettingsControls, compact ? 0 : 2);
		Grid.SetColumnSpan(SnapSettingsControls, compact ? 3 : 1);
		SnapSettingsControls.Margin = compact ? new Thickness(0, 16, 0, 0) : new Thickness(0);
	}
	private readonly IToolRegistry _toolRegistry;
	private bool _closed;
	public void Dispose()
	{
		if (_closed) return;
		_closed = true;
		if (ToolsDashboardHost.Content is FrameworkElement child && child.DataContext is IDisposable disposable) disposable.Dispose();
		ToolsDashboardHost.Content = null;
		Unloaded -= OnUnloaded;
	}

	public SettingsView(SettingsViewModel vm, IToolRegistry toolRegistry)
	{
		InitializeComponent();
		DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
		_toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
		LoadToolsDashboard();
		Unloaded += OnUnloaded;
	}

	private void LoadToolsDashboard()
	{
		try
		{
			var tool = _toolRegistry.Tools.FirstOrDefault(t => string.Equals(t.Key, "UnifiedToolsManager", StringComparison.Ordinal));
			if (tool != null)
			{
				ToolsDashboardHost.Content = tool.CreateView();
			}
		}
		catch (Exception ex)
		{
			ToolsDashboardHost.Content = new TextBlock
			{
				Text = $"Unable to load Tools & Plugins dashboard: {ex.Message}",
				TextWrapping = TextWrapping.Wrap
			};
		}
	}

	private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
	{
		// Unloaded can fire during tab reparenting/tear-off; do not dispose view model here.
		Unloaded -= OnUnloaded;
	}

	private void SettingsRoot_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm || !vm.IsCapturingMesharpDebugMenuHotkey)
		{
			return;
		}

		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		vm.CaptureMesharpDebugMenuHotkey(key, Keyboard.Modifiers);
		e.Handled = true;
	}
}
