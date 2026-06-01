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
	public async Task ReassertInputPassthroughAsync_ReassertsInputOnlyWhenMenuHidden()
	{
		using var process = Process.GetCurrentProcess();
		var client = new FakeOrbitCommandClient();
		var service = new MesharpSessionCommandService(client);
		var hiddenMenu = CreateInjectedSession(process);
		var visibleMenu = CreateInjectedSession(process);
		visibleMenu.SetNativeDebugMenuVisible(true);

		await service.ReassertInputPassthroughAsync(new[] { hiddenMenu, visibleMenu });

		Assert.Equal(
			new[] { "InputMode:1", "DebugMenu:False", "FocusSpoof:False", "DebugMenu:True", "FocusSpoof:False" },
			client.Calls);
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
			return Task.FromResult(DebugMenuResult);
		}

		public Task<OrbitRuntimeStatus?> QueryStatusAsync(int processId, string? scriptId = null, CancellationToken cancellationToken = default)
			=> Task.FromResult<OrbitRuntimeStatus?>(null);
	}
}
