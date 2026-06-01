using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services;

public sealed class OrbitCommandClientAdapter : IOrbitCommandClient
{
	public Task<OrbitCommandResponse> SendLoadWithRetryAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendLoadWithStatusAsync(scriptPath, scriptId, processId, maxAttempts, initialDelay, cancellationToken);

	public Task<OrbitCommandResponse> SendReloadWithRetryAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendReloadWithStatusAsync(scriptPath, scriptId, processId, maxAttempts, initialDelay, cancellationToken);

	public Task<OrbitCommandResponse> SendUnloadScriptWithRetryAsync(
		string scriptId,
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendUnloadScriptWithStatusAsync(scriptId, processId, maxAttempts, initialDelay, cancellationToken);

	public Task<OrbitCommandResponse> SendStartRuntimeWithRetryAsync(
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendStartRuntimeWithStatusAsync(processId, maxAttempts, initialDelay, cancellationToken);

	public Task<bool> SendInputModeWithRetryAsync(
		int mode,
		int processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendInputModeWithRetryAsync(mode, processId, maxAttempts, initialDelay, cancellationToken);

	public Task<bool> SendFocusSpoofWithRetryAsync(
		bool enabled,
		int processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendFocusSpoofWithRetryAsync(enabled, processId, maxAttempts, initialDelay, cancellationToken);

	public Task<bool> SendDebugMenuVisibleWithRetryAsync(
		bool visible,
		int processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.SendDebugMenuVisibleWithRetryAsync(visible, processId, maxAttempts, initialDelay, cancellationToken);

	public Task<OrbitRuntimeStatus?> QueryStatusAsync(
		int processId,
		string? scriptId = null,
		CancellationToken cancellationToken = default)
		=> OrbitCommandClient.QueryStatusAsync(processId, scriptId, cancellationToken);
}
