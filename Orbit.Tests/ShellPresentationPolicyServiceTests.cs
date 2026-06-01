using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using MahApps.Metro.IconPacks;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellPresentationPolicyServiceTests
{
	[Fact]
	public void CanMoveTabToOrbit_AllowsSessions()
	{
		var service = new ShellPresentationPolicyService();
		var session = new SessionModel { Name = "Session" };

		Assert.True(service.CanMoveTabToOrbit(session, new List<object>()));
	}

	[Fact]
	public void CanMoveTabToOrbit_BlocksOrbitViewTool()
	{
		var service = new ShellPresentationPolicyService();
		var tool = NewTool(ShellPresentationPolicyService.OrbitViewToolKey);

		Assert.False(service.CanMoveTabToOrbit(tool, new List<object>()));
	}

	[Fact]
	public void CanMoveTabToOrbit_BlocksToolAlreadyInOrbitWorkspace()
	{
		var service = new ShellPresentationPolicyService();
		var tool = NewTool("Console");

		Assert.False(service.CanMoveTabToOrbit(tool, new[] { tool }));
	}

	[Fact]
	public void CanMoveTabToOrbit_AllowsToolNotAlreadyInOrbitWorkspace()
	{
		var service = new ShellPresentationPolicyService();
		var tool = NewTool("Console");

		Assert.True(service.CanMoveTabToOrbit(tool, new List<object>()));
	}

	[Fact]
	public void CanMoveSessionToIndividualTabs_AllowsOnlySessionsNotAlreadyInTabs()
	{
		var service = new ShellPresentationPolicyService();
		var session = new SessionModel { Name = "Session" };

		Assert.True(service.CanMoveSessionToIndividualTabs(session, new List<object>()));
		Assert.False(service.CanMoveSessionToIndividualTabs(session, new[] { session }));
		Assert.False(service.CanMoveSessionToIndividualTabs(new object(), new List<object>()));
	}

	[Fact]
	public void FindToolTab_ReturnsMatchingToolByKey()
	{
		var service = new ShellPresentationPolicyService();
		var console = NewTool("Console");
		var settings = NewTool("Settings");

		Assert.Same(settings, service.FindToolTab(new object[] { console, settings }, "Settings"));
		Assert.Null(service.FindToolTab(new object[] { console }, "settings"));
	}

	private static ToolTabItem NewTool(string key)
	{
		var tool = (ToolTabItem)RuntimeHelpers.GetUninitializedObject(typeof(ToolTabItem));
		SetBackingField(tool, nameof(ToolTabItem.Key), key);
		SetBackingField(tool, nameof(ToolTabItem.Name), key);
		SetBackingField(tool, nameof(ToolTabItem.Icon), PackIconMaterialKind.Tools);
		return tool;
	}

	private static void SetBackingField<T>(ToolTabItem tool, string propertyName, T value)
	{
		var field = typeof(ToolTabItem).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
		field?.SetValue(tool, value);
	}
}
