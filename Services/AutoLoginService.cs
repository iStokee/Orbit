using Orbit.Models;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Orbit.Services
{
	public class AutoLoginService
	{
		private const int KeyPressDelayMs = 30;
		private const int KeyPressSpacingMinMs = 35;
		private const int KeyPressSpacingMaxMs = 65;
		private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(180);
		private static readonly Random RandomDelay = new();

		private readonly AccountService accountService;

		public AutoLoginService(AccountService accountService)
		{
			this.accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
		}

		public async Task<bool> LoginAsync(SessionModel session, AccountModel account, CancellationToken cancellationToken = default)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));
			if (account == null)
				throw new ArgumentNullException(nameof(account));

			if (session.InjectionState != InjectionState.Injected)
			{
				Console.WriteLine("[Orbit] AutoLogin aborted: session is not injected.");
				return false;
			}

			if (session.HostControl == null)
			{
				Console.WriteLine("[Orbit] AutoLogin aborted: session host control is unavailable.");
				return false;
			}

			// Ensure the embedded client owns focus before we start typing.
			await Application.Current.Dispatcher.InvokeAsync(
				() => session.HostControl.FocusEmbeddedClient(),
				DispatcherPriority.Background);

			await Task.Delay(FocusDelay, cancellationToken).ConfigureAwait(false);

			// Type username, tab into password, then type password and submit.
			if (!await SendTextAsync(account.Username, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			await Task.Delay(RandomBetween(90, 140), cancellationToken).ConfigureAwait(false);

			if (!await PressVirtualKeyAsync(0x09, cancellationToken).ConfigureAwait(false)) // Tab
			{
				return false;
			}

			await Task.Delay(RandomBetween(90, 140), cancellationToken).ConfigureAwait(false);

			if (!await SendTextAsync(account.Password, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			await Task.Delay(RandomBetween(110, 160), cancellationToken).ConfigureAwait(false);

			if (!await PressVirtualKeyAsync(0x0D, cancellationToken).ConfigureAwait(false)) // VK_RETURN (Enter)
			{
				return false;
			}

			account.LastUsed = DateTime.UtcNow;
			accountService.UpdateAccount(account);

			return true;
		}

		private static async Task<bool> SendTextAsync(string text, CancellationToken cancellationToken)
		{
			if (string.IsNullOrEmpty(text))
			{
				return true;
			}

			foreach (var ch in text)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!TryGetKeyInfo(ch, out var keyInfo))
				{
					Console.WriteLine($"[Orbit] AutoLogin skipped unsupported character U+{(int)ch:X4}.");
					continue;
				}

				var sent = await PressVirtualKeyAsync(
					keyInfo.VirtualKey,
					cancellationToken,
					keyInfo.Shift,
					keyInfo.Control,
					keyInfo.Alt).ConfigureAwait(false);

				if (!sent)
				{
					return false;
				}

				await Task.Delay(RandomBetween(KeyPressSpacingMinMs, KeyPressSpacingMaxMs), cancellationToken).ConfigureAwait(false);
			}

			return true;
		}

		private static async Task<bool> PressVirtualKeyAsync(
			int virtualKey,
			CancellationToken cancellationToken,
			bool shift = false,
			bool control = false,
			bool alt = false)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var shiftPressed = shift && SendKeyDown(0x10); // VK_SHIFT
			var controlPressed = control && SendKeyDown(0x11); // VK_CONTROL
			var altPressed = alt && SendKeyDown(0x12); // VK_MENU (Alt)

			try
			{
				if (!SendKeyDown(virtualKey))
				{
					Console.WriteLine($"[Orbit] AutoLogin failed to press virtual key 0x{virtualKey:X2}.");
					return false;
				}

				await Task.Delay(KeyPressDelayMs, cancellationToken).ConfigureAwait(false);
				SendKeyUp(virtualKey);
			}
			finally
			{
				if (altPressed)
				{
					SendKeyUp(0x12); // VK_MENU
				}

				if (controlPressed)
				{
					SendKeyUp(0x11); // VK_CONTROL
				}

				if (shiftPressed)
				{
					SendKeyUp(0x10); // VK_SHIFT
				}
			}

			return true;
		}

		private static bool SendKeyDown(int virtualKey)
		{
			var input = new INPUT
			{
				Type = INPUT_KEYBOARD,
				Data = new INPUTUNION
				{
					Keyboard = new KEYBDINPUT
					{
						Vk = (ushort)virtualKey,
						Scan = 0,
						Flags = 0,
						Time = 0,
						ExtraInfo = IntPtr.Zero
					}
				}
			};

			return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
		}

		private static bool SendKeyUp(int virtualKey)
		{
			var input = new INPUT
			{
				Type = INPUT_KEYBOARD,
				Data = new INPUTUNION
				{
					Keyboard = new KEYBDINPUT
					{
						Vk = (ushort)virtualKey,
						Scan = 0,
						Flags = KEYEVENTF_KEYUP,
						Time = 0,
						ExtraInfo = IntPtr.Zero
					}
				}
			};

			return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
		}

		private static bool TryGetKeyInfo(char ch, out KeyInfo keyInfo)
		{
			var layout = GetKeyboardLayout(0);
			var vkScan = VkKeyScanEx(ch, layout);
			if (vkScan == -1)
			{
				keyInfo = default;
				return false;
			}

			var vk = vkScan & 0xFF;
			var shift = (vkScan & 0x0100) != 0;
			var control = (vkScan & 0x0200) != 0;
			var alt = (vkScan & 0x0400) != 0;

			keyInfo = new KeyInfo(vk, shift, control, alt);
			return true;
		}

		private static int RandomBetween(int minInclusive, int maxInclusive)
		{
			lock (RandomDelay)
			{
				return RandomDelay.Next(minInclusive, maxInclusive + 1);
			}
		}

		[StructLayout(LayoutKind.Auto)]
		private readonly struct KeyInfo
		{
			public KeyInfo(int virtualKey, bool shift, bool control, bool alt)
			{
				VirtualKey = virtualKey;
				Shift = shift;
				Control = control;
				Alt = alt;
			}

			public int VirtualKey { get; }
			public bool Shift { get; }
			public bool Control { get; }
			public bool Alt { get; }
		}

		[DllImport("user32.dll")]
		private static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

		[DllImport("user32.dll")]
		private static extern IntPtr GetKeyboardLayout(uint idThread);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

		private const int INPUT_KEYBOARD = 1;
		private const uint KEYEVENTF_KEYUP = 0x0002;

		[StructLayout(LayoutKind.Sequential)]
		private struct INPUT
		{
			public int Type;
			public INPUTUNION Data;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct INPUTUNION
		{
			[FieldOffset(0)]
			public MOUSEINPUT Mouse;
			[FieldOffset(0)]
			public KEYBDINPUT Keyboard;
			[FieldOffset(0)]
			public HARDWAREINPUT Hardware;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MOUSEINPUT
		{
			public int X;
			public int Y;
			public uint MouseData;
			public uint Flags;
			public uint Time;
			public IntPtr ExtraInfo;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct KEYBDINPUT
		{
			public ushort Vk;
			public ushort Scan;
			public uint Flags;
			public uint Time;
			public IntPtr ExtraInfo;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct HARDWAREINPUT
		{
			public uint Msg;
			public ushort ParamL;
			public ushort ParamH;
		}
	}
}
