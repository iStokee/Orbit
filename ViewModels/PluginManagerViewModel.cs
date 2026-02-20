using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Orbit.Plugins;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Orbit.ViewModels;

public class PluginManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PluginManager _pluginManager;
    private string _statusMessage = "Ready";
    private bool _isLoading;
    private bool _disposed;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public ObservableCollection<PluginItemViewModel> Plugins { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public IRelayCommand LoadPluginCommand { get; }
    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand AutoLoadAllCommand { get; }

    public PluginManagerViewModel(PluginManager pluginManager)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        Plugins = new ObservableCollection<PluginItemViewModel>();

        LoadPluginCommand = new RelayCommand(async () => await LoadPluginAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshPluginsAsync());
        AutoLoadAllCommand = new RelayCommand(async () => await AutoLoadAllAsync());

        // Subscribe to plugin status changes
        _pluginManager.PluginStatusChanged += OnPluginStatusChanged;

        // Initial load
        _ = RefreshPluginsAsync();
    }

    private async Task LoadPluginAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
        var dialog = new OpenFileDialog
        {
            Title = "Select Plugin DLL",
            Filter = "Plugin Files (*.dll)|*.dll|All Files (*.*)|*.*",
            InitialDirectory = PluginManager.GetDefaultPluginDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = "Loading plugin...";

            try
            {
                var result = await _pluginManager.LoadPluginAsync(dialog.FileName);

                if (result.Success)
                {
                    StatusMessage = result.WasReloaded
                        ? $"Plugin '{result.Metadata!.DisplayName}' hot-reloaded successfully"
                        : $"Plugin '{result.Metadata!.DisplayName}' loaded successfully";

                    await RefreshPluginsCoreAsync();
                }
                else
                {
                    StatusMessage = $"Failed to load plugin: {result.ErrorMessage}";
                    MessageBox.Show(result.ErrorMessage, "Plugin Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to load plugin: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RefreshPluginsAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            await RefreshPluginsCoreAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RefreshPluginsCoreAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing plugins...";

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Plugins.Clear();

                foreach (var metadata in _pluginManager.LoadedPlugins)
                {
                    Plugins.Add(new PluginItemViewModel(metadata, _pluginManager));
                }

                StatusMessage = $"{Plugins.Count} plugin(s) loaded";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error refreshing: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AutoLoadAllAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
        IsLoading = true;
        StatusMessage = "Auto-loading plugins...";

        try
        {
            var count = await _pluginManager.AutoLoadPluginsAsync();
            StatusMessage = $"Auto-loaded {count} plugin(s)";
            await RefreshPluginsCoreAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error auto-loading: {ex.Message}";
            MessageBox.Show($"Failed to auto-load plugins: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnPluginStatusChanged(object? sender, PluginStatusChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = e.Message;
            _ = RefreshPluginsAsync();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pluginManager.PluginStatusChanged -= OnPluginStatusChanged;
        _operationGate.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class PluginItemViewModel : INotifyPropertyChanged
{
    private readonly PluginMetadata _metadata;
    private readonly PluginManager _pluginManager;

    public string DisplayName => _metadata.DisplayName;
    public string Key => _metadata.Key;
    public string Version => _metadata.Version;
    public string Author => _metadata.Author;
    public string Description => _metadata.Description;
    public string PluginPath => _metadata.PluginPath;
    public string LoadedAt => _metadata.LoadedAt.ToString("g");
    public bool IsLoaded => _metadata.IsLoaded;

    public IRelayCommand UnloadCommand { get; }
    public IRelayCommand ReloadCommand { get; }

    public PluginItemViewModel(PluginMetadata metadata, PluginManager pluginManager)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));

        UnloadCommand = new RelayCommand(async () => await UnloadAsync());
        ReloadCommand = new RelayCommand(async () => await ReloadAsync());
    }

    private async Task UnloadAsync()
    {
        try
        {
            await _pluginManager.UnloadPluginAsync(_metadata.PluginPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to unload plugin: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            var result = await _pluginManager.LoadPluginAsync(_metadata.PluginPath);
            if (!result.Success)
            {
                MessageBox.Show($"Failed to reload plugin: {result.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reload plugin: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
