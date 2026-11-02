using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Orbit.Logging;
using Orbit.Views;
using Xunit;

namespace Orbit.Tests;

public sealed class UiWorkSchedulingTests
{
	[Fact]
	public void ResizeBeforeNativeFormExists_AllowsSubsequentRequests()
	{
		RunOnSta(() =>
		{
			var view = new ChildClientView();
			try
			{
				view.ResizeWindowAsync(800, 600).GetAwaiter().GetResult();
				view.ResizeWindowAsync(900, 700).GetAwaiter().GetResult();
				Assert.Equal(Size.Empty, view.LastAppliedViewportSize);
			}
			finally { view.DetachSession(); }
		});
	}

	[Fact]
	public void Detach_CancelsPendingResizeAndRejectsFutureRequests()
	{
		RunOnSta(() =>
		{
			var view = new ChildClientView();
			var pending = view.ResizeWindowAsync(800, 600);
			view.DetachSession();
			pending.GetAwaiter().GetResult();
			view.ResizeWindowAsync(900, 700).GetAwaiter().GetResult();
			Assert.Equal(Size.Empty, view.LastAppliedViewportSize);
		});
	}

	[Fact]
	public void UnloadedActivation_DoesNotPollDispatcher()
	{
		RunOnSta(() =>
		{
			var view = new ChildClientView();
			var posted = 0;
			DispatcherHookEventHandler handler = (_, _) => posted++;
			view.Dispatcher.Hooks.OperationPosted += handler;
			try
			{
				for (var i = 0; i < 20; i++) view.RefreshDockedContentActivation(true);
				Assert.Equal(0, posted);
			}
			finally
			{
				view.Dispatcher.Hooks.OperationPosted -= handler;
				view.DetachSession();
			}
		});
	}

	[Fact]
	public void LogBurst_YieldsBetweenBatchesAndRetainsNewestEntries()
	{
		RunOnSta(() =>
		{
			var log = new ConsoleLogService();
			for (var i = 0; i < 10000; i++) log.Append($"entry-{i}", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
			PumpThrough(DispatcherPriority.Background);
			Assert.InRange(log.Entries.Count, 1, 250);
			PumpThrough(DispatcherPriority.ApplicationIdle);
			Assert.Equal(5000, log.Entries.Count);
			Assert.Equal("entry-5000", log.Entries[0].Message);
			Assert.Equal("entry-9999", log.Entries[^1].Message);
		});
	}

	private static void PumpThrough(DispatcherPriority priority)
	{
		var frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
		Dispatcher.PushFrame(frame);
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
		Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI work did not finish within 10 seconds.");
		if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
