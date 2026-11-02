using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionManagerShutdownTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ShutdownClient_CompletesCleanupBeforeDisposingProcess(bool forceKill)
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = forceKill
				? "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\""
				: "-NoProfile -NonInteractive -Command \"exit 0\"",
			UseShellExecute = false,
			CreateNoWindow = true
		})!;
		using var observer = Process.GetProcessById(process.Id);
		if (!forceKill) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
		var session = new SessionModel { Id = Guid.NewGuid(), Name = "exited-client", RSProcess = process };
		session.UpdateState(SessionState.ClientReady);
		session.SetScriptLoaded("test.dll", "test");
		try
		{
			await new SessionManagerService().ShutdownSessionAsync(session, forceKillOnTimeout: forceKill);
		}
		finally
		{
			// An independent wrapper also permits cleanup if the assertion path fails after disposal.
			if (!observer.HasExited)
			{
				observer.Kill();
				await observer.WaitForExitAsync();
			}
		}
		Assert.Equal(SessionState.Closed, session.State);
		Assert.Equal(InjectionState.NotReady, session.InjectionState);
		Assert.Null(session.RSProcess);
		Assert.Null(session.ActiveScriptId);
		Assert.Equal(nint.Zero, session.ExternalHandle);
		Assert.Throws<InvalidOperationException>(() => process.Id);
	}

	[Fact]
	public async Task ClosingExternalWindow_DoesNotShutDownItsProcess()
	{
		using var observer = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"",
			UseShellExecute = false,
			CreateNoWindow = true
		})!;
		try
		{
			var session = new SessionModel { Id = Guid.NewGuid(), SessionType = SessionType.ExternalScript, RSProcess = Process.GetProcessById(observer.Id) };
			session.UpdateState(SessionState.ClientReady);
			await new SessionManagerService().ShutdownSessionAsync(session, forceKillOnTimeout: true);
			Assert.False(observer.HasExited);
			Assert.Equal(SessionState.Closed, session.State);
			Assert.Null(session.RSProcess);
		}
		finally
		{
			if (!observer.HasExited) observer.Kill();
			await observer.WaitForExitAsync();
		}
	}
	[Fact]
	public async Task ShutdownFailure_PreservesInjectedClientAndUnconfirmedScript()
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"",
			UseShellExecute = false,
			CreateNoWindow = true
		})!;
		var integrationEnabled = Settings.Default.MesharpIntegrationEnabled;
		try
		{
			Settings.Default.MesharpIntegrationEnabled = false;
			var session = new SessionModel { Id = Guid.NewGuid(), Name = "shutdown-test", RSProcess = process };
			session.UpdateState(SessionState.ClientReady);
			session.UpdateInjectionState(InjectionState.Ready);
			session.UpdateInjectionState(InjectionState.Injecting);
			session.UpdateInjectionState(InjectionState.Injected);
			session.UpdateState(SessionState.Injected);
			session.SetScriptLoaded("test.dll", "test");
			await Assert.ThrowsAsync<TimeoutException>(() => new SessionManagerService()
				.ShutdownSessionAsync(session, TimeSpan.FromMilliseconds(20)));
			Assert.False(process.HasExited);
			Assert.Equal(SessionState.Injected, session.State);
			Assert.Equal(InjectionState.Injected, session.InjectionState);
			Assert.Equal("test", session.ActiveScriptId);
			Assert.Same(process, session.RSProcess);
		}
		finally
		{
			Settings.Default.MesharpIntegrationEnabled = integrationEnabled;
			if (!process.HasExited) process.Kill();
			await process.WaitForExitAsync();
		}
	}
}
