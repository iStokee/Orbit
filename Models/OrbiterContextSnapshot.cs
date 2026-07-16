namespace Orbit.Models;

/// <summary>
/// Lightweight, presentation-neutral description of the object currently
/// supplying contextual Orbiter commands.
/// </summary>
/// <param name="Kind">Stable context category, such as session, script, theme, or log-event.</param>
/// <param name="DisplayName">User-facing name for the active context.</param>
/// <param name="Source">Optional domain object associated with the context.</param>
public sealed record OrbiterContextSnapshot(
	string Kind,
	string DisplayName,
	object? Source = null);
