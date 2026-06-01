using Orbit.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit.Services
{
	public class AccountService
	{
		private const string AccountsFileName = "accounts.json";
		private readonly ObservableCollection<AccountModel> _accounts;
		private readonly string _accountsFilePath;

		public ObservableCollection<AccountModel> Accounts => _accounts;

		public AccountService()
			: this(GetDefaultAccountsFilePath())
		{
		}

		internal AccountService(string accountsFilePath)
		{
			_accounts = new ObservableCollection<AccountModel>();
			_accountsFilePath = accountsFilePath ?? throw new ArgumentNullException(nameof(accountsFilePath));
			var dataDir = Path.GetDirectoryName(_accountsFilePath);
			if (!string.IsNullOrWhiteSpace(dataDir))
			{
				Directory.CreateDirectory(dataDir);
			}

			var legacyPath = Path.Combine(AppContext.BaseDirectory, AccountsFileName);

			if (!File.Exists(_accountsFilePath) && File.Exists(legacyPath))
			{
				try
				{
					File.Copy(legacyPath, _accountsFilePath, overwrite: false);
				}
				catch
				{
					// Best effort migration; continue with whichever path is available.
				}
			}
			LoadAccounts();
		}

		private static string GetDefaultAccountsFilePath()
		{
			var dataDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"MemoryError",
				"Orbit");
			return Path.Combine(dataDir, AccountsFileName);
		}

		public void LoadAccounts()
		{
			if (!File.Exists(_accountsFilePath))
			{
				return;
			}

			try
			{
				var json = File.ReadAllText(_accountsFilePath);
				var accounts = JsonSerializer.Deserialize<AccountStorageRecord[]>(json);

				_accounts.Clear();
				if (accounts != null)
				{
					var migratedPlaintext = false;
					foreach (var record in accounts)
					{
						var account = record.ToAccountModel(out var usedLegacyPlaintext);
						migratedPlaintext |= usedLegacyPlaintext;
						_accounts.Add(account);
					}

					if (migratedPlaintext)
					{
						SaveAccounts();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Failed to load accounts: {ex.Message}");
			}
		}

		public void SaveAccounts()
		{
			try
			{
				var options = new JsonSerializerOptions
				{
					WriteIndented = true,
					DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
				};

				var records = _accounts.Select(AccountStorageRecord.FromAccountModel).ToArray();
				var json = JsonSerializer.Serialize(records, options);
				File.WriteAllText(_accountsFilePath, json);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Failed to save accounts: {ex.Message}");
			}
		}

		public void AddAccount(AccountModel account)
		{
			if (account == null) return;

			_accounts.Add(account);
			SaveAccounts();
		}

		public void RemoveAccount(AccountModel account)
		{
			if (account == null) return;

			_accounts.Remove(account);
			SaveAccounts();
		}

		public void UpdateAccount(AccountModel account)
		{
			// Account is already in the collection and updated via binding
			// Just save the changes
			SaveAccounts();
		}

		public bool AccountExists(string username)
		{
			return _accounts.Any(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
		}

		public AccountModel? GetAccount(string username)
		{
			return _accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
		}

		private sealed class AccountStorageRecord
		{
			[JsonPropertyName("username")]
			public string? Username { get; set; }

			[JsonPropertyName("password")]
			public string? LegacyPassword { get; set; }

			[JsonPropertyName("encryptedPassword")]
			public string? EncryptedPassword { get; set; }

			[JsonPropertyName("preferredWorld")]
			public int PreferredWorld { get; set; } = 1;

			[JsonPropertyName("lastUsed")]
			public DateTime LastUsed { get; set; } = DateTime.MinValue;

			[JsonPropertyName("autoLogin")]
			public bool AutoLogin { get; set; }

			[JsonPropertyName("notes")]
			public string? Notes { get; set; }

			[JsonPropertyName("nickname")]
			public string? Nickname { get; set; }

			public static AccountStorageRecord FromAccountModel(AccountModel account)
			{
				return new AccountStorageRecord
				{
					Username = account.Username,
					EncryptedPassword = AccountCredentialProtector.Protect(account.Password ?? string.Empty),
					PreferredWorld = account.PreferredWorld,
					LastUsed = account.LastUsed,
					AutoLogin = account.AutoLogin,
					Notes = account.Notes,
					Nickname = account.Nickname
				};
			}

			public AccountModel ToAccountModel(out bool usedLegacyPlaintext)
			{
				usedLegacyPlaintext = false;
				var password = string.Empty;

				if (!string.IsNullOrWhiteSpace(EncryptedPassword))
				{
					password = AccountCredentialProtector.Unprotect(EncryptedPassword);
				}
				else if (!string.IsNullOrEmpty(LegacyPassword))
				{
					password = LegacyPassword;
					usedLegacyPlaintext = true;
				}

				return new AccountModel
				{
					Username = Username ?? string.Empty,
					Password = password,
					PreferredWorld = PreferredWorld <= 0 ? 1 : PreferredWorld,
					LastUsed = LastUsed,
					AutoLogin = AutoLogin,
					Notes = Notes ?? string.Empty,
					Nickname = Nickname ?? string.Empty
				};
			}
		}
	}
}
