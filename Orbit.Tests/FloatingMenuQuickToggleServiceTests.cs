using System.Windows.Input;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class FloatingMenuQuickToggleServiceTests
{
	private readonly FloatingMenuQuickToggleService _service = new();

	[Theory]
	[InlineData(FloatingMenuQuickToggleMode.HomeKey, Key.Home, true)]
	[InlineData(FloatingMenuQuickToggleMode.HomeKey, Key.End, false)]
	[InlineData(FloatingMenuQuickToggleMode.EndKey, Key.End, true)]
	[InlineData(FloatingMenuQuickToggleMode.MiddleMouse, Key.Home, false)]
	public void ShouldToggleFromKeyboard_RecognizesConfiguredKeys(
		FloatingMenuQuickToggleMode mode,
		Key key,
		bool expected)
	{
		Assert.Equal(expected, _service.ShouldToggleFromKeyboard(mode, key, ModifierKeys.None, isTextInputContext: false));
	}

	[Fact]
	public void ShouldToggleFromKeyboard_IgnoresTextInputAndModifiers()
	{
		Assert.False(_service.ShouldToggleFromKeyboard(FloatingMenuQuickToggleMode.HomeKey, Key.Home, ModifierKeys.None, isTextInputContext: true));
		Assert.False(_service.ShouldToggleFromKeyboard(FloatingMenuQuickToggleMode.HomeKey, Key.Home, ModifierKeys.Control, isTextInputContext: false));
	}

	[Theory]
	[InlineData(FloatingMenuQuickToggleMode.MiddleMouse, MouseButton.Middle, 1, ModifierKeys.None, true)]
	[InlineData(FloatingMenuQuickToggleMode.RightDoubleClick, MouseButton.Right, 2, ModifierKeys.None, true)]
	[InlineData(FloatingMenuQuickToggleMode.RightDoubleClick, MouseButton.Right, 1, ModifierKeys.None, false)]
	[InlineData(FloatingMenuQuickToggleMode.CtrlLeftClick, MouseButton.Left, 1, ModifierKeys.Control, true)]
	[InlineData(FloatingMenuQuickToggleMode.CtrlLeftClick, MouseButton.Left, 1, ModifierKeys.None, false)]
	public void ShouldToggleFromMouse_RecognizesConfiguredGestures(
		FloatingMenuQuickToggleMode mode,
		MouseButton button,
		int clickCount,
		ModifierKeys modifiers,
		bool expected)
	{
		Assert.Equal(expected, _service.ShouldToggleFromMouse(mode, button, clickCount, modifiers, isTextInputContext: false));
	}

	[Fact]
	public void ShouldToggleFromMouse_IgnoresTextInputAndUnexpectedModifiers()
	{
		Assert.False(_service.ShouldToggleFromMouse(FloatingMenuQuickToggleMode.MiddleMouse, MouseButton.Middle, 1, ModifierKeys.None, isTextInputContext: true));
		Assert.False(_service.ShouldToggleFromMouse(FloatingMenuQuickToggleMode.RightDoubleClick, MouseButton.Right, 2, ModifierKeys.Control, isTextInputContext: false));
	}
}
