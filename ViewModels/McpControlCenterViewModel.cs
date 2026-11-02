using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Clipboard = System.Windows.Clipboard;

namespace Orbit.ViewModels;

public sealed class McpControlCenterViewModel : ObservableObject, IDisposable
{
    private readonly SessionCollectionService _sessionCollectionService;
    private readonly McpBridgeClientService _bridgeClient;
    private readonly McpPreferencesStore _preferencesStore;
    private readonly McpInjectorSettingsService _injectorSettingsService;
    private readonly DispatcherTimer _autoProbeTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private readonly NotifyCollectionChangedEventHandler _sessionsChangedHandler;
    private readonly HashSet<SessionModel> _observedSessions = new();

    private SessionProbeItem? _selectedSession;
    private string _lastResponseJson = "{}";
    private bool _isBusy;
    private string _statusSummary = "Idle";
    private bool _autoStartRuntimeOnInject;
    private bool _autoProbeEnabled;
    private int _autoProbeSeconds;
    private bool _sidecarArtifactsReady;
    private string _sidecarArtifactsSummary = string.Empty;
    private string _sidecarPath = string.Empty;
    private string _injectorSettingsPath = string.Empty;
    private bool _basicInjectorAutoStart = true;
    private bool _hasInjectorSettingsFile;

    public McpControlCenterViewModel(
        SessionCollectionService sessionCollectionService,
        McpBridgeClientService bridgeClient,
        McpPreferencesStore preferencesStore,
        McpInjectorSettingsService injectorSettingsService)
    {
        _sessionCollectionService = sessionCollectionService;
        _bridgeClient = bridgeClient;
        _preferencesStore = preferencesStore;
        _injectorSettingsService = injectorSettingsService;

        Sessions = new ObservableCollection<SessionProbeItem>();
        ActivityLog = new ObservableCollection<string>();

        RefreshSessionsCommand = new RelayCommand(RefreshSessions);
        StartRuntimeCommand = new AsyncRelayCommand(StartRuntimeAsync, () => CanInteractWithSession && !IsBusy);
        ProbeStatusCommand = new AsyncRelayCommand(() => ProbeAsync("me.get_status"), () => CanInteractWithSession && !IsBusy);
        ProbeCapabilitiesCommand = new AsyncRelayCommand(() => ProbeAsync("system.get_capabilities"), () => CanInteractWithSession && !IsBusy);
        ProbeQuickSnapshotCommand = new AsyncRelayCommand(() => ProbeAsync("me.quick_snapshot"), () => CanInteractWithSession && !IsBusy);
        ProbeDungeoneeringSnapshotCommand = new AsyncRelayCommand(() => ProbeAsync("dg.read_snapshot"), () => CanInteractWithSession && !IsBusy);
        ProbeDoActionSignalsCommand = new AsyncRelayCommand(() => ProbeAsync("me.get_doaction_signals"), () => CanInteractWithSession && !IsBusy);
        ValidateSidecarCommand = new RelayCommand(ValidateSidecarArtifacts);
        ReloadInjectorSettingsCommand = new RelayCommand(LoadInjectorSettings);
        SaveInjectorSettingsCommand = new RelayCommand(SaveInjectorSettings);
        CopyResponseCommand = new RelayCommand(CopyLastResponse);
        ClearLogCommand = new RelayCommand(() => ActivityLog.Clear());

        var preferences = _preferencesStore.Load();
        _autoStartRuntimeOnInject = preferences.AutoStartRuntimeOnInject;
        _autoProbeEnabled = preferences.AutoProbeEnabled;
        _autoProbeSeconds = Math.Max(1, preferences.AutoProbeSeconds);

        _autoProbeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(_autoProbeSeconds)
        };
        _autoProbeTimer.Tick += AutoProbeTimerOnTick;

        RefreshSessions();
        ValidateSidecarArtifacts();
        LoadInjectorSettings();
        UpdateAutoProbeState();
        if (_preferencesStore.LastError is { } preferenceError)
        {
            StatusSummary = preferenceError;
            AppendLog(preferenceError, ConsoleLogLevel.Error);
        }

        _sessionsChangedHandler = (_, _) => RefreshSessions();
        _sessionCollectionService.Sessions.CollectionChanged += _sessionsChangedHandler;
    }

    public ObservableCollection<SessionProbeItem> Sessions { get; }

    public ObservableCollection<string> ActivityLog { get; }

    public IRelayCommand RefreshSessionsCommand { get; }
    public IAsyncRelayCommand StartRuntimeCommand { get; }
    public IAsyncRelayCommand ProbeStatusCommand { get; }
    public IAsyncRelayCommand ProbeCapabilitiesCommand { get; }
    public IAsyncRelayCommand ProbeQuickSnapshotCommand { get; }
    public IAsyncRelayCommand ProbeDungeoneeringSnapshotCommand { get; }
    public IAsyncRelayCommand ProbeDoActionSignalsCommand { get; }
    public IRelayCommand ValidateSidecarCommand { get; }
    public IRelayCommand ReloadInjectorSettingsCommand { get; }
    public IRelayCommand SaveInjectorSettingsCommand { get; }
    public IRelayCommand CopyResponseCommand { get; }
    public IRelayCommand ClearLogCommand { get; }

    public SessionProbeItem? SelectedSession
    {
        get => _selectedSession;
        set
        {
            var changedTarget = _selectedSession?.SessionId != value?.SessionId || _selectedSession?.ProcessId != value?.ProcessId;
            if (SetProperty(ref _selectedSession, value))
            {
                if (changedTarget) LastResponseJson = "{}";
                OnPropertyChanged(nameof(CanInteractWithSession));
                OnPropertyChanged(nameof(SelectedSessionSummary));
                NotifyCommandStateChanged();
            }
        }
    }

    public bool CanInteractWithSession => !_disposed && SelectedSession is { ProcessId: > 0 } selected
        && _sessionCollectionService.Sessions.Any(s => s.Id == selected.SessionId
            && s.IsRuneScapeClient && s.InjectionState == InjectionState.Injected
            && s.State is not (SessionState.Closed or SessionState.ShuttingDown) && IsSessionProcessAlive(s));

    public string SelectedSessionSummary =>
        SelectedSession is null
            ? "No session selected."
            : $"Session: {SelectedSession.DisplayName} | PID {SelectedSession.ProcessId} | Injection {SelectedSession.InjectionStatus}";

    public string LastResponseJson
    {
        get => _lastResponseJson;
        set => SetProperty(ref _lastResponseJson, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string StatusSummary
    {
        get => _statusSummary;
        set => SetProperty(ref _statusSummary, value);
    }

    public bool AutoStartRuntimeOnInject
    {
        get => _autoStartRuntimeOnInject;
        set
        {
            if (SetProperty(ref _autoStartRuntimeOnInject, value))
            {
                SavePreferences();
                AppendLog($"Policy updated: Auto-start runtime on inject {(value ? "enabled" : "disabled")}.", ConsoleLogLevel.Info);
            }
        }
    }

    public bool AutoProbeEnabled
    {
        get => _autoProbeEnabled;
        set
        {
            if (SetProperty(ref _autoProbeEnabled, value))
            {
                SavePreferences();
                UpdateAutoProbeState();
            }
        }
    }

    public int AutoProbeSeconds
    {
        get => _autoProbeSeconds;
        set
        {
            var normalized = Math.Max(1, value);
            if (SetProperty(ref _autoProbeSeconds, normalized))
            {
                _autoProbeTimer.Interval = TimeSpan.FromSeconds(normalized);
                SavePreferences();
            }
        }
    }

    public bool SidecarArtifactsReady
    {
        get => _sidecarArtifactsReady;
        set => SetProperty(ref _sidecarArtifactsReady, value);
    }

    public string SidecarArtifactsSummary
    {
        get => _sidecarArtifactsSummary;
        set => SetProperty(ref _sidecarArtifactsSummary, value);
    }

    public string SidecarPath
    {
        get => _sidecarPath;
        set => SetProperty(ref _sidecarPath, value);
    }

    public string InjectorSettingsPath
    {
        get => _injectorSettingsPath;
        set => SetProperty(ref _injectorSettingsPath, value);
    }

    public bool BasicInjectorAutoStart
    {
        get => _basicInjectorAutoStart;
        set => SetProperty(ref _basicInjectorAutoStart, value);
    }

    public bool HasInjectorSettingsFile
    {
        get => _hasInjectorSettingsFile;
        set => SetProperty(ref _hasInjectorSettingsFile, value);
    }

    private void RefreshSessions()
    {
        foreach (var old in _observedSessions.Where(s => !_sessionCollectionService.Sessions.Contains(s)).ToArray())
        {
            old.PropertyChanged -= OnSessionChanged;
            _observedSessions.Remove(old);
        }
        foreach (var current in _sessionCollectionService.Sessions)
            if (_observedSessions.Add(current)) current.PropertyChanged += OnSessionChanged;
        var alive = _sessionCollectionService.Sessions
            .Where(s => s.IsRuneScapeClient && s.InjectionState == InjectionState.Injected
                && s.State is not (SessionState.Closed or SessionState.ShuttingDown) && IsSessionProcessAlive(s))
            .OrderBy(s => s.Name)
            .ToList();

        var previousSessionId = SelectedSession?.SessionId;
        Sessions.Clear();

        foreach (var session in alive)
        {
            var pid = session.RSProcess?.Id ?? 0;
            Sessions.Add(new SessionProbeItem(
                session.Id,
                pid,
                session.DisplayName,
                session.StatusSummary,
                session.InjectionState.ToString(),
                session.InjectionState == InjectionState.Injected ? PackIconMaterialKind.CheckCircle : PackIconMaterialKind.CircleOutline));
        }

        if (Sessions.Count == 0)
        {
            SelectedSession = null;
            StatusSummary = "No active injected sessions available.";
            return;
        }

        SelectedSession = previousSessionId.HasValue
            ? Sessions.FirstOrDefault(s => s.SessionId == previousSessionId.Value)
            : Sessions[0];

        StatusSummary = $"{Sessions.Count} active session(s) available for MCP probes.";
        OnPropertyChanged(nameof(SelectedSessionSummary));
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_disposed && e.PropertyName is nameof(SessionModel.RSProcess) or nameof(SessionModel.State)
            or nameof(SessionModel.InjectionState) or nameof(SessionModel.DisplayName) or nameof(SessionModel.Name))
            RefreshSessions();
    }

    private static bool IsSessionProcessAlive(SessionModel? session)
    {
        var process = session?.RSProcess;
        if (process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private async Task StartRuntimeAsync()
    {
        var target = SelectedSession;
        if (_disposed || target == null || !CanInteractWithSession)
        {
            return;
        }

        var live = _sessionCollectionService.Sessions.FirstOrDefault(s => s.Id == target.SessionId);
        if (live == null || live.RSProcess?.Id != target.ProcessId) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, live.WorkCancellation);
        IsBusy = true;
        try
        {
            var ok = await OrbitCommandClient.SendStartRuntimeWithRetryAsync(target.ProcessId, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(180), cancellationToken: request.Token).ConfigureAwait(true);
            if (_disposed || request.IsCancellationRequested || SelectedSession?.SessionId != target.SessionId || SelectedSession?.ProcessId != target.ProcessId) return;
            if (ok)
            {
                AppendLog($"START_RUNTIME sent to PID {target.ProcessId}.", ConsoleLogLevel.Info);
                StatusSummary = $"Runtime start requested for PID {target.ProcessId}.";
            }
            else
            {
                AppendLog($"Failed to send START_RUNTIME to PID {target.ProcessId}.", ConsoleLogLevel.Warning);
                StatusSummary = "Failed to send runtime start command.";
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && !request.IsCancellationRequested && SelectedSession?.SessionId == target.SessionId && SelectedSession?.ProcessId == target.ProcessId) { StatusSummary = ex.Message; AppendLog(ex.Message, ConsoleLogLevel.Error); } }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ProbeAsync(string command)
    {
        var target = SelectedSession;
        if (_disposed || target == null || !CanInteractWithSession)
        {
            return;
        }

        var live = _sessionCollectionService.Sessions.FirstOrDefault(s => s.Id == target.SessionId);
        if (live == null || live.RSProcess?.Id != target.ProcessId) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, live.WorkCancellation);
        IsBusy = true;
        try
        {
            var result = await _bridgeClient.CallAsync(target.ProcessId, command, new { }, request.Token).ConfigureAwait(true);
            if (_disposed || request.IsCancellationRequested || SelectedSession?.SessionId != target.SessionId || SelectedSession?.ProcessId != target.ProcessId) return;
            if (result.IsSuccess)
            {
                LastResponseJson = result.PayloadJson ?? "{}";
                StatusSummary = $"{command} succeeded (PID {target.ProcessId}).";
                AppendLog($"{command} OK pid={target.ProcessId} trace={result.TraceId ?? "n/a"} runtime={result.DurationMs?.ToString() ?? "n/a"}ms rtt={result.RoundTripMs}ms", ConsoleLogLevel.Info);
                return;
            }

            LastResponseJson = result.RawResponse ?? "{}";
            StatusSummary = $"{command} failed: {result.Message}";
            AppendLog($"{command} failed pid={target.ProcessId}: {result.Message}", ConsoleLogLevel.Warning);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && !request.IsCancellationRequested && SelectedSession?.SessionId == target.SessionId && SelectedSession?.ProcessId == target.ProcessId) { StatusSummary = ex.Message; AppendLog(ex.Message, ConsoleLogLevel.Error); } }
        finally
        {
            IsBusy = false;
        }
    }

    private void ValidateSidecarArtifacts()
    {
        var baseDir = AppContext.BaseDirectory;
        var exe = Path.Combine(baseDir, "MESharp.McpServer.exe");
        var dll = Path.Combine(baseDir, "MESharp.McpServer.dll");
        var deps = Path.Combine(baseDir, "MESharp.McpServer.deps.json");
        var runtime = Path.Combine(baseDir, "MESharp.McpServer.runtimeconfig.json");

        var missing = new List<string>();
        if (!File.Exists(exe)) missing.Add(Path.GetFileName(exe));
        if (!File.Exists(dll)) missing.Add(Path.GetFileName(dll));
        if (!File.Exists(deps)) missing.Add(Path.GetFileName(deps));
        if (!File.Exists(runtime)) missing.Add(Path.GetFileName(runtime));

        SidecarPath = exe;
        SidecarArtifactsReady = missing.Count == 0;
        SidecarArtifactsSummary = missing.Count == 0
            ? "All MCP sidecar artifacts are present in Orbit runtime directory."
            : $"Missing artifacts: {string.Join(", ", missing)}";

        AppendLog(SidecarArtifactsSummary, missing.Count == 0 ? ConsoleLogLevel.Info : ConsoleLogLevel.Warning);
    }

    private void LoadInjectorSettings()
    {
        var settings = _injectorSettingsService.Load();
        InjectorSettingsPath = settings.SettingsPath;
        BasicInjectorAutoStart = settings.AutoStart;
        HasInjectorSettingsFile = settings.Exists;
        if (settings.Error != null) { StatusSummary = settings.Error; AppendLog(settings.Error, ConsoleLogLevel.Error); }

        AppendLog(
            settings.Exists
                ? $"Loaded injector MCP settings from '{settings.SettingsPath}'."
                : $"Injector settings file not found; defaults loaded for '{settings.SettingsPath}'.",
            ConsoleLogLevel.Info);
    }

    private void SaveInjectorSettings()
    {
        var settings = new McpInjectorSettings(
            InjectorSettingsPath,
            BasicInjectorAutoStart,
            Exists: true);
        try { _injectorSettingsService.Save(settings); }
        catch (Exception ex) { StatusSummary = ex.Message; AppendLog(ex.Message, ConsoleLogLevel.Error); return; }
        HasInjectorSettingsFile = true;
        AppendLog(
            $"Saved injector MCP settings (MCP_AUTOSTART={(BasicInjectorAutoStart ? "1" : "0")}).",
            ConsoleLogLevel.Info);
    }

    private void CopyLastResponse()
    {
        try
        {
            Clipboard.SetText(LastResponseJson ?? "{}");
            AppendLog("Copied response JSON to clipboard.", ConsoleLogLevel.Info);
        }
        catch (Exception ex)
        {
            AppendLog($"Clipboard copy failed: {ex.Message}", ConsoleLogLevel.Warning);
        }
    }

    private async void AutoProbeTimerOnTick(object? sender, EventArgs e)
    {
        if (_disposed || !AutoProbeEnabled || IsBusy || SelectedSession == null)
        {
            return;
        }

        await ProbeAsync("me.quick_snapshot").ConfigureAwait(true);
    }

    private void SavePreferences()
    {
        try
        {
        _preferencesStore.Save(new McpPreferences
        {
            AutoStartRuntimeOnInject = AutoStartRuntimeOnInject,
            AutoProbeEnabled = AutoProbeEnabled,
            AutoProbeSeconds = AutoProbeSeconds
        });
        }
        catch (Exception ex) { StatusSummary = $"Cannot save MCP preferences: {ex.Message}"; AppendLog(StatusSummary, ConsoleLogLevel.Error); }
    }

    private void UpdateAutoProbeState()
    {
        if (AutoProbeEnabled)
        {
            _autoProbeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, AutoProbeSeconds));
            _autoProbeTimer.Start();
            AppendLog($"Auto-probe enabled ({AutoProbeSeconds}s).", ConsoleLogLevel.Info);
        }
        else
        {
            _autoProbeTimer.Stop();
            AppendLog("Auto-probe disabled.", ConsoleLogLevel.Info);
        }
    }

    private void AppendLog(string message, ConsoleLogLevel level)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ActivityLog.Insert(0, line);
        while (ActivityLog.Count > 250)
        {
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }

        ConsoleLogService.Instance.Append($"[MCP Control] {message}", ConsoleLogSource.Orbit, level);
    }

    private void NotifyCommandStateChanged()
    {
        StartRuntimeCommand.NotifyCanExecuteChanged();
        ProbeStatusCommand.NotifyCanExecuteChanged();
        ProbeCapabilitiesCommand.NotifyCanExecuteChanged();
        ProbeQuickSnapshotCommand.NotifyCanExecuteChanged();
        ProbeDungeoneeringSnapshotCommand.NotifyCanExecuteChanged();
        ProbeDoActionSignalsCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _autoProbeTimer.Stop();
        _autoProbeTimer.Tick -= AutoProbeTimerOnTick;
        _sessionCollectionService.Sessions.CollectionChanged -= _sessionsChangedHandler;
        foreach (var session in _observedSessions) session.PropertyChanged -= OnSessionChanged;
        _observedSessions.Clear();
    }
}

public sealed record SessionProbeItem(
    Guid SessionId,
    int ProcessId,
    string DisplayName,
    string Status,
    string InjectionStatus,
    PackIconMaterialKind Icon);
