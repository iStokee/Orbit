using System;
using System.IO;
using System.IO.Pipes;
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

	private async Task ListenAsync()
	{
		var token = _cts.Token;

		while (!token.IsCancellationRequested)
		{
			try
			{
				using var pipe = new NamedPipeServerStream(
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

	private async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
	{
		using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
		using var writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

		while (!token.IsCancellationRequested && pipe.IsConnected)
		{
			string? line;
			try
			{
				line = await reader.ReadLineAsync().ConfigureAwait(false);
			}
			catch (IOException)
			{
				break;
			}

			if (string.IsNullOrWhiteSpace(line))
			{
				break;
			}

			var response = HandleRequest(line);
			try
			{
				await writer.WriteLineAsync(response).ConfigureAwait(false);
			}
			catch (IOException)
			{
				break;
			}
		}
	}

	private string HandleRequest(string line)
	{
		try
		{
			using var doc = JsonDocument.Parse(line);
			var root = doc.RootElement;
			var action = GetString(root, "action")?.Trim().ToLowerInvariant();

			return action switch
			{
				"isavailable" or "ping" => Serialize(ok: true, available: true, embeddingEnabled: Orbit.Settings.Default.ScriptWindowEmbeddingEnabled),
				"register" => HandleRegister(root),
				"unregister" => HandleUnregister(root),
				_ => Serialize(ok: false, message: $"Unknown action '{action ?? "<null>"}'")
			};
		}
		catch (Exception ex)
		{
			return Serialize(ok: false, message: ex.Message);
		}
	}

	private string HandleRegister(JsonElement root)
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
			var sessionId = _scriptIntegration.RegisterScriptWindow(windowHandle, tabName, processId);
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

	private string HandleUnregister(JsonElement root)
	{
		var sessionIdRaw = GetString(root, "sessionId");
		if (!Guid.TryParse(sessionIdRaw, out var sessionId))
		{
			return Serialize(ok: false, message: "Missing or invalid 'sessionId'.");
		}

		var removed = _scriptIntegration.UnregisterScriptWindow(sessionId);
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
		try { _cts.Cancel(); } catch { }

		try
		{
			_listenerTask?.Wait(1000);
		}
		catch { }

		_cts.Dispose();
	}
}
