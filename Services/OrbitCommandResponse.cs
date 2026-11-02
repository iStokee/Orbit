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
	public bool HasScriptInventory
	{
		get
		{
			if (!Ok || !RuntimeRunning || string.IsNullOrWhiteSpace(ScriptsInfo)) return false;
			var lines = ScriptsInfo.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length < 2 || lines[0].TrimEnd('\r') != "DASHBOARD_V1" ||
				!lines[1].StartsWith("COUNT\t", System.StringComparison.Ordinal) ||
				!int.TryParse(lines[1].AsSpan(6), out var count) || count < 0 || count != lines.Length - 2) return false;
			for (var i = 2; i < lines.Length; i++)
				if (!lines[i].StartsWith("SCRIPT\t", System.StringComparison.Ordinal) || lines[i].Split('\t').Length < 6) return false;
			return true;
		}
	}

	public bool IsScriptLoaded(string? scriptId)
	{
		if (string.IsNullOrWhiteSpace(scriptId) || string.IsNullOrWhiteSpace(ScriptsInfo))
		{
			return false;
		}

		return Ok && RuntimeRunning && GetScriptPath(scriptId) != null;
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
