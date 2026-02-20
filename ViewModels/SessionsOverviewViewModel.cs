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
		private SessionModel? selectedSession;
		private bool _isScriptCommandInFlight;

		public SessionsOverviewViewModel(
			ObservableCollection<SessionModel> sessions,
			Action<SessionModel> activateSession,
			Action<SessionModel> focusSession,
			Action<SessionModel> closeSession)
		{
			Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
			this.activateSession = activateSession ?? throw new ArgumentNullException(nameof(activateSession));
			this.focusSession = focusSession ?? throw new ArgumentNullException(nameof(focusSession));
			this.closeSession = closeSession ?? throw new ArgumentNullException(nameof(closeSession));

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

			LoadScriptCommand = new RelayCommand<SessionModel?>(async session => await LoadScriptAsync(session), CanLoadScript);
			ReloadScriptCommand = new RelayCommand<SessionModel?>(async session => await ReloadScriptAsync(session), CanReloadScript);
			StopScriptCommand = new RelayCommand<SessionModel?>(async session => await StopScriptAsync(session), CanStopScript);

			Sessions.CollectionChanged += OnSessionsCollectionChanged;
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

		public IRelayCommand<SessionModel?> SetActiveCommand { get; }
		public IRelayCommand<SessionModel?> FocusCommand { get; }
		public IRelayCommand<SessionModel?> CloseCommand { get; }
		public IRelayCommand<SessionModel?> LoadScriptCommand { get; }
		public IRelayCommand<SessionModel?> ReloadScriptCommand { get; }
		public IRelayCommand<SessionModel?> StopScriptCommand { get; }

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

		private bool CanLoadScript(SessionModel? session)
		{
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

			if (!HasConfiguredScriptPath)
			{
				target.SetScriptRuntimeError("No script selected. Choose a script in Script Manager first.");
				ConsoleLogService.Instance.Append(
					$"[Sessions] Cannot load script for '{target.Name}' - no configured script path.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			await RunScriptCommandAsync(target, ConfiguredScriptPath, actionLabel: "Loading script");
		}

		private async Task ReloadScriptAsync(SessionModel? session)
		{
			if (!TryResolveSession(session, out var target))
				return;

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

			await RunScriptCommandAsync(target, path, actionLabel: "Reloading script");
		}

		private async Task StopScriptAsync(SessionModel? session)
		{
			if (!TryResolveSession(session, out var target))
				return;

			if (target.InjectionState != InjectionState.Injected || target.RSProcess == null)
			{
				target.SetScriptRuntimeError("Session is not injected.");
				return;
			}

			target.SetScriptRuntimePending("Stopping script");
			await SetScriptCommandInFlightAsync(async () =>
			{
				var success = await OrbitCommandClient
					.SendUnloadScriptWithRetryAsync(target.RSProcess.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);

				if (!success)
				{
					target.SetScriptRuntimeError("Failed to stop script.");
					ConsoleLogService.Instance.Append(
						$"[Sessions] Failed to stop script for '{target.Name}'.",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Warning);
					return;
				}

				target.SetScriptStopped();
				ConsoleLogService.Instance.Append(
					$"[Sessions] Stopped script for '{target.Name}'.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);
			}).ConfigureAwait(true);
		}

		private async Task RunScriptCommandAsync(SessionModel target, string scriptPath, string actionLabel)
		{
			if (target.InjectionState != InjectionState.Injected || target.RSProcess == null)
			{
				target.SetScriptRuntimeError("Session is not injected.");
				return;
			}

			target.SetScriptRuntimePending(actionLabel);

			await SetScriptCommandInFlightAsync(async () =>
			{
				var pid = target.RSProcess!.Id;
				var runtimeReady = await OrbitCommandClient
					.SendStartRuntimeWithRetryAsync(pid, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!runtimeReady)
				{
					ConsoleLogService.Instance.Append(
						$"[Sessions] Unable to start ME .NET runtime for '{target.Name}'. Script command may fail.",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Warning);
				}

				var success = await OrbitCommandClient
					.SendReloadWithRetryAsync(scriptPath, pid, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
					.ConfigureAwait(true);
				if (!success)
				{
					target.SetScriptRuntimeError($"Failed command for '{Path.GetFileNameWithoutExtension(scriptPath)}'.");
					ConsoleLogService.Instance.Append(
						$"[Sessions] Script command failed for '{target.Name}' (script '{scriptPath}').",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Warning);
					return;
				}

				target.SetScriptLoaded(scriptPath);
				ConsoleLogService.Instance.Append(
					$"[Sessions] Script command sent for '{target.Name}' (script '{scriptPath}').",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);
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
			CommandManager.InvalidateRequerySuggested();
		}

		private void NotifySummaryChanged()
		{
			OnPropertyChanged(nameof(SessionCount));
			OnPropertyChanged(nameof(InjectedSessionCount));
			OnPropertyChanged(nameof(ScriptLoadedCount));
			OnPropertyChanged(nameof(ErrorSessionCount));
		}

		public void Dispose()
		{
			Sessions.CollectionChanged -= OnSessionsCollectionChanged;
			foreach (var session in Sessions)
			{
				session.PropertyChanged -= OnSessionPropertyChanged;
			}
		}
	}
}
