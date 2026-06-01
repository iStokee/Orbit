using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ScriptManagerServiceTests
{
	[Theory]
	[InlineData("C:\\Scripts\\Alpha Bot.dll", "alpha.bot")]
	[InlineData("C:\\Scripts\\Alpha__Bot!!.dll", "alpha.bot")]
	[InlineData("", "default")]
	[InlineData(null, "default")]
	public void DeriveScriptIdFromPath_NormalizesNames(string? path, string expected)
	{
		Assert.Equal(expected, ScriptManagerService.DeriveScriptIdFromPath(path));
	}
}
