using MahApps.Metro.Controls;
using Orbit.Models;
using Orbit.Services;
using Orbit.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

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
		LaunchDelaySlider.Value = ClampLaunchDelaySeconds(Settings.Default.LauncherBatchLaunchDelaySeconds);
		FullWaitCheckBox.IsChecked = Settings.Default.LauncherBatchWaitForDockBeforeNext;
	}

	private void LoadAccounts()
	{
		_accounts.Clear();
		var selectedDisplayName = Settings.Default.LauncherSelectedDisplayName ?? string.Empty;
		var loaded = LauncherAccountStore.Load();
		var hasExplicitSelections = loaded.Any(a => a.IsSelected);

		foreach (var account in loaded)
		{
			_accounts.Add(new EditableLauncherAccount
			{
				// Prefer persisted multi-select flags. Fall back to legacy single selection only when
				// no SELECTED flags exist in env_vars.json (migration compatibility).
				IsSelected = hasExplicitSelections
					? account.IsSelected
					: string.Equals(account.DisplayName, selectedDisplayName, System.StringComparison.Ordinal),
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
		SaveEntries();

		DialogResult = true;
		Close();
	}

	private void LaunchSelected_Click(object sender, RoutedEventArgs e)
	{
		SaveEntries();

		var ownerViewModel = Owner?.DataContext as MainWindowViewModel
			?? Application.Current?.Windows
				.OfType<Window>()
				.Select(window => window.DataContext)
				.OfType<MainWindowViewModel>()
				.FirstOrDefault();

		if (ownerViewModel == null)
		{
			MessageBox.Show("Could not find the active Orbit window to launch sessions.", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		if (!ownerViewModel.AddSessionCommand.CanExecute(null))
		{
			MessageBox.Show("Launch command is currently unavailable.", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		ownerViewModel.AddSessionCommand.Execute(null);
		DialogResult = true;
		Close();
	}

	private void SaveEntries()
	{
		AccountsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
		AccountsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

		var selectedAccounts = _accounts
			.Where(a => a.IsSelected && !string.IsNullOrWhiteSpace(a.DisplayName))
			.ToList();
		var primarySelected = selectedAccounts.FirstOrDefault();

		var normalized = _accounts
			.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName))
			.Select(a => new LauncherAccountModel
			{
				DisplayName = a.DisplayName.Trim(),
				CharacterId = (a.CharacterId ?? string.Empty).Trim(),
				SessionId = (a.SessionId ?? string.Empty).Trim(),
				IsSelected = a.IsSelected
			})
			.ToList();

		LauncherAccountStore.Save(normalized);
		Settings.Default.LauncherSelectedDisplayName = primarySelected?.DisplayName?.Trim() ?? string.Empty;
		Settings.Default.LauncherBatchLaunchDelaySeconds = ClampLaunchDelaySeconds(LaunchDelaySlider.Value);
		Settings.Default.LauncherBatchWaitForDockBeforeNext = FullWaitCheckBox.IsChecked == true;
		Settings.Default.Save();
	}

	private static int ClampLaunchDelaySeconds(double candidate)
	{
		var rounded = (int)System.Math.Round(candidate);
		return System.Math.Clamp(rounded, 5, 30);
	}
}
