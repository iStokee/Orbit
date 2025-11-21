using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Models;

namespace Orbit.Services;

/// <summary>
/// Lightweight FSM runner that walks nodes and edges and emits events for UI feedback.
/// Consumers are responsible for reacting to node and transition events (e.g., highlighting).
/// </summary>
public class FsmExecutionEngine
{
	public event EventHandler<FsmNodeModel>? NodeEntered;
	public event EventHandler<FsmTransitionModel>? TransitionTaken;
	public event EventHandler? Completed;
	public event EventHandler<Exception>? Faulted;

	public bool IsRunning { get; private set; }

	public async Task RunAsync(
		FsmScriptModel script,
		IReadOnlyDictionary<string, bool> signals,
		bool loop,
		CancellationToken cancellationToken = default)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));
		if (signals == null) throw new ArgumentNullException(nameof(signals));
		if (script.Nodes.Count == 0) return;

		IsRunning = true;

		try
		{
			do
			{
				var startNode = ResolveStartNode(script);
				var current = startNode;

				while (current != null && !cancellationToken.IsCancellationRequested)
				{
					NodeEntered?.Invoke(this, current);

					if (current.DwellMilliseconds > 0)
					{
						await Task.Delay(current.DwellMilliseconds, cancellationToken);
					}

					var nextTransition = ResolveTransition(current, signals);
					if (nextTransition == null)
					{
						break;
					}

					TransitionTaken?.Invoke(this, nextTransition);

					current = script.Nodes.FirstOrDefault(n => n.Id == nextTransition.ToNodeId);
				}
			} while (loop && !cancellationToken.IsCancellationRequested);

			if (!cancellationToken.IsCancellationRequested)
			{
				Completed?.Invoke(this, EventArgs.Empty);
			}
		}
		catch (OperationCanceledException)
		{
			// Swallow cancellations silently
		}
		catch (Exception ex)
		{
			Faulted?.Invoke(this, ex);
		}
		finally
		{
			IsRunning = false;
		}
	}

	private static FsmNodeModel ResolveStartNode(FsmScriptModel script)
	{
		if (script.StartNodeId.HasValue)
		{
			var start = script.Nodes.FirstOrDefault(n => n.Id == script.StartNodeId.Value);
			if (start != null)
				return start;
		}

		return script.Nodes.First();
	}

	private static FsmTransitionModel? ResolveTransition(FsmNodeModel current, IReadOnlyDictionary<string, bool> signals)
	{
		if (current.Transitions.Count == 0)
			return null;

		// Evaluate conditional edges first (in order)
		foreach (var transition in current.Transitions.Where(t => t.HasCondition))
		{
			if (!signals.TryGetValue(transition.ConditionKey, out var value))
				continue;

			if (value == transition.ExpectedValue)
				return transition;
		}

		// Then fall back to the declared fallback edge
		var fallback = current.Transitions.FirstOrDefault(t => t.IsFallback);
		return fallback ?? current.Transitions.FirstOrDefault();
	}
}
