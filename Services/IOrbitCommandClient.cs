using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services;

public interface IOrbitCommandClient
{
	Task<OrbitCommandResponse> SendLoadWithRetryAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<OrbitCommandResponse> SendReloadWithRetryAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<OrbitCommandResponse> SendUnloadScriptWithRetryAsync(
		string scriptId,
		int? processId,
		int maxAttempts = 3,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<OrbitCommandResponse> SendStartRuntimeWithRetryAsync(
		int? processId,
		int maxAttempts = 3,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<bool> SendInputModeWithRetryAsync(
		int mode,
		int processId,
		int maxAttempts = 3,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<bool> SendFocusSpoofWithRetryAsync(
		bool enabled,
		int processId,
		int maxAttempts = 3,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<bool> SendDebugMenuVisibleWithRetryAsync(
		bool visible,
		int processId,
		int maxAttempts = 3,
		System.TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default);

	Task<OrbitRuntimeStatus?> QueryStatusAsync(
		int processId,
		string? scriptId = null,
		CancellationToken cancellationToken = default);
}
