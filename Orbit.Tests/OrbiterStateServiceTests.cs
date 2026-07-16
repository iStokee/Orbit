using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class OrbiterStateServiceTests
{
	[Fact]
	public void NewService_StartsDormantWithoutContext()
	{
		var service = new OrbiterStateService();

		Assert.Equal(OrbiterMode.Dormant, service.Mode);
		Assert.True(service.IsDormant);
		Assert.False(service.IsVisible);
		Assert.False(service.HasContext);
		Assert.Equal(string.Empty, service.Query);
	}

	[Fact]
	public void Summon_WithoutContext_ShowsGlobalNavigation()
	{
		var service = new OrbiterStateService();

		service.Summon();

		Assert.Equal(OrbiterMode.GlobalNavigation, service.Mode);
		Assert.True(service.IsGlobalNavigation);
		Assert.True(service.IsVisible);
	}

	[Fact]
	public void ActivateContext_ShowsContextAndClearsQuery()
	{
		var service = new OrbiterStateService();
		service.BeginCommandLens("client");
		var context = new OrbiterContextSnapshot("session", "Client 1");

		service.ActivateContext(context);

		Assert.Same(context, service.Context);
		Assert.True(service.HasContext);
		Assert.Equal(OrbiterMode.Context, service.Mode);
		Assert.True(service.IsContextMode);
		Assert.Equal(string.Empty, service.Query);
	}

	[Fact]
	public void Dismiss_RetainsContext_AndSummonRestoresContextMode()
	{
		var service = new OrbiterStateService();
		var context = new OrbiterContextSnapshot("session", "Client 1");
		service.ActivateContext(context);

		service.Dismiss();

		Assert.Equal(OrbiterMode.Dormant, service.Mode);
		Assert.Same(context, service.Context);

		service.Summon();

		Assert.Equal(OrbiterMode.Context, service.Mode);
		Assert.Same(context, service.Context);
	}

	[Fact]
	public void NavigateBack_FromCommandLens_ReturnsToRetainedContext()
	{
		var service = new OrbiterStateService();
		var context = new OrbiterContextSnapshot("session", "Client 1");
		service.ActivateContext(context);
		service.BeginCommandLens("reload");

		var handled = service.NavigateBack();

		Assert.True(handled);
		Assert.Equal(OrbiterMode.Context, service.Mode);
		Assert.Same(context, service.Context);
		Assert.Equal(string.Empty, service.Query);
	}

	[Fact]
	public void NavigateBack_FromCommandLensWithoutContext_ReturnsToGlobalNavigation()
	{
		var service = new OrbiterStateService();
		service.BeginCommandLens("settings");

		var handled = service.NavigateBack();

		Assert.True(handled);
		Assert.Equal(OrbiterMode.GlobalNavigation, service.Mode);
		Assert.Null(service.Context);
		Assert.Equal(string.Empty, service.Query);
	}

	[Fact]
	public void NavigateBack_FromContext_ClearsContextThenGlobalNavigationDismisses()
	{
		var service = new OrbiterStateService();
		service.ActivateContext(new OrbiterContextSnapshot("session", "Client 1"));

		Assert.True(service.NavigateBack());
		Assert.Equal(OrbiterMode.GlobalNavigation, service.Mode);
		Assert.Null(service.Context);

		Assert.True(service.NavigateBack());
		Assert.Equal(OrbiterMode.Dormant, service.Mode);

		Assert.False(service.NavigateBack());
		Assert.Equal(OrbiterMode.Dormant, service.Mode);
	}

	[Fact]
	public void ClearContext_CanReturnDirectlyToDormant()
	{
		var service = new OrbiterStateService();
		service.ActivateContext(new OrbiterContextSnapshot("script", "Abyss Runner"));

		service.ClearContext(showGlobalNavigation: false);

		Assert.Null(service.Context);
		Assert.Equal(OrbiterMode.Dormant, service.Mode);
	}

	[Theory]
	[InlineData("", "Client 1")]
	[InlineData("   ", "Client 1")]
	[InlineData("session", "")]
	[InlineData("session", "   ")]
	public void ActivateContext_RejectsIncompleteContext(string kind, string displayName)
	{
		var service = new OrbiterStateService();
		var context = new OrbiterContextSnapshot(kind, displayName);

		Assert.Throws<ArgumentException>(() => service.ActivateContext(context));
		Assert.Equal(OrbiterMode.Dormant, service.Mode);
		Assert.Null(service.Context);
	}
}
