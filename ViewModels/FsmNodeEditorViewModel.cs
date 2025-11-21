using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using Microsoft.Win32;
using Orbit.Models;
using Orbit.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Orbit.ViewModels;

/// <summary>
/// Backing view model for the FSM node editor. Handles script persistence, runtime execution,
/// and light-weight visual state (selection, active trail).
/// </summary>
public class FsmNodeEditorViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly FsmScriptService _scriptService;
	private readonly FsmExecutionEngine _engine;
	private readonly NodeCatalogService _catalogService;
	private CancellationTokenSource? _runCts;
	private FsmScriptModel _script;
	private FsmNodeModel? _selectedNode;
	private NodeDefinition? _selectedNodeDefinition;
	private FsmTransitionModel? _selectedTransition;
	private readonly ObservableCollection<FsmNodeModel> _selectedNodes = new();
	private bool _isRunning;
	private bool _isLooping = true;
	private string _status = "Idle";
	private string? _currentFilePath;
	private NodeCategory? _selectedCategory;
	private bool _isLeftCollapsed;
	private bool _isRightCollapsed;

	public FsmNodeEditorViewModel()
		: this(new NodeCatalogService())
	{
	}

	private FsmNodeEditorViewModel(NodeCatalogService catalogService)
		: this(new FsmScriptService(catalogService), new FsmExecutionEngine(catalogService, new NodeExecutorRegistry()), catalogService)
	{
	}

	public FsmNodeEditorViewModel(
		FsmScriptService scriptService,
		FsmExecutionEngine engine,
		NodeCatalogService catalogService)
	{
		_scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));

		Categories = new ObservableCollection<NodeCategory>(_catalogService.Categories);
		Definitions = new ObservableCollection<NodeDefinition>(_catalogService.Definitions);
		_selectedCategory = Categories.FirstOrDefault();
		SelectedNodes = new ReadOnlyObservableCollection<FsmNodeModel>(_selectedNodes);

		AddNodeCommand = new RelayCommand(AddNode);
		CreateNodeFromDefinitionCommand = new RelayCommand<NodeDefinition?>(AddNodeFromDefinition);
		RemoveNodeCommand = new RelayCommand(RemoveSelectedNode, () => SelectedNode != null);
		AddTransitionCommand = new RelayCommand(AddTransition, () => SelectedNode != null && Script.Nodes.Count > 1);
		RemoveTransitionCommand = new RelayCommand<FsmTransitionModel?>(RemoveTransition, _ => SelectedNode != null);
		SetAsStartCommand = new RelayCommand(SetSelectedAsStart, () => SelectedNode != null);
		ClearTrailCommand = new RelayCommand(ClearTrail);
		DeleteSelectedCommand = new RelayCommand(DeleteSelection);
		ToggleLeftPanelCommand = new RelayCommand(() => IsLeftCollapsed = !IsLeftCollapsed);
		ToggleRightPanelCommand = new RelayCommand(() => IsRightCollapsed = !IsRightCollapsed);

		NewScriptCommand = new RelayCommand(CreateBlankScript);
		LoadScriptCommand = new AsyncRelayCommand(LoadScriptAsync);
		SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
		ExportScriptCommand = new AsyncRelayCommand(ExportScriptAsync);
		LoadTemplateCommand = new RelayCommand(LoadTemplate);

		StartCommand = new AsyncRelayCommand(async () => await StartRunAsync(_isLooping));
		StepCommand = new AsyncRelayCommand(async () => await StartRunAsync(false));
		StopCommand = new RelayCommand(StopRun, () => IsRunning);
		AddListItemCommand = new RelayCommand<NodeParamBinding?>(AddListEntry);
		RemoveListItemCommand = new RelayCommand<(NodeParamBinding binding, string value)?>(RemoveListEntry);

		_engine.NodeEntered += OnNodeEntered;
		_engine.TransitionTaken += OnTransitionTaken;
		_engine.Completed += OnEngineCompleted;
		_engine.Faulted += OnEngineFaulted;

		_script = _scriptService.CreatePowerFishingTemplate();
		AttachScript(_script);
		RefreshSignals();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ObservableCollection<FsmRuntimeSignal> Signals { get; } = new();
	public ObservableCollection<string> SignalSuggestions { get; } = new();
	public ObservableCollection<NodeCategory> Categories { get; }
	public ObservableCollection<NodeDefinition> Definitions { get; }
	public ObservableCollection<NodeParamBinding> ParameterBindings { get; } = new();
	public ReadOnlyObservableCollection<FsmNodeModel> SelectedNodes { get; }
	// Keep collapse width large enough to show the toggle affordance plus padding.
	public GridLength LeftColumnWidth => _isLeftCollapsed ? new GridLength(84) : new GridLength(320);
	public GridLength RightColumnWidth => _isRightCollapsed ? new GridLength(84) : new GridLength(360);

	public IEnumerable<FsmTransitionModel> AllTransitions =>
		Script.Nodes.SelectMany(n => n.Transitions);

	public FsmScriptModel Script
	{
		get => _script;
		private set
		{
			if (ReferenceEquals(_script, value))
				return;

			DetachScript();
			_script = value;
			AttachScript(_script);
			OnPropertyChanged();
			OnPropertyChanged(nameof(AllTransitions));
		}
	}

	public FsmNodeModel? SelectedNode
	{
		get => _selectedNode;
		set
		{
			if (ReferenceEquals(_selectedNode, value))
				return;

			UpdatePrimarySelection(value);

			SelectedNodeDefinition = _selectedNode == null
				? null
				: _catalogService.GetDefinition(_selectedNode.DefinitionId);

			OnPropertyChanged();
			OnPropertyChanged(nameof(CanEditNode));
			OnPropertyChanged(nameof(CanEditTransitions));
			RemoveNodeCommand.NotifyCanExecuteChanged();
			AddTransitionCommand.NotifyCanExecuteChanged();
			SetAsStartCommand.NotifyCanExecuteChanged();
			RemoveTransitionCommand.NotifyCanExecuteChanged();
			RefreshParameterBindings();
		}
	}

	public NodeDefinition? SelectedNodeDefinition
	{
		get => _selectedNodeDefinition;
		set
		{
			_selectedNodeDefinition = value;
			if (SelectedNode != null && value != null)
			{
				ApplyDefinitionToNode(SelectedNode, value);
			}

			OnPropertyChanged();
			RefreshParameterBindings();
		}
	}

	public void SelectNode(FsmNodeModel node, bool toggle)
	{
		if (node == null) return;

		if (toggle)
		{
			if (_selectedNodes.Contains(node))
			{
				node.IsSelected = false;
				_selectedNodes.Remove(node);
			}
			else
			{
				node.IsSelected = true;
				_selectedNodes.Add(node);
			}

			_selectedNode = _selectedNodes.LastOrDefault();
		}
		else
		{
			foreach (var existing in _selectedNodes.ToList())
			{
				existing.IsSelected = false;
				_selectedNodes.Remove(existing);
			}

			node.IsSelected = true;
			_selectedNodes.Add(node);
			_selectedNode = node;
		}

		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
	}

	/// <summary>
	/// Clears all selected nodes.
	/// </summary>
	public void ClearSelection()
	{
		foreach (var node in _selectedNodes.ToList())
		{
			node.IsSelected = false;
			_selectedNodes.Remove(node);
		}

		_selectedNode = null;
		SelectedNodeDefinition = null;

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
	}

	/// <summary>
	/// Selects all nodes within the specified bounds (for box/marquee selection).
	/// </summary>
	/// <param name="bounds">The selection rectangle in canvas coordinates.</param>
	public void SelectNodesInBounds(Rect bounds)
	{
		const double nodeWidth = 220;
		const double nodeHeight = 130;

		foreach (var node in Script.Nodes)
		{
			var nodeRect = new Rect(node.X, node.Y, nodeWidth, nodeHeight);
			if (bounds.IntersectsWith(nodeRect))
			{
				if (!_selectedNodes.Contains(node))
				{
					node.IsSelected = true;
					_selectedNodes.Add(node);
				}
			}
		}

		_selectedNode = _selectedNodes.LastOrDefault();
		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
	}

	private void UpdatePrimarySelection(FsmNodeModel? node)
	{
		foreach (var existing in _selectedNodes.ToList())
		{
			existing.IsSelected = false;
			_selectedNodes.Remove(existing);
		}

		_selectedNode = node;

		if (_selectedNode != null)
		{
			_selectedNode.IsSelected = true;
			_selectedNodes.Add(_selectedNode);
		}
	}

	public FsmTransitionModel? SelectedTransition
	{
		get => _selectedTransition;
		set
		{
			if (ReferenceEquals(_selectedTransition, value))
				return;

			_selectedTransition = value;
			OnPropertyChanged();
			RemoveTransitionCommand.NotifyCanExecuteChanged();
		}
	}

	public bool IsRunning
	{
		get => _isRunning;
		private set
		{
			if (_isRunning == value) return;
			_isRunning = value;
			OnPropertyChanged();
			StopCommand.NotifyCanExecuteChanged();
		}
	}

	public bool IsLooping
	{
		get => _isLooping;
		set
		{
			if (_isLooping == value) return;
			_isLooping = value;
			OnPropertyChanged();
		}
	}

	public string Status
	{
		get => _status;
		set
		{
			if (_status == value) return;
			_status = value;
			OnPropertyChanged();
		}
	}

	public string? CurrentFilePath
	{
		get => _currentFilePath;
		private set
		{
			_currentFilePath = value;
			OnPropertyChanged();
		}
	}

	public NodeCategory? SelectedCategory
	{
		get => _selectedCategory;
		set
		{
			_selectedCategory = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(FilteredDefinitions));
		}
	}

	public IEnumerable<NodeDefinition> FilteredDefinitions =>
		SelectedCategory == null || string.Equals(SelectedCategory.Id, "all", StringComparison.OrdinalIgnoreCase)
			? Definitions
			: Definitions.Where(d => string.Equals(d.CategoryId, SelectedCategory.Id, StringComparison.OrdinalIgnoreCase));

	public bool IsLeftCollapsed
	{
		get => _isLeftCollapsed;
		set
		{
			if (_isLeftCollapsed == value) return;
			_isLeftCollapsed = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(LeftColumnWidth));
		}
	}

	public bool IsRightCollapsed
	{
		get => _isRightCollapsed;
		set
		{
			if (_isRightCollapsed == value) return;
			_isRightCollapsed = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(RightColumnWidth));
		}
	}

	public bool CanEditNode => SelectedNode != null;

	public bool CanEditTransitions => SelectedNode != null && Script.Nodes.Count > 1;

	public IRelayCommand AddNodeCommand { get; }
	public IRelayCommand<NodeDefinition?> CreateNodeFromDefinitionCommand { get; }
	public IRelayCommand RemoveNodeCommand { get; }
	public IRelayCommand AddTransitionCommand { get; }
	public IRelayCommand<FsmTransitionModel?> RemoveTransitionCommand { get; }
	public IRelayCommand SetAsStartCommand { get; }
	public IRelayCommand ClearTrailCommand { get; }
	public IRelayCommand DeleteSelectedCommand { get; }
	public IRelayCommand ToggleLeftPanelCommand { get; }
	public IRelayCommand ToggleRightPanelCommand { get; }

	public IRelayCommand NewScriptCommand { get; }
	public IAsyncRelayCommand LoadScriptCommand { get; }
	public IAsyncRelayCommand SaveScriptCommand { get; }
	public IAsyncRelayCommand ExportScriptCommand { get; }
	public IRelayCommand LoadTemplateCommand { get; }

	public IAsyncRelayCommand StartCommand { get; }
	public IAsyncRelayCommand StepCommand { get; }
	public IRelayCommand StopCommand { get; }
	public IRelayCommand<NodeParamBinding?> AddListItemCommand { get; }
	public IRelayCommand<(NodeParamBinding binding, string value)?> RemoveListItemCommand { get; }

	public void Dispose()
	{
		_runCts?.Cancel();
		_engine.NodeEntered -= OnNodeEntered;
		_engine.TransitionTaken -= OnTransitionTaken;
		_engine.Completed -= OnEngineCompleted;
		_engine.Faulted -= OnEngineFaulted;
	}

	private void AttachScript(FsmScriptModel script)
	{
		script.Nodes.CollectionChanged += OnNodesChanged;
		foreach (var node in script.Nodes)
		{
			EnsureDefinition(node);
			node.PropertyChanged += OnNodePropertyChanged;
			node.Transitions.CollectionChanged += OnTransitionsChanged;
			node.Parameters.CollectionChanged += OnParametersChanged;
			foreach (var param in node.Parameters)
			{
				param.PropertyChanged += OnParameterPropertyChanged;
			}
		}

		SelectedNode = script.Nodes.FirstOrDefault();
		Status = $"Loaded \"{script.Name}\"";
	}

	private void DetachScript()
	{
		_script.Nodes.CollectionChanged -= OnNodesChanged;
		foreach (var node in _script.Nodes)
		{
			node.PropertyChanged -= OnNodePropertyChanged;
			node.Transitions.CollectionChanged -= OnTransitionsChanged;
			node.Parameters.CollectionChanged -= OnParametersChanged;
			foreach (var param in node.Parameters)
			{
				param.PropertyChanged -= OnParameterPropertyChanged;
			}
		}
	}

	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (FsmNodeModel node in e.NewItems)
			{
				EnsureDefinition(node);
				node.PropertyChanged += OnNodePropertyChanged;
				node.Transitions.CollectionChanged += OnTransitionsChanged;
				node.Parameters.CollectionChanged += OnParametersChanged;
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged += OnParameterPropertyChanged;
				}
			}
		}

		if (e.OldItems != null)
		{
			foreach (FsmNodeModel node in e.OldItems)
			{
				node.PropertyChanged -= OnNodePropertyChanged;
				node.Transitions.CollectionChanged -= OnTransitionsChanged;
				node.Parameters.CollectionChanged -= OnParametersChanged;
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged -= OnParameterPropertyChanged;
				}
				_selectedNodes.Remove(node);
			}
		}

		RefreshSignals();
		OnPropertyChanged(nameof(AllTransitions));
		AddTransitionCommand.NotifyCanExecuteChanged();
	}

	private void OnTransitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		RefreshSignals();
		OnPropertyChanged(nameof(AllTransitions));
	}

	private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (NodeParameterValue param in e.NewItems)
			{
				param.PropertyChanged += OnParameterPropertyChanged;
			}
		}

		if (e.OldItems != null)
		{
			foreach (NodeParameterValue param in e.OldItems)
			{
				param.PropertyChanged -= OnParameterPropertyChanged;
			}
		}

		RefreshSignals();
		RefreshParameterBindings();
	}

	private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.Equals(e.PropertyName, nameof(NodeParameterValue.RawValue), StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(e.PropertyName, nameof(NodeParameterValue.BoolValue), StringComparison.OrdinalIgnoreCase))
		{
			RefreshSignals();
		}
	}

	private void AddNode()
	{
		var defaultDefinition = _catalogService.GetDefaultDefinitionForType(Script.Nodes.Count == 0 ? FsmNodeType.Start : FsmNodeType.Action);
		AddNodeFromDefinition(defaultDefinition);
	}

	private void AddNodeFromDefinition(NodeDefinition? definition)
	{
		if (definition == null)
			return;

		var offset = Script.Nodes.Count * 30;
		var node = new FsmNodeModel
		{
			Title = definition.Title,
			Description = definition.ShortDescription,
			DefinitionId = definition.Id,
			DefinitionTitle = definition.Title,
			Type = ResolveNodeType(definition),
			X = 80 + offset,
			Y = 80 + offset,
			DwellMilliseconds = 250
		};

		if (Script.Nodes.Count == 0)
		{
			definition = _catalogService.GetDefaultDefinitionForType(FsmNodeType.Start);
			node.Type = FsmNodeType.Start;
			node.DefinitionId = definition.Id;
			node.DefinitionTitle = definition.Title;
		}

		EnsureNodeParameters(node, definition);

		Script.Nodes.Add(node);
		if (!Script.StartNodeId.HasValue || node.Type == FsmNodeType.Start)
		{
			Script.StartNodeId = node.Id;
		}

		SelectedNode = node;
		Status = $"Added {node.Title}";
	}

	private void RemoveSelectedNode()
	{
		if (SelectedNode == null)
			return;

		var targetId = SelectedNode.Id;

		foreach (var node in Script.Nodes.ToList())
		{
			var toRemove = node.Transitions.Where(t => t.FromNodeId == targetId || t.ToNodeId == targetId).ToList();
			foreach (var edge in toRemove)
			{
				node.Transitions.Remove(edge);
			}
		}

		Script.Nodes.Remove(SelectedNode);
		_selectedNodes.Remove(SelectedNode);

		if (Script.StartNodeId == targetId)
		{
			Script.StartNodeId = Script.Nodes.FirstOrDefault()?.Id;
		}

		SelectedNode = Script.Nodes.FirstOrDefault();
		SelectedTransition = null;

		Status = "Removed node";
	}

	private void AddTransition()
	{
		if (SelectedNode == null || Script.Nodes.Count < 2)
			return;

		var target = Script.Nodes.First(n => n.Id != SelectedNode.Id);

		var transition = new FsmTransitionModel
		{
			FromNodeId = SelectedNode.Id,
			ToNodeId = target.Id,
			Label = "Next",
			IsFallback = !SelectedNode.Transitions.Any()
		};

		SelectedNode.Transitions.Add(transition);
		SelectedTransition = transition;
		Status = "Added transition";
	}

	private void RemoveTransition(FsmTransitionModel? transition)
	{
		if (SelectedNode == null || transition == null)
			return;

		SelectedNode.Transitions.Remove(transition);
		if (ReferenceEquals(SelectedTransition, transition))
		{
			SelectedTransition = SelectedNode.Transitions.FirstOrDefault();
		}

		Status = "Removed transition";
	}

	private void SetSelectedAsStart()
	{
		if (SelectedNode == null)
			return;

		Script.StartNodeId = SelectedNode.Id;
		Status = $"{SelectedNode.Title} marked as start";
	}

	private void ClearTrail()
	{
		foreach (var node in Script.Nodes)
		{
			node.IsActive = false;
		}

		foreach (var transition in AllTransitions)
		{
			transition.IsActive = false;
		}
	}

	private void DeleteSelection()
	{
		if (SelectedTransition != null && SelectedNode != null)
		{
			RemoveTransition(SelectedTransition);
			return;
		}

		if (_selectedNodes.Count == 0)
			return;

		foreach (var node in _selectedNodes.ToList())
		{
			SelectedNode = node;
			RemoveSelectedNode();
		}
	}

	private void CreateBlankScript()
	{
		StopRun();
		Script = _scriptService.CreateNew("New FSM");
		CurrentFilePath = null;
		RefreshSignals();
	}

	private void LoadTemplate()
	{
		StopRun();
		Script = _scriptService.CreatePowerFishingTemplate();
		CurrentFilePath = null;
		RefreshSignals();
	}

	private async Task LoadScriptAsync()
	{
		var dialog = new OpenFileDialog
		{
			Title = "Open FSM script",
			Filter = "Orbit FSM (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
			InitialDirectory = _scriptService.ScriptsDirectory
		};

		if (dialog.ShowDialog() != true)
			return;

		var loaded = await _scriptService.LoadAsync(dialog.FileName);
		if (loaded == null)
		{
			MessageBox.Show("Unable to load selected script.", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		StopRun();
		Script = loaded;
		CurrentFilePath = dialog.FileName;
		RefreshSignals();
	}

	private async Task SaveScriptAsync()
	{
		if (string.IsNullOrWhiteSpace(CurrentFilePath))
		{
			var dialog = new SaveFileDialog
			{
				Title = "Save FSM script",
				Filter = "Orbit FSM (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
				FileName = $"{Script.Name}.orbitfsm.json",
				InitialDirectory = _scriptService.ScriptsDirectory
			};

			if (dialog.ShowDialog() != true)
				return;

			CurrentFilePath = dialog.FileName;
		}

		await _scriptService.SaveAsync(Script, CurrentFilePath);
		Status = $"Saved to {CurrentFilePath}";
	}

	private async Task ExportScriptAsync()
	{
		var dialog = new SaveFileDialog
		{
			Title = "Export / share FSM script",
			Filter = "Orbit FSM (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
			FileName = $"{Script.Name}.orbitfsm.json",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
		};

		if (dialog.ShowDialog() != true)
			return;

		await _scriptService.SaveAsync(Script, dialog.FileName);
		Status = $"Exported to {dialog.FileName}";
	}

	private async Task StartRunAsync(bool loop)
	{
		if (IsRunning)
			return;

		var signals = BuildSignalMap();
		if (signals == null)
			return;

		IsRunning = true;
		IsLooping = loop;
		Status = loop ? "Running (loop)" : "Running once";

		ClearTrail();

		_runCts = new CancellationTokenSource();
		try
		{
			await Task.Run(async () => await _engine.RunAsync(Script, signals, loop, _runCts.Token), _runCts.Token);
		}
		finally
		{
			IsRunning = false;
		}
	}

	private void StopRun()
	{
		if (!IsRunning)
			return;

		_runCts?.Cancel();
		IsRunning = false;
		Status = "Stopped";
	}

	private IReadOnlyDictionary<string, bool>? BuildSignalMap()
	{
		try
		{
			return Signals
				.Where(s => !string.IsNullOrWhiteSpace(s.Key))
				.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			Status = $"Signal error: {ex.Message}";
			return null;
		}
	}

	private void RefreshSignals()
	{
		var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var transition in Script.Nodes.SelectMany(n => n.Transitions).Where(t => t.HasCondition))
		{
			var key = transition.ConditionKey.Trim();
			if (!string.IsNullOrWhiteSpace(key))
				keys.Add(key);
		}

		foreach (var node in Script.Nodes)
		{
			foreach (var param in node.Parameters.Where(p => string.Equals(p.Key, "signal", StringComparison.OrdinalIgnoreCase)))
			{
				foreach (var value in param.SplitValues())
				{
					keys.Add(value);
				}

				if (!param.AllowMultiple && !string.IsNullOrWhiteSpace(param.RawValue))
				{
					keys.Add(param.RawValue.Trim());
				}
			}
		}

		// Remove stale signals
		for (var i = Signals.Count - 1; i >= 0; i--)
		{
			if (!keys.Contains(Signals[i].Key, StringComparer.OrdinalIgnoreCase))
			{
				Signals.RemoveAt(i);
			}
		}

		// Add missing signals (default false)
		foreach (var key in keys)
		{
			if (!Signals.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
			{
				Signals.Add(new FsmRuntimeSignal { Key = key, Value = false });
			}
		}

		SignalSuggestions.Clear();
		foreach (var key in keys.OrderBy(k => k))
		{
			SignalSuggestions.Add(key);
		}

		OnPropertyChanged(nameof(Signals));
		OnPropertyChanged(nameof(SignalSuggestions));
	}

	private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FsmNodeModel.X) || e.PropertyName == nameof(FsmNodeModel.Y))
		{
			OnPropertyChanged(nameof(AllTransitions));
		}
	}

	private void EnsureDefinition(FsmNodeModel node)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId) ?? _catalogService.GetDefaultDefinitionForType(node.Type);
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		EnsureNodeParameters(node, definition);
	}

	private static FsmNodeType ResolveNodeType(NodeDefinition definition)
	{
		if (string.Equals(definition.Id, NodeCatalogDefaults.StartId, StringComparison.OrdinalIgnoreCase))
			return FsmNodeType.Start;
		if (string.Equals(definition.Id, NodeCatalogDefaults.TerminalId, StringComparison.OrdinalIgnoreCase))
			return FsmNodeType.Terminal;
		if (string.Equals(definition.CategoryId, "conditions", StringComparison.OrdinalIgnoreCase))
			return FsmNodeType.Condition;

		return FsmNodeType.Action;
	}

	private void EnsureNodeParameters(FsmNodeModel node, NodeDefinition definition)
	{
		if (definition.Parameters == null)
			return;

		foreach (var parameter in definition.Parameters)
		{
			var existing = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
			if (existing == null)
			{
				node.Parameters.Add(new NodeParameterValue
				{
					Key = parameter.Key,
					Type = parameter.Type,
					AllowMultiple = parameter.AllowMultiple
				});
			}
			else
			{
				existing.Type = parameter.Type;
				existing.AllowMultiple = parameter.AllowMultiple;
			}
		}

		for (var i = node.Parameters.Count - 1; i >= 0; i--)
		{
			if (definition.Parameters.All(p => !string.Equals(p.Key, node.Parameters[i].Key, StringComparison.OrdinalIgnoreCase)))
			{
				node.Parameters.RemoveAt(i);
			}
		}
	}

	private void ApplyDefinitionToNode(FsmNodeModel node, NodeDefinition definition)
	{
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		node.Type = ResolveNodeType(definition);
		EnsureNodeParameters(node, definition);
		RefreshSignals();
	}

	private void RefreshParameterBindings()
	{
		ParameterBindings.Clear();
		if (SelectedNode == null)
			return;

		var definition = SelectedNodeDefinition ?? _catalogService.GetDefinition(SelectedNode.DefinitionId);
		if (definition?.Parameters == null)
			return;

		EnsureNodeParameters(SelectedNode, definition);

		foreach (var parameter in definition.Parameters)
		{
			var value = SelectedNode.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
			if (value != null)
			{
				ParameterBindings.Add(new NodeParamBinding(parameter, value));
			}
		}

		OnPropertyChanged(nameof(ParameterBindings));
	}

	private void AddListEntry(NodeParamBinding? binding)
	{
		if (binding == null)
			return;

		if (!string.IsNullOrWhiteSpace(binding.Value.RawValue))
		{
			binding.Value.RawValue += Environment.NewLine;
		}
	}

	private void RemoveListEntry((NodeParamBinding binding, string value)? args)
	{
		if (args == null)
			return;

		var (binding, value) = args.Value;
		var filtered = binding.Value.SplitValues()
			.Where(v => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
			.ToList();
		binding.Value.RawValue = string.Join(Environment.NewLine, filtered);
	}

	private void OnNodeEntered(object? sender, FsmNodeModel node)
	{
		RunOnUi(() =>
		{
			node.IsActive = true;
			Status = $"Entered {node.Title}";
		});
	}

	private void OnTransitionTaken(object? sender, FsmTransitionModel transition)
	{
		RunOnUi(() =>
		{
			transition.IsActive = true;
		});
	}

	private void OnEngineCompleted(object? sender, EventArgs e)
	{
		RunOnUi(() =>
		{
			IsRunning = false;
			Status = "Cycle complete";
		});
	}

	private void OnEngineFaulted(object? sender, Exception ex)
	{
		RunOnUi(() =>
		{
			IsRunning = false;
			Status = $"Error: {ex.Message}";
			MessageBox.Show($"FSM engine failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
		});
	}

	private static void RunOnUi(Action action)
	{
		if (Application.Current?.Dispatcher != null)
		{
			if (Application.Current.Dispatcher.CheckAccess())
			{
				action();
			}
			else
			{
				Application.Current.Dispatcher.Invoke(action);
			}
		}
		else
		{
			action();
		}
	}

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
