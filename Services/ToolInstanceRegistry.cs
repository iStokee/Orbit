using System;
using System.Collections.Generic;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

/// <summary>Tracks tool instances independently of their current tab or split host.</summary>
public sealed class ToolInstanceRegistry
{
	public static ToolInstanceRegistry Instance { get; } = new();
	private readonly List<WeakReference<ToolTabItem>> _items = new();
	private readonly object _sync = new();
	internal void Register(ToolTabItem item) { lock (_sync) _items.Add(new(item)); }
	public IReadOnlyList<ToolTabItem> OpenTools
	{
		get
		{
			lock (_sync)
			{
				var open = new List<ToolTabItem>();
				for (var i = _items.Count - 1; i >= 0; i--)
				{
					if (!_items[i].TryGetTarget(out var item) || item.IsClosed) _items.RemoveAt(i);
					else open.Add(item);
				}
				return open;
			}
		}
	}
}
