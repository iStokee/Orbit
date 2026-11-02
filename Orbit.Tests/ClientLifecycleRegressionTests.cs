using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Orbit.Models;
using Orbit.Services;
using Orbit.Views;
using Xunit;

namespace Orbit.Tests;

public sealed class ClientLifecycleRegressionTests
{
	[Fact]
	public async Task Shutdown_IsSharedAndWaitsForCleanup()
	{
		var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var calls = 0;
		var service = new ApplicationShutdownService(() => { calls++; return pending.Task; });
		var normalExit = service.PrepareAsync();
		var updateExit = service.PrepareAsync();
		Assert.Same(normalExit, updateExit);
		Assert.True(service.IsShuttingDown);
		Assert.False(normalExit.IsCompleted);
		pending.SetResult();
		await normalExit;
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task ClosingSession_CancelsAndWaitsForStartup()
	{
		var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var session = new SessionModel { Id = Guid.NewGuid(), StartupTask = startup.Task };
		using var manager = new SessionManagerService();
		var shutdown = manager.ShutdownSessionAsync(session);
		var secondShutdown = manager.ShutdownSessionAsync(session);
		Assert.True(session.LifetimeCancellation.IsCancellationRequested);
		Assert.False(shutdown.IsCompleted);
		startup.SetCanceled();
		await Task.WhenAll(shutdown, secondShutdown).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(SessionState.Closed, session.State);
	}

	[Fact]
	public void LaunchObservation_DoesNotNeedLauncherPidAndRejectsAmbiguity()
	{
		var now = DateTime.UtcNow;
		var observation = new ClientLaunchObservation(new[] { 10 }, now);
		Assert.False(observation.IsFresh(10, now));
		Assert.False(observation.IsFresh(11, now.AddSeconds(-1)));
		Assert.True(observation.IsFresh(11, now.AddSeconds(1)));
		Assert.Equal(11, ClientLaunchObservation.SelectCandidate(new[] { (11, false) }));
		Assert.Null(ClientLaunchObservation.SelectCandidate(new[] { (11, false), (12, false) }));
		Assert.Equal(12, ClientLaunchObservation.SelectCandidate(new[] { (11, false), (12, true) }));
		Assert.Null(ClientLaunchObservation.SelectCandidate(new[] { (11, true), (12, true) }));
	}

	[Fact]
	public async Task UiRequest_CancelledWhileQueuedNeverMutatesUi()
	{
		Dispatcher? dispatcher = null;
		using var ready = new ManualResetEventSlim();
		using var pump = new ManualResetEventSlim();
		var mutations = 0;
		var thread = new Thread(() =>
		{
			dispatcher = Dispatcher.CurrentDispatcher;
			ready.Set();
			pump.Wait();
			Dispatcher.Run();
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
		try
		{
			using var cts = new CancellationTokenSource();
			var request = UiRequestDispatcher.RunAsync(dispatcher, () => Interlocked.Increment(ref mutations), cts.Token);
			cts.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
			pump.Set();
			await dispatcher!.InvokeAsync(() => { }, DispatcherPriority.Background).Task.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal(0, mutations);
		}
		finally
		{
			pump.Set();
			dispatcher!.BeginInvokeShutdown(DispatcherPriority.Send);
			Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
		}
	}

	[Fact]
	public async Task Login_NeverDispatchesCredentialsOrChangesUsage()
	{
		var folder = CreateFolder();
		var service = new AutoLoginService(new AccountService(Path.Combine(folder, "accounts.json")));
		var account = new AccountModel { Username = "sample", Password = "secret" };
		var session = new SessionModel();
		session.UpdateInjectionState(InjectionState.Injected);
		Assert.False(await service.LoginAsync(session, account));
		Assert.Equal(DateTime.MinValue, account.LastUsed);
		Assert.Empty(Directory.GetFiles(folder));
	}

	[Fact]
	public void ViewLoad_DoesNotStartAClient()
		=> RunOnSta(() =>
		{
			var view = new ChildClientView();
			var session = new SessionModel { Id = Guid.NewGuid(), HostControl = view };
			view.DataContext = session;
			view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
			Assert.Null(session.StartupTask);
			Assert.Null(session.RSForm);
			view.DetachSession();
		});

	[Fact]
	public void WindowIdentity_RejectsReplacedRegistrationAndRetiresOnlyItsOwnMarker()
		=> RunOnSta(() =>
		{
			var hwnd = CreateWindowEx(0, "STATIC", "Orbit identity test", 0, 0, 0, 20, 20, 0, 0, 0, 0);
			Assert.NotEqual(nint.Zero, hwnd);
			try
			{
				using var process = Process.GetCurrentProcess();
				using var first = new HostedWindowIdentity(hwnd, process);
				Assert.True(first.IsCurrent);
				using var replacement = new HostedWindowIdentity(hwnd, process);
				Assert.False(first.IsCurrent);
				first.Dispose();
				Assert.True(replacement.IsCurrent);
				DestroyWindow(hwnd);
				Assert.False(replacement.IsCurrent);
			}
			finally { DestroyWindow(hwnd); }
		});

	[Fact]
	public void ProcessClaim_SurvivesHostDisposalUntilSessionRemoval()
		=> RunOnSta(() =>
		{
			var collection = SessionCollectionService.Instance;
			var session = new SessionModel { Id = Guid.NewGuid(), Name = "host lifetime test" };
			using var process = Process.GetCurrentProcess();
			using var manager = new SessionManagerService();
			manager.StartTracking(collection);
			collection.Sessions.Add(session);
			try
			{
				using (var form = new RSForm())
				{
					SessionManagerService.TrackSessionProcess(form, session, Dispatcher.CurrentDispatcher);
					form.ProcessChanged!(process);
					Assert.True(SessionProcessOwnership.Instance.TryClaim(process.Id, process.StartTime.ToUniversalTime(), session.Id));
				}
				Assert.False(SessionProcessOwnership.Instance.TryClaim(process.Id, process.StartTime.ToUniversalTime(), Guid.NewGuid()));
			}
			finally { collection.Sessions.Remove(session); SessionProcessOwnership.Instance.ReleaseOwner(session.Id); }
		});

	[Fact]
	public void ExternalWindow_DetachRestoresParentBeforeHostDestruction()
		=> RunOnSta(() =>
		{
			var hwnd = CreateWindowEx(0, "STATIC", "Orbit detach test", 0, 0, 0, 20, 20, 0, 0, 0, 0);
			Assert.NotEqual(nint.Zero, hwnd);
			using var process = Process.GetCurrentProcess();
			using var identity = new HostedWindowIdentity(hwnd, process);
			var originalParent = GetParent(hwnd);
			var originalStyle = GetWindowLong(hwnd, -16);
			var view = new ChildClientView();
			var session = new SessionModel
			{
				Id = Guid.NewGuid(), SessionType = SessionType.ExternalScript, ExternalHandle = hwnd,
				RSProcess = process, ExternalWindowIdentity = identity, HostControl = view
			};
			view.DataContext = session;
			try
			{
				view.AttachExternalWindow(session);
				Assert.NotEqual(originalParent, GetParent(hwnd));
				view.DetachSession();
				Assert.True(identity.IsCurrent);
				Assert.Equal(originalParent, GetParent(hwnd));
				Assert.Equal(originalStyle, GetWindowLong(hwnd, -16));
			}
			finally { view.DetachSession(); DestroyWindow(hwnd); }
		});

	[Fact]
	public void WindowTransfer_PreservesSessionProcessAndCancellationTokens()
	{
		var collection = SessionCollectionService.Instance;
		var placement = new SessionPlacementService();
		var reconciliation = new SessionReconciliationService(collection, placement, new TearOffHostRegistry());
		var layout = new OrbitLayoutStateService(collection, placement, reconciliation);
		var transfer = new ShellWindowTransferService(placement, layout);
		using var process = Process.GetCurrentProcess();
		var session = new SessionModel { Id = Guid.NewGuid(), RSProcess = process };
		var source = new System.Collections.ObjectModel.ObservableCollection<object> { session };
		var destination = new System.Collections.ObjectModel.ObservableCollection<object>();
		var token = session.LifetimeCancellation;
		transfer.Transfer(source.ToArray(), item => source.Remove(item), item => source.Add(item), destination,
			new object(), new object(), () => { }, SessionPlacementKind.MainTabs);
		Assert.Empty(source);
		Assert.Same(session, Assert.Single(destination));
		Assert.Same(process, session.RSProcess);
		Assert.False(token.IsCancellationRequested);
		Assert.Equal(SessionPlacementKind.MainTabs, placement.GetPlacement(session));
		Assert.False(placement.IsMoveInProgress(session));
	}

	[Fact]
	public void InputDelivery_UnresponsiveWindowReturnsWithinBound()
	{
		nint hwnd = 0;
		var stop = 0;
		using var ready = new ManualResetEventSlim();
		var thread = new Thread(() =>
		{
			hwnd = CreateWindowEx(0, "STATIC", "Orbit stalled input test", 0, 0, 0, 20, 20, 0, 0, 0, 0);
			ready.Set();
			while (Volatile.Read(ref stop) == 0) Thread.SpinWait(1000);
			DestroyWindow(hwnd);
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
		try
		{
			Assert.NotEqual(nint.Zero, hwnd);
			var elapsed = Stopwatch.StartNew();
			Assert.False(Orbit.Interop.ClientInputDispatcher.SendKeyDown(hwnd, 0x41, emitChar: false));
			Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
		}
		finally { Volatile.Write(ref stop, 1); Assert.True(thread.Join(TimeSpan.FromSeconds(5))); }
	}

	[Fact]
	public void ScriptRegistration_CannotTakeOverAnOwnedGameWindow()
		=> RunOnSta(() =>
		{
			var enabled = Settings.Default.ScriptWindowEmbeddingEnabled;
			Settings.Default.ScriptWindowEmbeddingEnabled = true;
			var hwnd = CreateWindowEx(0, "STATIC", "Orbit duplicate host test", 0, 0, 0, 20, 20, 0, 0, 0, 0);
			var collection = SessionCollectionService.Instance;
			var session = new SessionModel { Id = Guid.NewGuid(), ExternalHandle = hwnd };
			collection.Sessions.Add(session);
			try
			{
				var placement = new SessionPlacementService();
				var reconciliation = new SessionReconciliationService(collection, placement, new TearOffHostRegistry());
				var layout = new OrbitLayoutStateService(collection, placement, reconciliation);
				var integration = new ScriptIntegrationService(collection, placement, layout);
				var error = Assert.Throws<InvalidOperationException>(() => integration.RegisterScriptWindow(hwnd, "duplicate"));
				Assert.Contains("managed game window", error.Message);
			}
			finally
			{
				collection.Sessions.Remove(session);
				DestroyWindow(hwnd);
				Settings.Default.ScriptWindowEmbeddingEnabled = enabled;
			}
		});

	internal static void RunOnSta(Action action)
	{
		Exception? error = null;
		var thread = new Thread(() =>
		{
			try { action(); } catch (Exception ex) { error = ex; }
			finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
		if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
	}

	internal static string CreateFolder()
	{
		var folder = Path.Combine(Path.GetTempPath(), "OrbitRegression-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		return folder;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
	[DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
	[DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
	[DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
}
