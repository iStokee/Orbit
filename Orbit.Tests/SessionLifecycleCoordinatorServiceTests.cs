using System;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionLifecycleCoordinatorServiceTests
{
	[Fact]
	public void TryBeginClose_PreventsDuplicateCloseUntilCompleted()
	{
		var coordinator = new SessionLifecycleCoordinatorService();
		var session = NewSession();

		Assert.True(coordinator.TryBeginClose(session));
		Assert.True(coordinator.IsCloseInProgress(session));
		Assert.False(coordinator.TryBeginClose(session));

		coordinator.CompleteClose(session);

		Assert.False(coordinator.IsCloseInProgress(session));
		Assert.True(coordinator.TryBeginClose(session));
	}

	[Fact]
	public void TryBeginOrphanValidation_PreventsDuplicateValidationUntilCompleted()
	{
		var coordinator = new SessionLifecycleCoordinatorService();
		var session = NewSession();

		Assert.True(coordinator.TryBeginOrphanValidation(session));
		Assert.False(coordinator.TryBeginOrphanValidation(session));

		coordinator.CompleteOrphanValidation(session);

		Assert.True(coordinator.TryBeginOrphanValidation(session));
	}

	[Fact]
	public void TryBeginRelaunch_PreventsDuplicateRelaunchUntilCompleted()
	{
		var coordinator = new SessionLifecycleCoordinatorService();
		var session = NewSession();

		Assert.True(coordinator.TryBeginRelaunch(session));
		Assert.False(coordinator.TryBeginRelaunch(session));

		coordinator.CompleteRelaunch(session);

		Assert.True(coordinator.TryBeginRelaunch(session));
	}

	[Theory]
	[InlineData(SessionState.Initializing, InjectionState.NotReady, true, false)]
	[InlineData(SessionState.ClientReady, InjectionState.Ready, true, true)]
	[InlineData(SessionState.Injecting, InjectionState.Injecting, true, true)]
	[InlineData(SessionState.Injected, InjectionState.Injected, true, true)]
	[InlineData(SessionState.Failed, InjectionState.Injected, true, true)]
	[InlineData(SessionState.ClientReady, InjectionState.Ready, false, false)]
	public void ShouldConfirmClose_OnlyPromptsForActiveRuneScapeClients(
		SessionState state,
		InjectionState injectionState,
		bool isRuneScapeClient,
		bool expected)
	{
		var coordinator = new SessionLifecycleCoordinatorService();
		var session = NewSession();
		session.SessionType = isRuneScapeClient ? SessionType.RuneScape : SessionType.ExternalScript;
		session.UpdateState(state);
		session.UpdateInjectionState(injectionState);

		Assert.Equal(expected, coordinator.ShouldConfirmClose(session));
	}

	[Fact]
	public void IsUnexpectedExit_RequiresRuneScapeClientActiveStateNoCloseAndExitedProcess()
	{
		var coordinator = new SessionLifecycleCoordinatorService();
		var activeSession = NewSession();
		activeSession.UpdateState(SessionState.ClientReady);

		Assert.True(coordinator.IsUnexpectedExit(activeSession, _ => true));
		Assert.False(coordinator.IsUnexpectedExit(activeSession, _ => false));

		coordinator.TryBeginClose(activeSession);
		Assert.False(coordinator.IsUnexpectedExit(activeSession, _ => true));
		coordinator.CompleteClose(activeSession);

		activeSession.UpdateState(SessionState.Closed);
		Assert.False(coordinator.IsUnexpectedExit(activeSession, _ => true));

		var externalSession = NewSession();
		externalSession.SessionType = SessionType.ExternalScript;
		externalSession.UpdateState(SessionState.ClientReady);
		Assert.False(coordinator.IsUnexpectedExit(externalSession, _ => true));
	}

	private static SessionModel NewSession()
	{
		return new SessionModel
		{
			Id = Guid.NewGuid(),
			Name = "Session"
		};
	}
}
