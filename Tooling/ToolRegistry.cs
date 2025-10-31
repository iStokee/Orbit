using System;
using System.Collections.Generic;
using System.Linq;

namespace Orbit.Tooling;

public interface IToolRegistry
{
	IEnumerable<IOrbitTool> Tools { get; }
	IOrbitTool? Find(string key);
}

internal sealed class ToolRegistry : IToolRegistry
{
	private readonly Dictionary<string, IOrbitTool> _tools;

	public ToolRegistry(IEnumerable<IOrbitTool> tools)
	{
		if (tools == null)
		{
			throw new ArgumentNullException(nameof(tools));
		}

		_tools = tools
			.GroupBy(tool => tool.Key, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group =>
			{
				// Prefer first registration when duplicates exist to avoid runtime collisions.
				return group.First();
			}, StringComparer.Ordinal);
	}

	public IEnumerable<IOrbitTool> Tools => _tools.Values;

	public IOrbitTool? Find(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
			return null;

		return _tools.TryGetValue(key, out var tool) ? tool : null;
	}
}
