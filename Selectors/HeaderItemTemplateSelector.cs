using System.Windows;
using System.Windows.Controls;
using Orbit.Models;

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
			return item switch
			{
				SessionModel => SessionHeaderTemplate,
				ToolTabItem => ToolHeaderTemplate,
				_ => base.SelectTemplate(item, container)
			};
		}
	}
}
