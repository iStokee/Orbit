using System;
using System.IO;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class OwnershipAndPersistenceTests
{
	[Fact]
	public void ProcessClaim_RejectsDuplicateOwnerAndHandlesPidReuse()
	{
		var claims = new SessionProcessOwnership();
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		var start = DateTime.UtcNow;
		Assert.True(claims.TryClaim(123, start, first));
		Assert.False(claims.TryClaim(123, start, second));
		Assert.True(claims.TryClaim(123, start.AddSeconds(1), second));
		claims.ReleaseOwner(first);
		Assert.False(claims.TryClaim(123, start.AddSeconds(1), first));
		claims.ReleaseOwner(second);
		Assert.True(claims.TryClaim(123, start.AddSeconds(1), first));
	}

	[Fact]
	public void LauncherScope_RestoresExistingEnvironmentAfterFailure()
	{
		var original = Environment.GetEnvironmentVariable("JX_DISPLAY_NAME");
		try
		{
			Environment.SetEnvironmentVariable("JX_DISPLAY_NAME", "previous");
			using (new LauncherEnvironmentScope()) Environment.SetEnvironmentVariable("JX_DISPLAY_NAME", "temporary");
			Assert.Equal("previous", Environment.GetEnvironmentVariable("JX_DISPLAY_NAME"));
		}
		finally { Environment.SetEnvironmentVariable("JX_DISPLAY_NAME", original); }
	}

	[Fact]
	public void AccountReloadFailure_PreservesCollectionAndBlocksOverwrite()
	{
		var folder = Path.Combine(Path.GetTempPath(), "OrbitAccountAudit-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		var path = Path.Combine(folder, "accounts.json");
		File.WriteAllText(path, "[{\"username\":\"original\"}]");
		var service = new AccountService(path);
		var damaged = "[{\"username\":\"partial\"},{\"username\":\"bad\",\"encryptedPassword\":\"dpapi:v1:invalid!\"}]";
		File.WriteAllText(path, damaged);
		service.LoadAccounts();
		Assert.Equal("original", Assert.Single(service.Accounts).Username);
		Assert.NotNull(service.LastError);
		service.AddAccount(new AccountModel { Username = "new", Password = "" });
		service.SaveAccounts();
		Assert.Equal(damaged, File.ReadAllText(path));
	}

	[Fact]
	public void AccountSaveFailure_PreservesFileAndRollsBackAddition()
	{
		var folder = Path.Combine(Path.GetTempPath(), "OrbitAccountAudit-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		var path = Path.Combine(folder, "accounts.json");
		File.WriteAllText(path, "[]");
		var service = new AccountService(path);
		using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			service.AddAccount(new AccountModel { Username = "new", Password = "" });
		Assert.Empty(service.Accounts);
		Assert.NotNull(service.LastError);
		Assert.Equal("[]", File.ReadAllText(path));
		Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
	}
}
