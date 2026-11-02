using System;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Models;

namespace Orbit.Services;

public sealed class AutoLoginService
{
	public const string UnavailableReason = "Credential typing is disabled until Orbit can verify the login screen and target fields. Log in through the game client or launcher.";

	public AutoLoginService(AccountService accountService)
		=> ArgumentNullException.ThrowIfNull(accountService);

	// Keep the API for existing callers, without dispatching credentials speculatively.
	public Task<bool> LoginAsync(SessionModel session, AccountModel account, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(account);
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(false);
	}
}
