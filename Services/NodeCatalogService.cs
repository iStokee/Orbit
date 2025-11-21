using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

/// <summary>
/// Provides seeded node categories/definitions and lookup helpers for the visual node editor.
/// </summary>
public class NodeCatalogService
{
	private readonly ReadOnlyCollection<NodeCategory> _categories;
	private readonly ReadOnlyCollection<NodeDefinition> _definitions;
	private readonly IReadOnlyDictionary<string, NodeDefinition> _definitionById;

	public NodeCatalogService()
	{
		var categories = new[]
		{
			new NodeCategory { Id = "all", Title = "All", Description = "All node types", Icon = "Apps", Order = -1, Slug = "all" },
			new NodeCategory { Id = "control", Title = "Control", Description = "Start/terminal helpers", Icon = "Rocket", Order = 0, Slug = "control" },
			new NodeCategory { Id = "conditions", Title = "Conditions", Description = "Checks that gate transitions", Icon = "HelpCircle", Order = 1, Slug = "conditions" },
			new NodeCategory { Id = "actions", Title = "Actions", Description = "Game-facing actions (interaction, traversal, shop)", Icon = "Play", Order = 2, Slug = "actions" },
			new NodeCategory { Id = "traversal", Title = "Traversal", Description = "Movement and world hops", Icon = "Map", Order = 3, Slug = "traversal" },
			new NodeCategory { Id = "inventory", Title = "Inventory", Description = "Bag logic, item usage, gear", Icon = "BagPersonal", Order = 4, Slug = "inventory" },
			new NodeCategory { Id = "bank", Title = "Bank", Description = "Banking operations and presets", Icon = "Safe", Order = 5, Slug = "bank" },
			new NodeCategory { Id = "npcs", Title = "NPCs", Description = "Interact, attack, query NPCs", Icon = "AccountGroup", Order = 6, Slug = "npcs" },
			new NodeCategory { Id = "objects", Title = "Objects", Description = "Interact with world objects", Icon = "CubeOutline", Order = 7, Slug = "objects" },
			new NodeCategory { Id = "loot", Title = "Loot", Description = "Ground item pickup / filters", Icon = "TreasureChest", Order = 8, Slug = "loot" },
			new NodeCategory { Id = "input", Title = "Input", Description = "Keyboard and mouse dispatch", Icon = "Keyboard", Order = 9, Slug = "input" },
			new NodeCategory { Id = "skills", Title = "Skills", Description = "Skill checks and thresholds", Icon = "ChartLine", Order = 10, Slug = "skills" },
			new NodeCategory { Id = "trade", Title = "Trade/UI", Description = "Trade window and UI helpers", Icon = "Handshake", Order = 11, Slug = "trade" },
			new NodeCategory { Id = "misc", Title = "Misc", Description = "Utility nodes", Icon = "DotsHorizontal", Order = 12, Slug = "misc" }
		};

		var definitions = new List<NodeDefinition>
		{
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.StartId,
				Title = "Start",
				ShortDescription = "Entry point to mark as the graph start.",
				Icon = "Flag",
				CategoryId = "control",
				Order = 0,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.TerminalId,
				Title = "End / Terminal",
				ShortDescription = "Terminal node that stops the runner.",
				Icon = "Stop",
				CategoryId = "control",
				Order = 1,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.BooleanConditionId,
				Title = "Boolean Condition",
				ShortDescription = "Evaluates a boolean signal and branches flows.",
				Icon = "Help",
				CategoryId = "conditions",
				Order = 0,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam
					{
						Key = "signal",
						Label = "Signal key",
						Type = NodeParamType.String,
						IsRequired = true,
						Placeholder = "inventoryFull / hasNearbySpot / custom"
					},
					new NodeParam
					{
						Key = "expected",
						Label = "Expect true?",
						Type = NodeParamType.Bool,
						IsRequired = true
					}
				}
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.GenericActionId,
				Title = "Generic Action",
				ShortDescription = "Freeform action with optional dwell delay.",
				Icon = "GestureTapButton",
				CategoryId = "actions",
				Order = 0,
				Parameters = new []
				{
					new NodeParam
					{
						Key = "action",
						Label = "Action / Notes",
						Type = NodeParamType.String,
						Placeholder = "Click fishing spot, drop inventory, etc."
					}
				}
			},
			new NodeDefinition
			{
				Id = "actions.interaction",
				Title = "Interaction",
				ShortDescription = "Interact with a target (entity/object/NPC) with a menu option.",
				Icon = "CursorDefaultClick",
				CategoryId = "actions",
				Order = 1,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "target", Label = "Target name / query", Type = NodeParamType.String, IsRequired = true, HasQuery = true, AllowPartial = true, Placeholder = "Bank booth, Mud rune altar" },
					new NodeParam { Key = "option", Label = "Interaction option", Type = NodeParamType.Enum, EnumValues = new [] { "Use", "Talk-to", "Pickpocket", "Trade", "Attack" }, Placeholder = "Talk-to / Trade / Use" },
					new NodeParam { Key = "acceptPartial", Label = "Allow partial match", Type = NodeParamType.Bool, AllowPartial = true }
				}
			},
			new NodeDefinition
			{
				Id = "actions.shop",
				Title = "Shop",
				ShortDescription = "Buy or sell items via shop interface.",
				Icon = "Store",
				CategoryId = "actions",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "mode", Label = "Mode", Type = NodeParamType.Enum, EnumValues = new [] { "Buy", "Sell" }, IsRequired = true },
					new NodeParam { Key = "items", Label = "Items", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Item name(s) separated by newlines" },
					new NodeParam { Key = "quantity", Label = "Quantity", Type = NodeParamType.Number, Placeholder = "0 = all" }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.worldhop",
				Title = "World hop",
				ShortDescription = "Switch to a target world, optionally filtered by region/type.",
				Icon = "Earth",
				CategoryId = "traversal",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "world", Label = "World", Type = NodeParamType.Number, Placeholder = "E.g., 1-140" },
					new NodeParam { Key = "preferMembers", Label = "Members only", Type = NodeParamType.Bool },
					new NodeParam { Key = "region", Label = "Region", Type = NodeParamType.Enum, EnumValues = new [] { "US", "EU", "AUS" } }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.walk",
				Title = "Walk / traverse path",
				ShortDescription = "Traverse to coordinate(s) or area.",
				Icon = "MapMarkerPath",
				CategoryId = "traversal",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "target", Label = "Target tile(s)", Type = NodeParamType.Coordinate, AllowMultiple = true, Placeholder = "x,y or x,y,z" },
					new NodeParam { Key = "area", Label = "Target area", Type = NodeParamType.Area, AllowPartial = true, Placeholder = "Named area or polygon list" },
					new NodeParam { Key = "stopShort", Label = "Stop short (tiles)", Type = NodeParamType.Number, Placeholder = "2" },
					new NodeParam { Key = "timeoutMs", Label = "Timeout per segment (ms)", Type = NodeParamType.Number, Placeholder = "8000" },
					new NodeParam { Key = "jitter", Label = "Jitter (tiles)", Type = NodeParamType.Number, Placeholder = "1" }
				}
			},
			new NodeDefinition
			{
				Id = "keyboard.send",
				Title = "Keyboard macro",
				ShortDescription = "Send keys or text to the game client.",
				Icon = "KeyboardOutline",
				CategoryId = "input",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "keys", Label = "Keys / text", Type = NodeParamType.String, IsRequired = true, Placeholder = "e.g., Ctrl+1 or ::home" },
					new NodeParam { Key = "delayMs", Label = "Delay after send (ms)", Type = NodeParamType.Number }
				}
			},
			new NodeDefinition
			{
				Id = "input.click",
				Title = "Mouse click",
				ShortDescription = "Click at a screen coordinate or center of client.",
				Icon = "CursorDefault",
				CategoryId = "input",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "x", Label = "X", Type = NodeParamType.Number, Placeholder = "Screen X (optional)" },
					new NodeParam { Key = "y", Label = "Y", Type = NodeParamType.Number, Placeholder = "Screen Y (optional)" },
					new NodeParam { Key = "button", Label = "Button", Type = NodeParamType.Enum, EnumValues = new [] { "Left", "Right" }, Placeholder = "Left" }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.contains",
				Title = "Inventory contains",
				ShortDescription = "Check if inventory has any of the specified items.",
				Icon = "BagPersonal",
				CategoryId = "inventory",
				Order = 0,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "ids", Label = "Item id(s)", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "7946, 379" },
					new NodeParam { Key = "names", Label = "Item name(s)", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Lobster\nNature rune" },
					new NodeParam { Key = "expected", Label = "Expect present?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.count",
				Title = "Inventory count",
				ShortDescription = "Emit or guard on count of an item by id or name.",
				Icon = "Counter",
				CategoryId = "inventory",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "id", Label = "Item id", Type = NodeParamType.Number, Placeholder = "Optional if using name" },
					new NodeParam { Key = "name", Label = "Item name", Type = NodeParamType.String, Placeholder = "Shark" },
					new NodeParam { Key = "min", Label = "Min count", Type = NodeParamType.Number },
					new NodeParam { Key = "max", Label = "Max count", Type = NodeParamType.Number }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.use",
				Title = "Use item",
				ShortDescription = "Use an item (by id or name).",
				Icon = "Hand",
				CategoryId = "inventory",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "id", Label = "Item id", Type = NodeParamType.Number },
					new NodeParam { Key = "name", Label = "Item name", Type = NodeParamType.String },
					new NodeParam { Key = "action", Label = "Action", Type = NodeParamType.Enum, EnumValues = new [] { "Use", "Eat/Drink", "Drop", "Equip" } }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.useOn",
				Title = "Use item on item",
				ShortDescription = "Use one inventory item on another.",
				Icon = "LinkVariant",
				CategoryId = "inventory",
				Order = 3,
				Parameters = new []
				{
					new NodeParam { Key = "from", Label = "Item (use)", Type = NodeParamType.String, IsRequired = true, Placeholder = "Needle / 1733" },
					new NodeParam { Key = "to", Label = "Item (target)", Type = NodeParamType.String, IsRequired = true, Placeholder = "Thread / 1734" }
				}
			},
			new NodeDefinition
			{
				Id = "bank.open",
				Title = "Open bank",
				ShortDescription = "Open nearest bank booth/chest.",
				Icon = "Safe",
				CategoryId = "bank",
				Order = 0,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = "bank.depositAll",
				Title = "Deposit all",
				ShortDescription = "Deposit everything (optionally except specific items).",
				Icon = "PackageDown",
				CategoryId = "bank",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "exceptIds", Label = "Keep item ids", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Notepaper id, food id" },
					new NodeParam { Key = "exceptNames", Label = "Keep item names", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Shark, Coins" }
				}
			},
			new NodeDefinition
			{
				Id = "bank.withdraw",
				Title = "Withdraw",
				ShortDescription = "Withdraw item(s) by id or name.",
				Icon = "PackageUp",
				CategoryId = "bank",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "id", Label = "Item id", Type = NodeParamType.Number },
					new NodeParam { Key = "name", Label = "Item name", Type = NodeParamType.String },
					new NodeParam { Key = "amount", Label = "Amount", Type = NodeParamType.Number, Placeholder = "0 = all" }
				}
			},
			new NodeDefinition
			{
				Id = "bank.close",
				Title = "Close bank",
				ShortDescription = "Close the bank interface.",
				Icon = "CloseCircle",
				CategoryId = "bank",
				Order = 3,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = "npcs.interact",
				Title = "NPC interact",
				ShortDescription = "Interact with an NPC by name/query and option.",
				Icon = "AccountVoice",
				CategoryId = "npcs",
				Order = 0,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "target", Label = "NPC name", Type = NodeParamType.String, HasQuery = true, IsRequired = true, Placeholder = "Banker / Fisherman" },
					new NodeParam { Key = "option", Label = "Option", Type = NodeParamType.Enum, EnumValues = new [] { "Talk-to", "Trade", "Pickpocket", "Attack" }, Placeholder = "Talk-to" },
					new NodeParam { Key = "allowPartial", Label = "Allow partial", Type = NodeParamType.Bool }
				}
			},
			new NodeDefinition
			{
				Id = "npcs.attack",
				Title = "Attack NPC",
				ShortDescription = "Attack an NPC by name or id.",
				Icon = "Sword",
				CategoryId = "npcs",
				Order = 1,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "name", Label = "NPC name", Type = NodeParamType.String, HasQuery = true, Placeholder = "Green dragon" },
					new NodeParam { Key = "id", Label = "NPC id", Type = NodeParamType.Number, Placeholder = "Optional" },
					new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, Placeholder = "Optional range" }
				}
			},
			new NodeDefinition
			{
				Id = "objects.interact",
				Title = "Object interact",
				ShortDescription = "Interact with a world object by name/id.",
				Icon = "HammerWrench",
				CategoryId = "objects",
				Order = 0,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "name", Label = "Object name", Type = NodeParamType.String, HasQuery = true, Placeholder = "Bank chest / Gate" },
					new NodeParam { Key = "id", Label = "Object id", Type = NodeParamType.Number },
					new NodeParam { Key = "option", Label = "Option", Type = NodeParamType.String, Placeholder = "Open / Climb / Use" }
				}
			},
			new NodeDefinition
			{
				Id = "objects.exists",
				Title = "Object exists?",
				ShortDescription = "Check if an object is nearby (by id/name).",
				Icon = "Magnify",
				CategoryId = "objects",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "name", Label = "Object name", Type = NodeParamType.String, Placeholder = "Gate" },
					new NodeParam { Key = "id", Label = "Object id", Type = NodeParamType.Number },
					new NodeParam { Key = "expected", Label = "Should exist?", Type = NodeParamType.Bool }
				}
			},
			new NodeDefinition
			{
				Id = "loot.pickup",
				Title = "Loot ground items",
				ShortDescription = "Pick up ground items by name/id filter.",
				Icon = "TreasureChest",
				CategoryId = "loot",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "names", Label = "Item names", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Bones\nGreen dragonhide" },
					new NodeParam { Key = "ids", Label = "Item ids", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "526\n1753" },
					new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, Placeholder = "Optional range" }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.inCombat",
				Title = "In combat?",
				ShortDescription = "Check the combat state.",
				Icon = "ShieldCheck",
				CategoryId = "conditions",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "expected", Label = "Expect in combat?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.inventoryFull",
				Title = "Inventory full?",
				ShortDescription = "Check whether the inventory is full.",
				Icon = "BagChecked",
				CategoryId = "conditions",
				Order = 3,
				Parameters = new []
				{
					new NodeParam { Key = "expected", Label = "Expect full?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.teleportLodestone",
				Title = "Teleport (lodestone)",
				ShortDescription = "Teleport via lodestone network.",
				Icon = "MapMarker",
				CategoryId = "traversal",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "destination", Label = "Destination", Type = NodeParamType.Enum, EnumValues = new [] { "Edgeville", "Lumbridge", "Varrock", "Falador", "Port Sarim", "Draynor", "Burthorpe" }, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.wait",
				Title = "Wait / pause",
				ShortDescription = "Sleep for a duration (ms).",
				Icon = "TimerSand",
				CategoryId = "traversal",
				Order = 3,
				Parameters = new []
				{
					new NodeParam { Key = "delayMs", Label = "Delay (ms)", Type = NodeParamType.Number, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "skills.requireLevel",
				Title = "Require skill level",
				ShortDescription = "Guard based on a minimum skill level.",
				Icon = "ChartLine",
				CategoryId = "skills",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "skill", Label = "Skill", Type = NodeParamType.Enum, EnumValues = new [] { "Attack", "Strength", "Defence", "Magic", "Ranged", "Prayer", "Mining", "Smithing", "Fishing", "Cooking", "Crafting", "Fletching", "Woodcutting", "Agility", "Slayer" }, IsRequired = true },
					new NodeParam { Key = "level", Label = "Min level", Type = NodeParamType.Number, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "trade.accept",
				Title = "Trade accept / confirm",
				ShortDescription = "Accept or confirm an open trade window.",
				Icon = "Handshake",
				CategoryId = "trade",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "stage", Label = "Stage", Type = NodeParamType.Enum, EnumValues = new [] { "First", "Second" }, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "actions.setSignal",
				Title = "Set signal",
				ShortDescription = "Set or clear a runtime signal key.",
				Icon = "ToggleSwitch",
				CategoryId = "misc",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "signal", Label = "Signal key", Type = NodeParamType.String, IsRequired = true, Placeholder = "inventoryFull" },
					new NodeParam { Key = "value", Label = "Value", Type = NodeParamType.Bool, IsRequired = true }
				}
			}
		};

		_categories = new ReadOnlyCollection<NodeCategory>(categories.OrderBy(c => c.Order).ToList());
		_definitions = new ReadOnlyCollection<NodeDefinition>(definitions.OrderBy(d => d.Order).ToList());
		_definitionById = _definitions.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
	}

	public IReadOnlyList<NodeCategory> Categories => _categories;
	public IReadOnlyList<NodeDefinition> Definitions => _definitions;

	public NodeDefinition? GetDefinition(string? id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		return _definitionById.TryGetValue(id, out var def) ? def : null;
	}

	public NodeDefinition GetDefaultDefinitionForType(FsmNodeType type)
	{
		return type switch
		{
			FsmNodeType.Start => GetDefinition(NodeCatalogDefaults.StartId)!,
			FsmNodeType.Terminal => GetDefinition(NodeCatalogDefaults.TerminalId)!,
			FsmNodeType.Condition => GetDefinition(NodeCatalogDefaults.BooleanConditionId)!,
			_ => GetDefinition(NodeCatalogDefaults.GenericActionId)!
		};
	}
}
