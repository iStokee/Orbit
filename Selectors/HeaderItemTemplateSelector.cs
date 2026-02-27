using System.Windows;
using System.Windows.Controls;
using Orbit.Models;
using System.Reflection;

namespace Orbit.Selectors
{
	/// <summary>
	/// Selects the appropriate header template based on tab item type (SessionModel vs ToolTabItem)
	/// </summary>
	public class HeaderItemTemplateSelector : DataTemplateSelector
	{
		/// <summary>
		/// Template for SessionModel tab headers (with status indicators, rename buttons, etc.)
		/// </summary>
		public DataTemplate? SessionHeaderTemplate { get; set; }

		/// <summary>
		/// Template for ToolTabItem headers (simpler, with just icon and name)
		/// </summary>
		public DataTemplate? ToolHeaderTemplate { get; set; }

		public override DataTemplate? SelectTemplate(object item, DependencyObject container)
		{
			var resolvedItem = ResolveItem(item) ?? ResolveItem((container as FrameworkElement)?.DataContext);

			return resolvedItem switch
			{
				SessionModel => SessionHeaderTemplate,
				ToolTabItem => ToolHeaderTemplate,
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
}
