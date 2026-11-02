using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionAutoRelaunchServiceTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task CheckAndRelaunchAsync_RechecksPolicyAfterClose(bool shutdown)
	{
		var service = new SessionAutoRelaunchService(new SessionLifecycleCoordinatorService(), new ConsoleLogService());
		using var process = CreateExitedProcess();
		var session = NewExitedSession(process);
		var stopping = false;
		var enabled = true;
		var launches = 0;
		await service.CheckAndRelaunchAsync(new[] { session }, () => stopping, () => enabled,
			_ => { if (shutdown) stopping = true; else enabled = false; return Task.CompletedTask; },
			_ => { launches++; return Task.FromResult(true); });
		Assert.Equal(0, launches);
	}
	[Fact]
	public async Task CheckAndRelaunchAsync_DoesNothingWhenDisabled()
	{
		var service = new SessionAutoRelaunchService(new SessionLifecycleCoordinatorService(), new ConsoleLogService());
		using var process = CreateExitedProcess();
		var session = NewExitedSession(process);
		var closeCalls = 0;
		var addCalls = 0;

		await service.CheckAndRelaunchAsync(
			new[] { session },
			isShuttingDown: () => false,
			autoRelaunchEnabled: () => false,
			session =>
			{
				closeCalls++;
				return Task.CompletedTask;
			},
			name =>
			{
				addCalls++;
				return Task.FromResult(true);
			});

		Assert.Equal(0, closeCalls);
		Assert.Equal(0, addCalls);
	}

	[Fact]
	public async Task CheckAndRelaunchAsync_ClosesUnexpectedExitAndRelaunchesWithOriginalName()
	{
		var service = new SessionAutoRelaunchService(new SessionLifecycleCoordinatorService(), new ConsoleLogService());
		using var process = CreateExitedProcess();
		var session = NewExitedSession(process);
		var closeCalls = 0;
		string? relaunchedName = null;

		await service.CheckAndRelaunchAsync(
			new[] { session },
			isShuttingDown: () => false,
			autoRelaunchEnabled: () => true,
			closed =>
			{
				closeCalls++;
				Assert.Same(session, closed);
				return Task.CompletedTask;
			},
			name =>
			{
				Assert.Same(session, name);
				relaunchedName = name.Name;
				return Task.FromResult(true);
			});

		Assert.Equal(1, closeCalls);
		Assert.Equal("Crashed", relaunchedName);
	}

	[Fact]
	public async Task CheckAndRelaunchAsync_DoesNotRelaunchWhenCloseDelegateThrows()
	{
		var service = new SessionAutoRelaunchService(new SessionLifecycleCoordinatorService(), new ConsoleLogService());
		using var process = CreateExitedProcess();
		var session = NewExitedSession(process);
		var addCalls = 0;

		await service.CheckAndRelaunchAsync(
			new[] { session },
			isShuttingDown: () => false,
			autoRelaunchEnabled: () => true,
			_ => throw new InvalidOperationException("close failed"),
			name =>
			{
				addCalls++;
				return Task.FromResult(true);
			});

		Assert.Equal(0, addCalls);
	}

	private static Process CreateExitedProcess()
	{
		var process = Process.Start(new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = "/c exit 0",
			CreateNoWindow = true,
			UseShellExecute = false
		}) ?? throw new InvalidOperationException("Failed to start test process.");
		process.WaitForExit();
		return process;
	}

	private static SessionModel NewExitedSession(Process process)
	{
		var session = new SessionModel
		{
			Id = Guid.NewGuid(),
			Name = "Crashed",
			RSProcess = process
		};
		session.UpdateState(SessionState.ClientReady);
		session.UpdateInjectionState(InjectionState.Ready);
		return session;
	}
}
