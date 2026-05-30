using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Orbit.Views;

namespace Orbit.Models
{
	public class SessionModel : ObservableObject
	{
		private SessionState _state;
		private InjectionState _injectionState;
		private string _lastError;
		private Process _rsProcess;
		private nint _externalHandle;
		private int? _parentProcessId;
		private SessionType _sessionType;
		private bool _isRenaming;
		private string _editableName = string.Empty;
		private BitmapSource _thumbnail;
		private DateTime _lastThumbnailUpdate = DateTime.MinValue;
		private bool _galleryOverrideEnabled;
		private bool _galleryAutoRefreshEnabled = true;
		private double _galleryRefreshIntervalSeconds = 5;
		private nint _renderSurfaceHandle;
			private string? _activeScriptPath;
			private string? _activeScriptId;
			private string _scriptRuntimeStatus = "No script loaded";
		private DateTime? _scriptLastChangedAt;
		private bool _nativeDebugMenuVisible;
		private DateTime? _lastLifecycleChangedAt;
		private DateTime? _lastInjectionChangedAt;

		public SessionModel()
		{
			State = SessionState.Initializing;
			InjectionState = InjectionState.NotReady;
			SessionType = SessionType.RuneScape; // Default to RS3
			_editableName = Name ?? string.Empty;
		}

		public Guid Id { get; init; }

		/// <summary>
		/// Gets or sets the type of session (RuneScape client or external script)
		/// </summary>
		public SessionType SessionType
		{
			get => _sessionType;
			set
			{
				if (_sessionType == value)
					return;
				_sessionType = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsExternalScript));
				OnPropertyChanged(nameof(IsRuneScapeClient));
			}
		}

		/// <summary>
		/// Gets whether this session is an external script window
		/// </summary>
		public bool IsExternalScript => SessionType == SessionType.ExternalScript;

		/// <summary>
		/// Gets whether this session is a RuneScape client
		/// </summary>
		public bool IsRuneScapeClient => SessionType == SessionType.RuneScape;

		private string name;
		public string Name
		{
			get => name;
			set
			{
				if (name == value)
					return;
				name = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(DisplayName));

				if (_editableName != value)
				{
					_editableName = value;
					OnPropertyChanged(nameof(EditableName));
				}
			}
		}

		public string EditableName
		{
			get => _editableName;
			set
			{
				if (_editableName == value)
					return;
				_editableName = value;
				OnPropertyChanged();
			}
		}

		public bool IsRenaming
		{
			get => _isRenaming;
			set
			{
				if (_isRenaming == value)
					return;
				_isRenaming = value;
				OnPropertyChanged();
			}
		}

		public DateTime CreatedAt { get; init; }
		public ChildClientView HostControl { get; init; }
		public RSForm RSForm { get; set; }

		/// <summary>
		/// When true, postpone docking until MESharp injection finishes (used for auto-inject flows).
		/// Manual workflows set this to false so the client docks immediately.
		/// </summary>
		public bool RequireInjectionBeforeDock { get; set; } = true;

		public SessionState State
		{
			get => _state;
			private set
			{
				if (_state == value)
					return;

				_state = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(StatusSummary));
				OnPropertyChanged(nameof(IsInjectable));
				OnPropertyChanged(nameof(IsHealthy));
			}
		}

		public InjectionState InjectionState
		{
			get => _injectionState;
			private set
			{
				if (_injectionState == value)
					return;

				_injectionState = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(StatusSummary));
				OnPropertyChanged(nameof(IsInjectable));
				OnPropertyChanged(nameof(IsHealthy));
			}
		}

		public string LastError
		{
			get => _lastError;
			private set
			{
				if (_lastError == value)
					return;

				_lastError = value;
				OnPropertyChanged();
			}
		}

		public Process RSProcess
		{
			get => _rsProcess;
			set
			{
				if (_rsProcess == value)
					return;

				_rsProcess = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(DisplayName));
			}
		}

		/// <summary>
		/// Default user-facing session label. Includes PID when available.
		/// This keeps tabs distinguishable when multiple clients are running.
		/// </summary>
		public string DisplayName
		{
			get
			{
				var baseName = Name ?? string.Empty;
				if (string.IsNullOrWhiteSpace(baseName))
				{
					baseName = IsExternalScript ? "Script" : "Session";
				}

				var pid = RSProcess?.Id;
				if (!pid.HasValue)
				{
					return baseName;
				}

				// Avoid duplicating PID if the user already baked it into the name.
				var pidToken = pid.Value.ToString();
				if (baseName.Contains(pidToken, StringComparison.Ordinal))
				{
					return baseName;
				}

				return $"{baseName} (PID {pid.Value})";
			}
		}

		public int? ParentProcessId
		{
			get => _parentProcessId;
			set
			{
				if (_parentProcessId == value)
					return;

				_parentProcessId = value;
				OnPropertyChanged();
			}
		}

		public nint ExternalHandle
		{
			get => _externalHandle;
			set
			{
				if (_externalHandle == value)
					return;

				_externalHandle = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets the direct render surface handle (e.g. JagRenderView child window).
		/// </summary>
		public nint RenderSurfaceHandle
		{
			get => _renderSurfaceHandle;
			set
			{
				if (_renderSurfaceHandle == value)
					return;
				_renderSurfaceHandle = value;
				OnPropertyChanged();
			}
		}

		public string StatusSummary => $"{State} / {InjectionState}";

		public DateTime? LastLifecycleChangedAt
		{
			get => _lastLifecycleChangedAt;
			private set
			{
				if (_lastLifecycleChangedAt == value)
					return;
				_lastLifecycleChangedAt = value;
				OnPropertyChanged();
			}
		}

		public DateTime? LastInjectionChangedAt
		{
			get => _lastInjectionChangedAt;
			private set
			{
				if (_lastInjectionChangedAt == value)
					return;
				_lastInjectionChangedAt = value;
				OnPropertyChanged();
			}
		}

		public bool IsInjectable => InjectionState == InjectionState.Ready || InjectionState == InjectionState.Failed;
		public bool IsHealthy =>
			State != SessionState.Failed &&
			State != SessionState.ShuttingDown &&
			State != SessionState.Closed &&
			InjectionState != InjectionState.Failed;

		public string? ActiveScriptPath
		{
			get => _activeScriptPath;
			private set
			{
				if (string.Equals(_activeScriptPath, value, StringComparison.OrdinalIgnoreCase))
					return;

				_activeScriptPath = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(ActiveScriptName));
				OnPropertyChanged(nameof(HasActiveScript));
			}
		}

		public string ActiveScriptName => string.IsNullOrWhiteSpace(_activeScriptPath)
			? "None"
			: Path.GetFileNameWithoutExtension(_activeScriptPath);

		public bool HasActiveScript => !string.IsNullOrWhiteSpace(_activeScriptPath);

		public string? ActiveScriptId
		{
			get => _activeScriptId;
			private set
			{
				if (string.Equals(_activeScriptId, value, StringComparison.OrdinalIgnoreCase))
					return;

				_activeScriptId = value;
				OnPropertyChanged();
			}
		}

		public string ScriptRuntimeStatus
		{
			get => _scriptRuntimeStatus;
			private set
			{
				if (string.Equals(_scriptRuntimeStatus, value, StringComparison.Ordinal))
					return;
				_scriptRuntimeStatus = value;
				OnPropertyChanged();
			}
		}

		public DateTime? ScriptLastChangedAt
		{
			get => _scriptLastChangedAt;
			private set
			{
				if (_scriptLastChangedAt == value)
					return;
				_scriptLastChangedAt = value;
				OnPropertyChanged();
			}
		}

		public bool NativeDebugMenuVisible
		{
			get => _nativeDebugMenuVisible;
			private set
			{
				if (_nativeDebugMenuVisible == value)
					return;

				_nativeDebugMenuVisible = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(NativeDebugMenuStatus));
				OnPropertyChanged(nameof(NativeDebugMenuButtonText));
			}
		}

		public string NativeDebugMenuStatus => NativeDebugMenuVisible ? "Visible" : "Hidden";

		public string NativeDebugMenuButtonText => NativeDebugMenuVisible ? "Hide Menu" : "Show Menu";


		public void UpdateState(SessionState state, bool clearError = true, string? reason = null)
		{
			var previous = State;
			if (previous == state)
			{
				return;
			}

			if (!IsExpectedStateTransition(previous, state))
			{
				LogSessionTransitionWarning("lifecycle", previous, state, reason);
			}

			if (clearError)
			{
				LastError = null;
			}

			State = state;
			LastLifecycleChangedAt = DateTime.Now;
			LogSessionTransition("lifecycle", previous, state, reason);
		}

		public void UpdateInjectionState(InjectionState state, string? reason = null)
		{
			var previous = InjectionState;
			if (previous == state)
			{
				return;
			}

			if (!IsExpectedInjectionTransition(previous, state))
			{
				LogSessionTransitionWarning("injection", previous, state, reason);
			}

			InjectionState = state;
			LastInjectionChangedAt = DateTime.Now;
			LogSessionTransition("injection", previous, state, reason);
			if (state != InjectionState.Injected)
			{
				SetNativeDebugMenuVisible(false, $"injection state changed to {state}");
			}
		}

		public void SetNativeDebugMenuVisible(bool visible, string? reason = null)
		{
			var previous = NativeDebugMenuVisible;
			if (previous == visible)
			{
				return;
			}

			NativeDebugMenuVisible = visible;
			LogSessionTransition("native-menu", previous ? "Visible" : "Hidden", visible ? "Visible" : "Hidden", reason);
		}

		public void Fail(Exception exception)
		{
			LastError = exception?.Message ?? "Unknown error";
			UpdateState(SessionState.Failed, clearError: false, reason: LastError);
		}

		public void RecordInjectionFailure(Exception exception)
		{
			LastError = exception?.Message ?? "Unknown error";
			UpdateInjectionState(InjectionState.Failed, LastError);
			UpdateState(SessionState.ClientReady, clearError: false, reason: "recovering from injection failure");
		}

		public void SetScriptRuntimePending(string action)
		{
			ScriptRuntimeStatus = string.IsNullOrWhiteSpace(action) ? "Working..." : $"{action}...";
			ScriptLastChangedAt = DateTime.Now;
			LogSessionTransition("script", "Idle", ScriptRuntimeStatus, action);
		}

		public void SetScriptLoaded(string scriptPath, string? scriptId = null)
		{
			ActiveScriptPath = scriptPath;
			ActiveScriptId = string.IsNullOrWhiteSpace(scriptId) ? null : scriptId.Trim();
			ScriptRuntimeStatus = string.IsNullOrWhiteSpace(ActiveScriptId)
				? $"Loaded: {ActiveScriptName}"
				: $"Loaded [{ActiveScriptId}]: {ActiveScriptName}";
			ScriptLastChangedAt = DateTime.Now;
			LogSessionTransition("script", "Pending", ScriptRuntimeStatus, scriptPath);
		}

		public void SetScriptStopped()
		{
			ActiveScriptPath = null;
			ActiveScriptId = null;
			ScriptRuntimeStatus = "No script loaded";
			ScriptLastChangedAt = DateTime.Now;
			LogSessionTransition("script", "Loaded", ScriptRuntimeStatus, null);
		}

		public void SetScriptRuntimeError(string message)
		{
			ScriptRuntimeStatus = string.IsNullOrWhiteSpace(message) ? "Script command failed" : $"Error: {message}";
			ScriptLastChangedAt = DateTime.Now;
			LogSessionTransition("script", "Pending", ScriptRuntimeStatus, message);
		}

		private static bool IsExpectedStateTransition(SessionState from, SessionState to)
		{
			if (from == to)
			{
				return true;
			}

			return from switch
			{
				SessionState.Initializing => to is SessionState.ClientReady or SessionState.Failed or SessionState.ShuttingDown or SessionState.Closed,
				SessionState.ClientReady => to is SessionState.Injecting or SessionState.Failed or SessionState.ShuttingDown or SessionState.Closed,
				SessionState.Injecting => to is SessionState.Injected or SessionState.ClientReady or SessionState.Failed or SessionState.ShuttingDown or SessionState.Closed,
				SessionState.Injected => to is SessionState.ClientReady or SessionState.Failed or SessionState.ShuttingDown or SessionState.Closed,
				SessionState.Failed => to is SessionState.ClientReady or SessionState.Injecting or SessionState.ShuttingDown or SessionState.Closed,
				SessionState.ShuttingDown => to is SessionState.ClientReady or SessionState.Injected or SessionState.Failed or SessionState.Closed,
				SessionState.Closed => false,
				_ => false
			};
		}

		private static bool IsExpectedInjectionTransition(InjectionState from, InjectionState to)
		{
			if (from == to)
			{
				return true;
			}

			return from switch
			{
				InjectionState.NotReady => to is InjectionState.Ready or InjectionState.Failed,
				InjectionState.Ready => to is InjectionState.Injecting or InjectionState.NotReady or InjectionState.Failed,
				InjectionState.Injecting => to is InjectionState.Injected or InjectionState.Ready or InjectionState.NotReady or InjectionState.Failed,
				InjectionState.Injected => to is InjectionState.NotReady or InjectionState.Failed,
				InjectionState.Failed => to is InjectionState.Ready or InjectionState.Injecting or InjectionState.NotReady,
				_ => false
			};
		}

		private void LogSessionTransitionWarning<TState>(string lane, TState from, TState to, string? reason)
		{
			Console.WriteLine($"[Orbit][Session:{GetLogIdentity()}][Warning] Unexpected {lane} transition {from} -> {to}{FormatReason(reason)}");
		}

		private void LogSessionTransition<TState>(string lane, TState from, TState to, string? reason)
		{
			Console.WriteLine($"[Orbit][Session:{GetLogIdentity()}] {lane} {from} -> {to}{FormatReason(reason)}");
		}

		private string GetLogIdentity()
		{
			var pid = RSProcess?.Id.ToString() ?? "n/a";
			var handle = ExternalHandle == nint.Zero ? "n/a" : $"0x{ExternalHandle:X}";
			return $"{Name ?? Id.ToString()} id={Id:N} pid={pid} hwnd={handle}";
		}

		private static string FormatReason(string? reason)
			=> string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason.Trim()})";

		public void KillProcess()
		{
			try
			{
				if (RSProcess != null && !RSProcess.HasExited)
				{
					UpdateState(SessionState.ShuttingDown, clearError: false);
					UpdateInjectionState(InjectionState.NotReady);
					RSProcess.Kill();
					RSProcess.Dispose();
				}

				if (ParentProcessId is int parentPid)
				{
					try
					{
						var parentProcess = Process.GetProcessById(parentPid);
						if (!parentProcess.HasExited)
						{
							parentProcess.Kill();
							parentProcess.WaitForExit();
						}
						parentProcess.Dispose();
					}
					catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
					{
						// Parent may have already exited or be invalid; ignore.
					}
				}
			}
			catch
			{
				// Best-effort shutdown; swallow exceptions for now.
			}
			finally
			{
				RSProcess = null;
				ParentProcessId = null;
				ExternalHandle = nint.Zero;
				RenderSurfaceHandle = nint.Zero;
				UpdateState(SessionState.Closed, clearError: false);
			}
		}

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(nint hWnd);

		public void SetFocus()
		{
			if (ExternalHandle != nint.Zero)
			{
				SetForegroundWindow(ExternalHandle);
			}
		}


		/// <summary>
		/// Gets or sets the thumbnail preview image for this session
		/// </summary>
		public BitmapSource Thumbnail
		{
			get => _thumbnail;
			set
			{
				if (_thumbnail == value)
					return;
				_thumbnail = value;
				_lastThumbnailUpdate = DateTime.UtcNow;
				OnPropertyChanged();
				OnPropertyChanged(nameof(HasThumbnail));
			}
		}

		/// <summary>
		/// Gets whether this session has a thumbnail available
		/// </summary>
		public bool HasThumbnail => _thumbnail != null;

		/// <summary>
		/// Gets the age of the current thumbnail in seconds
		/// </summary>
		public double ThumbnailAge => _lastThumbnailUpdate == DateTime.MinValue
			? double.MaxValue
			: (DateTime.UtcNow - _lastThumbnailUpdate).TotalSeconds;

		/// <summary>
		/// Gets or sets whether this session overrides the global gallery refresh settings.
		/// </summary>
		public bool GalleryOverrideEnabled
		{
			get => _galleryOverrideEnabled;
			set
			{
				if (_galleryOverrideEnabled == value)
					return;
				_galleryOverrideEnabled = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(UsesGlobalGallerySettings));
				OnPropertyChanged(nameof(GalleryIntervalIsEnabled));
			}
		}

		/// <summary>
		/// Gets whether this session follows the global gallery refresh settings.
		/// </summary>
		public bool UsesGlobalGallerySettings => !_galleryOverrideEnabled;

		/// <summary>
		/// Gets or sets whether this session auto-refreshes thumbnails when overrides are enabled.
		/// </summary>
		public bool GalleryAutoRefreshEnabled
		{
			get => _galleryAutoRefreshEnabled;
			set
			{
				if (_galleryAutoRefreshEnabled == value)
					return;
				_galleryAutoRefreshEnabled = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(GalleryIntervalIsEnabled));
			}
		}

		/// <summary>
		/// Gets or sets the custom refresh interval (in seconds) when overrides are enabled.
		/// </summary>
		public double GalleryRefreshIntervalSeconds
		{
			get => _galleryRefreshIntervalSeconds;
			set
			{
				var clamped = Math.Clamp(value, 1, 120);
				if (Math.Abs(_galleryRefreshIntervalSeconds - clamped) < 0.01)
					return;
				_galleryRefreshIntervalSeconds = clamped;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets whether the interval slider should be enabled for this session.
		/// </summary>
		public bool GalleryIntervalIsEnabled => _galleryOverrideEnabled && _galleryAutoRefreshEnabled;

		public override string ToString()
		{
			return string.IsNullOrWhiteSpace(Name)
				? base.ToString()
				: Name;
		}
	}
}
