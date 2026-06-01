using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionRenameServiceTests
{
	[Fact]
	public void BeginRename_CopiesCurrentNameIntoEditableName()
	{
		var service = new SessionRenameService();
		var session = new SessionModel { Name = "Main" };

		service.BeginRename(session);

		Assert.True(session.IsRenaming);
		Assert.Equal("Main", session.EditableName);
	}

	[Fact]
	public void CommitRename_TrimsAndAppliesNonEmptyName()
	{
		var service = new SessionRenameService();
		var session = new SessionModel { Name = "Old", EditableName = "  New  ", IsRenaming = true };

		service.CommitRename(session);

		Assert.False(session.IsRenaming);
		Assert.Equal("New", session.Name);
		Assert.Equal("New", session.EditableName);
	}

	[Fact]
	public void CommitRename_RestoresEditableNameWhenProposedNameIsEmpty()
	{
		var service = new SessionRenameService();
		var session = new SessionModel { Name = "Old", EditableName = "  ", IsRenaming = true };

		service.CommitRename(session);

		Assert.False(session.IsRenaming);
		Assert.Equal("Old", session.Name);
		Assert.Equal("Old", session.EditableName);
	}

	[Fact]
	public void CancelRename_RestoresCurrentName()
	{
		var service = new SessionRenameService();
		var session = new SessionModel { Name = "Current", EditableName = "Draft", IsRenaming = true };

		service.CancelRename(session);

		Assert.False(session.IsRenaming);
		Assert.Equal("Current", session.EditableName);
	}
}
