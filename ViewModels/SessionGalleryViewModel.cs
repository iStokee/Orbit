using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Orbit.Models;
using Orbit.Services;
using Orbit.Utilities;
using Application = System.Windows.Application;

namespace Orbit.ViewModels
{
	/// <summary>
	/// ViewModel for the Session Gallery View - displays thumbnails of all sessions in a grid
	/// </summary>
	public class SessionGalleryViewModel : INotifyPropertyChanged, IDisposable
	{
		private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(1);
		private const double RefreshIntervalToleranceSeconds = 0.2;

		private readonly SessionCollectionService _sessionCollectionService;
		private readonly DispatcherTimer _refreshTimer;
		private readonly DispatcherTimer _refreshCheckTimer;
		private readonly SemaphoreSlim _refreshSignal = new(0, int.MaxValue);
		private readonly object _refreshQueueSync = new();
		private readonly HashSet<SessionModel> _queuedSessions = new();
		private readonly CancellationTokenSource _refreshLoopCts = new();
		private CancellationTokenSource? _activeRefreshCts;
		private bool _refreshAllRequested;
		private Task? _refreshLoopTask;

		private double _thumbnailSize = 300;
		private bool _autoRefreshEnabled = true;
		private double _globalRefreshIntervalSeconds = 5;
		private bool _allowSessionOverrides = true;
		private bool _disposed;
		private bool _useCustomThumbnailSize = false;
		private double _customThumbnailSize = 300;
		private const double ThumbnailCardPadding = 8;

		public SessionGalleryViewModel(SessionCollectionService sessionCollectionService)
		{
			_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));

			// Commands
			ActivateSessionCommand = new RelayCommand<SessionModel?>(session =>
			{
				if (session != null)
				{
					ActivateSession(session);
				}
			});
			RefreshThumbnailsCommand = new RelayCommand(async () => await RefreshAllThumbnailsAsync());

			// Auto-refresh timer (tick every second and check per-session intervals)
			_refreshTimer = new DispatcherTimer { Interval = TimerInterval };
			_refreshTimer.Tick += OnRefreshTimerTick;
			_refreshCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
			_refreshCheckTimer.Tick += OnRefreshCheckTimerTick;
			_refreshLoopTask = Task.Run(RefreshLoopAsync);

			Sessions.CollectionChanged += OnSessionsCollectionChanged;
			foreach (var session in Sessions.ToList())
			{
				AttachSession(session);
			}

			UpdateTimerState();

			// Initial thumbnail capture
			_ = RefreshAllThumbnailsAsync();
		}

		/// <summary>
		/// Gets the shared session collection
		/// </summary>
		public ObservableCollection<SessionModel> Sessions => _sessionCollectionService.Sessions;

		/// <summary>
		/// Gets or sets the thumbnail size (width in pixels)
		/// </summary>
		public double ThumbnailSize
		{
			get => _thumbnailSize;
			set
			{
				var clamped = Math.Clamp(value, 200, 800);
				if (Math.Abs(_thumbnailSize - clamped) < 0.01)
					return;
				_thumbnailSize = clamped;
				if (!UseCustomThumbnailSize)
				{
					CustomThumbnailSize = _thumbnailSize;
				}
				OnPropertyChanged();
				OnPropertyChanged(nameof(ThumbnailSizeWithAspect));
				OnPropertyChanged(nameof(EffectiveThumbnailSize));
				OnPropertyChanged(nameof(ThumbnailImageWidth));
				OnPropertyChanged(nameof(ThumbnailImageHeight));
				OnPropertyChanged(nameof(ThumbnailCardWidth));
				OnPropertyChanged(nameof(ThumbnailCardHeight));
			}
		}

		/// <summary>
		/// Whether to use a custom thumbnail size instead of the slider
		/// </summary>
		public bool UseCustomThumbnailSize
		{
			get => _useCustomThumbnailSize;
			set
			{
				if (_useCustomThumbnailSize == value)
					return;
				_useCustomThumbnailSize = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(EffectiveThumbnailSize));
				OnPropertyChanged(nameof(ThumbnailSizeWithAspect));
				OnPropertyChanged(nameof(ThumbnailImageWidth));
				OnPropertyChanged(nameof(ThumbnailImageHeight));
				OnPropertyChanged(nameof(ThumbnailCardWidth));
				OnPropertyChanged(nameof(ThumbnailCardHeight));
			}
		}

		/// <summary>
		/// Custom thumbnail size when override is enabled
		/// </summary>
		public double CustomThumbnailSize
		{
			get => _customThumbnailSize;
			set
			{
				var clamped = Math.Clamp(value, 200, 800);
				if (Math.Abs(_customThumbnailSize - clamped) < 0.01)
					return;
				_customThumbnailSize = clamped;
				OnPropertyChanged();
				if (UseCustomThumbnailSize)
				{
					OnPropertyChanged(nameof(EffectiveThumbnailSize));
					OnPropertyChanged(nameof(ThumbnailSizeWithAspect));
					OnPropertyChanged(nameof(ThumbnailImageWidth));
					OnPropertyChanged(nameof(ThumbnailImageHeight));
					OnPropertyChanged(nameof(ThumbnailCardWidth));
					OnPropertyChanged(nameof(ThumbnailCardHeight));
				}
			}
		}

		/// <summary>
		/// Gets the effective thumbnail size (either slider or custom)
		/// </summary>
		public double EffectiveThumbnailSize => UseCustomThumbnailSize ? _customThumbnailSize : _thumbnailSize;

		/// <summary>
		/// Gets the thumbnail height based on 4:3 aspect ratio
		/// </summary>
		public double ThumbnailSizeWithAspect => EffectiveThumbnailSize * 0.75;

		/// <summary>
		/// Width of the thumbnail image frame.
		/// </summary>
		public double ThumbnailImageWidth => EffectiveThumbnailSize;

		/// <summary>
		/// Height of the thumbnail image frame.
		/// </summary>
		public double ThumbnailImageHeight => ThumbnailSizeWithAspect;

		/// <summary>
		/// Width of the thumbnail card including padding so content does not get clipped.
		/// </summary>
		public double ThumbnailCardWidth => ThumbnailImageWidth + (ThumbnailCardPadding * 2);

		/// <summary>
		/// Height of the thumbnail card including padding so content does not get clipped.
		/// </summary>
		public double ThumbnailCardHeight => ThumbnailImageHeight + (ThumbnailCardPadding * 2);

		/// <summary>
		/// Minimum refresh interval (seconds) exposed to the UI sliders.
		/// </summary>
		public double MinimumRefreshInterval => 1;

		/// <summary>
		/// Maximum refresh interval (seconds) exposed to the UI sliders.
		/// </summary>
		public double MaximumRefreshInterval => 60;

		/// <summary>
		/// Gets or sets the global refresh interval in seconds.
		/// </summary>
		public double GlobalRefreshIntervalSeconds
		{
			get => _globalRefreshIntervalSeconds;
			set
			{
				var clamped = Math.Clamp(value, MinimumRefreshInterval, MaximumRefreshInterval);
				if (Math.Abs(_globalRefreshIntervalSeconds - clamped) < 0.01)
					return;
				_globalRefreshIntervalSeconds = clamped;
				OnPropertyChanged();
				RequestRefreshCheck();
			}
		}

		/// <summary>
		/// Gets or sets whether the global auto-refresh is enabled.
		/// </summary>
		public bool AutoRefreshEnabled
		{
			get => _autoRefreshEnabled;
			set
			{
				if (_autoRefreshEnabled == value)
					return;
				_autoRefreshEnabled = value;
				OnPropertyChanged();
				UpdateTimerState();

				if (_autoRefreshEnabled)
				{
					_ = RefreshAllThumbnailsAsync();
				}
			}
		}

		/// <summary>
		/// Gets or sets whether per-session overrides are respected.
		/// </summary>
		public bool AllowSessionOverrides
		{
			get => _allowSessionOverrides;
			set
			{
				if (_allowSessionOverrides == value)
					return;
				_allowSessionOverrides = value;
				OnPropertyChanged();
				UpdateTimerState();
				RequestRefreshCheck();
			}
		}

		/// <summary>
		/// Command to activate (switch to) a session when clicked
		/// </summary>
		public IRelayCommand<SessionModel?> ActivateSessionCommand { get; }

		/// <summary>
		/// Command to manually refresh all thumbnails
		/// </summary>
		public IRelayCommand RefreshThumbnailsCommand { get; }

		/// <summary>
		/// Activates the selected session (switches to its tab)
		/// </summary>
		private void ActivateSession(SessionModel session)
		{
			if (session == null)
				return;

			_sessionCollectionService.GlobalSelectedSession = session;
			session.HostControl?.FocusEmbeddedClient();
		}

		/// <summary>
		/// Refreshes thumbnails for all sessions
		/// </summary>
		private Task RefreshAllThumbnailsAsync()
		{
			QueueFullRefresh(cancelInFlight: true);
			return Task.CompletedTask;
		}

		/// <summary>
		/// Refreshes thumbnails that are due based on the global/per-session intervals.
		/// </summary>
		private Task RefreshDueThumbnailsAsync()
		{
			if (_disposed)
				return Task.CompletedTask;

			var snapshot = Sessions.ToList();
			var dueSessions = snapshot
				.Where(ShouldRefreshSession)
				.ToList();

			if (dueSessions.Count == 0)
				return Task.CompletedTask;

			QueueSessionRefresh(dueSessions);
			return Task.CompletedTask;
		}

		private async void OnRefreshTimerTick(object? sender, EventArgs e)
		{
			await RefreshDueThumbnailsAsync();
		}

		private async void OnRefreshCheckTimerTick(object? sender, EventArgs e)
		{
			if (_disposed)
			{
				return;
			}

			_refreshCheckTimer.Stop();
			await RefreshDueThumbnailsAsync();
		}

		private bool ShouldRefreshSession(SessionModel session)
		{
			if (session == null)
				return false;

			var canUseOverride = AllowSessionOverrides && session.GalleryOverrideEnabled;
			var autoEnabled = canUseOverride ? session.GalleryAutoRefreshEnabled : AutoRefreshEnabled;

			if (!autoEnabled)
				return false;

			var interval = canUseOverride
				? session.GalleryRefreshIntervalSeconds
				: GlobalRefreshIntervalSeconds;

			if (!session.HasThumbnail)
				return true;

			var targetInterval = Math.Max(interval, MinimumRefreshInterval);
			var threshold = Math.Max(0, targetInterval - RefreshIntervalToleranceSeconds);
			return session.ThumbnailAge >= threshold;
		}

		private async Task RefreshLoopAsync()
		{
			try
			{
				while (!_refreshLoopCts.IsCancellationRequested)
				{
					await _refreshSignal.WaitAsync(_refreshLoopCts.Token).ConfigureAwait(false);

					while (TryDequeueRefreshBatch(out var refreshAll, out var batch))
					{
						var token = PrepareBatchTokenSource();
						try
						{
							var sessionsToRefresh = refreshAll ? GetSessionSnapshot() : batch;
							foreach (var session in sessionsToRefresh)
							{
								if (_disposed || token.IsCancellationRequested || _refreshLoopCts.IsCancellationRequested)
								{
									break;
								}

								CaptureThumbnailForSession(session);
							}
						}
						catch (OperationCanceledException)
						{
							// Expected when a full refresh preempts current batch.
						}
						finally
						{
							ClearBatchTokenSource();
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
				// expected during disposal
			}
		}

		private CancellationToken PrepareBatchTokenSource()
		{
			lock (_refreshQueueSync)
			{
				_activeRefreshCts?.Dispose();
				_activeRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_refreshLoopCts.Token);
				return _activeRefreshCts.Token;
			}
		}

		private void ClearBatchTokenSource()
		{
			lock (_refreshQueueSync)
			{
				_activeRefreshCts?.Dispose();
				_activeRefreshCts = null;
			}
		}

		private bool TryDequeueRefreshBatch(out bool refreshAll, out List<SessionModel> sessions)
		{
			lock (_refreshQueueSync)
			{
				if (_refreshAllRequested)
				{
					_refreshAllRequested = false;
					_queuedSessions.Clear();
					refreshAll = true;
					sessions = new List<SessionModel>();
					return true;
				}

				if (_queuedSessions.Count == 0)
				{
					refreshAll = false;
					sessions = new List<SessionModel>();
					return false;
				}

				sessions = _queuedSessions.ToList();
				_queuedSessions.Clear();
				refreshAll = false;
				return true;
			}
		}

		private List<SessionModel> GetSessionSnapshot()
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher == null || dispatcher.CheckAccess())
			{
				return Sessions.ToList();
			}

			return dispatcher.Invoke(() => Sessions.ToList());
		}

		private void QueueFullRefresh(bool cancelInFlight)
		{
			if (_disposed)
			{
				return;
			}

			lock (_refreshQueueSync)
			{
				_refreshAllRequested = true;
				_queuedSessions.Clear();
				if (cancelInFlight)
				{
					try
					{
						_activeRefreshCts?.Cancel();
					}
					catch
					{
						// best effort cancellation
					}
				}
			}

			try
			{
				_refreshSignal.Release();
			}
			catch (SemaphoreFullException)
			{
				// best effort; queued work is already marked
			}
		}

		private void QueueSessionRefresh(IEnumerable<SessionModel> sessions)
		{
			if (_disposed || sessions == null)
			{
				return;
			}

			var hasQueuedWork = false;
			lock (_refreshQueueSync)
			{
				if (_refreshAllRequested)
				{
					return;
				}

				foreach (var session in sessions)
				{
					if (session != null)
					{
						hasQueuedWork |= _queuedSessions.Add(session);
					}
				}
			}

			if (!hasQueuedWork)
			{
				return;
			}

			try
			{
				_refreshSignal.Release();
			}
			catch (SemaphoreFullException)
			{
				// best effort; queued work is already marked
			}
		}

		/// <summary>
		/// Captures a thumbnail for the specified session
		/// </summary>
		private void CaptureThumbnailForSession(SessionModel session)
		{
			try
			{
				if (session == null)
					return;

				var targetHandle = ResolveCaptureHandle(session);
				if (targetHandle == IntPtr.Zero)
					return;

				// Capture the window thumbnail
				var thumbnail = WindowThumbnailCapture.CaptureWindow(
					targetHandle,
					(int)EffectiveThumbnailSize,
					(int)ThumbnailSizeWithAspect);

				if (thumbnail == null)
					return;

				// Update thumbnail on UI thread
				var dispatcher = session.HostControl?.Dispatcher ?? Application.Current?.Dispatcher;
				if (dispatcher == null || dispatcher.CheckAccess())
				{
					session.Thumbnail = thumbnail;
				}
				else
				{
					dispatcher.BeginInvoke(new Action(() =>
					{
						if (!_disposed)
						{
							session.Thumbnail = thumbnail;
						}
					}), DispatcherPriority.Background);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to capture thumbnail for session {session?.Name}: {ex.Message}");
			}
		}

		private IntPtr ResolveCaptureHandle(SessionModel session)
		{
			if (session == null)
				return IntPtr.Zero;

			// PERFORMANCE: Don't use Dispatcher.Invoke - it blocks the background thread
			// Instead, access handles directly since they're thread-safe IntPtrs
			return ResolveHandleDirectly(session);
		}

		private IntPtr ResolveHandleDirectly(SessionModel session)
		{
			if (session == null)
				return IntPtr.Zero;

			// Try cached handles first (thread-safe IntPtr reads)
			if (HandleMatchesSession(session.RenderSurfaceHandle, session))
				return session.RenderSurfaceHandle;

			if (HandleMatchesSession(session.ExternalHandle, session))
				return session.ExternalHandle;

			// Try RSForm handles (may require UI thread access for GetRenderSurfaceHandle)
			if (session.RSForm != null)
			{
				try
				{
					var renderSurface = session.RSForm.GetRenderSurfaceHandle();
					if (HandleMatchesSession(renderSurface, session))
						return renderSurface;

					var dockedHandle = session.RSForm.DockedClientHandle;
					if (HandleMatchesSession(dockedHandle, session))
						return dockedHandle;
				}
				catch
				{
					// RSForm may be disposed
				}
			}

			// Fallback to process main window
			if (session.RSProcess is Process process && !process.HasExited)
			{
				var mainWindow = process.MainWindowHandle;
				if (HandleMatchesSession(mainWindow, session))
					return mainWindow;
			}

			return IntPtr.Zero;
		}

		private IntPtr ResolveHandleOnUIThread(SessionModel session)
		{
			if (session == null)
				return IntPtr.Zero;

			static bool TryCandidate(SessionModel session, IntPtr candidate, out IntPtr handle)
			{
				if (HandleMatchesSession(candidate, session))
				{
					handle = candidate;
					return true;
				}

				handle = IntPtr.Zero;
				return false;
			}

			if (session.HostControl is Views.ChildClientView clientView)
			{
				if (TryCandidate(session, clientView.GetCaptureHandle(), out var handle))
					return handle;
			}

			if (TryCandidate(session, session.RenderSurfaceHandle, out var fromCache))
				return fromCache;

			if (TryCandidate(session, session.ExternalHandle, out var external))
				return external;

			if (session.RSForm != null)
			{
				if (TryCandidate(session, session.RSForm.GetRenderSurfaceHandle(), out var render))
					return render;

				if (TryCandidate(session, session.RSForm.DockedClientHandle, out var docked))
					return docked;
			}

			if (session.RSProcess is Process process && !process.HasExited)
			{
				if (TryCandidate(session, process.MainWindowHandle, out var main))
					return main;
			}

			return IntPtr.Zero;
		}

		private static bool HandleMatchesSession(IntPtr handle, SessionModel session)
		{
			if (handle == IntPtr.Zero)
				return false;

			if (!IsWindow(handle))
				return false;

			if (session?.RSProcess is not Process process || process.HasExited)
			{
				// Without a live process, fall back to matching stored handles
				return handle == session?.ExternalHandle || handle == session?.RenderSurfaceHandle;
			}

			GetWindowThreadProcessId(handle, out uint pid);
			return pid == (uint)process.Id;
		}

		private void UpdateTimerState()
		{
			var hasOverrideAuto = AllowSessionOverrides &&
				Sessions.Any(s => s.GalleryOverrideEnabled && s.GalleryAutoRefreshEnabled);

			var shouldRun = AutoRefreshEnabled || hasOverrideAuto;

			if (shouldRun && !_refreshTimer.IsEnabled)
			{
				_refreshTimer.Start();
			}
			else if (!shouldRun && _refreshTimer.IsEnabled)
			{
				_refreshTimer.Stop();
			}
		}

		private void OnSessionsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.NewItems != null)
			{
				foreach (var item in e.NewItems)
				{
					if (item is SessionModel session)
					{
						AttachSession(session);
					}
				}
			}

			if (e.OldItems != null)
			{
				foreach (var item in e.OldItems)
				{
					if (item is SessionModel session)
					{
						DetachSession(session);
					}
				}
			}

			UpdateTimerState();
			RequestRefreshCheck();
		}

		private void AttachSession(SessionModel session)
		{
			if (session == null)
				return;

			session.PropertyChanged += OnSessionPropertyChanged;

			if (!session.GalleryOverrideEnabled)
			{
				session.GalleryRefreshIntervalSeconds = GlobalRefreshIntervalSeconds;
			}
		}

		private void DetachSession(SessionModel session)
		{
			if (session == null)
				return;

			session.PropertyChanged -= OnSessionPropertyChanged;
		}

		private void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (sender is not SessionModel)
				return;

			switch (e.PropertyName)
			{
				case nameof(SessionModel.GalleryOverrideEnabled):
				case nameof(SessionModel.GalleryAutoRefreshEnabled):
					UpdateTimerState();
					RequestRefreshCheck();
					break;
				case nameof(SessionModel.GalleryRefreshIntervalSeconds):
					RequestRefreshCheck();
					break;
			}
		}

		private void RequestRefreshCheck()
		{
			if (_disposed)
				return;

			_refreshCheckTimer.Stop();
			_refreshCheckTimer.Start();
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_refreshTimer.Stop();
			_refreshTimer.Tick -= OnRefreshTimerTick;
			_refreshCheckTimer.Stop();
			_refreshCheckTimer.Tick -= OnRefreshCheckTimerTick;
			Sessions.CollectionChanged -= OnSessionsCollectionChanged;

			foreach (var session in Sessions.ToList())
			{
				DetachSession(session);
			}

			_refreshLoopCts.Cancel();
			lock (_refreshQueueSync)
			{
				try
				{
					_activeRefreshCts?.Cancel();
				}
				catch
				{
					// best effort cancellation
				}
			}

			try
			{
				_refreshSignal.Release();
			}
			catch (SemaphoreFullException)
			{
				// ignored during teardown
			}

			try
			{
				_refreshLoopTask?.Wait(TimeSpan.FromMilliseconds(500));
			}
			catch
			{
				// teardown best effort only
			}

			lock (_refreshQueueSync)
			{
				_activeRefreshCts?.Dispose();
				_activeRefreshCts = null;
				_queuedSessions.Clear();
			}

			_refreshLoopCts.Dispose();
			_refreshSignal.Dispose();
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
