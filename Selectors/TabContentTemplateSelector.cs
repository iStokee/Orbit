using System.Windows;
using System.Windows.Controls;
using Orbit.Models;

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
		return item switch
		{
			SessionModel => SessionContentTemplate,
			ToolTabItem => ToolContentTemplate,
			_ => base.SelectTemplate(item, container)
		};
	}
}

