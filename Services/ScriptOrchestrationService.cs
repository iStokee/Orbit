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
		=> await RunSerializedAsync(targetSession, token => LoadOrReloadAsync(targetSession, scriptPath, "load", token), cancellationToken).ConfigureAwait(true);

	public async Task<bool> ReloadAsync(SessionModel targetSession, string scriptPath, CancellationToken cancellationToken = default)
		=> await RunSerializedAsync(targetSession, token => LoadOrReloadAsync(targetSession, scriptPath, "reload", token), cancellationToken).ConfigureAwait(true);

	public Task<bool> UnloadActiveScriptAsync(SessionModel targetSession, string? nextPath = null, CancellationToken cancellationToken = default)
		=> RunSerializedAsync(targetSession, token => UnloadActiveScriptCoreAsync(targetSession, nextPath, token), cancellationToken);

	public Task<bool> LoadAsync(SessionModel session, string path, string scriptId, CancellationToken cancellationToken = default)
		=> RunSerializedAsync(session, token => LoadOrReloadAsync(session, path, "load", token, scriptId), cancellationToken);
	public Task<bool> ReloadAsync(SessionModel session, string path, string scriptId, CancellationToken cancellationToken = default)
		=> RunSerializedAsync(session, token => LoadOrReloadAsync(session, path, "reload", token, scriptId), cancellationToken);
	public Task<bool> UnloadScriptAsync(SessionModel session, string scriptId, CancellationToken cancellationToken = default)
		=> RunSerializedAsync(session, token => UnloadActiveScriptCoreAsync(session, null, token, scriptId), cancellationToken);

	private static async Task<bool> RunSerializedAsync(SessionModel session, Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(session);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.WorkCancellation);
		var entered = false;
		try
		{
			await session.ScriptCommandGate.WaitAsync(linked.Token).ConfigureAwait(true);
			entered = true;
			linked.Token.ThrowIfCancellationRequested();
			if (session.State is SessionState.Closed or SessionState.ShuttingDown) return false;
			return await operation(linked.Token).ConfigureAwait(true);
		}
		catch (OperationCanceledException) when (linked.IsCancellationRequested) { return false; }
		finally { if (entered) session.ScriptCommandGate.Release(); }
	}

	private async Task<bool> UnloadActiveScriptCoreAsync(SessionModel targetSession, string? nextPath, CancellationToken cancellationToken, string? requestedScriptId = null)
	{
		ArgumentNullException.ThrowIfNull(targetSession);
		if (targetSession.RSProcess == null)
		{
			return false;
		}

		var scriptIdToUnload = requestedScriptId ?? (!string.IsNullOrWhiteSpace(targetSession.ActiveScriptId)
			? targetSession.ActiveScriptId!
			: ScriptManagerService.DeriveScriptIdFromPath(targetSession.ActiveScriptPath));
		var pid = targetSession.RSProcess.Id;

		_consoleLog.Append(
			string.IsNullOrWhiteSpace(nextPath)
				? $"[OrbitCmd] Unloading active script '{targetSession.ActiveScriptName}' in session '{targetSession.Name}'."
				: $"[OrbitCmd] Unloading active script '{targetSession.ActiveScriptName}' before loading '{Path.GetFileNameWithoutExtension(nextPath)}' in session '{targetSession.Name}'.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		var response = await _commandClient
			.SendUnloadScriptWithRetryAsync(scriptIdToUnload, pid, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: cancellationToken)
			.ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();

		if (!response.Success)
		{
			_consoleLog.Append(
				$"[OrbitCmd] Failed to unload previous script in session '{targetSession.Name}': {response.ErrorMessage ?? "unknown error"}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return false;
		}

		var status = response.Status
			?? await _commandClient.QueryStatusAsync(pid, scriptIdToUnload, cancellationToken).ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
		if (status == null || !status.HasScriptInventory || status.ProcessId != pid)
		{
			targetSession.SetScriptRuntimeError("ME did not confirm the script inventory after unload.");
			return false;
		}
		if (status?.IsScriptLoaded(scriptIdToUnload) == true)
		{
			targetSession.SetScriptRuntimeError($"Script '{scriptIdToUnload}' still appears loaded after unload.");
			return false;
		}
		targetSession.ConfirmRuntimeStatus(status);

		if (requestedScriptId == null || string.Equals(targetSession.ActiveScriptId, scriptIdToUnload, StringComparison.OrdinalIgnoreCase))
			targetSession.SetScriptStopped();
		return true;
	}

	private async Task<bool> LoadOrReloadAsync(SessionModel targetSession, string scriptPath, string action, CancellationToken cancellationToken, string? requestedScriptId = null)
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

		var scriptId = requestedScriptId ?? ScriptManagerService.DeriveScriptIdFromPath(scriptPath);
		_scriptManager.AddOrUpdateScript(scriptPath, null, null, scriptId);

		var activePath = targetSession.ActiveScriptPath;
		var switchingScripts = !string.IsNullOrWhiteSpace(activePath) &&
			!string.Equals(activePath, scriptPath, StringComparison.OrdinalIgnoreCase);
		if (switchingScripts && requestedScriptId == null)
		{
			var unloaded = await UnloadActiveScriptCoreAsync(targetSession, scriptPath, cancellationToken).ConfigureAwait(true);
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
			.ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
		if (!runtimeReady.Success)
		{
			_consoleLog.Append(
				$"[OrbitCmd] Unable to start ME .NET runtime for '{targetSession.Name}'. {Capitalize(action)} may fail.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
		}

		var commandResponse = await _commandClient
			.SendReloadWithRetryAsync(scriptPath, scriptId, pid, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: cancellationToken)
			.ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
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
			?? await _commandClient.QueryStatusAsync(pid, scriptId, cancellationToken).ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
		if (status == null || !status.HasScriptInventory || status.ProcessId != pid || !status.IsScriptLoaded(scriptId))
		{
			targetSession.SetScriptRuntimeError($"ME did not confirm '{Path.GetFileNameWithoutExtension(scriptPath)}' is loaded.");
			return false;
		}

		targetSession.SetScriptLoaded(status.GetScriptPath(scriptId) ?? scriptPath, scriptId);
		targetSession.ConfirmRuntimeStatus(status);
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
