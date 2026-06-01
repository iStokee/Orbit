using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Logging;

namespace Orbit.Services;

internal static class OrbitCommandClient
{
	private const string PipeName = "MESharpControl";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public static Task<bool> SendLoadAsync(string scriptPath, CancellationToken cancellationToken)
		=> SendAsync(BuildScriptCommand("LOAD", scriptPath, null), "Load", null, cancellationToken);

	public static Task<bool> SendLoadAsync(string scriptPath, string scriptId, CancellationToken cancellationToken)
		=> SendAsync(BuildScriptCommand("LOAD", scriptPath, scriptId), "Load", null, cancellationToken);

	public static Task<bool> SendLoadAsync(string scriptPath, int processId, CancellationToken cancellationToken = default)
		=> SendAsync(BuildScriptCommand("LOAD", scriptPath, null), "Load", processId, cancellationToken);

	public static Task<bool> SendLoadAsync(string scriptPath, string scriptId, int processId, CancellationToken cancellationToken = default)
		=> SendAsync(BuildScriptCommand("LOAD", scriptPath, scriptId), "Load", processId, cancellationToken);

	public static Task<bool> SendReloadAsync(string scriptPath, CancellationToken cancellationToken)
		=> SendAsync(BuildScriptCommand("RELOAD", scriptPath, null), "Reload", null, cancellationToken);

	public static Task<bool> SendReloadAsync(string scriptPath, string scriptId, CancellationToken cancellationToken)
		=> SendAsync(BuildScriptCommand("RELOAD", scriptPath, scriptId), "Reload", null, cancellationToken);

	public static Task<bool> SendReloadAsync(string scriptPath, int processId, CancellationToken cancellationToken = default)
		=> SendAsync(BuildScriptCommand("RELOAD", scriptPath, null), "Reload", processId, cancellationToken);

	public static Task<bool> SendReloadAsync(string scriptPath, string scriptId, int processId, CancellationToken cancellationToken = default)
		=> SendAsync(BuildScriptCommand("RELOAD", scriptPath, scriptId), "Reload", processId, cancellationToken);

	public static async Task<bool> SendLoadWithRetryAsync(
		string scriptPath,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendLoadWithStatusAsync(scriptPath, string.Empty, processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendLoadWithRetryAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendLoadWithStatusAsync(scriptPath, scriptId, processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendReloadWithRetryAsync(
		string scriptPath,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendReloadWithStatusAsync(scriptPath, string.Empty, processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendReloadWithRetryAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendReloadWithStatusAsync(scriptPath, scriptId, processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static Task<bool> SendInputModeAsync(int mode, int processId, CancellationToken cancellationToken = default)
		=> SendAsync($"SET_INPUT_MODE\t{mode}", "InputMode", processId, cancellationToken);

	public static Task<bool> SendFocusSpoofAsync(bool enabled, int processId, CancellationToken cancellationToken = default)
		=> SendAsync($"SET_FOCUS_SPOOF\t{(enabled ? 1 : 0)}", "FocusSpoof", processId, cancellationToken);

	public static Task<bool> SendDebugMenuVisibleAsync(bool visible, int processId, CancellationToken cancellationToken = default)
		=> SendAsync($"SET_DEBUG_MENU\t{(visible ? 1 : 0)}", "DebugMenu", processId, cancellationToken);

	public static Task<bool> SendStartRuntimeAsync(int processId, CancellationToken cancellationToken = default)
		=> SendAsync("START_RUNTIME", "StartRuntime", processId, cancellationToken);

	public static Task<bool> SendStartRuntimeAsync(CancellationToken cancellationToken = default)
		=> SendAsync("START_RUNTIME", "StartRuntime", null, cancellationToken);

	public static Task<bool> SendUnloadScriptAsync(int processId, CancellationToken cancellationToken = default)
		=> SendAsync("UNLOAD_SCRIPT", "UnloadScript", processId, cancellationToken);

	public static Task<bool> SendUnloadScriptAsync(CancellationToken cancellationToken = default)
		=> SendAsync("UNLOAD_SCRIPT", "UnloadScript", null, cancellationToken);

	public static Task<bool> SendUnloadScriptAsync(string scriptId, int processId, CancellationToken cancellationToken = default)
		=> SendAsync(BuildUnloadScriptCommand(scriptId), "UnloadScript", processId, cancellationToken);

	public static Task<bool> SendUnloadScriptAsync(string scriptId, CancellationToken cancellationToken = default)
		=> SendAsync(BuildUnloadScriptCommand(scriptId), "UnloadScript", null, cancellationToken);

	public static Task<OrbitCommandResponse> SendLoadWithStatusAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> SendWithRetryForResponseAsync(BuildScriptCommand("LOAD", scriptPath, scriptId), "Load", processId, maxAttempts, initialDelay, cancellationToken);

	public static Task<OrbitCommandResponse> SendReloadWithStatusAsync(
		string scriptPath,
		string scriptId,
		int? processId,
		int maxAttempts = 4,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> SendWithRetryForResponseAsync(BuildScriptCommand("RELOAD", scriptPath, scriptId), "Reload", processId, maxAttempts, initialDelay, cancellationToken);

	public static async Task<bool> SendInputModeWithRetryAsync(
		int mode,
		int processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendWithRetryForResponseAsync($"SET_INPUT_MODE\t{mode}", "InputMode", processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendFocusSpoofWithRetryAsync(
		bool enabled,
		int processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendWithRetryForResponseAsync($"SET_FOCUS_SPOOF\t{(enabled ? 1 : 0)}", "FocusSpoof", processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendDebugMenuVisibleWithRetryAsync(
		bool visible,
		int processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendWithRetryForResponseAsync($"SET_DEBUG_MENU\t{(visible ? 1 : 0)}", "DebugMenu", processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendStartRuntimeWithRetryAsync(
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendStartRuntimeWithStatusAsync(processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static Task<OrbitCommandResponse> SendStartRuntimeWithStatusAsync(
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> SendWithRetryForResponseAsync("START_RUNTIME", "StartRuntime", processId, maxAttempts, initialDelay, cancellationToken);

	public static async Task<bool> SendUnloadScriptWithRetryAsync(
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendWithRetryForResponseAsync("UNLOAD_SCRIPT", "UnloadScript", processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static async Task<bool> SendUnloadScriptWithRetryAsync(
		string scriptId,
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> (await SendUnloadScriptWithStatusAsync(scriptId, processId, maxAttempts, initialDelay, cancellationToken).ConfigureAwait(false)).Success;

	public static Task<OrbitCommandResponse> SendUnloadScriptWithStatusAsync(
		string scriptId,
		int? processId,
		int maxAttempts = 3,
		TimeSpan? initialDelay = null,
		CancellationToken cancellationToken = default)
		=> SendWithRetryForResponseAsync(BuildUnloadScriptCommand(scriptId), "UnloadScript", processId, maxAttempts, initialDelay, cancellationToken);

	public static async Task<OrbitRuntimeStatus?> QueryStatusAsync(int processId, string? scriptId = null, CancellationToken cancellationToken = default)
	{
		var payload = string.IsNullOrWhiteSpace(scriptId)
			? "STATUS"
			: $"SCRIPT_STATUS\t{scriptId.Trim()}";
		var response = await SendForResponseAsync(payload, "Status", processId, cancellationToken).ConfigureAwait(false);
		return response.Status;
	}

	private static string BuildScriptCommand(string verb, string scriptPath, string? scriptId)
	{
		if (string.IsNullOrWhiteSpace(scriptId))
		{
			return $"{verb}\t{scriptPath}";
		}

		return $"{verb}\t{scriptId.Trim()}\t{scriptPath}";
	}

	private static string BuildUnloadScriptCommand(string? scriptId)
	{
		if (string.IsNullOrWhiteSpace(scriptId))
		{
			return "UNLOAD_SCRIPT";
		}

		return $"UNLOAD_SCRIPT\t{scriptId.Trim()}";
	}

	private static async Task<OrbitCommandResponse> SendWithRetryForResponseAsync(
		string payload,
		string operation,
		int? processId,
		int maxAttempts,
		TimeSpan? initialDelay,
		CancellationToken cancellationToken)
	{
		if (maxAttempts <= 0)
		{
			return new OrbitCommandResponse(false, ErrorMessage: "No command attempts were requested.");
		}

		var delay = initialDelay ?? TimeSpan.Zero;
		OrbitCommandResponse lastResponse = new(false, ErrorMessage: "Command was not attempted.");
		for (var attempt = 1; attempt <= maxAttempts; attempt++)
		{
			if (attempt > 1 && delay > TimeSpan.Zero)
			{
				try
				{
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return new OrbitCommandResponse(false, ErrorMessage: "Command was canceled.");
				}
			}

			lastResponse = await SendForResponseAsync(payload, operation, processId, cancellationToken).ConfigureAwait(false);
			if (lastResponse.Success)
			{
				return lastResponse;
			}

			delay = delay == TimeSpan.Zero
				? TimeSpan.FromMilliseconds(200)
				: TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1200));
		}

		return lastResponse;
	}

	private static async Task<bool> SendAsync(string payload, string operation, int? processId, CancellationToken cancellationToken)
		=> (await SendForResponseAsync(payload, operation, processId, cancellationToken).ConfigureAwait(false)).Success;

	private static async Task<OrbitCommandResponse> SendForResponseAsync(string payload, string operation, int? processId, CancellationToken cancellationToken)
	{
		if (processId.HasValue)
		{
			var perSessionPipe = $"{PipeName}.{processId.Value}";
			var response = await TrySendAsync(perSessionPipe, payload, cancellationToken).ConfigureAwait(false);
			if (response.Success)
			{
				return response;
			}

			ConsoleLogService.Instance.Append(
				$"[MESharpCmd] {operation} request failed for PID {processId.Value}: {DescribeFailure(response, "per-session pipe unavailable")}.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return response;
		}

		var sharedResponse = await TrySendAsync(PipeName, payload, cancellationToken).ConfigureAwait(false);
		if (sharedResponse.Success)
		{
			return sharedResponse;
		}

		ConsoleLogService.Instance.Append(
			$"[MESharpCmd] {operation} request failed: {DescribeFailure(sharedResponse, "pipe unavailable")}.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Warning);
		return sharedResponse;
	}

	private static string DescribeFailure(OrbitCommandResponse response, string fallback)
		=> string.IsNullOrWhiteSpace(response.ErrorMessage)
			? fallback
			: response.ErrorMessage;

	private static async Task<OrbitCommandResponse> TrySendAsync(string pipeName, string payload, CancellationToken cancellationToken)
	{
		try
		{
			using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromSeconds(3));
			await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

			var data = Encoding.UTF8.GetBytes(payload + "\n");
			await pipe.WriteAsync(data, 0, data.Length, cts.Token).ConfigureAwait(false);
			await pipe.FlushAsync(cts.Token).ConfigureAwait(false);

			using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
			var responseLine = await reader.ReadLineAsync().WaitAsync(cts.Token).ConfigureAwait(false);
			return ParseResponse(responseLine);
		}
		catch (OperationCanceledException)
		{
			return new OrbitCommandResponse(false, ErrorMessage: "Command timed out or was canceled.");
		}
		catch (UnauthorizedAccessException ex)
		{
			return new OrbitCommandResponse(
				false,
				ErrorMessage: $"Access denied connecting to MESharp command pipe '{pipeName}'. Restart the injected session with the updated bridge, or run Orbit and the client at matching integrity/elevation. {ex.Message}");
		}
		catch (IOException ex)
		{
			return new OrbitCommandResponse(
				false,
				ErrorMessage: $"MESharp command pipe '{pipeName}' I/O failed: {ex.Message}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[MESharpCmd] Pipe '{pipeName}' request failed: {ex.GetType().Name}: {ex.Message}");
			return new OrbitCommandResponse(false, ErrorMessage: ex.Message);
		}
	}

	internal static OrbitCommandResponse ParseResponse(string? responseLine)
	{
		if (string.IsNullOrWhiteSpace(responseLine))
		{
			return new OrbitCommandResponse(false, RawResponse: responseLine, ErrorMessage: "No response from MESharp command bridge.");
		}

		if (responseLine.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
		{
			return new OrbitCommandResponse(false, RawResponse: responseLine, ErrorMessage: responseLine);
		}

		return new OrbitCommandResponse(true, RawResponse: responseLine, Status: TryParseStatus(responseLine));
	}

	internal static OrbitRuntimeStatus? TryParseStatus(string responseLine)
	{
		if (string.IsNullOrWhiteSpace(responseLine) || responseLine[0] != '{')
		{
			return null;
		}

		try
		{
			var dto = JsonSerializer.Deserialize<StatusDto>(responseLine, JsonOptions);
			return dto == null
				? null
				: new OrbitRuntimeStatus(dto.Ok, dto.Pid, dto.RuntimeRunning, dto.ScriptId, dto.ScriptsInfo);
		}
		catch
		{
			return null;
		}
	}

	private sealed class StatusDto
	{
		public bool Ok { get; set; }
		public int Pid { get; set; }
		public bool RuntimeRunning { get; set; }
		public string? ScriptId { get; set; }
		public string? ScriptsInfo { get; set; }
	}
}
