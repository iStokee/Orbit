using System.Windows.Input;
using Orbit.Models;

namespace Orbit.Services;

public sealed class FloatingMenuQuickToggleService
{
	public bool ShouldToggleFromKeyboard(
		FloatingMenuQuickToggleMode mode,
		Key key,
		ModifierKeys modifiers,
		bool isTextInputContext)
	{
		if (mode == FloatingMenuQuickToggleMode.MiddleMouse || isTextInputContext)
		{
			return false;
		}

		if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) != ModifierKeys.None)
		{
			return false;
		}

		return mode switch
		{
			FloatingMenuQuickToggleMode.HomeKey => key == Key.Home,
			FloatingMenuQuickToggleMode.EndKey => key == Key.End,
			_ => false
		};
	}

	public bool ShouldToggleFromMouse(
		FloatingMenuQuickToggleMode mode,
		MouseButton button,
		int clickCount,
		ModifierKeys modifiers,
		bool isTextInputContext)
	{
		if (isTextInputContext)
		{
			return false;
		}

		if (mode != FloatingMenuQuickToggleMode.CtrlLeftClick &&
			(modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) != ModifierKeys.None)
		{
			return false;
		}

		return mode switch
		{
			FloatingMenuQuickToggleMode.MiddleMouse => button == MouseButton.Middle,
			FloatingMenuQuickToggleMode.RightDoubleClick => button == MouseButton.Right &&
				clickCount >= 2 &&
				modifiers == ModifierKeys.None,
			FloatingMenuQuickToggleMode.CtrlLeftClick => button == MouseButton.Left &&
				clickCount == 1 &&
				modifiers == ModifierKeys.Control,
			_ => false
		};
	}
}
