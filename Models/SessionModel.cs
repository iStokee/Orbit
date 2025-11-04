using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Orbit.Views;

namespace Orbit.Models
{
	public class SessionModel : INotifyPropertyChanged
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
		private bool _gallerySizeOverrideEnabled;
		private double _galleryCustomThumbnailSize = GallerySettingsDefaults.DefaultThumbnailSize;
		private bool _isCloseConfirmationVisible;
		private TaskCompletionSource<bool>? closeConfirmationTcs;

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

		public bool IsInjectable => InjectionState == InjectionState.Ready || InjectionState == InjectionState.Failed;
		public bool IsHealthy =>
			State != SessionState.Failed &&
			State != SessionState.ShuttingDown &&
			State != SessionState.Closed &&
			InjectionState != InjectionState.Failed;

		public event PropertyChangedEventHandler PropertyChanged;

		public void UpdateState(SessionState state, bool clearError = true)
		{
			if (clearError)
			{
				LastError = null;
			}
			State = state;
		}

		public void UpdateInjectionState(InjectionState state)
			=> InjectionState = state;

		public void Fail(Exception exception)
		{
			LastError = exception?.Message ?? "Unknown error";
			State = SessionState.Failed;
		}

		public void RecordInjectionFailure(Exception exception)
		{
			LastError = exception?.Message ?? "Unknown error";
			InjectionState = InjectionState.Failed;
			UpdateState(SessionState.ClientReady, clearError: false);
		}

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

		public bool IsCloseConfirmationVisible
		{
			get => _isCloseConfirmationVisible;
			private set
			{
				if (_isCloseConfirmationVisible == value)
					return;
				_isCloseConfirmationVisible = value;
				OnPropertyChanged();
			}
		}

		public void ShowCloseConfirmation(TaskCompletionSource<bool> completionSource)
		{
			if (completionSource == null)
			{
				throw new ArgumentNullException(nameof(completionSource));
			}

			if (IsCloseConfirmationVisible)
			{
				ResolveCloseConfirmation(false);
			}

			closeConfirmationTcs = completionSource;
			IsCloseConfirmationVisible = true;
		}

		public void ResolveCloseConfirmation(bool confirmed)
		{
			IsCloseConfirmationVisible = false;
			closeConfirmationTcs?.TrySetResult(confirmed);
			closeConfirmationTcs = null;
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

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
