using System;
using System.Collections.Generic;
using System.Windows;
using Orbit.Models;
using Orbit.Views;

namespace Orbit.Services;

public sealed class ShellClientResizeService
{
	public void ResizeVisibleClients(IEnumerable<SessionModel> sessions)
	{
		if (sessions == null)
		{
			throw new ArgumentNullException(nameof(sessions));
		}

		foreach (var session in sessions)
		{
			if (session.HostControl is not ChildClientView clientView)
			{
				continue;
			}

			if (PresentationSource.FromVisual(clientView) == null)
			{
				continue;
			}

			if (!clientView.IsVisible)
			{
				continue;
			}

			var viewportSize = clientView.GetHostViewportSize();
			var width = Math.Max(0, (int)Math.Round(viewportSize.Width));
			var height = Math.Max(0, (int)Math.Round(viewportSize.Height));

			if (width <= 0 || height <= 0)
			{
				continue;
			}

			_ = clientView.ResizeWindowAsync(width, height);
		}
	}
}
