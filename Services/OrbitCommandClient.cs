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

            ConsoleLogService.Instance.Append(
                $"[MESharpCmd] {operation} request failed: {ex.Message}",
                ConsoleLogSource.Orbit,
                ConsoleLogLevel.Warning);
            return false;
        }
    }
}
