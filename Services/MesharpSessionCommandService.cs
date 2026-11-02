using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Models;

namespace Orbit.Services;

public sealed class MesharpSessionCommandService
{
	private readonly IOrbitCommandClient commandClient;

	public MesharpSessionCommandService(IOrbitCommandClient commandClient)
	{
		this.commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
	}

	public bool CanToggleNativeDebugMenu(bool mesharpIntegrationEnabled, SessionModel? session)
		=> mesharpIntegrationEnabled && IsInjectedLiveSession(session);

	private static bool IsInjectedLiveSession(SessionModel? session)
	{
		if (session == null || session.State is SessionState.Closed or SessionState.ShuttingDown ||
			session.InjectionState != InjectionState.Injected) return false;
		try { return session.RSProcess != null && !session.RSProcess.HasExited; }
		catch (InvalidOperationException) { return false; }
		catch (System.ComponentModel.Win32Exception) { return false; }
	}

	public async Task ReassertInputPassthroughAsync(IEnumerable<SessionModel?> sessions)
	{
		foreach (var session in GetInjectedLiveSessions(sessions))
			await RunSerializedAsync(session, null, null).ConfigureAwait(true);
	}

	public Task SetNativeDebugMenuVisibleAsync(
		SessionModel? session,
		bool visible,
		Func<SessionModel, Task>? revealAndFocusAsync = null)
		=> session == null ? Task.CompletedTask : RunSerializedAsync(session, visible, revealAndFocusAsync);

	public Task ToggleNativeDebugMenuAsync(SessionModel session, Func<SessionModel, Task>? revealAndFocusAsync = null)
		=> RunSerializedAsync(session, null, revealAndFocusAsync, toggle: true);

	internal Task ApplyInjectionPreferenceAsync(SessionModel session, bool hide)
		=> RunSerializedAsync(session, !hide, null, startRuntime: false);

	private async Task RunSerializedAsync(SessionModel session, bool? requestedVisible, Func<SessionModel, Task>? revealAndFocusAsync, bool startRuntime = true, bool toggle = false)
	{
		var process = session.RSProcess;
		var token = session.WorkCancellation;
		var entered = false;
		var previousVisible = false;
		var pending = false;
		bool? confirmedVisible = null;
		try
		{
			await session.NativeCommandGate.WaitAsync(token).ConfigureAwait(true);
			entered = true;
			if (!IsCurrent()) return;
			var pid = process!.Id;
			previousVisible = session.NativeDebugMenuVisible;
			if (toggle) requestedVisible = !previousVisible;
			var visible = requestedVisible ?? previousVisible;
			if (requestedVisible.HasValue)
			{
				await SetVisibleAsync(visible, "native menu command pending");
				pending = true;
			}
			if (visible && revealAndFocusAsync != null)
				await revealAndFocusAsync(session).ConfigureAwait(true);
			if (!IsCurrent()) return;

			if (visible && requestedVisible.HasValue && startRuntime)
			{
				await commandClient.SendStartRuntimeWithRetryAsync(pid, maxAttempts: 3,
					initialDelay: TimeSpan.FromMilliseconds(120), cancellationToken: token).ConfigureAwait(true);
				if (!IsCurrent()) return;
			}

			// Restore input mode together with visibility, including when reasserting a visible menu.
			if (visible || !requestedVisible.HasValue)
			{
				await commandClient.SendInputModeWithRetryAsync(visible ? 0 : 1, pid, maxAttempts: 3,
					initialDelay: TimeSpan.FromMilliseconds(100), cancellationToken: token).ConfigureAwait(true);
				if (!IsCurrent()) return;
			}
			var applied = await commandClient.SendDebugMenuVisibleWithRetryAsync(visible, pid, maxAttempts: 3,
				initialDelay: TimeSpan.FromMilliseconds(100), cancellationToken: token).ConfigureAwait(true);
			if (!IsCurrent()) return;
			confirmedVisible = applied ? visible : previousVisible;
			if (!applied || (!visible && requestedVisible.HasValue))
			{
				await commandClient.SendInputModeWithRetryAsync(confirmedVisible.Value ? 0 : 1, pid, maxAttempts: 3,
					initialDelay: TimeSpan.FromMilliseconds(100), cancellationToken: token).ConfigureAwait(true);
				if (!IsCurrent()) return;
			}
			await commandClient.SendFocusSpoofWithRetryAsync(false, pid, maxAttempts: 3,
				initialDelay: TimeSpan.FromMilliseconds(100), cancellationToken: token).ConfigureAwait(true);
			if (requestedVisible.HasValue)
				await SetVisibleAsync(applied ? visible : previousVisible,
					applied ? "native menu command applied" : "native menu command failed");
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception)
		{
			if (pending) await SetVisibleAsync(confirmedVisible ?? previousVisible, "native menu command threw");
		}
		finally { if (entered) session.NativeCommandGate.Release(); }

		bool IsCurrent() => !token.IsCancellationRequested && ReferenceEquals(session.RSProcess, process) && IsInjectedLiveSession(session);

		async Task SetVisibleAsync(bool visible, string reason)
		{
			var dispatcher = session.HostControl?.Dispatcher;
			if (dispatcher != null && !dispatcher.CheckAccess())
				await dispatcher.InvokeAsync(() => { if (IsCurrent()) session.SetNativeDebugMenuVisible(visible, reason); });
			else if (IsCurrent()) session.SetNativeDebugMenuVisible(visible, reason);
		}
	}

	public async Task SetNativeDebugMenuVisibleAsync(IEnumerable<SessionModel?> sessions, bool visible)
	{
		foreach (var session in GetInjectedLiveSessions(sessions))
			await SetNativeDebugMenuVisibleAsync(session, visible).ConfigureAwait(true);
	}

	public Task ApplyNativeDebugMenuInjectionPreferenceAsync(IEnumerable<SessionModel?> sessions, bool hideOnInject)
		=> SetNativeDebugMenuVisibleAsync(sessions, !hideOnInject);

	private static List<SessionModel> GetInjectedLiveSessions(IEnumerable<SessionModel?> sessions)
		=> sessions.Where(IsInjectedLiveSession).Cast<SessionModel>().ToList();
}
