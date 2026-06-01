using System.Windows.Input;
using Orbit.Utilities;

namespace Orbit.Services;

public sealed class MesharpHotkeyService
{
	public bool MatchesDebugMenuHotkey(
		bool integrationEnabled,
		bool hotkeyEnabled,
		string? configuredHotkey,
		Key key,
		ModifierKeys modifiers)
	{
		if (!integrationEnabled || !hotkeyEnabled)
		{
			return false;
		}

		if (!HotkeySerializer.TryParse(configuredHotkey, out var configuredKey, out var configuredModifiers))
		{
			HotkeySerializer.TryParse(HotkeySerializer.DefaultMesharpDebugMenuHotkey, out configuredKey, out configuredModifiers);
		}

		var normalized = HotkeySerializer.NormalizeKey(key);
		return normalized == configuredKey && modifiers == configuredModifiers;
	}
}
