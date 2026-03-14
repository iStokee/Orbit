using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Orbit.Utilities;

internal static class HotkeySerializer
{
    public const string DefaultMesharpDebugMenuHotkey = "F10";

    public static bool TryParse(string? value, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (parts.Length == 0)
        {
            return false;
        }

        Key parsedKey = Key.None;
        ModifierKeys parsedModifiers = ModifierKeys.None;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                parsedModifiers |= ModifierKeys.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                parsedModifiers |= ModifierKeys.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                parsedModifiers |= ModifierKeys.Shift;
                continue;
            }

            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                parsedModifiers |= ModifierKeys.Windows;
                continue;
            }

            if (!Enum.TryParse(part, ignoreCase: true, out Key candidate) || IsModifierKey(candidate))
            {
                return false;
            }

            if (parsedKey != Key.None)
            {
                return false;
            }

            parsedKey = candidate;
        }

        if (parsedKey == Key.None)
        {
            return false;
        }

        key = parsedKey;
        modifiers = parsedModifiers;
        return true;
    }

    public static string Serialize(Key key, ModifierKeys modifiers)
    {
        key = NormalizeKey(key);
        if (key == Key.None || IsModifierKey(key))
        {
            return DefaultMesharpDebugMenuHotkey;
        }

        var parts = new List<string>(5);
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static string ToDisplayString(string? serialized, string fallback)
    {
        if (TryParse(serialized, out var key, out var modifiers))
        {
            return Serialize(key, modifiers);
        }

        return fallback;
    }

    public static Key NormalizeKey(Key key)
        => key == Key.System ? Key.None : key;

    public static bool IsModifierKey(Key key)
        => key is Key.LeftAlt or Key.RightAlt
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
}
