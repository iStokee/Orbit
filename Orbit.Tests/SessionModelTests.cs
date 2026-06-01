using System;
using Orbit.Models;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionModelTests
{
	[Fact]
	public void RecordInjectionFailure_LeavesClientReadyAndStoresError()
	{
		var session = new SessionModel();

		session.UpdateState(SessionState.ClientReady);
		session.UpdateInjectionState(InjectionState.Ready);
		session.RecordInjectionFailure(new InvalidOperationException("inject failed"));

		Assert.Equal(SessionState.ClientReady, session.State);
		Assert.Equal(InjectionState.Failed, session.InjectionState);
		Assert.Equal("inject failed", session.LastError);
		Assert.False(session.IsHealthy);
	}

	[Fact]
	public void ScriptLifecycle_TracksLoadedStoppedAndErrors()
	{
		var session = new SessionModel();

		session.SetScriptRuntimePending("Loading");
		Assert.Equal("Loading...", session.ScriptRuntimeStatus);

		session.SetScriptLoaded("C:\\Scripts\\Alpha.dll", "alpha");
		Assert.True(session.HasActiveScript);
		Assert.Equal("Alpha", session.ActiveScriptName);
		Assert.Equal("alpha", session.ActiveScriptId);
		Assert.Contains("Loaded [alpha]", session.ScriptRuntimeStatus, StringComparison.Ordinal);

		session.SetScriptRuntimeError("boom");
		Assert.Equal("Error: boom", session.ScriptRuntimeStatus);

		session.SetScriptStopped();
		Assert.False(session.HasActiveScript);
		Assert.Null(session.ActiveScriptPath);
		Assert.Null(session.ActiveScriptId);
		Assert.Equal("No script loaded", session.ScriptRuntimeStatus);
	}

	[Fact]
	public void NativeDebugMenuVisibility_ResetsWhenInjectionLeavesInjectedState()
	{
		var session = new SessionModel();

		session.UpdateInjectionState(InjectionState.Ready);
		session.UpdateInjectionState(InjectionState.Injecting);
		session.UpdateInjectionState(InjectionState.Injected);
		session.SetNativeDebugMenuVisible(true);

		session.UpdateInjectionState(InjectionState.NotReady);

		Assert.False(session.NativeDebugMenuVisible);
		Assert.Equal("Hidden", session.NativeDebugMenuStatus);
	}
}
