using MahApps.Metro.Controls;
using Orbit.Models;
using Orbit.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Orbit.Views;

public partial class LauncherAccountConfigWindow : MetroWindow
{
	private sealed class EditableLauncherAccount : INotifyPropertyChanged
	{
		private bool _isSelected;
		private string _displayName = string.Empty;
		private string _characterId = string.Empty;
		private string _sessionId = string.Empty;

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected == value) return;
				_isSelected = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
			}
		}

		public string DisplayName
		{
			get => _displayName;
			set
			{
				if (_displayName == value) return;
				_displayName = value ?? string.Empty;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
			}
		}

		public string CharacterId
		{
			get => _characterId;
			set
			{
				if (_characterId == value) return;
				_characterId = value ?? string.Empty;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterId)));
			}
		}

		public string SessionId
		{
			get => _sessionId;
			set
			{
				if (_sessionId == value) return;
				_sessionId = value ?? string.Empty;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionId)));
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;
	}

	private readonly ObservableCollection<EditableLauncherAccount> _accounts = new();

	public LauncherAccountConfigWindow()
	{
		InitializeComponent();
		AccountsGrid.ItemsSource = _accounts;
		LoadAccounts();
	}

	private void LoadAccounts()
	{
		_accounts.Clear();
		var selectedDisplayName = Settings.Default.LauncherSelectedDisplayName ?? string.Empty;

		foreach (var account in LauncherAccountStore.Load())
		{
			_accounts.Add(new EditableLauncherAccount
			{
				IsSelected = string.Equals(account.DisplayName, selectedDisplayName, System.StringComparison.Ordinal),
				DisplayName = account.DisplayName,
				CharacterId = account.CharacterId,
				SessionId = account.SessionId
			});
		}
	}

	private void AddRow_Click(object sender, RoutedEventArgs e)
	{
		var row = new EditableLauncherAccount();
		_accounts.Add(row);
		AccountsGrid.SelectedItem = row;
		AccountsGrid.ScrollIntoView(row);
	}

	private void RemoveRow_Click(object sender, RoutedEventArgs e)
	{
		if (AccountsGrid.SelectedItem is EditableLauncherAccount selected)
		{
			_accounts.Remove(selected);
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		AccountsGrid.CommitEdit();

		var selected = _accounts.FirstOrDefault(a => a.IsSelected && !string.IsNullOrWhiteSpace(a.DisplayName));
		foreach (var account in _accounts.Where(a => !ReferenceEquals(a, selected)))
		{
			account.IsSelected = false;
		}

		var normalized = _accounts
			.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName))
			.Select(a => new LauncherAccountModel
			{
				DisplayName = a.DisplayName.Trim(),
				CharacterId = (a.CharacterId ?? string.Empty).Trim(),
				SessionId = (a.SessionId ?? string.Empty).Trim()
			})
			.ToList();

		LauncherAccountStore.Save(normalized);
		Settings.Default.LauncherSelectedDisplayName = selected?.DisplayName?.Trim() ?? string.Empty;
		Settings.Default.Save();

		DialogResult = true;
		Close();
	}
}
