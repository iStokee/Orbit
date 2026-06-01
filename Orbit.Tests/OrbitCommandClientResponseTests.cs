using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class OrbitCommandClientResponseTests
{
	[Fact]
	public void ParseResponse_ReturnsFailureForEmptyResponse()
	{
		var response = OrbitCommandClient.ParseResponse(null);

		Assert.False(response.Success);
		Assert.Contains("No response", response.ErrorMessage);
	}

	[Theory]
	[InlineData("ERROR")]
	[InlineData("ERROR runtime failed")]
	[InlineData("error pipe failed")]
	public void ParseResponse_ReturnsFailureForErrorResponse(string raw)
	{
		var response = OrbitCommandClient.ParseResponse(raw);

		Assert.False(response.Success);
		Assert.Equal(raw, response.RawResponse);
		Assert.Equal(raw, response.ErrorMessage);
	}

	[Fact]
	public void ParseResponse_ReturnsSuccessForPlainOkResponse()
	{
		var response = OrbitCommandClient.ParseResponse("OK");

		Assert.True(response.Success);
		Assert.Equal("OK", response.RawResponse);
		Assert.Null(response.Status);
	}

	[Fact]
	public void ParseResponse_ParsesJsonStatusResponse()
	{
		var raw = """
			{"ok":true,"pid":123,"runtimeRunning":true,"scriptId":"alpha","scriptsInfo":"DASHBOARD_V1\nCOUNT\t1\nSCRIPT\talpha\tRunning\tAlpha\t1.0.0.0\tC:\\Scripts\\Alpha.dll\tloaded\tupdated\t\tAlive"}
			""";

		var response = OrbitCommandClient.ParseResponse(raw);

		Assert.True(response.Success);
		Assert.NotNull(response.Status);
		Assert.True(response.Status!.Ok);
		Assert.Equal(123, response.Status.ProcessId);
		Assert.True(response.Status.RuntimeRunning);
		Assert.Equal("alpha", response.Status.ScriptId);
		Assert.True(response.Status.IsScriptLoaded("alpha"));
		Assert.Equal("C:\\Scripts\\Alpha.dll", response.Status.GetScriptPath("alpha"));
	}

	[Fact]
	public void TryParseStatus_ReturnsNullForMalformedJson()
	{
		var status = OrbitCommandClient.TryParseStatus("{not-json");

		Assert.Null(status);
	}
}
