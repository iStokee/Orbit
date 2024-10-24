using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Interop;

public class RunescapeHost : HwndHost
{
	private IntPtr hwndRunescape;

	[DllImport("user32.dll")]
	private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

	private const int GWL_STYLE = -16;
	private const int WS_POPUP = unchecked((int)0x80000000);
	private const int WS_CHILD = 0x40000000;

	public RunescapeHost(IntPtr hwnd)
	{
		hwndRunescape = hwnd;
	}

	protected override HandleRef BuildWindowCore(HandleRef hwndParent)
	{
		// Set the parent of the external window to this HwndHost's handle
		SetParent(hwndRunescape, hwndParent.Handle);

		// Modify window styles to make it a child window
		int style = GetWindowLong(hwndRunescape, GWL_STYLE);
		style = (style & ~WS_POPUP) | WS_CHILD;
		SetWindowLong(hwndRunescape, GWL_STYLE, style);

		// Resize and position the window
		MoveWindow(hwndRunescape, 0, 0, (int)ActualWidth, (int)ActualHeight, true);

		return new HandleRef(this, hwndRunescape);
	}

	protected override void DestroyWindowCore(HandleRef hwnd)
	{
		// Optional: Clean up or reset window styles
	}

	protected override void OnWindowPositionChanged(Rect rcBoundingBox)
	{
		base.OnWindowPositionChanged(rcBoundingBox);

		// Resize the embedded window when the host control resizes
		MoveWindow(hwndRunescape, 0, 0, (int)rcBoundingBox.Width, (int)rcBoundingBox.Height, true);
	}

	public static implicit operator WindowsFormsHost(RunescapeHost v)
	{
		throw new NotImplementedException();
	}
}
