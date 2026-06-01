namespace Orbit.Services;

public sealed record OrbitCommandResponse(
	bool Success,
	string? RawResponse = null,
	string? ErrorMessage = null,
	OrbitRuntimeStatus? Status = null);

public sealed record OrbitRuntimeStatus(
	bool Ok,
	int ProcessId,
	bool RuntimeRunning,
	string? ScriptId,
	string? ScriptsInfo)
{
	public bool IsScriptLoaded(string? scriptId)
	{
		if (string.IsNullOrWhiteSpace(scriptId) || string.IsNullOrWhiteSpace(ScriptsInfo))
		{
			return false;
		}

		var scriptToken = $"SCRIPT\t{scriptId.Trim()}\t";
		return ScriptsInfo.Contains(scriptToken, System.StringComparison.OrdinalIgnoreCase);
	}

	public string? GetScriptPath(string? scriptId)
	{
		if (string.IsNullOrWhiteSpace(scriptId) || string.IsNullOrWhiteSpace(ScriptsInfo))
		{
			return null;
		}

		var scriptToken = $"SCRIPT\t{scriptId.Trim()}\t";
		foreach (var line in ScriptsInfo.Split('\n'))
		{
			if (!line.StartsWith(scriptToken, System.StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var parts = line.Split('\t');
			return parts.Length >= 6 ? parts[5] : null;
		}

		return null;
	}
}
