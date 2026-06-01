using System.Collections.Generic;
using Orbit.Models;

namespace Orbit.Services;

public sealed record ShellTabCollectionChangeResult(
	object? SelectedTab,
	bool ClearSelectedSession,
	IReadOnlyList<SessionModel> SessionsNeedingOrphanValidation);
