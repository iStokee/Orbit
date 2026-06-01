using System;
using System.Collections.Generic;
using System.Linq;
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
	{
		return mesharpIntegrationEnabled &&
			   session != null &&
			   session.InjectionState == InjectionState.Injected &&
			   session.RSProcess != null &&
			   !session.RSProcess.HasExited;
	}

	public Task ReassertInputPassthroughAsync(IEnumerable<SessionModel?> sessions)
	{
		var targets = GetInjectedLiveSessions(sessions);
		if (targets.Count == 0)
		{
			return Task.CompletedTask;
		}

		return Task.Run(async () =>
		{
			foreach (var session in targets)
			{
				try
				{
					var process = session.RSProcess;
					if (process == null || process.HasExited)
					{
						continue;
					}

					var targetDebugMenuVisible = session.NativeDebugMenuVisible;
					if (!targetDebugMenuVisible)
					{
						await commandClient
							.SendInputModeWithRetryAsync(1, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
							.ConfigureAwait(false);
					}

					await commandClient
						.SendDebugMenuVisibleWithRetryAsync(targetDebugMenuVisible, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
						.ConfigureAwait(false);

					await commandClient
						.SendFocusSpoofWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
						.ConfigureAwait(false);
				}
				catch
				{
					// Best-effort only; individual sessions may be shutting down.
				}
			}
		});
	}

	public async Task SetNativeDebugMenuVisibleAsync(
		SessionModel? session,
		bool visible,
		Func<SessionModel, Task>? revealAndFocusAsync = null)
	{
		if (session == null)
		{
			return;
		}

		var process = session.RSProcess;
		if (process == null || process.HasExited)
		{
			session.SetNativeDebugMenuVisible(false, "process unavailable");
			return;
		}

		var previousVisible = session.NativeDebugMenuVisible;
		session.SetNativeDebugMenuVisible(visible, "native menu command pending");

		try
		{
			if (visible && revealAndFocusAsync != null)
			{
				await revealAndFocusAsync(session).ConfigureAwait(true);
			}

			var applied = false;
			if (visible)
			{
				await commandClient
					.SendStartRuntimeWithRetryAsync(process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(120))
					.ConfigureAwait(false);

				await commandClient
					.SendInputModeWithRetryAsync(0, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
					.ConfigureAwait(false);

				applied = await commandClient
					.SendDebugMenuVisibleWithRetryAsync(true, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
					.ConfigureAwait(false);

				await commandClient
					.SendFocusSpoofWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
					.ConfigureAwait(false);
			}
			else
			{
				applied = await commandClient
					.SendDebugMenuVisibleWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
					.ConfigureAwait(false);

				await commandClient
					.SendInputModeWithRetryAsync(1, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
					.ConfigureAwait(false);

				await commandClient
					.SendFocusSpoofWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
					.ConfigureAwait(false);
			}

			session.SetNativeDebugMenuVisible(
				applied ? visible : previousVisible,
				applied ? "native menu command applied" : "native menu command failed");
		}
		catch
		{
			session.SetNativeDebugMenuVisible(previousVisible, "native menu command threw");
		}
	}

	public async Task SetNativeDebugMenuVisibleAsync(IEnumerable<SessionModel?> sessions, bool visible)
	{
		foreach (var session in GetInjectedLiveSessions(sessions))
		{
			await SetNativeDebugMenuVisibleAsync(session, visible).ConfigureAwait(false);
		}
	}

	public Task ApplyNativeDebugMenuInjectionPreferenceAsync(IEnumerable<SessionModel?> sessions, bool hideOnInject)
	{
		return SetNativeDebugMenuVisibleAsync(sessions, !hideOnInject);
	}

	private static List<SessionModel> GetInjectedLiveSessions(IEnumerable<SessionModel?> sessions)
	{
		return sessions
			.Where(s => s != null && s.InjectionState == InjectionState.Injected && s.RSProcess != null && !s.RSProcess.HasExited)
			.Cast<SessionModel>()
			.ToList();
	}
}
