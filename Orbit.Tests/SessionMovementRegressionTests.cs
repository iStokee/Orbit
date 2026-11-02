using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Orbit.Views;
using Xunit;
using NativeSize = System.Drawing.Size;
using Point = System.Drawing.Point;

namespace Orbit.Tests;

public sealed class SessionMovementRegressionTests
{
	[Theory]
	[InlineData(0u)]
	[InlineData(0x00C40000u)]
	public void Resize_FitsNativeClientAreaWithOrWithoutWindowFrame(uint frame)
		=> ClientLifecycleRegressionTests.RunOnSta(() =>
		{
			var parent = CreateWindowEx(0, "STATIC", "host", 0, 0, 0, 497, 311, 0, 0, 0, 0);
			var child = CreateWindowEx(0, "STATIC", "client", 0x40000000 | frame, 0, 0, 200, 100, parent, 0, 0, 0);
			try
			{
				Assert.True(NativeWindowGeometry.FitToParent(child, parent));
				Assert.Equal(NativeWindowGeometry.ClientSize(parent), NativeWindowGeometry.ClientSize(child));
				var parentOrigin = new Point(); var childOrigin = new Point();
				Assert.True(ClientToScreen(parent, ref parentOrigin)); Assert.True(ClientToScreen(child, ref childOrigin));
				Assert.Equal(parentOrigin, childOrigin);
				Assert.False(NativeWindowGeometry.FitToParent(child, 0));
			}
			finally { DestroyWindow(child); DestroyWindow(parent); }
		});

	[Fact]
	public void ResizeBounds_UseMeasuredFrameAndNativeHostPixels()
	{
		var bounds = NativeWindowGeometry.FitBounds(new NativeSize(1200, 900),
			new Rectangle(40, 60, 530, 355), new Rectangle(55, 100, 500, 300));
		Assert.Equal(new Rectangle(-15, -40, 1230, 955), bounds);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ExternalResize_CancelledByDetachOrUnloadDoesNotLoseFinalSize(bool unload)
		=> await DeepAuditFixTests.RunOnStaAsync(async () =>
		{
			var hwnd = CreateWindowEx(0, "STATIC", "synthetic script", 0x00C40000, 0, 0, 220, 140, 0, 0, 0, 0);
			using var process = Process.GetCurrentProcess();
			using var identity = new HostedWindowIdentity(hwnd, process);
			var view = new ChildClientView();
			var session = new SessionModel { Id = Guid.NewGuid(), SessionType = SessionType.ExternalScript, ExternalHandle = hwnd,
				RSProcess = process, ExternalWindowIdentity = identity, HostControl = view };
			view.DataContext = session;
			var window = new Window { Content = view, Width = 420, Height = 320, Left = -30000, Top = -30000,
				ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
			try
			{
				window.Show();
				await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
				await view.ResizeWindowAsync(420, 320);
				var parent = NativeWindowGeometry.GetParent(hwnd);
				Assert.NotEqual(0, parent);
				Assert.Equal(NativeWindowGeometry.ClientSize(parent), NativeWindowGeometry.ClientSize(hwnd));
				window.Width += 31;
				window.UpdateLayout();
				var pending = view.ResizeWindowAsync(451, 320);
				if (unload)
				{
					window.Hide();
					await pending;
					window.Show();
					await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
					await view.ResizeWindowAsync(451, 320);
					Assert.Equal(NativeWindowGeometry.ClientSize(parent), NativeWindowGeometry.ClientSize(hwnd));
				}
				else
				{
					view.DetachSession();
					GetWindowRect(hwnd, out var afterDetach);
					await pending;
					await view.ResizeWindowAsync(900, 800);
					GetWindowRect(hwnd, out var afterPending);
					Assert.Equal(afterDetach, afterPending);
					Assert.Equal(0, NativeWindowGeometry.GetParent(hwnd));
				}
			}
			finally { view.DetachSession(); window.Close(); DestroyWindow(hwnd); }
		});

	[Fact]
	public async Task DetachingBeforeStartupCompletesReleasesDockWaiters()
		=> await DeepAuditFixTests.RunOnStaAsync(async () =>
		{
			var view = new ChildClientView();
			var dock = view.WaitForDockAsync();
			view.DetachSession();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dock);
		});

	[Fact]
	public async Task GameHost_UsesAvailableLayoutSizeInsteadOfAutosizingItself()
		=> await DeepAuditFixTests.RunOnStaAsync(async () =>
		{
			var view = new ChildClientView();
			var form = new RSForm();
			view.AttachSessionHost(form);
			var window = new Window { Content = view, Width = 431, Height = 319, Left = -30000, Top = -30000,
				ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
			try
			{
				window.Show();
				await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
				Assert.False(form.AutoSize);
				Assert.False(form.Controls[0].AutoSize);
				Assert.True(form.ClientSize.Width > 300);
				window.Width = 620;
				window.UpdateLayout();
				await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
				Assert.True(form.ClientSize.Width > 500);
			}
			finally { view.DetachSession(); window.Close(); }
		});

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void LandingPublishesDestinationBeforeCompletingMove(bool orbit)
	{
		var sessions = SessionCollectionService.Instance;
		var placement = new SessionPlacementService();
		var reconciliation = new SessionReconciliationService(sessions, placement, new TearOffHostRegistry());
		var layout = new OrbitLayoutStateService(sessions, placement, reconciliation);
		var coordinator = new ShellTabCollectionCoordinatorService(new ShellTabSelectionService(), placement, layout);
		var session = new SessionModel { Id = Guid.NewGuid() };
		placement.SetPlacement(session, orbit ? SessionPlacementKind.MainTabs : SessionPlacementKind.OrbitWorkspace);
		placement.BeginExternalMove(session);
		SessionPlacementKind? onCompletion = null;
		placement.MoveCompleted += (_, args) => onCompletion = placement.GetPlacement(args.Session);
		if (orbit) reconciliation.RegisterOrbitLanding(session);
		else coordinator.HandleCollectionChanged(new object[] { session }, session, NotifyCollectionChangedAction.Add,
			null, new object[] { session }, false, false);
		Assert.Equal(orbit ? SessionPlacementKind.OrbitWorkspace : SessionPlacementKind.TearOffWindow, onCompletion);
		Assert.False(placement.IsMoveInProgress(session));
	}

	[Theory]
	[InlineData(SessionPlacementKind.MainTabs)]
	[InlineData(SessionPlacementKind.TearOffWindow)]
	[InlineData(SessionPlacementKind.OrbitWorkspace)]
	public void Recovery_RepairsLostTabDespiteStalePlacement(SessionPlacementKind oldPlacement)
	{
		var sessions = SessionCollectionService.Instance;
		var placement = new SessionPlacementService();
		var lifecycle = new SessionLifecycleCoordinatorService();
		var reconciliation = new SessionReconciliationService(sessions, placement, new TearOffHostRegistry());
		var recovery = new ShellSessionRecoveryService(placement, reconciliation, lifecycle, new ConsoleLogService());
		var session = new SessionModel { Id = Guid.NewGuid(), Name = "lost tab" };
		var tabs = new ObservableCollection<object>();
		sessions.Sessions.Add(session);
		placement.SetPlacement(session, oldPlacement);
		try
		{
			Assert.True(lifecycle.TryBeginOrphanValidation(session));
			recovery.ValidateOrphanedSession(session, false, Array.Empty<object>(), s =>
				recovery.EnsureSessionRemainsVisible(s, sessions.Sessions, tabs, Array.Empty<object>(), _ => { }, _ => { }));
			Assert.Same(session, Assert.Single(tabs));
			Assert.Equal(SessionPlacementKind.MainTabs, placement.GetPlacement(session));
		}
		finally { sessions.Sessions.Remove(session); }
	}

	[StructLayout(LayoutKind.Sequential)] private record struct Rect(int Left, int Top, int Right, int Bottom);
	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
	[DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
	[DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
	[DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
