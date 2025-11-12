using System;
using System.Runtime.InteropServices;

namespace Orbit.Interop
{
	internal static class ClientInputDispatcher
	{
		private const int WM_KEYDOWN = 0x0100;
		private const int WM_KEYUP = 0x0101;
		private const int WM_CHAR = 0x0102;
		private const uint MAPVK_VK_TO_CHAR = 0x02;
		private const int WM_ACTIVATE = 0x0006;
		private const int WA_ACTIVE = 1;
		private const uint MAPVK_VK_TO_VSC = 0x00;

		public static bool SendKeyDown(IntPtr targetWindow, int virtualKey, bool emitChar, char? character = null)
		{
			if (targetWindow == IntPtr.Zero)
				return false;

			ActivateWindow(targetWindow);
			var keyDownLParam = BuildKeyLParam(virtualKey, isKeyUp: false);

			SendMessage(targetWindow, WM_KEYDOWN, (IntPtr)virtualKey, keyDownLParam);

			if (emitChar)
			{
				var charCode = ResolveChar(virtualKey, character);
				if (charCode.HasValue)
				{
					SendMessage(targetWindow, WM_CHAR, (IntPtr)charCode.Value, keyDownLParam);
				}
			}

			return true;
		}

		public static bool SendKeyUp(IntPtr targetWindow, int virtualKey)
		{
			if (targetWindow == IntPtr.Zero)
				return false;

			ActivateWindow(targetWindow);
			var lParam = BuildKeyLParam(virtualKey, isKeyUp: true);
			SendMessage(targetWindow, WM_KEYUP, (IntPtr)virtualKey, lParam);
			return true;
		}

		private static char? ResolveChar(int virtualKey, char? explicitChar)
		{
			if (explicitChar.HasValue)
			{
				return explicitChar.Value;
			}

			var mapped = MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_CHAR);
			if (mapped == 0)
			{
				return null;
			}

			return (char)mapped;
		}

		private static void ActivateWindow(IntPtr hWnd)
		{
			if (hWnd == IntPtr.Zero)
				return;

			SendMessage(hWnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
			SetFocus(hWnd);
			SetForegroundWindow(hWnd);
		}

		private static IntPtr BuildKeyLParam(int virtualKey, bool isKeyUp)
		{
			uint scanCode = MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC) & 0xFF;
			uint lParam = 1; // repeat count
			lParam |= scanCode << 16;

			if (IsExtendedKey(virtualKey))
			{
				lParam |= 1u << 24;
			}

			if (isKeyUp)
			{
				lParam |= 1u << 30;
				lParam |= 1u << 31;
			}

			return (IntPtr)(long)lParam;
		}

		private static bool IsExtendedKey(int virtualKey)
		{
			return virtualKey switch
			{
				0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or
				0x2D or 0x2E or 0x6F or 0x90 or 0xA3 or 0xA5 => true,
				_ => false
			};
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern uint MapVirtualKey(uint uCode, uint uMapType);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);
	}
}
