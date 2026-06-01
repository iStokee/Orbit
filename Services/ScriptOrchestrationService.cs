using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Logging;
using Orbit.Models;

namespace Orbit.Services;

public sealed class ScriptOrchestrationService
{
	private readonly ScriptManagerService _scriptManager;
	private readonly ConsoleLogService _consoleLog;
	private readonly IOrbitCommandClient _commandClient;

	public ScriptOrchestrationService(
		ScriptManagerService scriptManager,
		ConsoleLogService consoleLog,
		IOrbitCommandClient commandClient)
	{
		_scriptManager = scriptManager ?? throw new ArgumentNullException(nameof(scriptManager));
		_consoleLog = consoleLog ?? throw new ArgumentNullException(nameof(consoleLog));
		_commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
	}

	public async Task<bool> LoadAsync(SessionModel targetSession, string scriptPath, CancellationToken cancellationToken = default)
		=> await LoadOrReloadAsync(targetSession, scriptPath, "load", cancellationToken).ConfigureAwait(false);

	public async Task<bool> ReloadAsync(SessionModel targetSession, string scriptPath, CancellationToken cancellationToken = default)
		=> await LoadOrReloadAsync(targetSession, scriptPath, "reload", cancellationToken).ConfigureAwait(false);

	public async Task<bool> UnloadActiveScriptAsync(SessionModel targetSession, string? nextPath = null, CancellationToken cancellationToken = default)
	{
		if (targetSession.RSProcess == null)
		{
			return false;
		}

		var scriptIdToUnload = !string.IsNullOrWhiteSpace(targetSession.ActiveScriptId)
			? targetSession.ActiveScriptId!
			: ScriptManagerService.DeriveScriptIdFromPath(targetSession.ActiveScriptPath);

		_consoleLog.Append(
			string.IsNullOrWhiteSpace(nextPath)
				? $"[OrbitCmd] Unloading active script '{targetSession.ActiveScriptName}' in session '{targetSession.Name}'."
				: $"[OrbitCmd] Unloading active script '{targetSession.ActiveScriptName}' before loading '{Path.GetFileNameWithoutExtension(nextPath)}' in session '{targetSession.Name}'.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		var response = await _commandClient
			.SendUnloadScriptWithRetryAsync(scriptIdToUnload, targetSession.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		if (!response.Success)
		{
			_consoleLog.Append(
				$"[OrbitCmd] Failed to unload previous script in session '{targetSession.Name}': {response.ErrorMessage ?? "unknown error"}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return false;
		}

		var status = response.Status
			?? await _commandClient.QueryStatusAsync(targetSession.RSProcess.Id, scriptIdToUnload, cancellationToken).ConfigureAwait(false);
		if (status?.IsScriptLoaded(scriptIdToUnload) == true)
		{
			targetSession.SetScriptRuntimeError($"Script '{scriptIdToUnload}' still appears loaded after unload.");
			return false;
		}

		targetSession.SetScriptStopped();
		return true;
	}

	private async Task<bool> LoadOrReloadAsync(SessionModel targetSession, string scriptPath, string action, CancellationToken cancellationToken)
	{
		if (targetSession == null)
		{
			throw new ArgumentNullException(nameof(targetSession));
		}

		if (targetSession.InjectionState != InjectionState.Injected)
		{
			_consoleLog.Append($"[OrbitCmd] Session '{targetSession.Name}' is not injected; {action} aborted.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return false;
		}

		if (targetSession.RSProcess == null)
		{
			_consoleLog.Append($"[OrbitCmd] Session '{targetSession.Name}' has no active process; {action} aborted.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return false;
		}

		if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
		{
			_consoleLog.Append($"[OrbitCmd] Script not found: {scriptPath}", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return false;
		}

		var scriptId = ScriptManagerService.DeriveScriptIdFromPath(scriptPath);
		_scriptManager.AddOrUpdateScript(scriptPath, null, null, scriptId);

		var activePath = targetSession.ActiveScriptPath;
		var switchingScripts = !string.IsNullOrWhiteSpace(activePath) &&
			!string.Equals(activePath, scriptPath, StringComparison.OrdinalIgnoreCase);
		if (switchingScripts)
		{
			var unloaded = await UnloadActiveScriptAsync(targetSession, scriptPath, cancellationToken).ConfigureAwait(false);
			if (!unloaded)
			{
				targetSession.SetScriptRuntimeError($"Failed to unload previous script before {action}ing a new one.");
				return false;
			}
		}

		var pid = targetSession.RSProcess.Id;
		targetSession.SetScriptRuntimePending(action);
		_consoleLog.Append(
			$"[OrbitCmd] Requesting {action} for '{scriptPath}' as scriptId '{scriptId}' (session '{targetSession.Name}' PID {pid})",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		var runtimeReady = await _commandClient
			.SendStartRuntimeWithRetryAsync(pid, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		if (!runtimeReady.Success)
		{
			_consoleLog.Append(
				$"[OrbitCmd] Unable to start ME .NET runtime for '{targetSession.Name}'. {Capitalize(action)} may fail.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
		}

		var commandResponse = await _commandClient
			.SendReloadWithRetryAsync(scriptPath, scriptId, pid, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		if (!commandResponse.Success)
		{
			_consoleLog.Append(
				$"[OrbitCmd] Failed to send {action} command: {commandResponse.ErrorMessage ?? "unknown error"}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			targetSession.SetScriptRuntimeError($"Failed to {action} '{Path.GetFileNameWithoutExtension(scriptPath)}'.");
			return false;
		}

		var status = commandResponse.Status
			?? await _commandClient.QueryStatusAsync(pid, scriptId, cancellationToken).ConfigureAwait(false);
		if (status?.IsScriptLoaded(scriptId) != true)
		{
			targetSession.SetScriptRuntimeError($"ME did not confirm '{Path.GetFileNameWithoutExtension(scriptPath)}' is loaded.");
			return false;
		}

		targetSession.SetScriptLoaded(status.GetScriptPath(scriptId) ?? scriptPath, scriptId);
		return true;
	}

	private static string Capitalize(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return value;
		}

		return char.ToUpperInvariant(value[0]) + value[1..];
	}
}
