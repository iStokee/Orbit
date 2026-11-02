using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Orbit.Services;

/// <summary>Measures in native coordinates so DPI and non-client frames need no fixed offsets.</summary>
internal static class NativeWindowGeometry
{
	internal static bool IsCurrentThreadWindow(nint hwnd) => GetWindowThreadProcessId(hwnd, out _) == GetCurrentThreadId();
	internal static Size ClientSize(nint hwnd)
		=> hwnd != 0 && GetClientRect(hwnd, out var rect) ? new(rect.Right - rect.Left, rect.Bottom - rect.Top) : Size.Empty;

	internal static bool FitToParent(nint hwnd, nint parent)
	{
		if (hwnd == 0 || parent == 0 || GetParent(hwnd) != parent) return false;
		var host = ClientSize(parent);
		if (host.Width <= 0 || host.Height <= 0 || !GetWindowRect(hwnd, out var outer) || !GetClientRect(hwnd, out var client)) return false;
		var origin = new Point();
		if (!ClientToScreen(hwnd, ref origin)) return false;
		var bounds = FitBounds(host, new Rectangle(outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top),
			new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top));
		return SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0004);
	}

	internal static Rectangle FitBounds(Size host, Rectangle outer, Rectangle client)
		=> new(outer.Left - client.Left, outer.Top - client.Top,
			host.Width + Math.Max(0, outer.Width - client.Width), host.Height + Math.Max(0, outer.Height - client.Height));

	[StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
	[DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
	[DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
	[DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
	[DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
	[DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
	[DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
