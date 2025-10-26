using Microsoft.Win32;
using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Orbit.ViewModels;

public class ScriptManagerViewModel : INotifyPropertyChanged
{
	private readonly ScriptManagerService _scriptService;
	private readonly Window _owner;

	public ScriptManagerViewModel(Window owner)
	{
		_owner = owner;
		_scriptService = new ScriptManagerService();

		AddScriptCommand = new RelayCommand(_ => AddScript());
		RemoveScriptCommand = new RelayCommand(p => RemoveScript(p as ScriptProfile), p => p is ScriptProfile);
		ToggleFavoriteCommand = new RelayCommand(p => ToggleFavorite(p as ScriptProfile), p => p is ScriptProfile);
		LoadScriptCommand = new RelayCommand(async p => await LoadScriptAsync(p as ScriptProfile), p => p is ScriptProfile sp && sp.FileExists);
		ClearMissingCommand = new RelayCommand(_ => ClearMissing());
	}

	public ObservableCollection<ScriptProfile> RecentScripts => _scriptService.RecentScripts;

	public ObservableCollection<ScriptProfile> Favorites
	{
		get
		{
			var fav = _scriptService.GetFavorites().ToList();
			return new ObservableCollection<ScriptProfile>(fav);
		}
	}

	public bool HasFavorites => Favorites.Any();

	public ICommand AddScriptCommand { get; }
	public ICommand RemoveScriptCommand { get; }
	public ICommand ToggleFavoriteCommand { get; }
	public ICommand LoadScriptCommand { get; }
	public ICommand ClearMissingCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	private void AddScript()
	{
		var dialog = new OpenFileDialog
		{
			Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
			Title = "Select Script DLL",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\MemoryError\\CSharp_scripts\\"
		};

		if (dialog.ShowDialog() == true)
		{
			_scriptService.AddOrUpdateScript(dialog.FileName);
			OnPropertyChanged(nameof(Favorites));
			OnPropertyChanged(nameof(HasFavorites));
		}
	}

	private void RemoveScript(ScriptProfile? profile)
	{
		if (profile == null) return;

		var result = MessageBox.Show(
			$"Remove '{profile.Name}' from the list?",
			"Confirm Removal",
			MessageBoxButton.YesNo,
			MessageBoxImage.Question);

		if (result == MessageBoxResult.Yes)
		{
			_scriptService.RemoveScript(profile);
			OnPropertyChanged(nameof(Favorites));
			OnPropertyChanged(nameof(HasFavorites));
		}
	}

	private void ToggleFavorite(ScriptProfile? profile)
	{
		if (profile == null) return;

		_scriptService.ToggleFavorite(profile);
		OnPropertyChanged(nameof(Favorites));
		OnPropertyChanged(nameof(HasFavorites));
	}

    private async Task LoadScriptAsync(ScriptProfile? profile)
    {
        if (profile == null || !profile.FileExists) return;

		// Update last used
		profile.LastUsed = DateTime.Now;
		_scriptService.AddOrUpdateScript(profile.FilePath, profile.Name, profile.Description);

        // Use RELOAD for both initial and subsequent loads (avoids legacy runtime restart)
        // Route to the currently selected session in the main window when available
        var mainWindow = System.Windows.Application.Current?.MainWindow as Orbit.MainWindow;
        var mainVm = mainWindow?.DataContext as Orbit.ViewModels.MainWindowViewModel;
        var pid = mainVm?.SelectedSession?.RSProcess?.Id;
        var success = pid.HasValue
            ? await OrbitCommandClient.SendReloadAsync(profile.FilePath, pid.Value, CancellationToken.None)
            : await OrbitCommandClient.SendReloadAsync(profile.FilePath, CancellationToken.None);

		if (success)
		{
			MessageBox.Show(
				$"Script '{profile.Name}' loaded successfully!",
				"Script Loaded",
				MessageBoxButton.OK,
				MessageBoxImage.Information);
		}
		else
		{
			MessageBox.Show(
				$"Failed to load script '{profile.Name}'. Check console for details.",
				"Load Failed",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
		}
	}

	private void ClearMissing()
	{
		_scriptService.ClearNonExisting();
		OnPropertyChanged(nameof(Favorites));
		OnPropertyChanged(nameof(HasFavorites));
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
