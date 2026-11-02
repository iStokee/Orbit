using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace Orbit.ViewModels
{
    public class AccountManagerViewModel : ObservableObject, IDisposable
    {
        private readonly AccountService _accountService;
        private string _searchText = string.Empty;
        private string _newUsername = string.Empty;
        private SecureString _newPassword = new();
        private string _newNickname = string.Empty;
        private AccountModel? _selectedAccount;
        private bool _disposed;

        public ObservableCollection<AccountModel> Accounts => _accountService.Accounts;
        public string? StorageError => _accountService.LastError;

        public ObservableCollection<AccountModel> FilteredAccounts { get; }

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
                    DeleteAccountCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string LoginStatusMessage => AutoLoginService.UnavailableReason;

        public bool CanAddAccount =>
            !string.IsNullOrWhiteSpace(NewUsername) &&
            _newPassword != null &&
            _newPassword.Length >= 5;

        public IRelayCommand AddAccountCommand { get; }
        public IRelayCommand DeleteAccountCommand { get; }
        public IRelayCommand ClearFormCommand { get; }

        public AccountManagerViewModel(AccountService accountService)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            FilteredAccounts = new ObservableCollection<AccountModel>();

            AddAccountCommand = new RelayCommand(AddAccount, () => CanAddAccount);
            DeleteAccountCommand = new RelayCommand<object?>(DeleteAccount,
                parameter => parameter is AccountModel account && Accounts.Contains(account));
            ClearFormCommand = new RelayCommand(ClearForm);

            // Subscribe to collection changes
            _accountService.Accounts.CollectionChanged += OnAccountsCollectionChanged;
            _accountService.PropertyChanged += OnStoragePropertyChanged;

            // Subscribe to property changes on existing accounts
            foreach (var account in _accountService.Accounts)
            {
                account.PropertyChanged += OnAccountPropertyChanged;
            }

            UpdateFilteredAccounts();
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

        private void OnStoragePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccountService.LastError)) OnPropertyChanged(nameof(StorageError));
        }

        private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Nickname is committed once by the grid after its edit transaction ends.
            if (e.PropertyName == nameof(AccountModel.Nickname)) return;
            _accountService.SaveAccounts();
        }

        public void CommitAccountEdit()
        {
            if (_disposed) return;
            _accountService.SaveAccounts();
            UpdateFilteredAccounts();
        }

        private void AddAccount()
        {
            if (!CanAddAccount) return;

            if (_accountService.AccountExists(NewUsername.Trim()))
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
                LastUsed = DateTime.MinValue,
                AutoLogin = false
            };

            _accountService.AddAccount(account);
            if (_accountService.LastError == null) ClearForm();
        }

        private void DeleteAccount(object? parameter)
        {
            if (parameter is not AccountModel account || !Accounts.Contains(account)) return;
            _accountService.RemoveAccount(account);
            if (_accountService.LastError == null && ReferenceEquals(SelectedAccount, account)) SelectedAccount = null;
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

            _newPassword?.Dispose();
            _newPassword = new SecureString();

            _accountService.Accounts.CollectionChanged -= OnAccountsCollectionChanged;
            _accountService.PropertyChanged -= OnStoragePropertyChanged;

            foreach (var account in _accountService.Accounts)
            {
                account.PropertyChanged -= OnAccountPropertyChanged;
            }
        }
    }
}
