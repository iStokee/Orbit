using System.Windows.Input;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class MesharpHotkeyServiceTests
{
	[Fact]
	public void MatchesDebugMenuHotkey_ReturnsFalseWhenIntegrationOrHotkeyDisabled()
	{
		var service = new MesharpHotkeyService();

		Assert.False(service.MatchesDebugMenuHotkey(false, true, "F10", Key.F10, ModifierKeys.None));
		Assert.False(service.MatchesDebugMenuHotkey(true, false, "F10", Key.F10, ModifierKeys.None));
	}

	[Theory]
	[InlineData("F10", Key.F10, ModifierKeys.None, true)]
	[InlineData("Ctrl+Shift+F10", Key.F10, ModifierKeys.Control | ModifierKeys.Shift, true)]
	[InlineData("Ctrl+F10", Key.F10, ModifierKeys.None, false)]
	[InlineData("F9", Key.F10, ModifierKeys.None, false)]
	public void MatchesDebugMenuHotkey_MatchesConfiguredHotkey(
		string configured,
		Key key,
		ModifierKeys modifiers,
		bool expected)
	{
		var service = new MesharpHotkeyService();

		var matches = service.MatchesDebugMenuHotkey(true, true, configured, key, modifiers);

		Assert.Equal(expected, matches);
	}

	[Fact]
	public void MatchesDebugMenuHotkey_FallsBackToDefaultWhenConfiguredHotkeyIsInvalid()
	{
		var service = new MesharpHotkeyService();

		Assert.True(service.MatchesDebugMenuHotkey(true, true, "Ctrl", Key.F10, ModifierKeys.None));
	}
}
