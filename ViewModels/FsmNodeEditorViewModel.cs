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
	private CancellationTokenSource? _runCts;
	private FsmScriptModel _script;
	private FsmNodeModel? _selectedNode;
	private FsmTransitionModel? _selectedTransition;
	private bool _isRunning;
	private bool _isLooping = true;
	private string _status = "Idle";
	private string? _currentFilePath;

	public FsmNodeEditorViewModel()
		: this(new FsmScriptService(), new FsmExecutionEngine())
	{
	}

	public FsmNodeEditorViewModel(FsmScriptService scriptService, FsmExecutionEngine engine)
	{
		_scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));

		AddNodeCommand = new RelayCommand(AddNode);
		RemoveNodeCommand = new RelayCommand(RemoveSelectedNode, () => SelectedNode != null);
		AddTransitionCommand = new RelayCommand(AddTransition, () => SelectedNode != null && Script.Nodes.Count > 1);
		RemoveTransitionCommand = new RelayCommand<FsmTransitionModel?>(RemoveTransition, _ => SelectedNode != null);
		SetAsStartCommand = new RelayCommand(SetSelectedAsStart, () => SelectedNode != null);
		ClearTrailCommand = new RelayCommand(ClearTrail);

		NewScriptCommand = new RelayCommand(CreateBlankScript);
		LoadScriptCommand = new AsyncRelayCommand(LoadScriptAsync);
		SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
		ExportScriptCommand = new AsyncRelayCommand(ExportScriptAsync);
		LoadTemplateCommand = new RelayCommand(LoadTemplate);

		StartCommand = new AsyncRelayCommand(async () => await StartRunAsync(_isLooping));
		StepCommand = new AsyncRelayCommand(async () => await StartRunAsync(false));
		StopCommand = new RelayCommand(StopRun, () => IsRunning);

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

			if (_selectedNode != null)
				_selectedNode.IsSelected = false;

			_selectedNode = value;

			if (_selectedNode != null)
				_selectedNode.IsSelected = true;

			OnPropertyChanged();
			OnPropertyChanged(nameof(CanEditNode));
			OnPropertyChanged(nameof(CanEditTransitions));
			RemoveNodeCommand.NotifyCanExecuteChanged();
			AddTransitionCommand.NotifyCanExecuteChanged();
			SetAsStartCommand.NotifyCanExecuteChanged();
			RemoveTransitionCommand.NotifyCanExecuteChanged();
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

	public bool CanEditNode => SelectedNode != null;

	public bool CanEditTransitions => SelectedNode != null && Script.Nodes.Count > 1;

	public IRelayCommand AddNodeCommand { get; }
	public IRelayCommand RemoveNodeCommand { get; }
	public IRelayCommand AddTransitionCommand { get; }
	public IRelayCommand<FsmTransitionModel?> RemoveTransitionCommand { get; }
	public IRelayCommand SetAsStartCommand { get; }
	public IRelayCommand ClearTrailCommand { get; }

	public IRelayCommand NewScriptCommand { get; }
	public IAsyncRelayCommand LoadScriptCommand { get; }
	public IAsyncRelayCommand SaveScriptCommand { get; }
	public IAsyncRelayCommand ExportScriptCommand { get; }
	public IRelayCommand LoadTemplateCommand { get; }

	public IAsyncRelayCommand StartCommand { get; }
	public IAsyncRelayCommand StepCommand { get; }
	public IRelayCommand StopCommand { get; }

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
			node.PropertyChanged += OnNodePropertyChanged;
			node.Transitions.CollectionChanged += OnTransitionsChanged;
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
		}
	}

	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (FsmNodeModel node in e.NewItems)
			{
				node.PropertyChanged += OnNodePropertyChanged;
				node.Transitions.CollectionChanged += OnTransitionsChanged;
			}
		}

		if (e.OldItems != null)
		{
			foreach (FsmNodeModel node in e.OldItems)
			{
				node.PropertyChanged -= OnNodePropertyChanged;
				node.Transitions.CollectionChanged -= OnTransitionsChanged;
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

	private void AddNode()
	{
		var offset = Script.Nodes.Count * 30;
		var node = new FsmNodeModel
		{
			Title = $"State {Script.Nodes.Count + 1}",
			Description = "Describe what should happen here.",
			Type = Script.Nodes.Count == 0 ? FsmNodeType.Start : FsmNodeType.Action,
			X = 80 + offset,
			Y = 80 + offset,
			DwellMilliseconds = 250
		};

		Script.Nodes.Add(node);
		if (!Script.StartNodeId.HasValue)
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
		var keys = Script.Nodes
			.SelectMany(n => n.Transitions)
			.Where(t => t.HasCondition)
			.Select(t => t.ConditionKey.Trim())
			.Where(k => !string.IsNullOrWhiteSpace(k))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

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

		OnPropertyChanged(nameof(Signals));
	}

	private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FsmNodeModel.X) || e.PropertyName == nameof(FsmNodeModel.Y))
		{
			OnPropertyChanged(nameof(AllTransitions));
		}
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
