using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services;

public sealed class McpBridgeClientService
{
    private const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<McpBridgeCallResult> CallAsync(int sessionPid, string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        if (sessionPid <= 0)
        {
            return McpBridgeCallResult.Fail("Invalid session PID.");
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return McpBridgeCallResult.Fail("Command cannot be empty.");
        }

        var request = new
        {
            protocolVersion = ProtocolVersion,
            requestId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            sessionPid,
            command,
            payload = payload ?? new { }
        };

        var pipeName = $"MESharpMcpBridge.{sessionPid}";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(2.5));
            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(requestJson).ConfigureAwait(false);

            string? responseLine;
            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readCts.CancelAfter(TimeSpan.FromSeconds(4));
                responseLine = await reader.ReadLineAsync().WaitAsync(readCts.Token).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return McpBridgeCallResult.Fail("Bridge returned an empty response.");
            }

            using var doc = JsonDocument.Parse(responseLine);
            var root = doc.RootElement;

            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            string? traceId = root.TryGetProperty("traceId", out var traceEl) ? traceEl.GetString() : null;
            int? durationMs = root.TryGetProperty("durationMs", out var durEl) && durEl.TryGetInt32(out var parsedMs) ? parsedMs : null;

            if (!ok)
            {
                var message = "Bridge command failed.";
                if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
                {
                    if (errEl.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String)
                    {
                        message = messageEl.GetString() ?? message;
                    }
                }

                return McpBridgeCallResult.Fail(message, traceId, durationMs, responseLine);
            }

            var payloadJson = root.TryGetProperty("payload", out var payloadEl)
                ? JsonSerializer.Serialize(payloadEl, PrettyJsonOptions)
                : "{}";

            return McpBridgeCallResult.Success(payloadJson, traceId, durationMs, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return McpBridgeCallResult.Fail("Bridge call timed out.");
        }
        catch (IOException ex)
        {
            return McpBridgeCallResult.Fail($"Pipe I/O failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return McpBridgeCallResult.Fail($"Bridge call failed: {ex.Message}");
        }
    }
}

public sealed record McpBridgeCallResult(
    bool IsSuccess,
    string Message,
    string? PayloadJson,
    string? TraceId,
    int? DurationMs,
    long RoundTripMs,
    string? RawResponse)
{
    public static McpBridgeCallResult Success(string payloadJson, string? traceId, int? durationMs, long roundTripMs)
        => new(true, "OK", payloadJson, traceId, durationMs, roundTripMs, null);

    public static McpBridgeCallResult Fail(string message, string? traceId = null, int? durationMs = null, string? rawResponse = null)
        => new(false, message, null, traceId, durationMs, 0, rawResponse);
}
