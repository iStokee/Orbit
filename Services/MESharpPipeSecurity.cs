using System;
using System.IO.Pipes;
using System.Security.Principal;

namespace Orbit.Services;

/// <summary>
/// Shared pipe security policy for Orbit's named-pipe servers, per docs/IPC_CONVENTIONS.md §2.5:
/// allow SYSTEM, Administrators, and the current user; low mandatory label so medium-integrity
/// processes can reach servers across integrity levels. Any other local user/process is denied.
/// </summary>
internal static class MESharpPipeSecurity
{
	public static NamedPipeServerStream CreateServer(
		string pipeName,
		PipeDirection direction,
		int maxInstances,
		PipeTransmissionMode transmissionMode,
		PipeOptions options)
	{
		return NamedPipeServerStreamAcl.Create(
			pipeName,
			direction,
			maxInstances,
			transmissionMode,
			options,
			inBufferSize: 0,
			outBufferSize: 0,
			CreatePipeSecurity());
	}

	public static PipeSecurity CreatePipeSecurity()
	{
		using var identity = WindowsIdentity.GetCurrent();
		var userSid = identity.User
			?? throw new InvalidOperationException("Unable to resolve the current user SID for pipe security.");

		var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{userSid.Value})S:(ML;;NW;;;LW)";
		var security = new PipeSecurity();
		security.SetSecurityDescriptorSddlForm(sddl);
		return security;
	}
}
