using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Logging;

namespace Orbit.Services;

internal sealed class OrbitApiPipeServer : IDisposable
{
	private const string PipeName = "OrbitApiBridge";
	private readonly ScriptIntegrationService _scriptIntegration;
	private readonly CancellationTokenSource _cts = new();
	private Task? _listenerTask;
	private bool _disposed;

	public OrbitApiPipeServer(ScriptIntegrationService scriptIntegration)
	{
		_scriptIntegration = scriptIntegration ?? throw new ArgumentNullException(nameof(scriptIntegration));
	}

	public void Start()
	{
		if (_disposed)
		{
			return;
		}

		_listenerTask ??= Task.Run(ListenAsync, _cts.Token);
	}

	private Task ListenAsync() => Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ListenWorkerAsync()));

	private async Task ListenWorkerAsync()
	{
		var token = _cts.Token;

		while (!token.IsCancellationRequested)
		{
			try
			{
				using var pipe = MESharpPipeSecurity.CreateServer(
					PipeName,
					PipeDirection.InOut,
					4,
					PipeTransmissionMode.Message,
					PipeOptions.Asynchronous);

				using var cancelRegistration = token.Register(static state =>
				{
					try { ((NamedPipeServerStream)state!).Dispose(); } catch { }
				}, pipe);

				await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
				await ProcessConnectionAsync(pipe, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (ObjectDisposedException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch (IOException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				ConsoleLogService.Instance.Append(
					$"[OrbitAPI][IPC] Listener error: {ex.Message}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);

				try
				{
					await Task.Delay(100, token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
	}

	internal async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
	{
		var legacyDeadline = DateTimeOffset.UtcNow.AddSeconds(2);
		using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
		requestCts.CancelAfter(TimeSpan.FromSeconds(5));
		using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
		using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
		try
		{
			// Clients open a connection per command. Do not let one persistent/idle client
			// monopolize the listener; accept both legacy BOM-prefixed and plain UTF-8 input.
			var line = await ReadRequestLineAsync(reader, requestCts.Token).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(line)) return;
			var response = await HandleRequestAsync(line, requestCts.Token, legacyDeadline).ConfigureAwait(false);
			await writer.WriteLineAsync(response.AsMemory(), requestCts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
		catch (IOException) { }
	}

	internal static async Task<string?> ReadRequestLineAsync(StreamReader reader, CancellationToken token)
	{
		const int maxLength = 16 * 1024;
		var text = new StringBuilder();
		var buffer = new char[1024];
		int count;
		while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
		{
			var newline = Array.IndexOf(buffer, '\n', 0, count);
			var length = newline < 0 ? count : newline;
			if (text.Length + length > maxLength) throw new InvalidDataException("Orbit API request exceeds 16,384 characters.");
			text.Append(buffer, 0, length);
			if (newline >= 0) return text.ToString().TrimEnd('\r');
		}
		return text.Length == 0 ? null : text.ToString();
	}

	private async Task<string> HandleRequestAsync(string line, CancellationToken token, DateTimeOffset legacyDeadline)
	{
		try
		{
			using var doc = JsonDocument.Parse(line);
			var root = doc.RootElement;
			var deadline = root.TryGetProperty("deadlineUnixMs", out var deadlineValue) && deadlineValue.TryGetInt64(out var unixMs)
				? DateTimeOffset.FromUnixTimeMilliseconds(unixMs) : legacyDeadline;
			if (deadline > DateTimeOffset.UtcNow.AddSeconds(5)) deadline = DateTimeOffset.UtcNow.AddSeconds(5);
			if (deadline <= DateTimeOffset.UtcNow) throw new TimeoutException("Orbit request expired.");
			using var execution = CancellationTokenSource.CreateLinkedTokenSource(token);
			execution.CancelAfter(deadline - DateTimeOffset.UtcNow);
			var action = GetString(root, "action")?.Trim().ToLowerInvariant();

			return action switch
			{
				"isavailable" or "ping" => Serialize(ok: true, available: true, embeddingEnabled: Orbit.Settings.Default.ScriptWindowEmbeddingEnabled),
				"register" => await HandleRegisterAsync(root, execution.Token, deadline).ConfigureAwait(false),
				"unregister" => await HandleUnregisterAsync(root, execution.Token, deadline).ConfigureAwait(false),
				_ => Serialize(ok: false, message: $"Unknown action '{action ?? "<null>"}'")
			};
		}
		catch (Exception ex)
		{
			return Serialize(ok: false, message: ex.Message);
		}
	}

	private async Task<string> HandleRegisterAsync(JsonElement root, CancellationToken token, DateTimeOffset deadline)
	{
		if (!Orbit.Settings.Default.ScriptWindowEmbeddingEnabled)
		{
			return Serialize(ok: false, message: "Script window embedding is disabled in Orbit settings.");
		}

		var rawHandle = GetString(root, "windowHandle");
		if (!TryParseHandle(rawHandle, out var windowHandle))
		{
			return Serialize(ok: false, message: "Missing or invalid 'windowHandle'.");
		}

		var tabName = GetString(root, "tabName");
		if (string.IsNullOrWhiteSpace(tabName))
		{
			tabName = "External Script";
		}

		var processId = GetNullableInt(root, "processId");

		try
		{
			var sessionId = await _scriptIntegration.RegisterScriptWindowAsync(windowHandle, tabName, processId, token, deadline).ConfigureAwait(false);
			ConsoleLogService.Instance.Append(
				$"[OrbitAPI][IPC] Registered script window '{tabName}' (HWND 0x{windowHandle.ToInt64():X}). Session={sessionId}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Info);
			return Serialize(ok: true, sessionId: sessionId.ToString());
		}
		catch (Exception ex)
		{
			ConsoleLogService.Instance.Append(
				$"[OrbitAPI][IPC] Register failed: {ex.Message}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return Serialize(ok: false, message: ex.Message);
		}
	}

	private async Task<string> HandleUnregisterAsync(JsonElement root, CancellationToken token, DateTimeOffset deadline)
	{
		var sessionIdRaw = GetString(root, "sessionId");
		if (!Guid.TryParse(sessionIdRaw, out var sessionId))
		{
			return Serialize(ok: false, message: "Missing or invalid 'sessionId'.");
		}

		var removed = await _scriptIntegration.UnregisterScriptWindowAsync(sessionId, token, deadline).ConfigureAwait(false);
		ConsoleLogService.Instance.Append(
			removed
				? $"[OrbitAPI][IPC] Unregistered script session {sessionId}"
				: $"[OrbitAPI][IPC] Unregister requested for unknown session {sessionId}",
			ConsoleLogSource.Orbit,
			removed ? ConsoleLogLevel.Info : ConsoleLogLevel.Debug);

		return Serialize(ok: removed, message: removed ? "Unregistered" : "Session not found");
	}

	private static bool TryParseHandle(string? value, out IntPtr handle)
	{
		handle = IntPtr.Zero;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			if (long.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
			{
				handle = (IntPtr)hex;
				return handle != IntPtr.Zero;
			}
			return false;
		}

		if (long.TryParse(value, out var dec))
		{
			handle = (IntPtr)dec;
			return handle != IntPtr.Zero;
		}

		return false;
	}

	private static string? GetString(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var property))
		{
			return null;
		}

		return property.ValueKind switch
		{
			JsonValueKind.String => property.GetString(),
			JsonValueKind.Number => property.GetRawText(),
			_ => property.GetRawText()
		};
	}

	private static int? GetNullableInt(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var property))
		{
			return null;
		}

		if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var n))
		{
			return n;
		}

		if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out n))
		{
			return n;
		}

		return null;
	}

	private static string Serialize(
		bool ok,
		string? message = null,
		bool? available = null,
		bool? embeddingEnabled = null,
		string? sessionId = null)
	{
		var payload = new
		{
			ok,
			message,
			available,
			embeddingEnabled,
			sessionId
		};

		return JsonSerializer.Serialize(payload);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Stop();

		try
		{
			_listenerTask?.Wait(1000);
		}
		catch { }

		_cts.Dispose();
	}

	internal void Stop() { try { _cts.Cancel(); } catch (ObjectDisposedException) { } }
}
