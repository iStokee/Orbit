using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Orbit.ViewModels
{
	public class AccountManagerViewModel : INotifyPropertyChanged
	{
		private readonly AccountService _accountService;
		private readonly SessionCollectionService _sessionCollectionService;
		private readonly AutoLoginService _autoLoginService;
		private CancellationTokenSource? _loginCts;
		private string _searchText = string.Empty;
		private string _newUsername = string.Empty;
		private string _newPassword = string.Empty;
		private string _newNickname = string.Empty;
		private int _newPreferredWorld = 1;
		private AccountModel? _selectedAccount;
		private SessionModel? _selectedSession;
		private bool _isLoggingIn;
		private string _loginStatusMessage = string.Empty;

		public ObservableCollection<AccountModel> Accounts => _accountService.Accounts;

		public ObservableCollection<AccountModel> FilteredAccounts { get; }

		public ObservableCollection<SessionModel> Sessions => _sessionCollectionService.Sessions;

		public bool HasSessions => Sessions.Count > 0;

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

		public string NewNickname
		{
			get => _newNickname;
			set
			{
				if (_newNickname == value) return;
				_newNickname = value;
				OnPropertyChanged();
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
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public SessionModel? SelectedSession
		{
			get => _selectedSession;
			set
			{
				if (_selectedSession == value) return;
				_selectedSession = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public bool IsLoggingIn
		{
			get => _isLoggingIn;
			private set
			{
				if (_isLoggingIn == value) return;
				_isLoggingIn = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsLoginAvailable));
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public string LoginStatusMessage
		{
			get => _loginStatusMessage;
			private set
			{
				if (_loginStatusMessage == value) return;
				_loginStatusMessage = value;
				OnPropertyChanged();
			}
		}

		public bool IsLoginAvailable => !IsLoggingIn && SelectedAccount != null && SelectedSession != null;

		public bool CanAddAccount =>
			!string.IsNullOrWhiteSpace(NewUsername) &&
			!string.IsNullOrWhiteSpace(NewPassword) &&
			NewPassword.Length >= 5;

		public ICommand AddAccountCommand { get; }
		public ICommand DeleteAccountCommand { get; }
		public ICommand ClearFormCommand { get; }
		public ICommand LoginSelectedAccountCommand { get; }

		public AccountManagerViewModel(AccountService accountService, SessionCollectionService? sessionCollectionService = null, AutoLoginService? autoLoginService = null)
		{
			_accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
			_sessionCollectionService = sessionCollectionService ?? SessionCollectionService.Instance;
			_autoLoginService = autoLoginService ?? new AutoLoginService(_accountService);
			FilteredAccounts = new ObservableCollection<AccountModel>();

			AddAccountCommand = new RelayCommand(_ => AddAccount(), _ => CanAddAccount);
			DeleteAccountCommand = new RelayCommand(DeleteAccount, _ => SelectedAccount != null);
			ClearFormCommand = new RelayCommand(_ => ClearForm());
			LoginSelectedAccountCommand = new RelayCommand(async _ => await LoginSelectedAccountAsync(), _ => CanExecuteLogin());

			// Subscribe to collection changes
			_accountService.Accounts.CollectionChanged += OnAccountsCollectionChanged;

			// Subscribe to property changes on existing accounts
			foreach (var account in _accountService.Accounts)
			{
				account.PropertyChanged += OnAccountPropertyChanged;
			}

			Sessions.CollectionChanged += OnSessionsCollectionChanged;
			_sessionCollectionService.PropertyChanged += OnSessionServicePropertyChanged;
			SelectedSession = _sessionCollectionService.GlobalSelectedSession ?? Sessions.FirstOrDefault();

			UpdateFilteredAccounts();
		}

		private bool CanExecuteLogin() => SelectedAccount != null && SelectedSession != null && !IsLoggingIn;

		private async Task LoginSelectedAccountAsync()
		{
			if (SelectedAccount == null || SelectedSession == null)
			{
				return;
			}

			IsLoggingIn = true;
			LoginStatusMessage = $"Sending auto-login for {SelectedAccount.Username}...";

			_loginCts?.Cancel();
			_loginCts?.Dispose();
			_loginCts = new CancellationTokenSource();

			try
			{
				var success = await _autoLoginService.LoginAsync(SelectedSession, SelectedAccount, _loginCts.Token).ConfigureAwait(false);
				LoginStatusMessage = success
					? $"Auto-login sequence dispatched for {SelectedAccount.Username}."
					: "Auto-login could not run. Ensure the session is injected and focused.";
			}
			catch (OperationCanceledException)
			{
				LoginStatusMessage = "Auto-login cancelled.";
			}
			catch (Exception ex)
			{
				LoginStatusMessage = $"Auto-login failed: {ex.Message}";
				Console.WriteLine($"[AccountManager] Auto-login failed: {ex}");
			}
			finally
			{
				_loginCts?.Dispose();
				_loginCts = null;
				IsLoggingIn = false;
			}
		}

		private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			OnPropertyChanged(nameof(HasSessions));
			CommandManager.InvalidateRequerySuggested();

			if (Sessions.Count == 0)
			{
				SelectedSession = null;
				return;
			}

			if (SelectedSession == null || !Sessions.Contains(SelectedSession))
			{
				SelectedSession = Sessions.FirstOrDefault();
			}
		}

		private void OnSessionServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SessionCollectionService.GlobalSelectedSession))
			{
				var global = _sessionCollectionService.GlobalSelectedSession;
				if (global != null)
				{
					SelectedSession = global;
				}

				CommandManager.InvalidateRequerySuggested();
			}
		}

		private void OnAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
			UpdateFilteredAccounts();
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
				Nickname = string.IsNullOrWhiteSpace(NewNickname) ? string.Empty : NewNickname.Trim(),
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
			NewNickname = string.Empty;
			NewPreferredWorld = 1;
		}

		private void UpdateFilteredAccounts()
		{
			FilteredAccounts.Clear();

			var filter = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
			var accounts = Accounts.AsEnumerable();

			if (!string.IsNullOrEmpty(filter))
			{
				accounts = accounts.Where(a =>
					a.Username.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
					(!string.IsNullOrWhiteSpace(a.Nickname) && a.Nickname.Contains(filter, StringComparison.OrdinalIgnoreCase)));
			}

			foreach (var account in accounts.OrderBy(a => string.IsNullOrWhiteSpace(a.Nickname) ? a.Username : a.Nickname, StringComparer.OrdinalIgnoreCase))
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
