using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace Orbit.Models;

public enum NodeParamType
{
	String,
	Number,
	Bool,
	Enum,
	List,
	Coordinate,
	Entity,
	Item,
	GameObject,
	Npc,
	Area
}

public class NodeCategory
{
	public string Id { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public string Icon { get; init; } = string.Empty;
	public int Order { get; init; }
	public string Slug { get; init; } = string.Empty;
}

public class NodeParam
{
	public string Key { get; init; } = string.Empty;
	public string Label { get; init; } = string.Empty;
	public NodeParamType Type { get; init; } = NodeParamType.String;
	public bool IsRequired { get; init; }
	public bool AllowMultiple { get; init; }
	public bool AllowPartial { get; init; }
	public bool HasQuery { get; init; }
	public string? Placeholder { get; init; }
	public IReadOnlyList<string>? EnumValues { get; init; }
}

public class NodeDefinition
{
	public string Id { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string ShortDescription { get; init; } = string.Empty;
	public string Icon { get; init; } = string.Empty;
	public string CategoryId { get; init; } = string.Empty;
	public int Order { get; init; }
	public bool HasQuery { get; init; }
	public IReadOnlyList<NodeParam> Parameters { get; init; } = Array.Empty<NodeParam>();
}

/// <summary>
/// Value holder for a definition parameter. Stored on the node instance and persisted to JSON.
/// </summary>
public class NodeParameterValue : ObservableObject
{
	private string _key = string.Empty;
	private NodeParamType _type;
	private bool _allowMultiple;
	private string _rawValue = string.Empty;
	private bool _boolValue;

	public string Key
	{
		get => _key;
		set => SetProperty(ref _key, value ?? string.Empty);
	}

	public NodeParamType Type
	{
		get => _type;
		set => SetProperty(ref _type, value);
	}

	public bool AllowMultiple
	{
		get => _allowMultiple;
		set => SetProperty(ref _allowMultiple, value);
	}

	/// <summary>
	/// Raw string form for text/number/enum/list parameters. Multi-value fields use newline or comma separation.
	/// </summary>
	public string RawValue
	{
		get => _rawValue;
		set
		{
			if (SetProperty(ref _rawValue, value ?? string.Empty))
			{
				OnPropertyChanged(nameof(Values));
			}
		}
	}

	/// <summary>
	/// Dedicated storage for booleans so the UI doesn't have to parse strings.
	/// </summary>
	public bool BoolValue
	{
		get => _boolValue;
		set => SetProperty(ref _boolValue, value);
	}

	[JsonIgnore]
	public IEnumerable<string> Values => SplitValues();

	public object? GetTypedValue()
	{
		return Type switch
		{
			NodeParamType.Bool => BoolValue,
			NodeParamType.Number => double.TryParse(RawValue, out var numeric) ? numeric : null,
			_ => AllowMultiple ? SplitValues() : RawValue
		};
	}

	public IReadOnlyList<string> SplitValues()
	{
		if (string.IsNullOrWhiteSpace(RawValue))
			return Array.Empty<string>();

		return RawValue
			.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(v => v.Trim())
			.Where(v => !string.IsNullOrWhiteSpace(v))
			.ToList();
	}
}

public class NodeParamBinding
{
	public NodeParamBinding(NodeParam definition, NodeParameterValue value)
	{
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Value = value ?? throw new ArgumentNullException(nameof(value));
	}

	public NodeParam Definition { get; }
	public NodeParameterValue Value { get; }
}

public static class NodeCatalogDefaults
{
	public const string GenericActionId = "actions.generic";
	public const string StartId = "control.start";
	public const string TerminalId = "control.terminal";
	public const string BooleanConditionId = "conditions.boolean";
}
