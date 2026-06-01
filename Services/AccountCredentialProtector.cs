using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Orbit.Services;

internal static class AccountCredentialProtector
{
	private const string Prefix = "dpapi:v1:";
	private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Orbit.AccountCredential.v1");

	public static bool IsProtectedValue(string? value)
		=> value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

	public static string Protect(string plaintext)
	{
		if (string.IsNullOrEmpty(plaintext))
		{
			return string.Empty;
		}

		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Orbit account password encryption requires Windows DPAPI.");
		}

		var bytes = Encoding.UTF8.GetBytes(plaintext);
		var protectedBytes = CryptProtect(bytes);
		return Prefix + Convert.ToBase64String(protectedBytes);
	}

	public static string Unprotect(string protectedValue)
	{
		if (string.IsNullOrEmpty(protectedValue))
		{
			return string.Empty;
		}

		if (!IsProtectedValue(protectedValue))
		{
			return protectedValue;
		}

		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Orbit account password decryption requires Windows DPAPI.");
		}

		var payload = protectedValue.Substring(Prefix.Length);
		var protectedBytes = Convert.FromBase64String(payload);
		var bytes = CryptUnprotect(protectedBytes);
		return Encoding.UTF8.GetString(bytes);
	}

	private static byte[] CryptProtect(byte[] bytes)
	{
		var input = CreateBlob(bytes);
		var entropy = CreateBlob(Entropy);
		var output = default(DATA_BLOB);
		try
		{
			if (!CryptProtectData(ref input, "Orbit account credential", ref entropy, nint.Zero, nint.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			return ReadBlob(output);
		}
		finally
		{
			FreeBlob(input);
			FreeBlob(entropy);
			FreeBlob(output, localFree: true);
		}
	}

	private static byte[] CryptUnprotect(byte[] bytes)
	{
		var input = CreateBlob(bytes);
		var entropy = CreateBlob(Entropy);
		var output = default(DATA_BLOB);
		try
		{
			if (!CryptUnprotectData(ref input, nint.Zero, ref entropy, nint.Zero, nint.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			return ReadBlob(output);
		}
		finally
		{
			FreeBlob(input);
			FreeBlob(entropy);
			FreeBlob(output, localFree: true);
		}
	}

	private static DATA_BLOB CreateBlob(byte[] bytes)
	{
		var blob = new DATA_BLOB { cbData = bytes.Length };
		blob.pbData = Marshal.AllocHGlobal(bytes.Length);
		Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
		return blob;
	}

	private static byte[] ReadBlob(DATA_BLOB blob)
	{
		if (blob.cbData <= 0 || blob.pbData == nint.Zero)
		{
			return Array.Empty<byte>();
		}

		var bytes = new byte[blob.cbData];
		Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
		return bytes;
	}

	private static void FreeBlob(DATA_BLOB blob, bool localFree = false)
	{
		if (blob.pbData == nint.Zero)
		{
			return;
		}

		if (localFree)
		{
			LocalFree(blob.pbData);
		}
		else
		{
			Marshal.FreeHGlobal(blob.pbData);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DATA_BLOB
	{
		public int cbData;
		public nint pbData;
	}

	[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptProtectData(
		ref DATA_BLOB pDataIn,
		string? szDataDescr,
		ref DATA_BLOB pOptionalEntropy,
		nint pvReserved,
		nint pPromptStruct,
		int dwFlags,
		out DATA_BLOB pDataOut);

	[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptUnprotectData(
		ref DATA_BLOB pDataIn,
		nint ppszDataDescr,
		ref DATA_BLOB pOptionalEntropy,
		nint pvReserved,
		nint pPromptStruct,
		int dwFlags,
		out DATA_BLOB pDataOut);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint hMem);
}
