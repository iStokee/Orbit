using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class MesharpSessionCommandServiceTests
{
	[Fact]
	public void CanToggleNativeDebugMenu_RequiresIntegrationInjectedLiveProcess()
	{
		using var process = Process.GetCurrentProcess();
		var service = new MesharpSessionCommandService(new FakeOrbitCommandClient());
		var session = CreateInjectedSession(process);

		Assert.True(service.CanToggleNativeDebugMenu(true, session));
		Assert.False(service.CanToggleNativeDebugMenu(false, session));

		session.UpdateInjectionState(InjectionState.Ready);
		Assert.False(service.CanToggleNativeDebugMenu(true, session));
	}

	[Fact]
	public async Task SetNativeDebugMenuVisibleAsync_ShowMenu_SendsExpectedCommandSequenceAndMarksApplied()
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient();
		var service = new MesharpSessionCommandService(client);
		var session = CreateInjectedSession(process);
		var revealed = false;

		await service.SetNativeDebugMenuVisibleAsync(
			session,
			visible: true,
			revealAndFocusAsync: _ =>
			{
				revealed = true;
				return Task.CompletedTask;
			});

		Assert.True(revealed);
		Assert.True(session.NativeDebugMenuVisible);
		Assert.Equal(
			new[] { "StartRuntime", "InputMode:0", "DebugMenu:True", "FocusSpoof:False" },
			client.Calls);
	}

	[Fact]
	public async Task SetNativeDebugMenuVisibleAsync_HideMenu_SendsExpectedCommandSequenceAndMarksApplied()
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient();
		var service = new MesharpSessionCommandService(client);
		var session = CreateInjectedSession(process);
		session.SetNativeDebugMenuVisible(true);

		await service.SetNativeDebugMenuVisibleAsync(session, visible: false);

		Assert.False(session.NativeDebugMenuVisible);
		Assert.Equal(
			new[] { "DebugMenu:False", "InputMode:1", "FocusSpoof:False" },
			client.Calls);
	}

	[Fact]
	public async Task SetNativeDebugMenuVisibleAsync_RevertsStateWhenDebugMenuCommandFails()
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient { DebugMenuResult = false };
		var service = new MesharpSessionCommandService(client);
		var session = CreateInjectedSession(process);

		await service.SetNativeDebugMenuVisibleAsync(session, visible: true);

		Assert.False(session.NativeDebugMenuVisible);
	}

	[Fact]
	public async Task ReassertInputPassthroughAsync_RestoresInputModeForBothMenuStates()
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient();
		var service = new MesharpSessionCommandService(client);
		var hiddenMenu = CreateInjectedSession(process);
		var visibleMenu = CreateInjectedSession(process);
		visibleMenu.SetNativeDebugMenuVisible(true);

		await service.ReassertInputPassthroughAsync(new[] { hiddenMenu, visibleMenu });

		Assert.Equal(
			new[] { "InputMode:1", "DebugMenu:False", "FocusSpoof:False", "InputMode:0", "DebugMenu:True", "FocusSpoof:False" },
			client.Calls);
	}

	[Fact]
	public async Task ConcurrentCommands_AreSerializedAcrossServiceInstances()
	{
		using var process = Process.GetCurrentProcess();
		var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new FakeOrbitCommandClient { DebugMenuHandler = (visible, _) => visible ? blocked.Task : Task.FromResult(true) };
		var session = CreateInjectedSession(process);
		var show = new MesharpSessionCommandService(client).SetNativeDebugMenuVisibleAsync(session, true);
		var hide = new MesharpSessionCommandService(client).SetNativeDebugMenuVisibleAsync(session, false);
		Assert.DoesNotContain("DebugMenu:False", client.Calls);
		Assert.False(hide.IsCompleted);
		blocked.SetResult(true);
		await Task.WhenAll(show, hide).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.False(session.NativeDebugMenuVisible);
		Assert.Equal(new[] { "StartRuntime", "InputMode:0", "DebugMenu:True", "FocusSpoof:False",
			"DebugMenu:False", "InputMode:1", "FocusSpoof:False" }, client.Calls);
	}

	[Fact]
	public async Task Reassert_WaitsForPendingMenuCommandAndReadsItsFinalState()
	{
		using var process = Process.GetCurrentProcess();
		var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new FakeOrbitCommandClient { DebugMenuHandler = (visible, _) => visible ? blocked.Task : Task.FromResult(true) };
		var session = CreateInjectedSession(process);
		var service = new MesharpSessionCommandService(client);
		var show = service.SetNativeDebugMenuVisibleAsync(session, true);
		var reassert = service.ReassertInputPassthroughAsync(new[] { session });
		Assert.False(reassert.IsCompleted);
		Assert.Equal(3, client.Calls.Count);
		blocked.SetResult(true);
		await Task.WhenAll(show, reassert).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(session.NativeDebugMenuVisible);
		Assert.Equal(new[] { "StartRuntime", "InputMode:0", "DebugMenu:True", "FocusSpoof:False",
			"InputMode:0", "DebugMenu:True", "FocusSpoof:False" }, client.Calls);
	}

	[Fact]
	public async Task InjectionPreference_DoesNotOverrideRuntimeStartupPolicy()
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient();
		var session = CreateInjectedSession(process);
		await new MesharpSessionCommandService(client).ApplyInjectionPreferenceAsync(session, hide: false);
		Assert.True(session.NativeDebugMenuVisible);
		Assert.DoesNotContain("StartRuntime", client.Calls);
	}

	[Fact]
	public async Task ThreeRapidToggles_ApplyEachToggleToTheLastCompletedState()
	{
		using var process = Process.GetCurrentProcess();
		var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new FakeOrbitCommandClient { DebugMenuHandler = (_, _) => blocked.Task };
		var session = CreateInjectedSession(process);
		var service = new MesharpSessionCommandService(client);
		var first = service.ToggleNativeDebugMenuAsync(session);
		var second = service.ToggleNativeDebugMenuAsync(session);
		var third = service.ToggleNativeDebugMenuAsync(session);
		blocked.SetResult(true);
		await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(session.NativeDebugMenuVisible);
		Assert.Equal(new[] { "DebugMenu:True", "DebugMenu:False", "DebugMenu:True" },
			client.Calls.FindAll(call => call.StartsWith("DebugMenu:", StringComparison.Ordinal)));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task FailedVisibilityCommand_RestoresInputModeToPreviousMenuState(bool visible)
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient { DebugMenuResult = false };
		var session = CreateInjectedSession(process);
		session.SetNativeDebugMenuVisible(!visible);
		await new MesharpSessionCommandService(client).SetNativeDebugMenuVisibleAsync(session, visible);
		Assert.Equal(!visible, session.NativeDebugMenuVisible);
		Assert.Equal(visible ? "InputMode:1" : "InputMode:0", client.Calls[^2]);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ShutdownDuringCommand_DoesNotPublishOrRollbackStaleVisibility(bool result)
	{
		using var process = Process.GetCurrentProcess();
		var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken observed = default;
		var client = new FakeOrbitCommandClient { DebugMenuHandler = (_, token) => { observed = token; return blocked.Task; } };
		var session = CreateInjectedSession(process);
		var command = new MesharpSessionCommandService(client).SetNativeDebugMenuVisibleAsync(session, true);
		session.UpdateState(SessionState.ShuttingDown);
		session.UpdateInjectionState(InjectionState.NotReady);
		session.RSProcess = null;
		session.UpdateState(SessionState.Closed);
		Assert.True(observed.IsCancellationRequested);
		blocked.SetResult(result);
		await command.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.False(session.NativeDebugMenuVisible);
		Assert.DoesNotContain("FocusSpoof:False", client.Calls);
	}

	[Fact]
	public async Task ProcessReplacement_CancelsQueuedCommandsAndRejectsDisposedProcess()
	{
		using var process = Process.GetCurrentProcess();
		using var replacement = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient();
		var service = new MesharpSessionCommandService(client);
		var session = CreateInjectedSession(process);
		await session.NativeCommandGate.WaitAsync();
		var command = service.SetNativeDebugMenuVisibleAsync(session, true);
		session.RSProcess = replacement;
		session.NativeCommandGate.Release();
		await command.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Empty(client.Calls);
		replacement.Dispose();
		Assert.False(service.CanToggleNativeDebugMenu(true, session));
		await service.SetNativeDebugMenuVisibleAsync(session, true);
		Assert.Empty(client.Calls);
	}

	private static SessionModel CreateInjectedSession(Process process)
	{
		var session = new SessionModel
		{
			Id = Guid.NewGuid(),
			Name = "Test Session",
			RSProcess = process
		};
		session.UpdateState(SessionState.ClientReady);
		session.UpdateInjectionState(InjectionState.Ready);
		session.UpdateInjectionState(InjectionState.Injecting);
		session.UpdateInjectionState(InjectionState.Injected);
		return session;
	}

	private sealed class FakeOrbitCommandClient : IOrbitCommandClient
	{
		public List<string> Calls { get; } = new();
		public Func<bool, CancellationToken, Task<bool>>? DebugMenuHandler { get; init; }
		public bool DebugMenuResult { get; init; } = true;

		public Task<OrbitCommandResponse> SendLoadWithRetryAsync(string scriptPath, string scriptId, int? processId, int maxAttempts = 4, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(new OrbitCommandResponse(true));

		public Task<OrbitCommandResponse> SendReloadWithRetryAsync(string scriptPath, string scriptId, int? processId, int maxAttempts = 4, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(new OrbitCommandResponse(true));

		public Task<OrbitCommandResponse> SendUnloadScriptWithRetryAsync(string scriptId, int? processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(new OrbitCommandResponse(true));

		public Task<OrbitCommandResponse> SendStartRuntimeWithRetryAsync(int? processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			Calls.Add("StartRuntime");
			return Task.FromResult(new OrbitCommandResponse(true));
		}

		public Task<bool> SendInputModeWithRetryAsync(int mode, int processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			Calls.Add($"InputMode:{mode}");
			return Task.FromResult(true);
		}

		public Task<bool> SendFocusSpoofWithRetryAsync(bool enabled, int processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			Calls.Add($"FocusSpoof:{enabled}");
			return Task.FromResult(true);
		}

		public Task<bool> SendDebugMenuVisibleWithRetryAsync(bool visible, int processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			Calls.Add($"DebugMenu:{visible}");
			return DebugMenuHandler?.Invoke(visible, cancellationToken) ?? Task.FromResult(DebugMenuResult);
		}

		public Task<OrbitRuntimeStatus?> QueryStatusAsync(int processId, string? scriptId = null, CancellationToken cancellationToken = default)
			=> Task.FromResult<OrbitRuntimeStatus?>(null);
	}
}
