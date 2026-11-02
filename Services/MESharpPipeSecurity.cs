using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

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
		var pipe = NamedPipeServerStreamAcl.Create(
			pipeName,
			direction,
			maxInstances,
			transmissionMode,
			options,
			inBufferSize: 0,
			outBufferSize: 0,
			CreatePipeSecurity(),
			HandleInheritability.None,
			PipeAccessRights.TakeOwnership);
		try
		{
			// Applying a SACL through NamedPipeServerStreamAcl requests audit privileges.
			// A mandatory label only needs WRITE_OWNER and LABEL_SECURITY_INFORMATION.
			var label = new RawSecurityDescriptor("S:(ML;;NW;;;LW)").SystemAcl!;
			var labelBytes = new byte[label.BinaryLength];
			label.GetBinaryForm(labelBytes, 0);
			var error = SetSecurityInfo(pipe.SafePipeHandle, 6 /* SE_KERNEL_OBJECT */,
				0x10 /* LABEL_SECURITY_INFORMATION */, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, labelBytes);
			if (error != 0) throw new Win32Exception((int)error, "Unable to set pipe integrity label.");
			return pipe;
		}
		catch
		{
			pipe.Dispose();
			throw;
		}
	}

	[DllImport("advapi32.dll", ExactSpelling = true)]
	private static extern uint SetSecurityInfo(SafePipeHandle handle, uint objectType,
		uint securityInformation, IntPtr owner, IntPtr group, IntPtr dacl, byte[] sacl);

	public static PipeSecurity CreatePipeSecurity()
	{
		using var identity = WindowsIdentity.GetCurrent();
		var userSid = identity.User
			?? throw new InvalidOperationException("Unable to resolve the current user SID for pipe security.");

		var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{userSid.Value})";
		var security = new PipeSecurity();
		security.SetSecurityDescriptorSddlForm(sddl);
		return security;
	}
}
