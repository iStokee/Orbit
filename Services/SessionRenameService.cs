using System;
using Orbit.Models;

namespace Orbit.Services;

public sealed class SessionRenameService
{
	public void BeginRename(SessionModel? session)
	{
		if (session == null)
		{
			return;
		}

		session.EditableName = session.Name ?? string.Empty;
		session.IsRenaming = true;
	}

	public void CommitRename(SessionModel? session)
	{
		if (session == null)
		{
			return;
		}

		var proposed = session.EditableName?.Trim();
		session.IsRenaming = false;

		if (string.IsNullOrEmpty(proposed))
		{
			session.EditableName = session.Name ?? string.Empty;
			return;
		}

		if (!string.Equals(session.Name, proposed, StringComparison.Ordinal))
		{
			session.Name = proposed;
		}

		session.EditableName = session.Name ?? string.Empty;
	}

	public void CancelRename(SessionModel? session)
	{
		if (session == null)
		{
			return;
		}

		session.IsRenaming = false;
		session.EditableName = session.Name ?? string.Empty;
	}
}
