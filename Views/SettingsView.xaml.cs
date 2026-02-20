using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Orbit.Tooling;
using Orbit.ViewModels;
using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class SettingsView : UserControl
{
	public SettingsView(SettingsViewModel vm)
	{
		InitializeComponent();
		DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
		LoadToolsDashboard();
		Unloaded += OnUnloaded;
	}

	private void LoadToolsDashboard()
	{
		try
		{
			var app = Application.Current as App;
			var registry = app?.Services.GetService(typeof(IToolRegistry)) as IToolRegistry;
			var tool = registry?.Tools.FirstOrDefault(t => string.Equals(t.Key, "UnifiedToolsManager", StringComparison.Ordinal));
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
