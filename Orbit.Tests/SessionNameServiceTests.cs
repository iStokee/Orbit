using System.Collections.Generic;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionNameServiceTests
{
	[Fact]
	public void ResolveSessionName_UsesUniquePreferredName()
	{
		var service = new SessionNameService();
		var sessions = new List<SessionModel>
		{
			new() { Name = "RuneScape Session 1" }
		};

		var name = service.ResolveSessionName(sessions, "  Main Account  ");

		Assert.Equal("Main Account", name);
	}

	[Fact]
	public void ResolveSessionName_FallsBackWhenPreferredNameAlreadyExists()
	{
		var service = new SessionNameService();
		var sessions = new List<SessionModel>
		{
			new() { Name = "Main Account" }
		};

		var name = service.ResolveSessionName(sessions, "main account");

		Assert.Equal("RuneScape Session 1", name);
	}

	[Fact]
	public void ResolveSessionName_ContinuesAfterHighestExistingDefaultOrdinal()
	{
		var service = new SessionNameService();
		var sessions = new List<SessionModel>
		{
			new() { Name = "RuneScape Session 2" },
			new() { Name = "RuneScape Session 9" },
			new() { Name = "RuneScape Session draft" }
		};

		var name = service.ResolveSessionName(sessions, null);

		Assert.Equal("RuneScape Session 10", name);
	}

	[Theory]
	[InlineData("RuneScape Session 3", 3)]
	[InlineData("runescape session 4", 4)]
	[InlineData("RuneScape Session 0", null)]
	[InlineData("RuneScape Session draft", null)]
	[InlineData("Other 5", null)]
	public void TryParseDefaultSessionOrdinal_ParsesOnlyValidDefaultNames(string name, int? expected)
	{
		Assert.Equal(expected, SessionNameService.TryParseDefaultSessionOrdinal(name));
	}
}
