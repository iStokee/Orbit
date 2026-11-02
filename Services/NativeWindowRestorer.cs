using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Orbit.Services;

internal static class NativeWindowRestorer
{
	internal static void Restore(nint hwnd, nint parent, int style)
	{
		if (parent != 0 && !IsWindow(parent))
		{
			parent = 0;
			style &= ~0x40000000; // A destroyed original parent requires a top-level window.
		}
		Marshal.SetLastPInvokeError(0);
		var previousStyle = SetWindowLong(hwnd, -16, style);
		var error = Marshal.GetLastPInvokeError();
		if (previousStyle == 0 && error != 0) throw new Win32Exception(error, "Unable to restore native window styles.");
		Marshal.SetLastPInvokeError(0);
		var previous = SetParent(hwnd, parent);
		error = Marshal.GetLastPInvokeError();
		var actualParent = GetParent(hwnd);
		if ((previous == 0 && error != 0) || actualParent != parent)
			throw new Win32Exception(error, $"The native window could not be restored (parent 0x{actualParent:X}, expected 0x{parent:X}, style 0x{style:X}); its host has been retained.");
		if (!SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0010 | 0x0020))
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to refresh restored native window.");
		ShowWindow(hwnd, (style & 0x10000000) != 0 ? 5 : 0);
	}
	[DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
	[DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
	[DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint hwnd, nint parent);
	[DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(nint hwnd, int index, int value);
	[DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
	[DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
}
