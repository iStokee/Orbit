using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Logging;

namespace Orbit.Services;

internal static class OrbitCommandClient
{
    private const string PipeName = "MESharpControl";

    public static Task<bool> SendLoadAsync(string scriptPath, CancellationToken cancellationToken)
        => SendAsync($"LOAD\t{scriptPath}", "Load", null, cancellationToken);

    public static Task<bool> SendLoadAsync(string scriptPath, int processId, CancellationToken cancellationToken = default)
        => SendAsync($"LOAD\t{scriptPath}", "Load", processId, cancellationToken);

    public static Task<bool> SendReloadAsync(string scriptPath, CancellationToken cancellationToken)
        => SendAsync($"RELOAD\t{scriptPath}", "Reload", null, cancellationToken);

    public static Task<bool> SendReloadAsync(string scriptPath, int processId, CancellationToken cancellationToken = default)
        => SendAsync($"RELOAD\t{scriptPath}", "Reload", processId, cancellationToken);

    public static async Task<bool> SendLoadWithRetryAsync(
        string scriptPath,
        int? processId,
        int maxAttempts = 4,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync(
            $"LOAD\t{scriptPath}",
            "Load",
            processId,
            maxAttempts,
            initialDelay,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> SendReloadWithRetryAsync(
        string scriptPath,
        int? processId,
        int maxAttempts = 4,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync(
            $"RELOAD\t{scriptPath}",
            "Reload",
            processId,
            maxAttempts,
            initialDelay,
            cancellationToken).ConfigureAwait(false);
    }

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

    public static async Task<bool> SendInputModeWithRetryAsync(
        int mode,
        int processId,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts <= 0)
        {
            return false;
        }

        var delay = initialDelay ?? TimeSpan.Zero;
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
                    return false;
                }
            }

            var success = await SendInputModeAsync(mode, processId, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                return true;
            }

            delay = delay == TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(200)
                : TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
        }

        return false;
    }

    public static async Task<bool> SendFocusSpoofWithRetryAsync(
        bool enabled,
        int processId,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts <= 0)
        {
            return false;
        }

        var delay = initialDelay ?? TimeSpan.Zero;
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
                    return false;
                }
            }

            var success = await SendFocusSpoofAsync(enabled, processId, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                return true;
            }

            delay = delay == TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(200)
                : TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
        }

        return false;
    }

    public static async Task<bool> SendDebugMenuVisibleWithRetryAsync(
        bool visible,
        int processId,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts <= 0)
        {
            return false;
        }

        var delay = initialDelay ?? TimeSpan.Zero;
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
                    return false;
                }
            }

            var success = await SendDebugMenuVisibleAsync(visible, processId, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                return true;
            }

            delay = delay == TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(200)
                : TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
        }

        return false;
    }

    public static async Task<bool> SendStartRuntimeWithRetryAsync(
        int? processId,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync(
            "START_RUNTIME",
            "StartRuntime",
            processId,
            maxAttempts,
            initialDelay,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> SendUnloadScriptWithRetryAsync(
        int? processId,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync(
            "UNLOAD_SCRIPT",
            "UnloadScript",
            processId,
            maxAttempts,
            initialDelay,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> SendWithRetryAsync(
        string payload,
        string operation,
        int? processId,
        int maxAttempts,
        TimeSpan? initialDelay,
        CancellationToken cancellationToken)
    {
        if (maxAttempts <= 0)
        {
            return false;
        }

        var delay = initialDelay ?? TimeSpan.Zero;
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
                    return false;
                }
            }

            var success = await SendAsync(payload, operation, processId, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                return true;
            }

            delay = delay == TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(200)
                : TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1200));
        }

        return false;
    }

    private static async Task<bool> SendAsync(string payload, string operation, int? processId, CancellationToken cancellationToken)
    {
        if (processId.HasValue)
        {
            var perSessionPipe = $"{PipeName}.{processId.Value}";
            if (await TrySendAsync(perSessionPipe, payload, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            // Fallback to legacy global pipe for backward compatibility if per-session fails or times out
            if (await TrySendAsync(PipeName, payload, cancellationToken).ConfigureAwait(false))
            {
                ConsoleLogService.Instance.Append(
                    $"[MESharpCmd] {operation} sent via legacy pipe; update native to per-session for full multi-session support.",
                    ConsoleLogSource.Orbit,
                    ConsoleLogLevel.Info);
                return true;
            }

            ConsoleLogService.Instance.Append(
                $"[MESharpCmd] {operation} request failed (per-session + legacy pipes unavailable).",
                ConsoleLogSource.Orbit,
                ConsoleLogLevel.Warning);
            return false;
        }

        if (await TrySendAsync(PipeName, payload, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        ConsoleLogService.Instance.Append(
            $"[MESharpCmd] {operation} request failed (pipe unavailable).",
            ConsoleLogSource.Orbit,
            ConsoleLogLevel.Warning);
        return false;
    }

    private static async Task<bool> TrySendAsync(string pipeName, string payload, CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

            var data = Encoding.UTF8.GetBytes(payload + "\n");
            await pipe.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            var detail = ex.ToString();
            Console.WriteLine($"[MESharpCmd] Pipe '{pipeName}' request failed: {detail}");
            return false;
        }
    }
}
