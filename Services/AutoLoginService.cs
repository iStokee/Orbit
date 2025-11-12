using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Orbit.Interop;
using Orbit.Models;
using System.Diagnostics;

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

			var targetWindow = EnsureTargetWindow(session);
			if (targetWindow == IntPtr.Zero)
			{
				Console.WriteLine("[Orbit] AutoLogin aborted: unable to resolve client window handle.");
				return false;
			}

			// Ensure the embedded client owns focus before we start typing.
			await Application.Current.Dispatcher.InvokeAsync(
				() => session.HostControl.FocusEmbeddedClient(),
				DispatcherPriority.Background);

			await Task.Delay(FocusDelay, cancellationToken).ConfigureAwait(false);

			// Type username, tab into password, then type password and submit.
			if (!await SendTextAsync(targetWindow, account.Username, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			await Task.Delay(RandomBetween(90, 140), cancellationToken).ConfigureAwait(false);

			if (!await PressVirtualKeyAsync(targetWindow, 0x09, cancellationToken).ConfigureAwait(false)) // Tab
			{
				return false;
			}

			await Task.Delay(RandomBetween(90, 140), cancellationToken).ConfigureAwait(false);

			if (!await SendTextAsync(targetWindow, account.Password, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			await Task.Delay(RandomBetween(110, 160), cancellationToken).ConfigureAwait(false);

			if (!await PressVirtualKeyAsync(targetWindow, 0x0D, cancellationToken).ConfigureAwait(false)) // VK_RETURN (Enter)
			{
				return false;
			}

			await Application.Current.Dispatcher.InvokeAsync(
				() =>
				{
					account.LastUsed = DateTime.UtcNow;
					accountService.UpdateAccount(account);
				},
				DispatcherPriority.Normal,
				cancellationToken);

			return true;
		}

		private static async Task<bool> SendTextAsync(IntPtr targetWindow, string text, CancellationToken cancellationToken)
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
					targetWindow,
					keyInfo.VirtualKey,
					cancellationToken,
					keyInfo.Shift,
					keyInfo.Control,
					keyInfo.Alt,
					emitChar: true,
					character: keyInfo.Character).ConfigureAwait(false);

				if (!sent)
				{
					return false;
				}

				await Task.Delay(RandomBetween(KeyPressSpacingMinMs, KeyPressSpacingMaxMs), cancellationToken).ConfigureAwait(false);
			}

			return true;
		}

		private static async Task<bool> PressVirtualKeyAsync(
			IntPtr targetWindow,
			int virtualKey,
			CancellationToken cancellationToken,
			bool shift = false,
			bool control = false,
			bool alt = false,
			bool emitChar = false,
			char? character = null)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var shiftPressed = shift && ClientInputDispatcher.SendKeyDown(targetWindow, 0x10, emitChar: false); // VK_SHIFT
			var controlPressed = control && ClientInputDispatcher.SendKeyDown(targetWindow, 0x11, emitChar: false); // VK_CONTROL
			var altPressed = alt && ClientInputDispatcher.SendKeyDown(targetWindow, 0x12, emitChar: false); // VK_MENU (Alt)

			try
			{
				if (!ClientInputDispatcher.SendKeyDown(targetWindow, virtualKey, emitChar, character))
				{
					Console.WriteLine($"[Orbit] AutoLogin failed to press virtual key 0x{virtualKey:X2}.");
					return false;
				}

				await Task.Delay(KeyPressDelayMs, cancellationToken).ConfigureAwait(false);
				ClientInputDispatcher.SendKeyUp(targetWindow, virtualKey);
			}
			finally
			{
				if (altPressed)
				{
					ClientInputDispatcher.SendKeyUp(targetWindow, 0x12); // VK_MENU
				}

				if (controlPressed)
				{
					ClientInputDispatcher.SendKeyUp(targetWindow, 0x11); // VK_CONTROL
				}

				if (shiftPressed)
				{
					ClientInputDispatcher.SendKeyUp(targetWindow, 0x10); // VK_SHIFT
				}
			}

			return true;
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

			keyInfo = new KeyInfo(vk, shift, control, alt, ch);
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
			public KeyInfo(int virtualKey, bool shift, bool control, bool alt, char character)
			{
				VirtualKey = virtualKey;
				Shift = shift;
				Control = control;
				Alt = alt;
				Character = character;
			}

			public int VirtualKey { get; }
			public bool Shift { get; }
			public bool Control { get; }
			public bool Alt { get; }
			public char Character { get; }
		}

		private static IntPtr EnsureTargetWindow(SessionModel session)
		{
			if (session == null)
				return IntPtr.Zero;

			var cached = session.RenderSurfaceHandle;
			if (cached != IntPtr.Zero && IsWindow(cached))
				return cached;

			var external = session.ExternalHandle;
			if (external != IntPtr.Zero && IsWindow(external))
				return external;

			IntPtr resolved = IntPtr.Zero;

			// Legacy fallback: try to locate JagRenderView or SunAwtCanvas dynamically
			var processId = session.RSProcess?.Id ?? ClientSettings.rs2cPID;
			if (processId > 0)
			{
				try
				{
					var process = Process.GetProcessById(processId);
					if (process.MainWindowHandle != IntPtr.Zero)
					{
						resolved = FindWindowEx(process.MainWindowHandle, IntPtr.Zero, "JagRenderView", null);
						if (resolved == IntPtr.Zero)
						{
							resolved = FindWindowEx(process.MainWindowHandle, IntPtr.Zero, "SunAwtCanvas", null);
						}
					}
				}
				catch
				{
					resolved = IntPtr.Zero;
				}
			}

			if (resolved == IntPtr.Zero)
			{
				resolved = ClientSettings.jagOpenGL;
			}

			if (resolved != IntPtr.Zero && IsWindow(resolved))
			{
				session.RenderSurfaceHandle = resolved;
				return resolved;
			}

			session.RenderSurfaceHandle = IntPtr.Zero;
			return IntPtr.Zero;
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lpszClass, string? lpszWindow);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

		[DllImport("user32.dll")]
		private static extern IntPtr GetKeyboardLayout(uint idThread);
	}
}
