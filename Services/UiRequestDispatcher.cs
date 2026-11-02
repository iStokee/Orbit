using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Orbit.Services;

internal static class UiRequestDispatcher
{
	internal static Task<T> RunAsync<T>(Dispatcher? dispatcher, Func<T> action, CancellationToken token, DateTimeOffset? deadline = null)
	{
		void CheckDeadline()
		{
			token.ThrowIfCancellationRequested();
			if (deadline.HasValue && DateTimeOffset.UtcNow >= deadline.Value) throw new TimeoutException("Orbit UI request expired before execution.");
		}
		CheckDeadline();
		if (dispatcher == null || dispatcher.CheckAccess()) return Task.FromResult(action());
		return dispatcher.InvokeAsync(() =>
		{
			CheckDeadline();
			return action();
		}, DispatcherPriority.Normal, token).Task;
	}
}
