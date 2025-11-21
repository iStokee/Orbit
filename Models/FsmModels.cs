using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Orbit.Models;

public enum FsmNodeType
{
	Start,
	Action,
	Condition,
	Terminal
}

public class FsmTransitionModel : ObservableObject
{
	private Guid _id = Guid.NewGuid();
	private Guid _fromNodeId;
	private Guid _toNodeId;
	private string _label = "Next";
	private string _conditionKey = string.Empty;
	private bool _expectedValue = true;
	private bool _isFallback;
	private bool _isActive;

	public Guid Id
	{
		get => _id;
		set => SetProperty(ref _id, value);
	}

	public Guid FromNodeId
	{
		get => _fromNodeId;
		set => SetProperty(ref _fromNodeId, value);
	}

	public Guid ToNodeId
	{
		get => _toNodeId;
		set => SetProperty(ref _toNodeId, value);
	}

	public string Label
	{
		get => _label;
		set => SetProperty(ref _label, value ?? string.Empty);
	}

	/// <summary>
	/// Optional key used to evaluate this transition. When empty, the transition acts as an unconditional edge.
	/// </summary>
	public string ConditionKey
	{
		get => _conditionKey;
		set => SetProperty(ref _conditionKey, value ?? string.Empty);
	}

	/// <summary>
	/// Expected value for <see cref="ConditionKey"/> when evaluating this edge.
	/// </summary>
	public bool ExpectedValue
	{
		get => _expectedValue;
		set => SetProperty(ref _expectedValue, value);
	}

	/// <summary>
	/// When true, this edge acts as the fallback if no other conditions match.
	/// </summary>
	public bool IsFallback
	{
		get => _isFallback;
		set => SetProperty(ref _isFallback, value);
	}

	public bool IsActive
	{
		get => _isActive;
		set => SetProperty(ref _isActive, value);
	}

	public bool HasCondition => !string.IsNullOrWhiteSpace(_conditionKey);
}

public class FsmNodeModel : ObservableObject
{
	private Guid _id = Guid.NewGuid();
	private string _title = "State";
	private string _description = string.Empty;
	private FsmNodeType _type = FsmNodeType.Action;
	private double _x = 60;
	private double _y = 60;
	private int _dwellMilliseconds = 300;
	private string _actionText = string.Empty;
	private bool _isActive;
	private bool _isSelected;
	private ObservableCollection<FsmTransitionModel> _transitions = new();

	public Guid Id
	{
		get => _id;
		set => SetProperty(ref _id, value);
	}

	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value ?? string.Empty);
	}

	public string Description
	{
		get => _description;
		set => SetProperty(ref _description, value ?? string.Empty);
	}

	public FsmNodeType Type
	{
		get => _type;
		set => SetProperty(ref _type, value);
	}

	public double X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	public double Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>
	/// Delay (in milliseconds) the runner should wait while visiting this node.
	/// </summary>
	public int DwellMilliseconds
	{
		get => _dwellMilliseconds;
		set => SetProperty(ref _dwellMilliseconds, value);
	}

	/// <summary>
	/// Optional human-readable action text that can be shown in the property editor.
	/// </summary>
	public string ActionText
	{
		get => _actionText;
		set => SetProperty(ref _actionText, value ?? string.Empty);
	}

	public bool IsActive
	{
		get => _isActive;
		set => SetProperty(ref _isActive, value);
	}

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}

	public ObservableCollection<FsmTransitionModel> Transitions
	{
		get => _transitions;
		set => SetProperty(ref _transitions, value ?? new ObservableCollection<FsmTransitionModel>());
	}
}

public class FsmScriptModel : ObservableObject
{
	private Guid _id = Guid.NewGuid();
	private string _name = "New Machine";
	private string _description = string.Empty;
	private string _author = "Local";
	private Guid? _startNodeId;
	private DateTime _updatedAt = DateTime.UtcNow;
	private ObservableCollection<FsmNodeModel> _nodes = new();

	public Guid Id
	{
		get => _id;
		set => SetProperty(ref _id, value);
	}

	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value ?? string.Empty);
	}

	public string Description
	{
		get => _description;
		set => SetProperty(ref _description, value ?? string.Empty);
	}

	public string Author
	{
		get => _author;
		set => SetProperty(ref _author, value ?? string.Empty);
	}

	public Guid? StartNodeId
	{
		get => _startNodeId;
		set => SetProperty(ref _startNodeId, value);
	}

	public DateTime UpdatedAt
	{
		get => _updatedAt;
		set => SetProperty(ref _updatedAt, value);
	}

	public ObservableCollection<FsmNodeModel> Nodes
	{
		get => _nodes;
		set => SetProperty(ref _nodes, value ?? new ObservableCollection<FsmNodeModel>());
	}
}

public class FsmRuntimeSignal : ObservableObject
{
	private string _key = string.Empty;
	private bool _value;

	public string Key
	{
		get => _key;
		set => SetProperty(ref _key, value ?? string.Empty);
	}

	public bool Value
	{
		get => _value;
		set => SetProperty(ref _value, value);
	}
}
