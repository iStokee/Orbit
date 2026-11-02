using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Orbit.ViewModels
{
	public class SessionsOverviewViewModel : INotifyPropertyChanged, IDisposable
	{
			private readonly Action<SessionModel> activateSession;
			private readonly Action<SessionModel> focusSession;
			private readonly Action<SessionModel> closeSession;
			private readonly Func<SessionModel, Task> toggleNativeDebugMenu;
			private readonly Func<SessionModel, bool> canToggleNativeDebugMenu;
			private readonly SessionReconciliationService reconciliationService;
			private readonly OrbitLayoutStateService orbitLayoutState;
			private readonly ScriptOrchestrationService orchestration;
			private readonly SessionPlacementService? placementService;
			private SessionModel? selectedSession;
			private bool _isScriptCommandInFlight;
			private string _diagnosticsStatus = "Diagnostics not captured yet.";

			public SessionsOverviewViewModel(
				ObservableCollection<SessionModel> sessions,
				Action<SessionModel> activateSession,
				Action<SessionModel> focusSession,
				Action<SessionModel> closeSession,
				Func<SessionModel, Task> toggleNativeDebugMenu,
				Func<SessionModel, bool> canToggleNativeDebugMenu,
				SessionReconciliationService reconciliationService,
				OrbitLayoutStateService orbitLayoutState,
				ScriptOrchestrationService orchestration, SessionPlacementService? placementService = null)
			{
				Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
				this.activateSession = activateSession ?? throw new ArgumentNullException(nameof(activateSession));
				this.focusSession = focusSession ?? throw new ArgumentNullException(nameof(focusSession));
				this.closeSession = closeSession ?? throw new ArgumentNullException(nameof(closeSession));
				this.toggleNativeDebugMenu = toggleNativeDebugMenu ?? throw new ArgumentNullException(nameof(toggleNativeDebugMenu));
				this.canToggleNativeDebugMenu = canToggleNativeDebugMenu ?? throw new ArgumentNullException(nameof(canToggleNativeDebugMenu));
				this.reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
				this.orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));
				this.orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
				this.placementService = placementService;

			SetActiveCommand = new RelayCommand<SessionModel?>(session =>
			{
				if (TryResolveSession(session, out var resolved))
				{
					activateSession.Invoke(resolved);
				}
			}, session => session is SessionModel);

			FocusCommand = new RelayCommand<SessionModel?>(session =>
			{
				if (TryResolveSession(session, out var resolved))
				{
					focusSession.Invoke(resolved);
				}
			}, session => session is SessionModel);

				CloseCommand = new RelayCommand<SessionModel?>(session =>
				{
					if (TryResolveSession(session, out var resolved))
					{
						closeSession.Invoke(resolved);
					}
				}, session => session is SessionModel);

				ToggleNativeDebugMenuCommand = new RelayCommand<SessionModel?>(
					async session =>
					{
						if (TryResolveSession(session, out var resolved))
						{
							await this.toggleNativeDebugMenu(resolved).ConfigureAwait(true);
						}
					},
					session => session is SessionModel model && this.canToggleNativeDebugMenu(model));

				LoadScriptCommand = new RelayCommand<SessionModel?>(async session => await LoadScriptAsync(session), CanLoadScript);
			ReloadScriptCommand = new RelayCommand<SessionModel?>(async session => await ReloadScriptAsync(session), CanReloadScript);
			StopScriptCommand = new RelayCommand<SessionModel?>(async session => await StopScriptAsync(session), CanStopScript);
			DumpDiagnosticsCommand = new RelayCommand(DumpDiagnostics);

			Sessions.CollectionChanged += OnSessionsCollectionChanged;
			Settings.Default.PropertyChanged += OnSettingsChanged;
			if (placementService != null)
			{
				placementService.PlacementChanged += OnPlacementChanged;
				placementService.MoveCompleted += OnPlacementChanged;
			}
			foreach (var session in Sessions)
			{
				AttachSession(session);
			}
		}

		public ObservableCollection<SessionModel> Sessions { get; }

		public SessionModel? SelectedSession
		{
			get => selectedSession;
			set
			{
				if (ReferenceEquals(selectedSession, value))
					return;
				selectedSession = value;
				OnPropertyChanged();
				CommandManager.InvalidateRequerySuggested();
			}
		}

		public string ConfiguredScriptPath => Settings.Default.HotReloadScriptPath ?? string.Empty;
		public bool HasConfiguredScriptPath => !string.IsNullOrWhiteSpace(ConfiguredScriptPath) && File.Exists(ConfiguredScriptPath);
		public int SessionCount => Sessions.Count;
		public int InjectedSessionCount => Sessions.Count(s => s.InjectionState == InjectionState.Injected);
		public int ScriptLoadedCount => Sessions.Count(s => !string.IsNullOrWhiteSpace(s.ActiveScriptPath));
		public int ErrorSessionCount => Sessions.Count(s => !string.IsNullOrWhiteSpace(s.LastError));
		public int UnplacedSessionCount => reconciliationService.CountUnplacedSessions();

		public string DiagnosticsStatus
		{
			get => _diagnosticsStatus;
			private set
			{
				if (_diagnosticsStatus == value)
					return;
				_diagnosticsStatus = value;
				OnPropertyChanged();
			}
		}

		public IRelayCommand<SessionModel?> SetActiveCommand { get; }
		public IRelayCommand<SessionModel?> FocusCommand { get; }
			public IRelayCommand<SessionModel?> CloseCommand { get; }
			public IRelayCommand<SessionModel?> ToggleNativeDebugMenuCommand { get; }
			public IRelayCommand<SessionModel?> LoadScriptCommand { get; }
		public IRelayCommand<SessionModel?> ReloadScriptCommand { get; }
		public IRelayCommand<SessionModel?> StopScriptCommand { get; }
		public IRelayCommand DumpDiagnosticsCommand { get; }

		public event PropertyChangedEventHandler? PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		private bool TryResolveSession(SessionModel? parameter, out SessionModel session)
		{
			if (parameter is SessionModel model)
			{
				SelectedSession = model;
				session = model;
				return true;
			}

			session = null!;
			return false;
		}

		private void DumpDiagnostics()
		{
			var unplaced = reconciliationService.CountUnplacedSessions();

			// LogDiagnostics performs the on-demand visual-tree dump — retained as a manual
			// debugging aid only; it no longer drives any automatic decision (Stage 2c).
			reconciliationService.LogDiagnostics(orbitLayoutState.Items.Cast<object>(), "sessions-overview-dump");
			DiagnosticsStatus = $"Logged diagnostics for {SessionCount} session(s); {unplaced} unplaced.";
			OnPropertyChanged(nameof(UnplacedSessionCount));
		}

		private bool CanLoadScript(SessionModel? session)
		{
			if (!Settings.Default.MesharpIntegrationEnabled)
				return false;

			if (_isScriptCommandInFlight)
				return false;

			if (session is not SessionModel target)
				return false;

			return target.InjectionState == InjectionState.Injected
				&& target.RSProcess != null
				&& HasConfiguredScriptPath;
		}

		private bool CanReloadScript(SessionModel? session)
		{
			if (!Settings.Default.MesharpIntegrationEnabled)
				return false;

			if (_isScriptCommandInFlight)
				return false;

			if (session is not SessionModel target)
				return false;

			if (target.InjectionState != InjectionState.Injected || target.RSProcess == null)
				return false;

			var active = target.ActiveScriptPath;
			if (!string.IsNullOrWhiteSpace(active) && File.Exists(active))
			{
				return true;
			}

			return HasConfiguredScriptPath;
		}

		private bool CanStopScript(SessionModel? session)
		{
			if (!Settings.Default.MesharpIntegrationEnabled)
				return false;

			if (_isScriptCommandInFlight)
				return false;

			if (session is not SessionModel target)
				return false;

			return target.InjectionState == InjectionState.Injected && target.RSProcess != null;
		}

		private async Task LoadScriptAsync(SessionModel? session)
		{
			if (!TryResolveSession(session, out var target))
				return;

			if (!Settings.Default.MesharpIntegrationEnabled)
			{
				target.SetScriptRuntimeError("MESharp integration is disabled.");
				ConsoleLogService.Instance.Append(
					$"[Sessions] MESharp integration is disabled. Script commands are unavailable for '{target.Name}'.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			if (!HasConfiguredScriptPath)
			{
				target.SetScriptRuntimeError("No script selected. Choose a script in Script Manager first.");
				ConsoleLogService.Instance.Append(
					$"[Sessions] Cannot load script for '{target.Name}' - no configured script path.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			await RunScriptCommandAsync(target, ConfiguredScriptPath, reload: false);
		}

		private async Task ReloadScriptAsync(SessionModel? session)
		{
			if (!TryResolveSession(session, out var target))
				return;

			if (!Settings.Default.MesharpIntegrationEnabled)
			{
				target.SetScriptRuntimeError("MESharp integration is disabled.");
				ConsoleLogService.Instance.Append(
					$"[Sessions] MESharp integration is disabled. Script commands are unavailable for '{target.Name}'.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			var path = target.ActiveScriptPath;
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				path = HasConfiguredScriptPath ? ConfiguredScriptPath : string.Empty;
			}

			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				target.SetScriptRuntimeError("No active/configured script available to reload.");
				ConsoleLogService.Instance.Append(
					$"[Sessions] Cannot reload script for '{target.Name}' - script path unavailable.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			await RunScriptCommandAsync(target, path, reload: true);
		}

		private async Task StopScriptAsync(SessionModel? session)
		{
			if (!TryResolveSession(session, out var target))
				return;

			if (!Settings.Default.MesharpIntegrationEnabled)
			{
				target.SetScriptRuntimeError("MESharp integration is disabled.");
				ConsoleLogService.Instance.Append(
					$"[Sessions] MESharp integration is disabled. Script stop is unavailable for '{target.Name}'.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			if (target.InjectionState != InjectionState.Injected || target.RSProcess == null)
			{
				target.SetScriptRuntimeError("Session is not injected.");
				return;
			}

			await SetScriptCommandInFlightAsync(async () =>
			{
				await orchestration.UnloadActiveScriptAsync(target).ConfigureAwait(true);
			}).ConfigureAwait(true);
		}

		private async Task RunScriptCommandAsync(SessionModel target, string scriptPath, bool reload)
		{
			if (target.InjectionState != InjectionState.Injected || target.RSProcess == null)
			{
				target.SetScriptRuntimeError("Session is not injected.");
				return;
			}

			await SetScriptCommandInFlightAsync(async () =>
			{
				if (reload)
					await orchestration.ReloadAsync(target, scriptPath).ConfigureAwait(true);
				else
					await orchestration.LoadAsync(target, scriptPath).ConfigureAwait(true);
			}).ConfigureAwait(true);
		}

		private async Task SetScriptCommandInFlightAsync(Func<Task> action)
		{
			if (_isScriptCommandInFlight)
				return;

			try
			{
				_isScriptCommandInFlight = true;
				RefreshCommandStates();
				await action().ConfigureAwait(true);
			}
			finally
			{
				_isScriptCommandInFlight = false;
				RefreshCommandStates();
			}
		}

		private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				foreach (var session in Sessions)
				{
					session.PropertyChanged -= OnSessionPropertyChanged;
					session.PropertyChanged += OnSessionPropertyChanged;
				}
			}

			if (e.OldItems != null)
			{
				foreach (SessionModel item in e.OldItems)
				{
					DetachSession(item);
				}
			}

			if (e.NewItems != null)
			{
				foreach (SessionModel item in e.NewItems)
				{
					AttachSession(item);
				}
			}

			if (selectedSession != null && !Sessions.Contains(selectedSession))
			{
				SelectedSession = Sessions.Count > 0 ? Sessions[0] : null;
			}

			OnPropertyChanged(nameof(HasConfiguredScriptPath));
			OnPropertyChanged(nameof(ConfiguredScriptPath));
			NotifySummaryChanged();
			RefreshCommandStates();
		}

		private void AttachSession(SessionModel session)
		{
			session.PropertyChanged += OnSessionPropertyChanged;
		}

		private void DetachSession(SessionModel session)
		{
			session.PropertyChanged -= OnSessionPropertyChanged;
		}

		private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SessionModel.InjectionState) ||
				e.PropertyName == nameof(SessionModel.ActiveScriptPath) ||
				e.PropertyName == nameof(SessionModel.LastError))
			{
				NotifySummaryChanged();
			}

				if (e.PropertyName == nameof(SessionModel.InjectionState) ||
					e.PropertyName == nameof(SessionModel.RSProcess) ||
					e.PropertyName == nameof(SessionModel.NativeDebugMenuVisible) ||
					e.PropertyName == nameof(SessionModel.ActiveScriptPath) ||
					e.PropertyName == nameof(SessionModel.ScriptRuntimeStatus) ||
					e.PropertyName == nameof(SessionModel.ScriptLastChangedAt))
			{
				RefreshCommandStates();
			}
		}

		private void RefreshCommandStates()
		{
				LoadScriptCommand.NotifyCanExecuteChanged();
				ReloadScriptCommand.NotifyCanExecuteChanged();
				StopScriptCommand.NotifyCanExecuteChanged();
				ToggleNativeDebugMenuCommand.NotifyCanExecuteChanged();
				CommandManager.InvalidateRequerySuggested();
			}

		private void NotifySummaryChanged()
		{
			OnPropertyChanged(nameof(SessionCount));
			OnPropertyChanged(nameof(InjectedSessionCount));
			OnPropertyChanged(nameof(ScriptLoadedCount));
			OnPropertyChanged(nameof(ErrorSessionCount));
			OnPropertyChanged(nameof(UnplacedSessionCount));
		}

		private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(Settings.Default.HotReloadScriptPath) or nameof(Settings.Default.MesharpIntegrationEnabled))
			{
				OnPropertyChanged(nameof(ConfiguredScriptPath));
				OnPropertyChanged(nameof(HasConfiguredScriptPath));
				RefreshCommandStates();
			}
		}

		private void OnPlacementChanged(object? sender, SessionPlacementChangedEventArgs e)
			=> OnPropertyChanged(nameof(UnplacedSessionCount));

		public void Dispose()
		{
			Settings.Default.PropertyChanged -= OnSettingsChanged;
			if (placementService != null)
			{
				placementService.PlacementChanged -= OnPlacementChanged;
				placementService.MoveCompleted -= OnPlacementChanged;
			}
			Sessions.CollectionChanged -= OnSessionsCollectionChanged;
			foreach (var session in Sessions)
			{
				session.PropertyChanged -= OnSessionPropertyChanged;
			}
		}
	}
}
