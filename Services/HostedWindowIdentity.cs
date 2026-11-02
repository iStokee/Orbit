using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbit.Services;

/// <summary>A registration marker disappears when Windows destroys the window.</summary>
internal sealed class HostedWindowIdentity : IDisposable
{
	private static readonly string PropertyName = "Orbit.ExternalWindow." + Environment.ProcessId;
	private static long _nextMarker;
	private readonly nint _marker = (nint)Interlocked.Increment(ref _nextMarker);
	internal nint Handle { get; }
	internal int ProcessId { get; }
	internal DateTime StartedUtc { get; }

	internal HostedWindowIdentity(nint handle, Process process)
	{
		Handle = handle;
		ProcessId = process.Id;
		StartedUtc = process.StartTime.ToUniversalTime();
		GetWindowThreadProcessId(handle, out var owner);
		if (!IsWindow(handle) || owner != ProcessId || process.HasExited)
			throw new ArgumentException("The script window no longer belongs to the supplied process.");
		if (!SetProp(handle, PropertyName, _marker)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to mark script window ownership.");
	}

	internal bool IsCurrent
	{
		get
		{
			if (!IsWindow(Handle) || GetProp(Handle, PropertyName) != _marker) return false;
			GetWindowThreadProcessId(Handle, out var owner);
			if (owner != ProcessId) return false;
			try
			{
				using var process = Process.GetProcessById(ProcessId);
				return !process.HasExited && process.StartTime.ToUniversalTime() == StartedUtc;
			}
			catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
		}
	}

	public void Dispose()
	{
		if (GetProp(Handle, PropertyName) == _marker) RemoveProp(Handle, PropertyName);
	}

	[DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetProp(nint hwnd, string name, nint data);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint hwnd, string name);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint hwnd, string name);
}
