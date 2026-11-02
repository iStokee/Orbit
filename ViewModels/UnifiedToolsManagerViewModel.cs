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
public class UnifiedToolsManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IToolRegistry _toolRegistry;
    private readonly PluginManager _pluginManager;
    private readonly WeakReference<MainWindowViewModel>? _mainWindowViewModel;
    private readonly Func<MainWindowViewModel?> _resolveOwner;
    private string _statusMessage = "Ready";
    private bool _isLoading;
    private string _searchFilter = string.Empty;
    private bool _disposed;

    public ObservableCollection<ToolCardViewModel> ToolCards { get; }
    public string InventorySummary { get; private set; } = string.Empty;
    public bool HasNoSearchResults => ToolCards.All(card => !card.IsVisible);

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
            ImportPluginCommand?.NotifyCanExecuteChanged();
            AutoLoadAllCommand?.NotifyCanExecuteChanged();
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
        MainWindowViewModel? mainWindowViewModel = null,
        Func<MainWindowViewModel?>? resolveOwner = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _mainWindowViewModel = mainWindowViewModel == null ? null : new(mainWindowViewModel);
        _resolveOwner = resolveOwner ?? (() => _mainWindowViewModel?.TryGetTarget(out var owner) == true ? owner : null);

        ToolCards = new ObservableCollection<ToolCardViewModel>();

        ImportPluginCommand = new RelayCommand(async () => await ImportPluginAsync(), () => !IsLoading);
        AutoLoadAllCommand = new RelayCommand(async () => await AutoLoadAllPluginsAsync(), () => !IsLoading);
        RefreshCommand = new RelayCommand(async () => await RefreshToolsAsync());

        // Subscribe to plugin events
        _pluginManager.PluginStatusChanged += OnPluginStatusChanged;
        Settings.Default.PropertyChanged += OnSettingsChanged;

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
        if (_disposed) return;

        try
        {
            void Refresh()
            {
                if (_disposed) return;
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
                        _resolveOwner(), _resolveOwner, message => StatusMessage = message));
                }

                var builtInCount = ToolCards.Count(t => !t.IsPlugin);
                var pluginCount = ToolCards.Count(t => t.IsPlugin);

                InventorySummary = $"{builtInCount} built-in tool(s), {pluginCount} plugin(s) loaded";
                OnPropertyChanged(nameof(InventorySummary));
                ApplyFilter();
            }
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                await dispatcher.InvokeAsync(Refresh);
            else Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        foreach (var card in ToolCards) card.NotifyMenuVisibilityChanged();
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
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    private void OnPluginStatusChanged(object? sender, PluginStatusChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        void Apply()
        {
            if (_disposed)
            {
                return;
            }

            StatusMessage = e.Message;
            _ = RefreshToolsAsync();
        }
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.InvokeAsync(Apply);
        else Apply();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pluginManager.PluginStatusChanged -= OnPluginStatusChanged;
        Settings.Default.PropertyChanged -= OnSettingsChanged;
        ToolCards.Clear();
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
    private readonly WeakReference<MainWindowViewModel>? _mainWindowViewModel;
    private readonly Func<MainWindowViewModel?> _resolveOwner;
    private readonly Action<string>? _setStatus;
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
    public bool SupportsMenuVisibility => Key is "SessionsOverview" or "Sessions" or "SessionGallery" or "OrbitView"
        or "AccountManager" or "ScriptManager" or "ThemeManager" or "Console" or "McpControl"
        or "SharpBuilder" or "ApiDocumentation" or "Settings" or "UnifiedToolsManager" or "ToolsOverview";
    public IRelayCommand OpenCommand { get; }
    public void NotifyMenuVisibilityChanged() => OnPropertyChanged(nameof(IsVisibleInMenu));

    public IRelayCommand UnloadCommand { get; }
    public IRelayCommand ReloadCommand { get; }

    public ToolCardViewModel(
        IOrbitTool tool,
        bool isPlugin,
        PluginMetadata? pluginMetadata,
        PluginManager? pluginManager,
        MainWindowViewModel? mainWindowViewModel,
        Func<MainWindowViewModel?>? resolveOwner = null,
        Action<string>? setStatus = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _isPlugin = isPlugin;
        _pluginMetadata = pluginMetadata;
        _pluginManager = pluginManager;
        _mainWindowViewModel = mainWindowViewModel == null ? null : new(mainWindowViewModel);
        _resolveOwner = resolveOwner ?? (() => _mainWindowViewModel?.TryGetTarget(out var owner) == true ? owner : null);
        _setStatus = setStatus;
        OpenCommand = new RelayCommand(() =>
        {
            try
            {
                var owner = _resolveOwner();
                if (owner == null) { _setStatus?.Invoke("Open this dashboard in an Orbit window to launch tools."); return; }
                if (!owner.TryOpenToolByKey(Key)) _setStatus?.Invoke($"Tool '{DisplayName}' is no longer available.");
            }
            catch (Exception ex) { _setStatus?.Invoke($"Cannot open '{DisplayName}': {ex.Message}"); }
        });

        UnloadCommand = new RelayCommand(async () => await UnloadPluginAsync(), () => IsPlugin);
        ReloadCommand = new RelayCommand(async () => await ReloadPluginAsync(), () => IsPlugin);
    }

    private async Task UnloadPluginAsync()
    {
        if (!IsPlugin || _pluginManager == null || _pluginMetadata == null)
            return;

        try
        {
            if (!await _pluginManager.UnloadPluginAsync(_pluginMetadata.PluginPath))
                _setStatus?.Invoke($"Could not unload '{DisplayName}'. Close its open views and retry.");
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
            "SessionsOverview" => Settings.Default.ShowMenuSessions,
            "Sessions" => Settings.Default.ShowMenuSessions,
            "SessionGallery" => Settings.Default.ShowMenuSessionGallery,
            "OrbitView" => Settings.Default.ShowMenuOrbitView,
            "AccountManager" => Settings.Default.ShowMenuAccountManager,
            "ScriptManager" => Settings.Default.ShowMenuScriptControls,
            "ThemeManager" => Settings.Default.ShowMenuThemeManager,
            "Console" => Settings.Default.ShowMenuConsole,
            "McpControl" => Settings.Default.ShowMenuMcpControl,
            "SharpBuilder" => Settings.Default.ShowMenuSharpBuilder,
            "ApiDocumentation" => Settings.Default.ShowMenuApiDocumentation,
            "Settings" => Settings.Default.ShowMenuSettings,
            "UnifiedToolsManager" => Settings.Default.ShowMenuToolsOverview,
            "ToolsOverview" => Settings.Default.ShowMenuToolsOverview,
            _ => true // Plugins and other tools default to visible
        };
    }

    private void SetMenuVisibilitySetting(bool value)
    {
        if (!SupportsMenuVisibility) return;
        switch (Key)
        {
            case "SessionsOverview":
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
            case "ScriptManager":
                Settings.Default.ShowMenuScriptControls = value;
                break;
            case "ThemeManager":
                Settings.Default.ShowMenuThemeManager = value;
                break;
            case "SharpBuilder":
                Settings.Default.ShowMenuSharpBuilder = value;
                break;
            case "Console":
                Settings.Default.ShowMenuConsole = value;
                break;
            case "McpControl":
                Settings.Default.ShowMenuMcpControl = value;
                break;
            case "ApiDocumentation":
                Settings.Default.ShowMenuApiDocumentation = value;
                break;
            case "Settings":
                Settings.Default.ShowMenuSettings = value;
                break;
            case "UnifiedToolsManager":
            case "ToolsOverview":
                Settings.Default.ShowMenuToolsOverview = value;
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
            "OrbitView" => "Arrange live sessions in a split workspace",
            "UnifiedToolsManager" => "Open tools and manage plugin lifecycle and menu visibility",
            "AccountManager" => "Organize saved accounts, credentials, and nicknames",
            "ScriptControls" => "Load and control C# scripts via hot reload",
            "ThemeManager" => "Customize themes, accents, and appearance",
            "ScriptManager" => "Browse and manage script library",
            "Console" => "View unified logs from Orbit, ME, and scripts",
            "McpControl" => "Control MCP runtime policy, probe bridge health, and inspect structured payloads",
            "SharpBuilder" => "SharpBuilder: visual editor and runner for automation graphs",
            "ApiDocumentation" => "Open the Orbit documentation hub",
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
