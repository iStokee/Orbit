using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Models;
using Orbit.ViewModels;
using Application = System.Windows.Application;

namespace Orbit.Services;

public sealed class ShellToolHostService
{
	private readonly OrbitLayoutStateService? _layout;
	private readonly SessionReconciliationService? _reconciliation;
	public ShellToolHostService(OrbitLayoutStateService? layout = null, SessionReconciliationService? reconciliation = null) { _layout = layout; _reconciliation = reconciliation; }

	public ToolTabItem OpenOrFocusToolTab(
		ObservableCollection<object> tabs,
		object ownerContext,
		string key,
		string name,
		Func<FrameworkElement> controlFactory,
		Action<object?> selectTab,
		PackIconMaterialKind icon = PackIconMaterialKind.Tools)
	{
		if (tabs == null)
		{
			throw new ArgumentNullException(nameof(tabs));
		}

		if (controlFactory == null)
		{
			throw new ArgumentNullException(nameof(controlFactory));
		}

		if (selectTab == null)
		{
			throw new ArgumentNullException(nameof(selectTab));
		}

		var existingTool = FindToolTab(tabs, key);
		if (existingTool != null)
		{
			selectTab(existingTool);
			return existingTool;
		}

		if (TryAdoptToolFromOtherWindow(key, ownerContext, out var adoptedTool))
		{
			tabs.Add(adoptedTool);
			selectTab(adoptedTool);
			return adoptedTool;
		}

		var control = controlFactory();
		if (control.DataContext == null)
		{
			control.DataContext = ownerContext;
		}

		var newTool = new ToolTabItem(key, name, control, icon);
		tabs.Add(newTool);
		selectTab(newTool);
		return newTool;
	}

	public bool TryAdoptToolFromOtherWindow(string key, object ownerContext, out ToolTabItem adopted)
	{
		adopted = null!;
		if (string.Equals(key, ShellPresentationPolicyService.OrbitViewToolKey, StringComparison.Ordinal))
		{
			return false;
		}

		var existing = ToolInstanceRegistry.Instance.OpenTools.FirstOrDefault(tool => tool.Key == key &&
			tool.HostControl.Dispatcher.CheckAccess() &&
			(Window.GetWindow(tool.HostControl) != null || _layout?.Items.Contains(tool) == true));
		if (existing == null) return false;
		var previousOwner = Window.GetWindow(existing.HostControl)?.DataContext;
		_layout?.RemoveItem(existing);
		if (_reconciliation != null) _reconciliation.RemoveItemFromTabOwners(existing, SessionTabOwnerScope.All, "adopt-tool");
		else
		{
			foreach (var window in Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>())
				if (window.DataContext is MainWindowViewModel shell) shell.Tabs.Remove(existing);
		}
		if (previousOwner != null && ReferenceEquals(existing.HostControl.DataContext, previousOwner))
			existing.HostControl.DataContext = ownerContext;
		adopted = existing;
		return true;
	}

	public void DisposeToolItem(object? item, object ownerContext)
	{
		if (item is not ToolTabItem tool || tool.HostControl == null)
		{
			return;
		}

		try { tool.Close(ownerContext); }
		catch (Exception ex) { Console.WriteLine("[Orbit] Tool cleanup failed: " + ex.Message); }
	}

	private static ToolTabItem? FindToolTab(ObservableCollection<object> tabs, string key)
	{
		return tabs.OfType<ToolTabItem>()
			.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
	}
}
