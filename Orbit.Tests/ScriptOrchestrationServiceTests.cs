using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ScriptOrchestrationServiceTests
{
	[Fact]
	public async Task LoadAsync_RecordsScriptOnlyAfterRuntimeConfirmsItIsLoaded()
	{
		var scriptPath = CreateTempScriptFile();
		var scriptId = ScriptManagerService.DeriveScriptIdFromPath(scriptPath);
		using var process = Process.GetCurrentProcess();
		var session = CreateInjectedSession(process);
		var commandClient = new FakeOrbitCommandClient
		{
			Status = BuildLoadedStatus(process.Id, scriptId, scriptPath)
		};
		var service = new ScriptOrchestrationService(
			new ScriptManagerService(),
			new ConsoleLogService(),
			commandClient);

		var loaded = await service.LoadAsync(session, scriptPath);

		Assert.True(loaded);
		Assert.Equal(scriptPath, session.ActiveScriptPath);
		Assert.Equal(scriptId, session.ActiveScriptId);
		Assert.True(commandClient.StartRuntimeCalls > 0);
		Assert.True(commandClient.ReloadCalls > 0);
	}

	[Fact]
	public async Task LoadAsync_ReturnsFalseWhenRuntimeDoesNotConfirmScript()
	{
		var scriptPath = CreateTempScriptFile();
		using var process = Process.GetCurrentProcess();
		var session = CreateInjectedSession(process);
		var commandClient = new FakeOrbitCommandClient
		{
			Status = new OrbitRuntimeStatus(true, process.Id, true, null, "DASHBOARD_V1\nCOUNT\t0")
		};
		var service = new ScriptOrchestrationService(
			new ScriptManagerService(),
			new ConsoleLogService(),
			commandClient);

		var loaded = await service.LoadAsync(session, scriptPath);

		Assert.False(loaded);
		Assert.False(session.HasActiveScript);
		Assert.StartsWith("Error:", session.ScriptRuntimeStatus, StringComparison.Ordinal);
	}

	[Fact]
	public async Task LoadAsync_UnloadsExistingScriptBeforeSwitchingToNewScript()
	{
		var oldScriptPath = CreateTempScriptFile();
		var oldScriptId = ScriptManagerService.DeriveScriptIdFromPath(oldScriptPath);
		var newScriptPath = CreateTempScriptFile();
		var newScriptId = ScriptManagerService.DeriveScriptIdFromPath(newScriptPath);
		using var process = Process.GetCurrentProcess();
		var session = CreateInjectedSession(process);
		session.SetScriptLoaded(oldScriptPath, oldScriptId);
		var commandClient = new FakeOrbitCommandClient
		{
			Status = BuildLoadedStatus(process.Id, newScriptId, newScriptPath)
		};
		var service = new ScriptOrchestrationService(
			new ScriptManagerService(),
			new ConsoleLogService(),
			commandClient);

		var loaded = await service.LoadAsync(session, newScriptPath);

		Assert.True(loaded);
		Assert.Equal(1, commandClient.UnloadCalls);
		Assert.Equal(1, commandClient.ReloadCalls);
		Assert.Equal(newScriptPath, session.ActiveScriptPath);
		Assert.Equal(newScriptId, session.ActiveScriptId);
	}

	[Fact]
	public async Task UnloadActiveScriptAsync_ReturnsFalseWhenRuntimeStillReportsScriptLoaded()
	{
		var scriptPath = CreateTempScriptFile();
		var scriptId = ScriptManagerService.DeriveScriptIdFromPath(scriptPath);
		using var process = Process.GetCurrentProcess();
		var session = CreateInjectedSession(process);
		session.SetScriptLoaded(scriptPath, scriptId);
		var commandClient = new FakeOrbitCommandClient
		{
			Status = BuildLoadedStatus(process.Id, scriptId, scriptPath)
		};
		var service = new ScriptOrchestrationService(
			new ScriptManagerService(),
			new ConsoleLogService(),
			commandClient);

		var unloaded = await service.UnloadActiveScriptAsync(session);

		Assert.False(unloaded);
		Assert.True(session.HasActiveScript);
		Assert.StartsWith("Error:", session.ScriptRuntimeStatus, StringComparison.Ordinal);
	}

	private static SessionModel CreateInjectedSession(Process process)
	{
		var session = new SessionModel
		{
			Id = Guid.NewGuid(),
			Name = "Test Session",
			CreatedAt = DateTime.UtcNow,
			RSProcess = process
		};
		session.UpdateState(SessionState.ClientReady);
		session.UpdateInjectionState(InjectionState.Ready);
		session.UpdateInjectionState(InjectionState.Injecting);
		session.UpdateInjectionState(InjectionState.Injected);
		return session;
	}

	private static string CreateTempScriptFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"OrbitTest_{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, Array.Empty<byte>());
		return path;
	}

	private static OrbitRuntimeStatus BuildLoadedStatus(int processId, string scriptId, string scriptPath)
	{
		var scriptsInfo = string.Join('\n',
			"DASHBOARD_V1",
			"COUNT\t1",
			$"SCRIPT\t{scriptId}\tRunning\t{Path.GetFileNameWithoutExtension(scriptPath)}\t1.0.0.0\t{scriptPath}\tloaded\tupdated\t\tAlive");
		return new OrbitRuntimeStatus(true, processId, true, scriptId, scriptsInfo);
	}

	private sealed class FakeOrbitCommandClient : IOrbitCommandClient
	{
		public OrbitRuntimeStatus? Status { get; init; }
		public int StartRuntimeCalls { get; private set; }
		public int ReloadCalls { get; private set; }
		public int UnloadCalls { get; private set; }

		public Task<OrbitCommandResponse> SendLoadWithRetryAsync(string scriptPath, string scriptId, int? processId, int maxAttempts = 4, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(new OrbitCommandResponse(true, Status: Status));

		public Task<OrbitCommandResponse> SendReloadWithRetryAsync(string scriptPath, string scriptId, int? processId, int maxAttempts = 4, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			ReloadCalls++;
			return Task.FromResult(new OrbitCommandResponse(true, Status: Status));
		}

		public Task<OrbitCommandResponse> SendUnloadScriptWithRetryAsync(string scriptId, int? processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			UnloadCalls++;
			return Task.FromResult(new OrbitCommandResponse(true, Status: Status));
		}

		public Task<OrbitCommandResponse> SendStartRuntimeWithRetryAsync(int? processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
		{
			StartRuntimeCalls++;
			return Task.FromResult(new OrbitCommandResponse(true, Status: Status));
		}

		public Task<bool> SendInputModeWithRetryAsync(int mode, int processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(true);

		public Task<bool> SendFocusSpoofWithRetryAsync(bool enabled, int processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(true);

		public Task<bool> SendDebugMenuVisibleWithRetryAsync(bool visible, int processId, int maxAttempts = 3, TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(true);

		public Task<OrbitRuntimeStatus?> QueryStatusAsync(int processId, string? scriptId = null, CancellationToken cancellationToken = default)
			=> Task.FromResult(Status);
	}
}
