using System;
using System.Collections.Generic;

namespace Orbit.Services;

internal sealed class LauncherEnvironmentScope : IDisposable
{
	private static readonly string[] Names = { "JX_ACCESS_TOKEN", "JX_DISPLAY_NAME", "JX_CHARACTER_ID", "JX_REFRESH_TOKEN", "JX_SESSION_ID" };
	private readonly Dictionary<string, string?> _previous = new();
	internal LauncherEnvironmentScope()
	{
		foreach (var name in Names) _previous[name] = Environment.GetEnvironmentVariable(name);
	}
	public void Dispose()
	{
		foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
	}
}
