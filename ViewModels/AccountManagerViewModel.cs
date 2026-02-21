using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace Orbit.ViewModels
{
	public class AccountManagerViewModel : ObservableObject, IDisposable
	{
		private readonly AccountService _accountService;
		private readonly SessionCollectionService _sessionCollectionService;
		private readonly AutoLoginService _autoLoginService;
		private CancellationTokenSource? _loginCts;
	private string _searchText = string.Empty;
	private string _newUsername = string.Empty;
	private SecureString _newPassword = new();
	private string _newNickname = string.Empty;
		private int _newPreferredWorld = 1;
		private AccountModel? _selectedAccount;
		private SessionModel? _selectedSession;
		private bool _isLoggingIn;
		private string _loginStatusMessage = string.Empty;
		private bool _disposed;

		public ObservableCollection<AccountModel> Accounts => _accountService.Accounts;

		public ObservableCollection<AccountModel> FilteredAccounts { get; }

		public ObservableCollection<SessionModel> Sessions => _sessionCollectionService.Sessions;

		public bool HasSessions => Sessions.Count > 0;

		public string SearchText
		{
			get => _searchText;
			set
			{
				if (SetProperty(ref _searchText, value))
				{
					UpdateFilteredAccounts();
				}
			}
		}

		public string NewUsername
		{
			get => _newUsername;
			set
			{
				if (SetProperty(ref _newUsername, value))
				{
					OnPropertyChanged(nameof(CanAddAccount));
					AddAccountCommand.NotifyCanExecuteChanged();
				}
			}
		}

	public string NewNickname
	{
		get => _newNickname;
		set
		{
			SetProperty(ref _newNickname, value);
		}
	}

	public int NewPreferredWorld
	{
		get => _newPreferredWorld;
		set => SetProperty(ref _newPreferredWorld, value);
	}

	public void UpdateNewPassword(SecureString? password)
	{
		var newPassword = password != null ? password.Copy() : new SecureString();
		_newPassword?.Dispose();
		_newPassword = newPassword;
		OnPropertyChanged(nameof(CanAddAccount));
		AddAccountCommand.NotifyCanExecuteChanged();
	}

		public AccountModel? SelectedAccount
		{
			get => _selectedAccount;
			set
			{
				if (SetProperty(ref _selectedAccount, value))
				{
					OnPropertyChanged(nameof(IsLoginAvailable));
					DeleteAccountCommand.NotifyCanExecuteChanged();
					LoginSelectedAccountCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public SessionModel? SelectedSession
		{
			get => _selectedSession;
			set
			{
				if (SetProperty(ref _selectedSession, value))
				{
					OnPropertyChanged(nameof(IsLoginAvailable));
					LoginSelectedAccountCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool IsLoggingIn
		{
			get => _isLoggingIn;
			private set
			{
				if (SetProperty(ref _isLoggingIn, value))
				{
					OnPropertyChanged(nameof(IsLoginAvailable));
					LoginSelectedAccountCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public string LoginStatusMessage
		{
			get => _loginStatusMessage;
			private set
			{
				SetProperty(ref _loginStatusMessage, value);
			}
		}

		public bool IsLoginAvailable => !IsLoggingIn && SelectedAccount != null && SelectedSession != null;

	public bool CanAddAccount =>
		!string.IsNullOrWhiteSpace(NewUsername) &&
		_newPassword != null &&
		_newPassword.Length >= 5;

		public IRelayCommand AddAccountCommand { get; }
		public IRelayCommand DeleteAccountCommand { get; }
		public IRelayCommand ClearFormCommand { get; }
		public IAsyncRelayCommand LoginSelectedAccountCommand { get; }

		public AccountManagerViewModel(AccountService accountService, SessionCollectionService sessionCollectionService, AutoLoginService autoLoginService)
		{
			_accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
			_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
			_autoLoginService = autoLoginService ?? throw new ArgumentNullException(nameof(autoLoginService));
			FilteredAccounts = new ObservableCollection<AccountModel>();

			AddAccountCommand = new RelayCommand(AddAccount, () => CanAddAccount);
			DeleteAccountCommand = new RelayCommand<object?>(DeleteAccount, _ => SelectedAccount != null);
			ClearFormCommand = new RelayCommand(ClearForm);
			LoginSelectedAccountCommand = new AsyncRelayCommand(LoginSelectedAccountAsync, CanExecuteLogin);

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
			var account = SelectedAccount;
			var session = SelectedSession;
			if (account == null || session == null)
			{
				return;
			}

			_loginCts?.Cancel();
			_loginCts?.Dispose();
			_loginCts = new CancellationTokenSource();

			try
			{
					await Application.Current.Dispatcher.InvokeAsync(() =>
					{
						IsLoggingIn = true;
						LoginStatusMessage = $"Sending auto-login for {account.Username}...";
					});

					var success = await _autoLoginService.LoginAsync(session, account, _loginCts.Token);

					await Application.Current.Dispatcher.InvokeAsync(() =>
					{
						LoginStatusMessage = success
							? $"Auto-login sequence dispatched for {account.Username}."
						: "Auto-login could not run. Ensure the session is injected and focused.";
				});
			}
			catch (OperationCanceledException)
			{
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					LoginStatusMessage = "Auto-login cancelled.";
				});
			}
			catch (Exception ex)
			{
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					LoginStatusMessage = $"Auto-login failed: {ex.Message}";
				});
				Console.WriteLine($"[AccountManager] Auto-login failed: {ex}");
			}
			finally
			{
				_loginCts?.Dispose();
				_loginCts = null;
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					IsLoggingIn = false;
				});
			}
		}

		private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			OnPropertyChanged(nameof(HasSessions));
			LoginSelectedAccountCommand.NotifyCanExecuteChanged();

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

				LoginSelectedAccountCommand.NotifyCanExecuteChanged();
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

		var password = ExtractPassword();

		var account = new AccountModel
		{
			Username = NewUsername.Trim(),
			Password = password,
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

private void ResetNewPassword()
{
	_newPassword?.Dispose();
	_newPassword = new SecureString();
	OnPropertyChanged(nameof(CanAddAccount));
	AddAccountCommand.NotifyCanExecuteChanged();
	PasswordReset?.Invoke(this, EventArgs.Empty);
}

	private void ClearForm()
	{
		NewUsername = string.Empty;
		ResetNewPassword();
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

	private string ExtractPassword()
	{
		if (_newPassword == null || _newPassword.Length == 0)
			return string.Empty;

		IntPtr ptr = IntPtr.Zero;
		try
		{
			ptr = Marshal.SecureStringToBSTR(_newPassword);
			return Marshal.PtrToStringBSTR(ptr) ?? string.Empty;
		}
		finally
		{
			if (ptr != IntPtr.Zero)
			{
				Marshal.ZeroFreeBSTR(ptr);
			}
		}
	}

public event EventHandler? PasswordReset;

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_loginCts?.Cancel();
			_loginCts?.Dispose();
			_loginCts = null;

			_newPassword?.Dispose();
			_newPassword = new SecureString();

			_accountService.Accounts.CollectionChanged -= OnAccountsCollectionChanged;
			Sessions.CollectionChanged -= OnSessionsCollectionChanged;
			_sessionCollectionService.PropertyChanged -= OnSessionServicePropertyChanged;

			foreach (var account in _accountService.Accounts)
			{
				account.PropertyChanged -= OnAccountPropertyChanged;
			}
		}
		}
	}
