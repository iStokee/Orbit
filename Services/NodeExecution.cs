using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;
using MESharp.API.Input;
using Orbit.Models;
using TraversalApi = MESharp.API.Traversal;

namespace Orbit.Services;

public enum NodeExecutionStatus
{
	Success,
	Fail,
	Retry
}

public record NodeExecutionResult(NodeExecutionStatus Status, IReadOnlyDictionary<string, bool>? Outputs = null)
{
	public static NodeExecutionResult Success(IDictionary<string, bool>? outputs = null)
		=> new(NodeExecutionStatus.Success, outputs != null ? new Dictionary<string, bool>(outputs) : null);

	public static NodeExecutionResult Fail(IDictionary<string, bool>? outputs = null)
		=> new(NodeExecutionStatus.Fail, outputs != null ? new Dictionary<string, bool>(outputs) : null);

	public static NodeExecutionResult Retry(IDictionary<string, bool>? outputs = null)
		=> new(NodeExecutionStatus.Retry, outputs != null ? new Dictionary<string, bool>(outputs) : null);
}

public class NodeExecutionContext
{
	public NodeExecutionContext(
		FsmNodeModel node,
		NodeDefinition definition,
		IReadOnlyDictionary<string, bool> signals,
		IReadOnlyDictionary<string, object?> parameters)
	{
		Node = node ?? throw new ArgumentNullException(nameof(node));
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Signals = signals ?? throw new ArgumentNullException(nameof(signals));
		Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
	}

	public FsmNodeModel Node { get; }
	public NodeDefinition Definition { get; }
	public IReadOnlyDictionary<string, bool> Signals { get; }
	public IReadOnlyDictionary<string, object?> Parameters { get; }
}

public interface INodeExecutor
{
	Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Registry of executors keyed by definition id, with a safe default that respects dwell delays.
/// </summary>
public class NodeExecutorRegistry
{
	private readonly Dictionary<string, INodeExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);
	private readonly INodeExecutor _default;

	public NodeExecutorRegistry()
	{
		_default = new DwellOnlyExecutor();
		Register(NodeCatalogDefaults.GenericActionId, _default);
		Register(NodeCatalogDefaults.StartId, new NoOpExecutor());
		Register(NodeCatalogDefaults.TerminalId, new NoOpExecutor());
		Register(NodeCatalogDefaults.BooleanConditionId, new BooleanConditionExecutor());

		// Core actions
		Register("actions.interaction", new LoggedDelayExecutor());
		Register("actions.shop", new LoggedDelayExecutor());
		Register("actions.setSignal", new SetSignalExecutor());

		// Traversal / timing
		Register("traversal.worldhop", new LoggedDelayExecutor());
		Register("traversal.walk", new WalkExecutor());
		Register("traversal.teleportLodestone", new LodestoneTeleportExecutor());
		Register("traversal.wait", new WaitExecutor());

		// Input
		Register("keyboard.send", new LoggedDelayExecutor());
		Register("input.click", new ClickExecutor());

		// Inventory / bank
		Register("inventory.contains", new InventoryContainsExecutor());
		Register("inventory.count", new InventoryCountExecutor());
		Register("inventory.use", new InventoryUseExecutor());
		Register("inventory.useOn", new InventoryUseOnExecutor());
		Register("bank.open", new BankOpenExecutor());
		Register("bank.depositAll", new BankDepositAllExecutor());
		Register("bank.withdraw", new BankWithdrawExecutor());
		Register("bank.close", new BankCloseExecutor());

		// NPCs / objects / loot
		Register("npcs.interact", new NpcInteractExecutor());
		Register("npcs.attack", new NpcAttackExecutor());
		Register("objects.interact", new ObjectInteractExecutor());
		Register("objects.exists", new ObjectExistsExecutor());
		Register("loot.pickup", new LootPickupExecutor());

		// Conditions / skills
		Register("conditions.inCombat", new InCombatExecutor());
		Register("conditions.inventoryFull", new InventoryFullExecutor());
		Register("skills.requireLevel", new SkillRequirementExecutor());

		// Trade
		Register("trade.accept", new TradeAcceptExecutor());
	}

	public void Register(string definitionId, INodeExecutor executor)
	{
		if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentNullException(nameof(definitionId));
		_executors[definitionId] = executor ?? throw new ArgumentNullException(nameof(executor));
	}

	public INodeExecutor Resolve(string? definitionId)
	{
		if (!string.IsNullOrWhiteSpace(definitionId) && _executors.TryGetValue(definitionId, out var executor))
		{
			return executor;
		}

		return _default;
	}

	private sealed class DwellOnlyExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			if (context.Node.DwellMilliseconds > 0)
			{
				await Task.Delay(context.Node.DwellMilliseconds, cancellationToken);
			}

			return NodeExecutionResult.Success();
		}
	}

	private sealed class NoOpExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private sealed class LoggedDelayExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			if (context.Node.DwellMilliseconds > 0)
			{
				await Task.Delay(context.Node.DwellMilliseconds, cancellationToken);
			}

			Console.WriteLine($"[Executor] {context.Definition.Title} ({context.Definition.Id}) with parameters: {string.Join(", ", context.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}");
			return NodeExecutionResult.Success();
		}
	}

	private sealed class BooleanConditionExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var parameters = context.Parameters;
			parameters.TryGetValue("signal", out var signalKeyObj);
			parameters.TryGetValue("expected", out var expectedObj);

			var signalKey = signalKeyObj?.ToString() ?? string.Empty;
			var expectsTrue = expectedObj is bool b ? b : true;

			var actual = false;
			if (!string.IsNullOrWhiteSpace(signalKey))
			{
				context.Signals.TryGetValue(signalKey, out actual);
			}

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(signalKey))
			{
				outputs[signalKey] = actual;
			}

			var status = actual == expectsTrue ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class SetSignalExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			context.Parameters.TryGetValue("signal", out var signalKeyObj);
			context.Parameters.TryGetValue("value", out var valueObj);

			var key = signalKeyObj?.ToString() ?? string.Empty;
			var value = valueObj is bool b ? b : true;

			if (string.IsNullOrWhiteSpace(key))
			{
				return Task.FromResult(NodeExecutionResult.Fail());
			}

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[key] = value
			};

			return Task.FromResult(NodeExecutionResult.Success(outputs));
		}
	}

	private sealed class InventoryContainsExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var ids = ParameterHelper.ToIntList(context.Parameters, "ids");
			var names = ParameterHelper.ToStringList(context.Parameters, "names");
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

			var hasIds = ids.Count > 0 && Inventory.ContainsAny(ids.ToArray());
			var hasNames = names.Count > 0 && names.Any(n => Inventory.Contains(n));
			var result = ids.Count == 0 && names.Count == 0 ? Inventory.IsFull : hasIds || hasNames;

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["inventory.contains"] = result
			};
			var status = result == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class InventoryCountExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var min = ParameterHelper.ToInt(context.Parameters, "min");
			var max = ParameterHelper.ToInt(context.Parameters, "max");

			ulong count = 0;
			if (id.HasValue)
				count = Inventory.CountOf(id.Value);
			else if (!string.IsNullOrWhiteSpace(name))
				count = Inventory.CountOf(name);

			var ok = true;
			if (min.HasValue) ok &= count >= (ulong)Math.Max(0, min.Value);
			if (max.HasValue) ok &= count <= (ulong)Math.Max(0, max.Value);

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["inventory.count.met"] = ok
			};

			return Task.FromResult(ok ? NodeExecutionResult.Success(outputs) : NodeExecutionResult.Fail(outputs));
		}
	}

	private sealed class InventoryUseExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var action = (ParameterHelper.ToString(context.Parameters, "action") ?? "Use").ToLowerInvariant();

			bool ok = action switch
			{
				"eat/drink" or "eat" or "drink" => id.HasValue ? Inventory.Eat(id.Value) : Inventory.Eat(name),
				"drop" => id.HasValue ? Inventory.Drop(id.Value) : Inventory.Drop(name),
				"equip" => id.HasValue ? Inventory.Equip(id.Value) : Inventory.Equip(name),
				_ => id.HasValue ? Inventory.Use(id.Value) : Inventory.Use(name)
			};

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class InventoryUseOnExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var fromRaw = ParameterHelper.ToString(context.Parameters, "from");
			var toRaw = ParameterHelper.ToString(context.Parameters, "to");

			if (string.IsNullOrWhiteSpace(fromRaw) || string.IsNullOrWhiteSpace(toRaw))
			{
				return Task.FromResult(NodeExecutionResult.Fail());
			}

			bool ok;
			if (int.TryParse(fromRaw, out var fromId) && int.TryParse(toRaw, out var toId))
				ok = Inventory.UseItemOnItem(fromId, toId);
			else
				ok = Inventory.UseItemOnItem(fromRaw, toRaw);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class BankOpenExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var success = TryInvokeNativeBankOpen() || Bank.IsOpen;
			return Task.FromResult(success ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}

		private static bool TryInvokeNativeBankOpen()
		{
			try
			{
				var nativeType = Type.GetType("csharp_interop.native.Native_Bank, csharp_interop", throwOnError: false);
				var method = nativeType?.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				if (method == null) return false;
				var result = method.Invoke(null, Array.Empty<object>());
				return result is bool b && b;
			}
			catch
			{
				return false;
			}
		}
	}

	private sealed class BankDepositAllExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var exceptIds = ParameterHelper.ToIntList(context.Parameters, "exceptIds");
			var exceptNames = ParameterHelper.ToStringList(context.Parameters, "exceptNames");

			bool ok;
			if (exceptIds.Count > 0)
				ok = Bank.DepositAllExcept(exceptIds.ToArray());
			else if (exceptNames.Count > 0)
				ok = Bank.DepositAllExcept(exceptNames.ToArray());
			else
				ok = Bank.DepositAll();

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class BankWithdrawExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var amount = ParameterHelper.ToInt(context.Parameters, "amount");

			// Without a dedicated API for quantities, use default action/quantity menu slot.
			var actionIndex = amount.HasValue && amount.Value > 1 ? 1 : 0;
			bool ok = false;

			if (id.HasValue)
				ok = Bank.DoActionById(id.Value, actionIndex);
			else if (!string.IsNullOrWhiteSpace(name))
				ok = Bank.DoActionByName(name, actionIndex);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class BankCloseExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			Bank.Close();
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private sealed class NpcInteractExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var target = ParameterHelper.ToString(context.Parameters, "target");
			var option = ParameterHelper.ToString(context.Parameters, "option") ?? "Talk-to";
			var allowPartial = ParameterHelper.ToBool(context.Parameters, "allowPartial", false);

			if (string.IsNullOrWhiteSpace(target))
				return Task.FromResult(NodeExecutionResult.Fail());

			var names = allowPartial ? new[] { target } : new[] { target };
			// We do not have menu index lookup by name; use primary option.
			var ok = Npcs.DoActionByNames(names, actionIndex: 0);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class NpcAttackExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;

			bool ok = false;
			if (id.HasValue)
				ok = Npcs.DoActionByIds(new[] { id.Value }, actionIndex: 0, maxDistance: maxDistance);
			else if (!string.IsNullOrWhiteSpace(name))
				ok = Npcs.DoActionByNames(new[] { name }, actionIndex: 0, maxDistance: maxDistance);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class ObjectInteractExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");

			bool ok = false;
			if (id.HasValue)
				ok = Objects.DoActionByIds(new[] { id.Value }, actionIndex: 0);
			else if (!string.IsNullOrWhiteSpace(name))
				ok = Objects.DoActionByNames(new[] { name }, actionIndex: 0);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class ObjectExistsExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

			var objs = id.HasValue
				? Objects.GetAll().Where(o => o.Id == id.Value)
				: Objects.GetAll().Where(o => string.IsNullOrWhiteSpace(name) || o.Name?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

			var exists = objs.Any();
			var status = exists == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["objects.exists"] = exists
			};
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class LootPickupExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			// Approximate pickup via object interaction on ground items.
			var names = ParameterHelper.ToStringList(context.Parameters, "names");
			var ids = ParameterHelper.ToIntList(context.Parameters, "ids");
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;

			bool ok = false;
			if (ids.Count > 0)
				ok = Objects.DoActionByIds(ids, actionIndex: 0, maxDistance: maxDistance, valid: true);
			else if (names.Count > 0)
				ok = Objects.DoActionByNames(names, actionIndex: 0, maxDistance: maxDistance, valid: true);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class InCombatExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var actual = LocalPlayer.IsInCombat();
			var status = actual == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["inCombat"] = actual
			};
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class InventoryFullExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var isFull = Inventory.IsFull;
			var status = isFull == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["inventoryFull"] = isFull
			};
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class LodestoneTeleportExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var destination = ParameterHelper.ToString(context.Parameters, "destination");
			var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 12000;

			bool ok = false;
			if (!string.IsNullOrWhiteSpace(destination))
			{
				ok = TraversalApi.Lodestone(destination, timeout);
			}

			return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
		}
	}

	private sealed class WalkExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			// Try to parse coordinates; support single or multiple waypoints.
			var coords = ParameterHelper.ToCoordinateList(context.Parameters, "target");
			var stopShort = ParameterHelper.ToInt(context.Parameters, "stopShort") ?? 2;
			var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 8000;
			var jitter = ParameterHelper.ToInt(context.Parameters, "jitter") ?? 1;

			bool ok;
			if (coords.Count > 1)
			{
				ok = TraversalApi.WalkPath(coords.Select(c => (c.x, c.y, c.z)), stopShort, timeout, jitter);
			}
			else if (coords.Count == 1)
			{
				var c = coords[0];
				ok = TraversalApi.WalkTo(c.x, c.y, c.z, stopShort, timeout, jitter);
			}
			else
			{
				return NodeExecutionResult.Fail();
			}

			// Honor dwell after we initiate movement (post-click pause).
			if (ok && context.Node.DwellMilliseconds > 0)
			{
				await Task.Delay(context.Node.DwellMilliseconds, cancellationToken);
			}

			return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
		}
	}

	private sealed class WaitExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var delay = ParameterHelper.ToInt(context.Parameters, "delayMs") ?? 0;
			if (delay > 0)
			{
				await Task.Delay(delay, cancellationToken);
			}
			return NodeExecutionResult.Success();
		}
	}

	private sealed class SkillRequirementExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var skill = ParameterHelper.ToString(context.Parameters, "skill") ?? string.Empty;
			var level = ParameterHelper.ToInt(context.Parameters, "level") ?? 0;
			var skillName = skill.ToLowerInvariant() switch
			{
				"attack" => SkillName.Attack,
				"strength" => SkillName.Strength,
				"defence" => SkillName.Defence,
				"defense" => SkillName.Defence,
				"magic" => SkillName.Magic,
				"ranged" => SkillName.Ranged,
				"prayer" => SkillName.Prayer,
				"mining" => SkillName.Mining,
				"smithing" => SkillName.Smithing,
				"fishing" => SkillName.Fishing,
				"cooking" => SkillName.Cooking,
				"crafting" => SkillName.Crafting,
				"fletching" => SkillName.Fletching,
				"woodcutting" => SkillName.Woodcutting,
				"agility" => SkillName.Agility,
				"slayer" => SkillName.Slayer,
				"herblore" => SkillName.Herblore,
				"runecrafting" => SkillName.Runecrafting,
				_ => SkillName.Attack
			};

			var current = Skills.Get(skillName).CurrentLevel;

			var ok = current >= level;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[$"skill.{skill}.met"] = ok
			};
			return Task.FromResult(ok ? NodeExecutionResult.Success(outputs) : NodeExecutionResult.Fail(outputs));
		}
	}

	private sealed class TradeAcceptExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			Console.WriteLine("[Executor] Trade accept not yet wired to native API.");
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private sealed class ClickExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			Console.WriteLine("[Executor] Mouse click not wired (no native mouse API available).");
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private static class ParameterHelper
	{
		public static string? ToString(IReadOnlyDictionary<string, object?> map, string key)
		{
			return map.TryGetValue(key, out var val) ? val?.ToString() : null;
		}

		public static int? ToInt(IReadOnlyDictionary<string, object?> map, string key)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return null;
			if (val is int i) return i;
			if (val is double d) return (int)d;
			return int.TryParse(val.ToString(), out var parsed) ? parsed : null;
		}

		public static bool ToBool(IReadOnlyDictionary<string, object?> map, string key, bool fallback = false)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return fallback;
			if (val is bool b) return b;
			return bool.TryParse(val.ToString(), out var parsed) ? parsed : fallback;
		}

		public static List<int> ToIntList(IReadOnlyDictionary<string, object?> map, string key)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return new List<int>();
			if (val is IEnumerable<string> listStrings)
				return listStrings.Select(v => int.TryParse(v, out var i) ? i : (int?)null).Where(i => i.HasValue).Select(i => i!.Value).ToList();
			if (val is IEnumerable<object> listObj)
				return listObj.Select(v => int.TryParse(v?.ToString(), out var i) ? i : (int?)null).Where(i => i.HasValue).Select(i => i!.Value).ToList();
			return val.ToString() is { } single && int.TryParse(single, out var parsed) ? new List<int> { parsed } : new List<int>();
		}

		public static List<string> ToStringList(IReadOnlyDictionary<string, object?> map, string key)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return new List<string>();
			if (val is IEnumerable<string> listStrings) return listStrings.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
			if (val is IEnumerable<object> listObj) return listObj.Select(v => v?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;
			var single = val.ToString();
			return string.IsNullOrWhiteSpace(single) ? new List<string>() : new List<string> { single };
		}

		public static List<(int x, int y, int z)> ToCoordinateList(IReadOnlyDictionary<string, object?> map, string key)
		{
			var output = new List<(int, int, int)>();
			var strings = ToStringList(map, key);
			foreach (var s in strings)
			{
				if (string.IsNullOrWhiteSpace(s)) continue;
				var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 2 &&
				    int.TryParse(parts[0], out var x) &&
				    int.TryParse(parts[1], out var y))
				{
					var z = 0;
					if (parts.Length >= 3) int.TryParse(parts[2], out z);
					output.Add((x, y, z));
				}
			}

			return output;
		}
	}
}
