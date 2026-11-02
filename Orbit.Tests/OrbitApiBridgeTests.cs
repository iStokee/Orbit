using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class OrbitApiBridgeTests
{
	[Fact]
	public async Task OversizedRequest_IsRejectedWithoutUnboundedBuffering()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 17 * 1024) + "\n"));
		using var reader = new StreamReader(stream);
		await Assert.ThrowsAsync<InvalidDataException>(() => OrbitApiPipeServer.ReadRequestLineAsync(reader, CancellationToken.None));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Ping_AcceptsLegacyBomAndReturnsPlainUtf8(bool emitBom)
	{
		var collection = SessionCollectionService.Instance;
		var placement = new SessionPlacementService();
		var reconciliation = new SessionReconciliationService(collection, placement, new TearOffHostRegistry());
		var layout = new OrbitLayoutStateService(collection, placement, reconciliation);
		using var bridge = new OrbitApiPipeServer(new ScriptIntegrationService(collection, placement, layout));
		var name = "OrbitBridgeTest-" + Guid.NewGuid().ToString("N");
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
		var accept = server.WaitForConnectionAsync(cts.Token);
		await client.ConnectAsync(cts.Token);
		await accept;
		var processing = bridge.ProcessConnectionAsync(server, cts.Token);
		using var writer = new StreamWriter(client, new UTF8Encoding(emitBom), leaveOpen: true) { AutoFlush = true };
		// Match the old callers: deliberately do not strip a response BOM.
		using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
		await writer.WriteLineAsync("{\"action\":\"ping\"}".AsMemory(), cts.Token);
		var line = await reader.ReadLineAsync(cts.Token);
		using var response = JsonDocument.Parse(line!);
		Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
		await processing;
	}
}
