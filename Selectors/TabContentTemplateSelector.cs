using System.Windows;
using System.Windows.Controls;
using Orbit.Models;
using System.Reflection;

namespace Orbit.Selectors;

/// <summary>
/// Chooses the appropriate content template for a tab item (SessionModel vs ToolTabItem).
/// This keeps tools "dock friendly" inside Orbit View without impacting session host surfaces.
/// </summary>
public sealed class TabContentTemplateSelector : DataTemplateSelector
{
	public DataTemplate? SessionContentTemplate { get; set; }
	public DataTemplate? ToolContentTemplate { get; set; }

	public override DataTemplate? SelectTemplate(object item, DependencyObject container)
	{
		var resolvedItem = ResolveItem(item) ?? ResolveItem((container as FrameworkElement)?.DataContext);

		return resolvedItem switch
		{
			SessionModel => SessionContentTemplate,
			ToolTabItem => ToolContentTemplate,
			_ => base.SelectTemplate(item, container)
		};
	}

	private static object? ResolveItem(object? item)
	{
		var current = item;
		for (var depth = 0; depth < 8 && current != null; depth++)
		{
			if (current is SessionModel or ToolTabItem)
			{
				return current;
			}

			var next = TryGet(current, "Content")
				?? TryGet(current, "Item")
				?? TryGet(current, "DataContext")
				?? TryGet(current, "DataItem")
				?? TryGet(current, "Model")
				?? TryGet(current, "Value");

			if (next == null || ReferenceEquals(next, current))
			{
				break;
			}

			current = next;
		}

		return current;
	}

	private static object? TryGet(object instance, string propertyName)
	{
		var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		return property?.GetValue(instance);
	}
}
