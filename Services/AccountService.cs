using Orbit.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit.Services
{
	public class AccountService : ObservableObject
	{
		private const string AccountsFileName = "accounts.json";
		private readonly ObservableCollection<AccountModel> _accounts;
		private readonly string _accountsFilePath;
		private bool _loadFailed;
		private string? _lastError;
		public string? LastError { get => _lastError; private set => SetProperty(ref _lastError, value); }

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
				var accounts = JsonSerializer.Deserialize<AccountStorageRecord[]>(json)
					?? throw new InvalidDataException("Account storage must contain an account array.");

				// Decode every record before replacing the live collection. One bad password
				// must not turn a complete account file into a partially loaded collection.
				var loaded = new System.Collections.Generic.List<AccountModel>();
				var migratedPlaintext = false;
				if (accounts != null)
				{
					foreach (var record in accounts)
					{
						var account = record.ToAccountModel(out var usedLegacyPlaintext);
						migratedPlaintext |= usedLegacyPlaintext;
						loaded.Add(account);
					}

				}
				_accounts.Clear();
				foreach (var account in loaded) _accounts.Add(account);
				_loadFailed = false;
				LastError = null;
				if (migratedPlaintext) SaveAccounts();
			}
			catch (Exception ex)
			{
				_loadFailed = true;
				LastError = $"Unable to load accounts. The original file is preserved: {ex.Message}";
				Console.WriteLine($"[Orbit] Failed to load accounts: {ex.Message}");
			}
		}

		public void SaveAccounts()
			=> TrySaveAccounts();

		private bool TrySaveAccounts()
		{
			if (_loadFailed) return false;
			var temporaryPath = _accountsFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				var options = new JsonSerializerOptions
				{
					WriteIndented = true,
					DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
				};

				var records = _accounts.Select(AccountStorageRecord.FromAccountModel).ToArray();
				var json = JsonSerializer.Serialize(records, options);
				File.WriteAllText(temporaryPath, json);
				if (File.Exists(_accountsFilePath)) File.Replace(temporaryPath, _accountsFilePath, null);
				else File.Move(temporaryPath, _accountsFilePath);
				LastError = null;
				return true;
			}
			catch (Exception ex)
			{
				LastError = $"Unable to save accounts: {ex.Message}";
				Console.WriteLine($"[Orbit] Failed to save accounts: {ex.Message}");
				return false;
			}
			finally { try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
		}

		public void AddAccount(AccountModel account)
		{
			if (account == null || _loadFailed) return;

			_accounts.Add(account);
			if (!TrySaveAccounts()) _accounts.Remove(account);
		}

		public void RemoveAccount(AccountModel account)
		{
			if (account == null || _loadFailed) return;

			var index = _accounts.IndexOf(account);
			if (index < 0) return;
			_accounts.RemoveAt(index);
			if (!TrySaveAccounts()) _accounts.Insert(index, account);
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
