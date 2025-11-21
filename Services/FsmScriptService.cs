using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Orbit.Models;

namespace Orbit.Services;

/// <summary>
/// Handles persistence and seeding for FSM-based automation scripts.
/// Scripts are stored as JSON files so they can be shared easily.
/// </summary>
public class FsmScriptService
{
	private readonly string _scriptsDirectory;
	private readonly NodeCatalogService _catalogService;

	public FsmScriptService()
		: this(new NodeCatalogService())
	{
	}

	public FsmScriptService(NodeCatalogService catalogService)
	{
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
		var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		_scriptsDirectory = Path.Combine(roaming, "Orbit", "fsm_scripts");
	}

	public string ScriptsDirectory => _scriptsDirectory;

	/// <summary>
	/// Saves a script to disk (using the provided path or a sanitized name in the default folder).
	/// </summary>
	public async Task<string> SaveAsync(FsmScriptModel script, string? path = null, CancellationToken cancellationToken = default)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));

		Directory.CreateDirectory(_scriptsDirectory);

		var fileName = path ?? Path.Combine(_scriptsDirectory, $"{SanitizeFileName(script.Name)}.orbitfsm.json");
		script.UpdatedAt = DateTime.UtcNow;

		var json = JsonConvert.SerializeObject(script, Formatting.Indented);
		await File.WriteAllTextAsync(fileName, json, cancellationToken);

		return fileName;
	}

	/// <summary>
	/// Loads a script from disk, returning null if parsing fails.
	/// </summary>
	public async Task<FsmScriptModel?> LoadAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			return null;

		try
		{
			var json = await File.ReadAllTextAsync(path, cancellationToken);
			var model = JsonConvert.DeserializeObject<FsmScriptModel>(json);
			if (model?.Nodes == null)
			{
				model!.Nodes = new ObservableCollection<FsmNodeModel>();
			}

			foreach (var node in model.Nodes)
			{
				node.Transitions ??= new ObservableCollection<FsmTransitionModel>();
				node.Parameters ??= new ObservableCollection<NodeParameterValue>();
				EnsureNodeDefinition(node);
			}

			if (model.SchemaVersion <= 1)
			{
				MigrateLegacyParameters(model);
			}

			model.SchemaVersion = 2;

			return model;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Enumerates saved scripts from the default folder.
	/// </summary>
	public async Task<IReadOnlyList<FsmScriptModel>> LoadAllAsync(CancellationToken cancellationToken = default)
	{
		var results = new List<FsmScriptModel>();

		if (!Directory.Exists(_scriptsDirectory))
			return results;

		foreach (var file in Directory.EnumerateFiles(_scriptsDirectory, "*.orbitfsm.json", SearchOption.TopDirectoryOnly))
		{
			var model = await LoadAsync(file, cancellationToken);
			if (model != null)
			{
				results.Add(model);
			}
		}

		return results;
	}

	public FsmScriptModel CreateNew(string name = "New Machine")
	{
		var startDefinition = _catalogService.GetDefaultDefinitionForType(FsmNodeType.Start);
		var node = new FsmNodeModel
		{
			Title = startDefinition.Title,
			Type = FsmNodeType.Start,
			Description = startDefinition.ShortDescription,
			DefinitionId = startDefinition.Id,
			DefinitionTitle = startDefinition.Title,
			DwellMilliseconds = 200
		};
		EnsureNodeParameters(node, startDefinition);

		return new FsmScriptModel
		{
			Name = name,
			Description = "Blank machine",
			Author = Environment.UserName,
			StartNodeId = node.Id,
			SchemaVersion = 2,
			Nodes = new System.Collections.ObjectModel.ObservableCollection<FsmNodeModel>
			{
				node
			}
		};
	}

	/// <summary>
	/// Builds a shareable "power fishing" machine with the states described in the feature request.
	/// </summary>
	public FsmScriptModel CreatePowerFishingTemplate()
	{
		var startDefinition = _catalogService.GetDefinition(NodeCatalogDefaults.BooleanConditionId)!;
		var start = new FsmNodeModel
		{
			Title = "Check inventory",
			Description = "Check if the bag is full before doing anything else.",
			Type = FsmNodeType.Condition,
			DefinitionId = startDefinition.Id,
			DefinitionTitle = startDefinition.Title,
			X = 120,
			Y = 80,
			ActionText = "Evaluate inventory status",
			DwellMilliseconds = 150
		};
		EnsureNodeParameters(start, startDefinition);
		SetParameterValue(start, "signal", "inventoryFull");
		SetParameterBool(start, "expected", true);

		var actionDefinition = _catalogService.GetDefinition(NodeCatalogDefaults.GenericActionId)!;
		var dropInventory = new FsmNodeModel
		{
			Title = "Drop inventory",
			Description = "Drop all fish to clear space.",
			Type = FsmNodeType.Action,
			DefinitionId = actionDefinition.Id,
			DefinitionTitle = actionDefinition.Title,
			X = 120,
			Y = 260,
			ActionText = "Drop raw fish until empty",
			DwellMilliseconds = 400
		};
		EnsureNodeParameters(dropInventory, actionDefinition);
		SetParameterValue(dropInventory, "action", "Drop all raw fish");

		var lookForSpot = new FsmNodeModel
		{
			Title = "Look for spot",
			Description = "Scan nearby area for a fishing spot.",
			Type = FsmNodeType.Condition,
			DefinitionId = startDefinition.Id,
			DefinitionTitle = startDefinition.Title,
			X = 420,
			Y = 80,
			ActionText = "Scan environment",
			DwellMilliseconds = 250
		};
		EnsureNodeParameters(lookForSpot, startDefinition);
		SetParameterValue(lookForSpot, "signal", "hasNearbySpot");
		SetParameterBool(lookForSpot, "expected", true);

		var walkDefinition = _catalogService.GetDefinition("traversal.walk")!;
		var moveToSpot = new FsmNodeModel
		{
			Title = "Move to spot",
			Description = "User-provided pathing to the latest known spot.",
			Type = FsmNodeType.Action,
			DefinitionId = walkDefinition.Id,
			DefinitionTitle = walkDefinition.Title,
			X = 420,
			Y = 260,
			ActionText = "Walk toward configured fishing tile",
			DwellMilliseconds = 450
		};
		EnsureNodeParameters(moveToSpot, walkDefinition);
		SetParameterValue(moveToSpot, "target", "3220,3150");

		var interactDefinition = _catalogService.GetDefinition("actions.interaction")!;
		var fish = new FsmNodeModel
		{
			Title = "Fish",
			Description = "Interact with the spot and continue until bag is full.",
			Type = FsmNodeType.Action,
			DefinitionId = interactDefinition.Id,
			DefinitionTitle = interactDefinition.Title,
			X = 700,
			Y = 160,
			ActionText = "Cast line / interact and wait",
			DwellMilliseconds = 500
		};
		EnsureNodeParameters(fish, interactDefinition);
		SetParameterValue(fish, "target", "Fishing spot");
		SetParameterValue(fish, "option", "Interact");

		var nodes = new[]
		{
			start,
			dropInventory,
			lookForSpot,
			moveToSpot,
			fish
		};

		var transitions = new[]
		{
			new FsmTransitionModel
			{
				FromNodeId = start.Id,
				ToNodeId = dropInventory.Id,
				Label = "Inventory full",
				ConditionKey = "inventoryFull",
				ExpectedValue = true
			},
			new FsmTransitionModel
			{
				FromNodeId = start.Id,
				ToNodeId = lookForSpot.Id,
				Label = "Space available",
				IsFallback = true
			},
			new FsmTransitionModel
			{
				FromNodeId = dropInventory.Id,
				ToNodeId = lookForSpot.Id,
				Label = "Cleared",
				IsFallback = true
			},
			new FsmTransitionModel
			{
				FromNodeId = lookForSpot.Id,
				ToNodeId = fish.Id,
				Label = "Spot nearby",
				ConditionKey = "hasNearbySpot",
				ExpectedValue = true
			},
			new FsmTransitionModel
			{
				FromNodeId = lookForSpot.Id,
				ToNodeId = moveToSpot.Id,
				Label = "No spot found",
				IsFallback = true
			},
			new FsmTransitionModel
			{
				FromNodeId = moveToSpot.Id,
				ToNodeId = lookForSpot.Id,
				Label = "Re-scan",
				IsFallback = true
			},
			new FsmTransitionModel
			{
				FromNodeId = fish.Id,
				ToNodeId = start.Id,
				Label = "Bag filled",
				ConditionKey = "inventoryFull",
				ExpectedValue = true
			},
			new FsmTransitionModel
			{
				FromNodeId = fish.Id,
				ToNodeId = fish.Id,
				Label = "Continue fishing",
				IsFallback = true
			}
		};

		foreach (var transition in transitions)
		{
			var fromNode = nodes.First(n => n.Id == transition.FromNodeId);
			fromNode.Transitions.Add(transition);
		}

		return new FsmScriptModel
		{
			Name = "Power fishing (template)",
			Description = "Minimal FSM to power fish: clear bag, find a spot, move, and fish in a loop.",
			Author = "Orbit",
			StartNodeId = start.Id,
			Nodes = new System.Collections.ObjectModel.ObservableCollection<FsmNodeModel>(nodes),
			SchemaVersion = 2,
			UpdatedAt = DateTime.UtcNow
		};
	}

	private static string SanitizeFileName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "untitled";

		var cleaned = Regex.Replace(name, @"[^\w\-.]+", "_");
		return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
	}

	private void EnsureNodeDefinition(FsmNodeModel node)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId) ?? _catalogService.GetDefaultDefinitionForType(node.Type);
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		EnsureNodeParameters(node, definition);
	}

	private static void EnsureNodeParameters(FsmNodeModel node, NodeDefinition definition)
	{
		if (definition.Parameters == null || definition.Parameters.Count == 0)
			return;

		foreach (var parameter in definition.Parameters)
		{
			if (node.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase)) != null)
				continue;

			node.Parameters.Add(new NodeParameterValue
			{
				Key = parameter.Key,
				Type = parameter.Type,
				AllowMultiple = parameter.AllowMultiple
			});
		}
	}

	private static void SetParameterValue(FsmNodeModel node, string key, string value)
	{
		var param = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (param == null) return;

		param.RawValue = value;
	}

	private static void SetParameterBool(FsmNodeModel node, string key, bool value)
	{
		var param = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (param == null) return;

		param.BoolValue = value;
	}

	private void MigrateLegacyParameters(FsmScriptModel model)
	{
		foreach (var node in model.Nodes)
		{
			if (node.Parameters.Count > 0)
				continue;

			var definition = _catalogService.GetDefaultDefinitionForType(node.Type);
			node.DefinitionId = definition.Id;
			node.DefinitionTitle = definition.Title;
			EnsureNodeParameters(node, definition);

			if (node.Type == FsmNodeType.Action && node.Parameters.Count > 0)
			{
				node.Parameters[0].RawValue = node.ActionText ?? node.Description;
			}
			else if (node.Type == FsmNodeType.Condition)
			{
				SetParameterValue(node, "signal", node.Transitions.FirstOrDefault(t => t.HasCondition)?.ConditionKey ?? string.Empty);
				SetParameterBool(node, "expected", true);
			}
		}
	}
}
