using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

/// <summary>Closing one of several shells moves its contents, not its processes.</summary>
public sealed class ShellWindowTransferService
{
	private readonly SessionPlacementService _placement;
	private readonly OrbitLayoutStateService _layout;
	public ShellWindowTransferService(SessionPlacementService placement, OrbitLayoutStateService layout)
	{
		_placement = placement;
		_layout = layout;
	}

	public void Transfer(IEnumerable<object> items, Action<object> removeSource, Action<object> restoreSource,
		ObservableCollection<object> destination, object oldOwner, object newOwner, Action openOrbitView,
		SessionPlacementKind destinationPlacement)
	{
		foreach (var item in items.Distinct().ToArray())
		{
			var previousPlacement = item is SessionModel original ? _placement.GetPlacement(original) : SessionPlacementKind.Unknown;
			using var move = item is SessionModel session ? _placement.BeginMove(session, destinationPlacement) : null;
			try
			{
				removeSource(item);
				if (item is ToolTabItem orbit && orbit.Key == ShellPresentationPolicyService.OrbitViewToolKey)
				{
					// Rebuild callbacks against the surviving shell; layout membership survives.
					openOrbitView();
					orbit.Close(oldOwner);
					continue;
				}
				if (item is ToolTabItem tool && ReferenceEquals(tool.HostControl?.DataContext, oldOwner))
					tool.HostControl.DataContext = newOwner;
				if (!destination.Contains(item)) destination.Add(item);
				if (item is SessionModel movedSession) _layout.RemoveItem(movedSession);
				if (item is ToolTabItem movedTool)
				{
					_layout.RemoveItem(movedTool);
					_placement.SetPlacement(movedTool, destinationPlacement, "shell-window-close");
				}
			}
			catch
			{
				if (!destination.Contains(item))
				{
					restoreSource(item);
					if (item is SessionModel restored) _placement.SetPlacement(restored, previousPlacement, "shell-transfer-failed");
					if (item is ToolTabItem restoredTool && ReferenceEquals(restoredTool.HostControl?.DataContext, newOwner))
						restoredTool.HostControl.DataContext = oldOwner;
				}
				throw;
			}
		}
	}
}
