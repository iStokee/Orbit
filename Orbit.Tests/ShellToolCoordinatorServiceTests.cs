using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Orbit.Tooling;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellToolCoordinatorServiceTests
{
	[Fact]
	public void TryOpenToolByKey_CreatesToolTabAndSelectsIt()
	{
		RunOnSta(() =>
		{
			var tool = new FakeTool("ExampleTool", "Example");
			var service = CreateService(tool);
			var tabs = new ObservableCollection<object>();
			object? selected = null;
			var owner = new object();

			var opened = service.TryOpenToolByKey(tabs, owner, tool.Key, tab => selected = tab);

			Assert.True(opened);
			var toolTab = Assert.Single(tabs.OfType<ToolTabItem>());
			Assert.Same(toolTab, selected);
			Assert.Equal(tool.Key, toolTab.Key);
			Assert.Equal(tool.DisplayName, toolTab.Name);
			Assert.Equal(1, tool.CreateViewCalls);
			Assert.Same(owner, tool.CreateViewContext);
		});
	}

	[Fact]
	public void TryOpenToolByKey_FocusesExistingToolTabWithoutCreatingDuplicate()
	{
		RunOnSta(() =>
		{
			var tool = new FakeTool("ExampleTool", "Example");
			var service = CreateService(tool);
			var tabs = new ObservableCollection<object>();
			object? selected = null;
			var owner = new object();

			Assert.True(service.TryOpenToolByKey(tabs, owner, tool.Key, tab => selected = tab));
			selected = null;

			var reopened = service.TryOpenToolByKey(tabs, owner, tool.Key, tab => selected = tab);

			Assert.True(reopened);
			var toolTab = Assert.Single(tabs.OfType<ToolTabItem>());
			Assert.Same(toolTab, selected);
			Assert.Equal(1, tool.CreateViewCalls);
		});
	}

	[Fact]
	public void TryOpenToolByKey_ReturnsFalseWhenToolIsUnavailable()
	{
		var service = CreateService();
		var tabs = new ObservableCollection<object>();
		var selected = false;

		var opened = service.TryOpenToolByKey(tabs, new object(), "MissingTool", _ => selected = true);

		Assert.False(opened);
		Assert.Empty(tabs);
		Assert.False(selected);
	}

	private static ShellToolCoordinatorService CreateService(params IOrbitTool[] tools)
	{
		var services = new ServiceCollection().BuildServiceProvider();
		return new ShellToolCoordinatorService(
			new FakeToolRegistry(tools),
			new ShellToolHostService(),
			ConsoleLogService.Instance,
			services);
	}

	private sealed class FakeToolRegistry : IToolRegistry
	{
		private readonly Dictionary<string, IOrbitTool> _tools;

		public FakeToolRegistry(IEnumerable<IOrbitTool> tools)
		{
			_tools = tools.ToDictionary(tool => tool.Key, StringComparer.Ordinal);
		}

		public IEnumerable<IOrbitTool> Tools => _tools.Values;

		public IOrbitTool? Find(string key)
			=> _tools.TryGetValue(key, out var tool) ? tool : null;

		public void RegisterPluginTool(IOrbitTool tool)
			=> _tools[tool.Key] = tool;

		public bool UnregisterPluginTool(string key)
			=> _tools.Remove(key);
	}

	private sealed class FakeTool : IOrbitTool
	{
		public FakeTool(string key, string displayName)
		{
			Key = key;
			DisplayName = displayName;
		}

		public string Key { get; }
		public string DisplayName { get; }
		public PackIconMaterialKind Icon => PackIconMaterialKind.Tools;
		public int CreateViewCalls { get; private set; }
		public object? CreateViewContext { get; private set; }

		public FrameworkElement CreateView(object? context = null)
		{
			CreateViewCalls++;
			CreateViewContext = context;
			return new Border();
		}
	}

	private static void RunOnSta(Action action)
	{
		Exception? exception = null;
		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				exception = ex;
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		if (exception != null)
		{
			throw exception;
		}
	}
}
