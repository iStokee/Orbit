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

		var windows = Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel otherVm || ReferenceEquals(otherVm, ownerContext))
			{
				continue;
			}

			var existingTool = otherVm.Tabs.OfType<ToolTabItem>()
				.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
			if (existingTool == null)
			{
				continue;
			}

			otherVm.Tabs.Remove(existingTool);
			if (ReferenceEquals(otherVm.SelectedTab, existingTool))
			{
				otherVm.SelectedTab = otherVm.Tabs.FirstOrDefault();
			}

			if (existingTool.HostControl != null && ReferenceEquals(existingTool.HostControl.DataContext, otherVm))
			{
				existingTool.HostControl.DataContext = ownerContext;
			}

			adopted = existingTool;
			return true;
		}

		return false;
	}

	public void DisposeToolItem(object? item, object ownerContext)
	{
		if (item is not ToolTabItem tool || tool.HostControl == null)
		{
			return;
		}

		try
		{
			if (tool.HostControl.DataContext is IDisposable disposable &&
				!ReferenceEquals(disposable, ownerContext))
			{
				disposable.Dispose();
			}
		}
		catch
		{
			// Best effort cleanup.
		}
	}

	private static ToolTabItem? FindToolTab(ObservableCollection<object> tabs, string key)
	{
		return tabs.OfType<ToolTabItem>()
			.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
	}
}
