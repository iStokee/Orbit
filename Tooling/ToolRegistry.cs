using System;
using System.Collections.Generic;
using System.Linq;

namespace Orbit.Tooling;

public interface IToolRegistry
{
	IEnumerable<IOrbitTool> Tools { get; }
	IOrbitTool? Find(string key);

	/// <summary>
	/// Registers a dynamically loaded plugin tool.
	/// </summary>
	void RegisterPluginTool(IOrbitTool tool);

	/// <summary>
	/// Unregisters a dynamically loaded plugin tool.
	/// </summary>
	bool UnregisterPluginTool(string key);
}

internal sealed class ToolRegistry : IToolRegistry
{
	private readonly Dictionary<string, IOrbitTool> _builtInTools;
	private readonly Dictionary<string, IOrbitTool> _pluginTools = new(StringComparer.Ordinal);
	private readonly object _lock = new();

	public ToolRegistry(IEnumerable<IOrbitTool> tools)
	{
		if (tools == null)
		{
			throw new ArgumentNullException(nameof(tools));
		}

		_builtInTools = tools
			.GroupBy(tool => tool.Key, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group =>
			{
				// Prefer first registration when duplicates exist to avoid runtime collisions.
				return group.First();
			}, StringComparer.Ordinal);
	}

	public IEnumerable<IOrbitTool> Tools
	{
		get
		{
			lock (_lock)
			{
				return _builtInTools.Values
					.Concat(_pluginTools.Values)
					.ToList();
			}
		}
	}

	public IOrbitTool? Find(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
			return null;

		lock (_lock)
		{
			if (_pluginTools.TryGetValue(key, out var pluginTool))
			{
				return pluginTool;
			}

			return _builtInTools.TryGetValue(key, out var tool) ? tool : null;
		}
	}

	public void RegisterPluginTool(IOrbitTool tool)
	{
		if (tool == null)
			throw new ArgumentNullException(nameof(tool));

		lock (_lock)
		{
			if (_builtInTools.ContainsKey(tool.Key))
			{
				Console.WriteLine($"[Orbit][Plugin] Ignoring plugin tool key '{tool.Key}' because it conflicts with a built-in Orbit tool.");
				return;
			}

			_pluginTools[tool.Key] = tool;
		}
	}

	public bool UnregisterPluginTool(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
			return false;

		lock (_lock)
		{
			return _pluginTools.Remove(key);
		}
	}
}
