using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class MESharpPipeSecurityTests
{
	[Theory]
	[InlineData(PipeDirection.In)]
	[InlineData(PipeDirection.InOut)]
	public async Task CreateServer_AllowsCurrentUserWithoutAuditPrivileges(PipeDirection direction)
	{
		var name = $"OrbitPipeSecurityTests.{Guid.NewGuid():N}";
		using var server = MESharpPipeSecurity.CreateServer(name, direction, 1,
			PipeTransmissionMode.Message, PipeOptions.Asynchronous);
		using var client = new NamedPipeClientStream(".", name,
			direction == PipeDirection.In ? PipeDirection.Out : PipeDirection.InOut, PipeOptions.Asynchronous);
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var accept = server.WaitForConnectionAsync(deadline.Token);
		await client.ConnectAsync(deadline.Token);
		await accept;
		var buffer = new byte[1];
		var read = server.ReadAsync(buffer, deadline.Token);
		await client.WriteAsync(new byte[] { 42 }, deadline.Token);
		Assert.Equal(1, await read);
		Assert.Equal(42, buffer[0]);
	}

	[Fact]
	public void CreatePipeSecurity_RestrictsAccessToSystemAdministratorsAndCurrentUser()
	{
		var security = MESharpPipeSecurity.CreatePipeSecurity();
		Assert.True(security.AreAccessRulesProtected);
		var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
		Assert.Equal(3, rules.Count);
		using var identity = WindowsIdentity.GetCurrent();
		Assert.Contains(rules.Cast<PipeAccessRule>(), rule => rule.IdentityReference.Equals(identity.User));
		Assert.All(rules.Cast<PipeAccessRule>(), rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
	}
}
