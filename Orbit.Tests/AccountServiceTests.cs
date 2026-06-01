using System;
using System.IO;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class AccountServiceTests
{
	[Fact]
	public void SaveAccounts_WritesEncryptedPasswordInsteadOfPlaintext()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		var filePath = CreateTempAccountsPath();
		var service = new AccountService(filePath);

		service.AddAccount(new AccountModel
		{
			Username = "test-user",
			Password = "plain-secret",
			PreferredWorld = 84
		});

		var json = File.ReadAllText(filePath);
		Assert.Contains("\"encryptedPassword\"", json);
		Assert.DoesNotContain("plain-secret", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"password\"", json, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void LoadAccounts_MigratesLegacyPlaintextPasswordToEncryptedStorage()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		var filePath = CreateTempAccountsPath();
		File.WriteAllText(filePath, """
		[
		  {
		    "username": "legacy-user",
		    "password": "legacy-secret",
		    "preferredWorld": 42,
		    "autoLogin": true,
		    "nickname": "Legacy"
		  }
		]
		""");

		var service = new AccountService(filePath);

		var account = Assert.Single(service.Accounts);
		Assert.Equal("legacy-secret", account.Password);
		Assert.Equal(42, account.PreferredWorld);

		var migratedJson = File.ReadAllText(filePath);
		Assert.Contains("\"encryptedPassword\"", migratedJson);
		Assert.DoesNotContain("legacy-secret", migratedJson, StringComparison.Ordinal);
		Assert.DoesNotContain("\"password\"", migratedJson, StringComparison.OrdinalIgnoreCase);
	}

	private static string CreateTempAccountsPath()
	{
		var dir = Path.Combine(Path.GetTempPath(), $"OrbitAccountTests_{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, "accounts.json");
	}
}
