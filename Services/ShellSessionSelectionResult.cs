using Orbit.Models;

namespace Orbit.Services;

public readonly record struct ShellSessionSelectionResult(
	SessionModel? SelectedSession,
	SessionModel? HotReloadTargetSession);
