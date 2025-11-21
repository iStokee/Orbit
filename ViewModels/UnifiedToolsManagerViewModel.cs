using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Orbit.Plugins;
using Orbit.Tooling;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Orbit.ViewModels;

/// <summary>
/// Unified dashboard for managing both built-in tools and dynamically loaded plugins.
/// Combines functionality from ToolsOverview and PluginManager into a single interface.
/// </summary>
public class UnifiedToolsManagerViewModel : INotifyPropertyChanged
{
    private readonly IToolRegistry _toolRegistry;
    private readonly PluginManager _pluginManager;
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private string _statusMessage = "Ready";
    private bool _isLoading;
    private string _searchFilter = string.Empty;

    public ObservableCollection<ToolCardViewModel> ToolCards { get; }

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

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            _searchFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public IRelayCommand ImportPluginCommand { get; }
    public IRelayCommand AutoLoadAllCommand { get; }
    public IRelayCommand RefreshCommand { get; }

    public UnifiedToolsManagerViewModel(
        IToolRegistry toolRegistry,
        PluginManager pluginManager,
        MainWindowViewModel? mainWindowViewModel = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _mainWindowViewModel = mainWindowViewModel;

        ToolCards = new ObservableCollection<ToolCardViewModel>();

        ImportPluginCommand = new RelayCommand(async () => await ImportPluginAsync());
        AutoLoadAllCommand = new RelayCommand(async () => await AutoLoadAllPluginsAsync());
        RefreshCommand = new RelayCommand(async () => await RefreshToolsAsync());

        // Subscribe to plugin events
        _pluginManager.PluginStatusChanged += OnPluginStatusChanged;

        // Initial load
        _ = RefreshToolsAsync();
    }

    private async Task ImportPluginAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Plugin DLL",
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
                        ? $"Plugin '{result.Metadata!.DisplayName}' hot-reloaded"
                        : $"Plugin '{result.Metadata!.DisplayName}' loaded successfully";

                    await RefreshToolsAsync();
                }
                else
                {
                    StatusMessage = $"Failed: {result.ErrorMessage}";
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

    private async Task AutoLoadAllPluginsAsync()
    {
        IsLoading = true;
        StatusMessage = "Auto-loading plugins...";

        try
        {
            var count = await _pluginManager.AutoLoadPluginsAsync();
            StatusMessage = $"Auto-loaded {count} plugin(s)";
            await RefreshToolsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Failed to auto-load plugins: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshToolsAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing tools...";

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ToolCards.Clear();

                // Get all tools from registry
                foreach (var tool in _toolRegistry.Tools)
                {
                    // Check if this is a plugin
                    var isPlugin = tool is IOrbitPlugin;
                    var pluginMetadata = isPlugin
                        ? _pluginManager.LoadedPlugins.FirstOrDefault(p => p.Key == tool.Key)
                        : null;

                    ToolCards.Add(new ToolCardViewModel(
                        tool,
                        isPlugin,
                        pluginMetadata,
                        _pluginManager,
                        _mainWindowViewModel));
                }

                var builtInCount = ToolCards.Count(t => !t.IsPlugin);
                var pluginCount = ToolCards.Count(t => t.IsPlugin);

                StatusMessage = $"{builtInCount} built-in tool(s), {pluginCount} plugin(s) loaded";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        foreach (var card in ToolCards)
        {
            card.IsVisible = string.IsNullOrWhiteSpace(_searchFilter) ||
                            card.DisplayName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                            card.Description.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                            card.Key.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnPluginStatusChanged(object? sender, PluginStatusChangedEventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = e.Message;
            _ = RefreshToolsAsync();
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a tool card in the unified manager (built-in or plugin).
/// </summary>
public class ToolCardViewModel : INotifyPropertyChanged
{
    private readonly IOrbitTool _tool;
    private readonly bool _isPlugin;
    private readonly PluginMetadata? _pluginMetadata;
    private readonly PluginManager? _pluginManager;
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private bool _isVisible = true;

    public string Key => _tool.Key;
    public string DisplayName => _tool.DisplayName;
    public MahApps.Metro.IconPacks.PackIconMaterialKind Icon => _tool.Icon;
    public bool IsPlugin => _isPlugin;
    public string ToolType => _isPlugin ? "Plugin" : "Built-in";

    public string Version => _pluginMetadata?.Version ?? "N/A";
    public string Author => _pluginMetadata?.Author ?? "Orbit";
    public string PluginPath => _pluginMetadata?.PluginPath ?? string.Empty;
    public string LoadedAt => _pluginMetadata?.LoadedAt.ToString("g") ?? string.Empty;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsVisibleInMenu
    {
        get => GetMenuVisibilitySetting();
        set
        {
            SetMenuVisibilitySetting(value);
            OnPropertyChanged();
        }
    }

    public string Description => GetToolDescription();

    public IRelayCommand UnloadCommand { get; }
    public IRelayCommand ReloadCommand { get; }

    public ToolCardViewModel(
        IOrbitTool tool,
        bool isPlugin,
        PluginMetadata? pluginMetadata,
        PluginManager? pluginManager,
        MainWindowViewModel? mainWindowViewModel)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _isPlugin = isPlugin;
        _pluginMetadata = pluginMetadata;
        _pluginManager = pluginManager;
        _mainWindowViewModel = mainWindowViewModel;

        UnloadCommand = new RelayCommand(async () => await UnloadPluginAsync(), () => IsPlugin);
        ReloadCommand = new RelayCommand(async () => await ReloadPluginAsync(), () => IsPlugin);
    }

    private async Task UnloadPluginAsync()
    {
        if (!IsPlugin || _pluginManager == null || _pluginMetadata == null)
            return;

        try
        {
            await _pluginManager.UnloadPluginAsync(_pluginMetadata.PluginPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to unload plugin: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ReloadPluginAsync()
    {
        if (!IsPlugin || _pluginManager == null || _pluginMetadata == null)
            return;

        try
        {
            var result = await _pluginManager.LoadPluginAsync(_pluginMetadata.PluginPath);
            if (!result.Success)
            {
                MessageBox.Show($"Failed to reload: {result.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reload plugin: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool GetMenuVisibilitySetting()
    {
        return Key switch
        {
            "Sessions" => Settings.Default.ShowMenuSessions,
            "SessionGallery" => Settings.Default.ShowMenuSessionGallery,
            "OrbitView" => Settings.Default.ShowMenuOrbitView,
            "AccountManager" => Settings.Default.ShowMenuAccountManager,
            "ThemeManager" => Settings.Default.ShowMenuThemeManager,
            "Console" => Settings.Default.ShowMenuConsole,
            "FsmNodeEditor" => Settings.Default.ShowMenuFsmNodeEditor,
            "ApiDocumentation" => Settings.Default.ShowMenuApiDocumentation,
            "Settings" => Settings.Default.ShowMenuSettings,
            _ => true // Plugins and other tools default to visible
        };
    }

    private void SetMenuVisibilitySetting(bool value)
    {
        if (_mainWindowViewModel != null)
        {
            void Apply()
            {
                switch (Key)
                {
                    case "Sessions":
                        _mainWindowViewModel.ShowMenuSessions = value;
                        break;
                    case "SessionGallery":
                        _mainWindowViewModel.ShowMenuSessionGallery = value;
                        break;
                    case "OrbitView":
                        _mainWindowViewModel.ShowMenuOrbitView = value;
                        break;
                    case "AccountManager":
                        _mainWindowViewModel.ShowMenuAccountManager = value;
                        break;
                case "ThemeManager":
                    _mainWindowViewModel.ShowMenuThemeManager = value;
                    break;
                case "FsmNodeEditor":
                    _mainWindowViewModel.ShowMenuFsmNodeEditor = value;
                    break;
                case "Console":
                    _mainWindowViewModel.ShowMenuConsole = value;
                    break;
                case "ApiDocumentation":
                    _mainWindowViewModel.ShowMenuGuide = value;
                        break;
                    case "Settings":
                        _mainWindowViewModel.ShowMenuSettings = value;
                        break;
                }
            }

            Application.Current?.Dispatcher?.Invoke(Apply);
        }

        // Also update settings directly
        switch (Key)
        {
            case "Sessions":
                Settings.Default.ShowMenuSessions = value;
                break;
            case "SessionGallery":
                Settings.Default.ShowMenuSessionGallery = value;
                break;
            case "OrbitView":
                Settings.Default.ShowMenuOrbitView = value;
                break;
            case "AccountManager":
                Settings.Default.ShowMenuAccountManager = value;
                break;
            case "ThemeManager":
                Settings.Default.ShowMenuThemeManager = value;
                break;
            case "FsmNodeEditor":
                Settings.Default.ShowMenuFsmNodeEditor = value;
                break;
            case "Console":
                Settings.Default.ShowMenuConsole = value;
                break;
            case "ApiDocumentation":
                Settings.Default.ShowMenuApiDocumentation = value;
                break;
            case "Settings":
                Settings.Default.ShowMenuSettings = value;
                break;
        }

        Settings.Default.Save();
    }

    private string GetToolDescription()
    {
        // Plugin description
        if (_isPlugin && _pluginMetadata != null)
        {
            return _pluginMetadata.Description;
        }

        // Built-in tool descriptions
        return Key switch
        {
            "SessionsOverview" => "Overview and management of all RuneScape 3 sessions",
            "Sessions" => "Manage RuneScape 3 sessions and embedded client windows",
            "SessionGallery" => "Visual gallery view of all active game sessions",
            "SessionGrid" => "Grid layout for managing multiple game sessions",
            "AccountManager" => "Manage account credentials and quick login",
            "ScriptControls" => "Load and control C# scripts via hot reload",
            "ThemeManager" => "Customize themes, accents, and appearance",
            "ScriptManager" => "Browse and manage script library",
            "Console" => "View unified logs from Orbit, ME, and scripts",
            "FsmNodeEditor" => "Visual editor and runner for FSM-based automations",
            "ApiDocumentation" => "Open the Orbiters Guide documentation hub",
            "ToolsOverview" => "Manage registered tools and their visibility",
            "PluginManager" => "Load and manage dynamic plugin tools",
            "Settings" => "Configure Orbit application settings",
            _ => "Tool - no description available"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
