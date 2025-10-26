using System;

namespace Orbit.Logging;

public sealed class ConsoleLogEntry
{
	public ConsoleLogEntry(DateTime timestamp, ConsoleLogSource source, ConsoleLogLevel level, string message)
	{
		Timestamp = timestamp;
		Source = source;
		Level = level;
		Message = message;
	}

	public DateTime Timestamp { get; }
	public ConsoleLogSource Source { get; }
	public ConsoleLogLevel Level { get; }
	public string Message { get; }

	public string DisplayText => $"[{Timestamp:HH:mm:ss}] [{Source}] {Message}";
}
