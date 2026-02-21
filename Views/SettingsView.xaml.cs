using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Orbit.Tooling;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class SettingsView : UserControl
{
	private readonly IToolRegistry _toolRegistry;

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
}
