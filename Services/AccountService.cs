using Orbit.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Orbit.Services
{
	public class AccountService
	{
		private const string AccountsFileName = "accounts.json";
		private readonly ObservableCollection<AccountModel> _accounts;
		private readonly string _accountsFilePath;

		public ObservableCollection<AccountModel> Accounts => _accounts;

		public AccountService()
		{
			_accounts = new ObservableCollection<AccountModel>();
			var legacyPath = Path.Combine(AppContext.BaseDirectory, AccountsFileName);
			var dataDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"MemoryError",
				"Orbit");
			Directory.CreateDirectory(dataDir);
			_accountsFilePath = Path.Combine(dataDir, AccountsFileName);

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

		public void LoadAccounts()
		{
			if (!File.Exists(_accountsFilePath))
			{
				return;
			}

			try
			{
				var json = File.ReadAllText(_accountsFilePath);
				var accounts = JsonSerializer.Deserialize<AccountModel[]>(json);

				_accounts.Clear();
				if (accounts != null)
				{
					foreach (var account in accounts)
					{
						_accounts.Add(account);
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
					WriteIndented = true
				};

				var json = JsonSerializer.Serialize(_accounts.ToArray(), options);
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
	}
}
