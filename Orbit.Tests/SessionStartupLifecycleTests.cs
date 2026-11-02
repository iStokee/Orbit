using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Orbit.Models;
using Orbit.Views;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionStartupLifecycleTests
{
	[Fact]
	public void InjectionGate_WaitsForCompletionEvenBeforeInjectionStarts()
	{
		RunOnSta(() =>
		{
			using var form = new RSForm { WaitForInjectionBeforeDock = true };
			var docking = form.WaitForInjectionGateAsync();
			Assert.False(docking.IsCompleted);
			form.SignalInjectionReady();
			docking.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
		});
	}

	[Fact]
	public void InjectionGate_ManualWorkflowDoesNotWait()
	{
		RunOnSta(() =>
		{
			using var form = new RSForm { WaitForInjectionBeforeDock = false };
			Assert.True(form.WaitForInjectionGateAsync().IsCompletedSuccessfully);
		});
	}

	[Fact]
	public void InjectionGate_ClosingSessionCancelsWait()
	{
		RunOnSta(() =>
		{
			using var form = new RSForm { WaitForInjectionBeforeDock = true };
			var session = new SessionModel { Id = Guid.NewGuid() };
			var docking = form.WaitForInjectionGateAsync(session.LifetimeCancellation);
			session.UpdateState(SessionState.ShuttingDown);
			Assert.ThrowsAny<OperationCanceledException>(() => docking.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
		});
	}

	[Fact]
	public void ProcessReplacement_CancelsOldWorkWithoutCancellingSessionLifetime()
	{
		RunOnSta(() =>
		{
			var view = new ChildClientView();
			var session = new SessionModel { Id = Guid.NewGuid(), Name = "retry", HostControl = view };
			view.DataContext = session;
			using var form = new RSForm();
			using var first = Process.GetCurrentProcess();
			using var second = Process.GetCurrentProcess();
			try
			{
				view.TrackSessionProcess(form, session);
				form.pDocked = first;
				form.ProcessChanged!(first);
				var firstWork = session.WorkCancellation;
				var launch = session.LifetimeCancellation;
				form.ProcessChanged!(null);
				first.Dispose();
				Assert.Null(session.RSProcess);
				Assert.Throws<InvalidOperationException>(() => first.Id);
				Assert.True(firstWork.IsCancellationRequested);
				Assert.False(launch.IsCancellationRequested);
				form.pDocked = second;
				form.ProcessChanged!(second);
				Assert.Same(second, session.RSProcess);
				Assert.False(session.WorkCancellation.IsCancellationRequested);
				Assert.Equal(launch, session.LifetimeCancellation);
				session.UpdateState(SessionState.ShuttingDown);
				Assert.True(launch.IsCancellationRequested);
				form.ProcessChanged!(null);
				Assert.Same(second, session.RSProcess);
			}
			finally { view.DetachSession(); }
		});
	}

	private static void RunOnSta(Action action)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try { action(); }
			catch (Exception ex) { failure = ex; }
			finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Startup check did not finish within 10 seconds.");
		if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
