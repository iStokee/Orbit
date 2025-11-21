using System.Windows;
using System.Windows.Controls;
using Orbit.Models;

namespace Orbit.Utilities;

/// <summary>
/// Chooses parameter editor templates based on the parameter type and multiplicity.
/// </summary>
public class NodeParameterTemplateSelector : DataTemplateSelector
{
	public DataTemplate? StringTemplate { get; set; }
	public DataTemplate? NumberTemplate { get; set; }
	public DataTemplate? BoolTemplate { get; set; }
	public DataTemplate? EnumTemplate { get; set; }
	public DataTemplate? ListTemplate { get; set; }

	public override DataTemplate? SelectTemplate(object item, DependencyObject container)
	{
		if (item is not NodeParamBinding binding)
			return base.SelectTemplate(item, container);

		if (binding.Definition.AllowMultiple || binding.Definition.Type == NodeParamType.List)
			return ListTemplate ?? StringTemplate;

		return binding.Definition.Type switch
		{
			NodeParamType.Bool => BoolTemplate,
			NodeParamType.Enum => EnumTemplate ?? StringTemplate,
			NodeParamType.Number => NumberTemplate ?? StringTemplate,
			_ => StringTemplate
		};
	}
}
