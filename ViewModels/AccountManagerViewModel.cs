using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Orbit.ViewModels
{
	public class AccountManagerViewModel : INotifyPropertyChanged
	{
		private readonly AccountService _accountService;
		private string _searchText = string.Empty;
		private string _newUsername = string.Empty;
		private string _newPassword = string.Empty;
		private int _newPreferredWorld = 1;
		private AccountModel? _selectedAccount;

		public ObservableCollection<AccountModel> Accounts => _accountService.Accounts;

		public ObservableCollection<AccountModel> FilteredAccounts { get; }

		public string SearchText
		{
			get => _searchText;
			set
			{
				if (_searchText == value) return;
				_searchText = value;
				OnPropertyChanged();
				UpdateFilteredAccounts();
			}
		}

		public string NewUsername
		{
			get => _newUsername;
			set
			{
				if (_newUsername == value) return;
				_newUsername = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(CanAddAccount));
			}
		}

		public string NewPassword
		{
			get => _newPassword;
			set
			{
				if (_newPassword == value) return;
				_newPassword = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(CanAddAccount));
			}
		}

		public int NewPreferredWorld
		{
			get => _newPreferredWorld;
			set
			{
				if (_newPreferredWorld == value) return;
				_newPreferredWorld = value;
				OnPropertyChanged();
			}
		}

		public AccountModel? SelectedAccount
		{
			get => _selectedAccount;
			set
			{
				if (_selectedAccount == value) return;
				_selectedAccount = value;
				OnPropertyChanged();
			}
		}

		public bool CanAddAccount =>
			!string.IsNullOrWhiteSpace(NewUsername) &&
			!string.IsNullOrWhiteSpace(NewPassword) &&
			NewPassword.Length >= 5;

		public ICommand AddAccountCommand { get; }
		public ICommand DeleteAccountCommand { get; }
		public ICommand ClearFormCommand { get; }

		public AccountManagerViewModel(AccountService accountService)
		{
			_accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
			FilteredAccounts = new ObservableCollection<AccountModel>();

			AddAccountCommand = new RelayCommand(_ => AddAccount(), _ => CanAddAccount);
			DeleteAccountCommand = new RelayCommand(DeleteAccount, _ => SelectedAccount != null);
			ClearFormCommand = new RelayCommand(_ => ClearForm());

			// Subscribe to collection changes
			_accountService.Accounts.CollectionChanged += OnAccountsCollectionChanged;

			// Subscribe to property changes on existing accounts
			foreach (var account in _accountService.Accounts)
			{
				account.PropertyChanged += OnAccountPropertyChanged;
			}

			UpdateFilteredAccounts();
		}

		private void OnAccountsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			// Subscribe to new accounts
			if (e.NewItems != null)
			{
				foreach (AccountModel account in e.NewItems)
				{
					account.PropertyChanged += OnAccountPropertyChanged;
				}
			}

			// Unsubscribe from removed accounts
			if (e.OldItems != null)
			{
				foreach (AccountModel account in e.OldItems)
				{
					account.PropertyChanged -= OnAccountPropertyChanged;
				}
			}

			UpdateFilteredAccounts();
		}

		private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			// Save accounts whenever any property changes (including AutoLogin)
			_accountService.SaveAccounts();
		}

		private void AddAccount()
		{
			if (!CanAddAccount) return;

			if (_accountService.AccountExists(NewUsername))
			{
				// TODO: Show error message to user
				Console.WriteLine($"[AccountManager] Account with username '{NewUsername}' already exists.");
				return;
			}

			var account = new AccountModel
			{
				Username = NewUsername.Trim(),
				Password = NewPassword,
				PreferredWorld = NewPreferredWorld,
				LastUsed = DateTime.MinValue,
				AutoLogin = false
			};

			_accountService.AddAccount(account);
			ClearForm();
		}

		private void DeleteAccount(object? parameter)
		{
			if (SelectedAccount == null) return;

			_accountService.RemoveAccount(SelectedAccount);
			SelectedAccount = null;
		}

		private void ClearForm()
		{
			NewUsername = string.Empty;
			NewPassword = string.Empty;
			NewPreferredWorld = 1;
		}

		private void UpdateFilteredAccounts()
		{
			FilteredAccounts.Clear();

			var accounts = string.IsNullOrWhiteSpace(SearchText)
				? Accounts
				: Accounts.Where(a => a.Username.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

			foreach (var account in accounts.OrderBy(a => a.Username))
			{
				FilteredAccounts.Add(account);
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
