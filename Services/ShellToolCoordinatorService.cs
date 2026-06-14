using System;
using System.Collections.ObjectModel;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Logging;
using Orbit.Tooling;

namespace Orbit.Services;

public sealed class ShellToolCoordinatorService
{
	public const string AccountManagerToolKey = "AccountManager";
	public const string SettingsToolKey = "Settings";
	public const string ConsoleToolKey = "Console";
	public const string ThemeManagerToolKey = "ThemeManager";
	public const string SessionsOverviewToolKey = "SessionsOverview";
	public const string ScriptManagerToolKey = "ScriptManager";
	public const string GuideToolKey = "ApiDocumentation";
	public const string ToolsOverviewToolKey = "UnifiedToolsManager";
	public const string McpControlToolKey = "McpControl";
	public const string SessionGalleryToolKey = "SessionGallery";
	public const string SharpBuilderToolKey = "SharpBuilder";

	private readonly IToolRegistry _toolRegistry;
	private readonly ShellToolHostService _toolHostService;
	private readonly ConsoleLogService _consoleLogService;
	private readonly IServiceProvider _serviceProvider;

	public ShellToolCoordinatorService(
		IToolRegistry toolRegistry,
		ShellToolHostService toolHostService,
		ConsoleLogService consoleLogService,
		IServiceProvider serviceProvider)
	{
		_toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
		_toolHostService = toolHostService ?? throw new ArgumentNullException(nameof(toolHostService));
		_consoleLogService = consoleLogService ?? throw new ArgumentNullException(nameof(consoleLogService));
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public bool IsToolAvailable(string key)
		=> _toolRegistry.Find(key) != null;

	public bool TryOpenToolByKey(
		ObservableCollection<object> tabs,
		object ownerContext,
		string key,
		Action<object?> selectTab)
	{
		var tool = _toolRegistry.Find(key);
		if (tool == null)
		{
			return false;
		}

		OpenOrFocusToolTab(tabs, ownerContext, tool.Key, tool.DisplayName, () => tool.CreateView(ownerContext), selectTab, tool.Icon);
		return true;
	}

	public void OpenAccountManager(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
	{
		if (!TryOpenToolByKey(tabs, ownerContext, AccountManagerToolKey, selectTab))
		{
			LogUnavailable("Account Manager tool is unavailable.");
		}
	}

	public void OpenSettings(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
	{
		if (TryOpenToolByKey(tabs, ownerContext, SettingsToolKey, selectTab))
		{
			return;
		}

		LogUnavailable("Settings tool is unavailable; falling back to legacy view.", ConsoleLogLevel.Warning);
		OpenOrFocusToolTab(
			tabs,
			ownerContext,
			SettingsToolKey,
			"Settings",
			() => ResolveRequiredService<Views.SettingsView>(),
			selectTab,
			PackIconMaterialKind.Cog);
	}

	public void OpenConsole(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
	{
		if (TryOpenToolByKey(tabs, ownerContext, ConsoleToolKey, selectTab))
		{
			return;
		}

		LogUnavailable("Console tool is unavailable; falling back to legacy view.", ConsoleLogLevel.Warning);
		OpenOrFocusToolTab(
			tabs,
			ownerContext,
			ConsoleToolKey,
			"Console",
			() => _serviceProvider.GetService<Views.ConsoleView>() ?? new Views.ConsoleView(),
			selectTab,
			PackIconMaterialKind.Console);
	}

	public void OpenThemeManager(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
	{
		if (TryOpenToolByKey(tabs, ownerContext, ThemeManagerToolKey, selectTab))
		{
			return;
		}

		LogUnavailable("Theme Manager tool is unavailable; falling back to legacy view.", ConsoleLogLevel.Warning);
		OpenOrFocusToolTab(
			tabs,
			ownerContext,
			ThemeManagerToolKey,
			"Theme Manager",
			() => ResolveRequiredService<Views.ThemeManagerPanel>(),
			selectTab,
			PackIconMaterialKind.Palette);
	}

	public void OpenScriptManager(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
	{
		if (TryOpenToolByKey(tabs, ownerContext, ScriptManagerToolKey, selectTab))
		{
			return;
		}

		LogUnavailable("Script Manager tool is unavailable; falling back to local instance.", ConsoleLogLevel.Warning);
		OpenOrFocusToolTab(
			tabs,
			ownerContext,
			ScriptManagerToolKey,
			"Script Manager",
			() => ResolveRequiredService<Views.ScriptManagerPanel>(),
			selectTab,
			PackIconMaterialKind.CodeBraces);
	}

	public void OpenSessionsOverview(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
		=> OpenRegisteredToolOrLog(tabs, ownerContext, SessionsOverviewToolKey, selectTab, "Sessions tool is unavailable.");

	public void OpenGuide(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
		=> OpenRegisteredToolOrLog(tabs, ownerContext, GuideToolKey, selectTab, "Guide tool is unavailable.");

	public void OpenToolsOverview(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
		=> OpenRegisteredToolOrLog(tabs, ownerContext, ToolsOverviewToolKey, selectTab, "Tools dashboard is unavailable.");

	public void OpenMcpControl(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
		=> OpenRegisteredToolOrLog(tabs, ownerContext, McpControlToolKey, selectTab, "MCP Control tool is unavailable.");

	public void OpenSharpBuilder(ObservableCollection<object> tabs, object ownerContext, Action<object?> selectTab)
	{
		if (!TryOpenToolByKey(tabs, ownerContext, SharpBuilderToolKey, selectTab))
		{
			LogUnavailable("SharpBuilder plugin is not loaded. Load it from Tools > Unified Tools Manager > Auto-load or Import Plugin.");
		}
	}

	public void OpenOrFocusToolTab(
		ObservableCollection<object> tabs,
		object ownerContext,
		string key,
		string name,
		Func<FrameworkElement> controlFactory,
		Action<object?> selectTab,
		PackIconMaterialKind icon = PackIconMaterialKind.Tools)
		=> _toolHostService.OpenOrFocusToolTab(tabs, ownerContext, key, name, controlFactory, selectTab, icon);

	public void DisposeToolItem(object? item, object ownerContext)
		=> _toolHostService.DisposeToolItem(item, ownerContext);

	private void OpenRegisteredToolOrLog(
		ObservableCollection<object> tabs,
		object ownerContext,
		string key,
		Action<object?> selectTab,
		string unavailableMessage)
	{
		if (!TryOpenToolByKey(tabs, ownerContext, key, selectTab))
		{
			LogUnavailable(unavailableMessage);
		}
	}

	private T ResolveRequiredService<T>() where T : class
		=> _serviceProvider.GetRequiredService<T>();

	private void LogUnavailable(string message, ConsoleLogLevel level = ConsoleLogLevel.Warning)
		=> _consoleLogService.Append($"[Orbit] {message}", ConsoleLogSource.Orbit, level);
}
