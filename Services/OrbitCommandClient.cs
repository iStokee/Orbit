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

    public static Task<bool> SendDebugMenuVisibleAsync(bool visible, int processId, CancellationToken cancellationToken = default)
        => SendAsync($"DEBUG_VISIBLE\t{(visible ? 1 : 0)}", "DebugVisible", processId, cancellationToken);

    public static Task<bool> SendInputModeAsync(int mode, int processId, CancellationToken cancellationToken = default)
        => SendAsync($"SET_INPUT_MODE\t{mode}", "InputMode", processId, cancellationToken);

    public static Task<bool> SendFocusSpoofAsync(bool enabled, int processId, CancellationToken cancellationToken = default)
        => SendAsync($"SET_FOCUS_SPOOF\t{(enabled ? 1 : 0)}", "FocusSpoof", processId, cancellationToken);

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

    private static async Task<bool> SendAsync(string payload, string operation, int? processId, CancellationToken cancellationToken)
    {
        try
        {
            string targetPipe = processId.HasValue ? $"{PipeName}.{processId.Value}" : PipeName;
            using var pipe = new NamedPipeClientStream(".", targetPipe, PipeDirection.Out);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

            var data = Encoding.UTF8.GetBytes(payload + "\n");
            await pipe.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            ConsoleLogService.Instance.Append(
                $"[MESharpCmd] {operation} request cancelled or timed out.",
                ConsoleLogSource.Orbit,
                ConsoleLogLevel.Warning);
            return false;
        }
        catch (Exception ex)
        {
            // Fallback to legacy global pipe for backward compatibility if per-session fails
            if (processId.HasValue)
            {
                try
                {
                    using var fallback = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts2.CancelAfter(TimeSpan.FromSeconds(2));
                    await fallback.ConnectAsync(cts2.Token).ConfigureAwait(false);
                    var data = Encoding.UTF8.GetBytes(payload + "\n");
                    await fallback.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    await fallback.FlushAsync(cancellationToken).ConfigureAwait(false);
                    ConsoleLogService.Instance.Append(
                        $"[MESharpCmd] {operation} sent via legacy pipe; update native to per-session for full multi-session support.",
                        ConsoleLogSource.Orbit,
                        ConsoleLogLevel.Info);
                    return true;
                }
                catch
                {
                    // fall through to error log
                }
            }

            var detail = ex.ToString();
            Console.WriteLine($"[MESharpCmd] {operation} request failed: {detail}");
            ConsoleLogService.Instance.Append(
                $"[MESharpCmd] {operation} request failed: {detail}",
                ConsoleLogSource.Orbit,
                ConsoleLogLevel.Warning);
            return false;
        }
    }
}
