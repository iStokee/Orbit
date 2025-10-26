using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;


namespace Orbit.ME
{

	public static class DllInjector
	{
		// Desired access flags for OpenProcess
		private const uint PROCESS_CREATE_THREAD = 0x0002;
		private const uint PROCESS_QUERY_INFORMATION = 0x0400;
		private const uint PROCESS_VM_OPERATION = 0x0008;
		private const uint PROCESS_VM_WRITE = 0x0020;
		private const uint PROCESS_VM_READ = 0x0010;

		// Memory allocation flags
		private const uint MEM_COMMIT = 0x1000;
		private const uint MEM_RESERVE = 0x2000;
		private const uint MEM_RELEASE = 0x8000;
		private const uint PAGE_READWRITE = 0x04;

		// Wait constants
		private const uint WAIT_OBJECT_0 = 0x00000000;
		private const uint WAIT_TIMEOUT = 0x00000102;
		private const uint WAIT_FAILED = 0xFFFFFFFF;

		[DllImport("kernel32", SetLastError = true)]
		static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

		[DllImport("kernel32", SetLastError = true)]
		static extern bool CloseHandle(IntPtr hHandle);

		[DllImport("kernel32", SetLastError = true)]
		static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
			uint dwSize, uint flAllocationType, uint flProtect);

		[DllImport("kernel32", SetLastError = true)]
		static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
			byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

		[DllImport("kernel32", SetLastError = true)]
		static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

		[DllImport("kernel32", SetLastError = true)]
		static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

		[DllImport("kernel32", SetLastError = true)]
		static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("kernel32", SetLastError = true)]
		static extern IntPtr CreateRemoteThread(IntPtr hProcess,
			IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress,
			IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

		[DllImport("kernel32", SetLastError = true)]
		static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

		[DllImport("kernel32", SetLastError = true)]
		static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

		/// <summary>
		/// Injects the specified DLL into the target process.
		/// </summary>
		/// <param name="processId">ID of the target process</param>
		/// <param name="dllPath">Full path to the DLL to inject</param>
		/// <returns>true on success</returns>
		public static bool Inject(int processId, string dllPath)
		{
			if (string.IsNullOrWhiteSpace(dllPath))
				throw new ArgumentException("DLL path must be provided.", nameof(dllPath));

			dllPath = Path.GetFullPath(dllPath);
			if (!File.Exists(dllPath))
				throw new FileNotFoundException("DLL not found", dllPath);

			// 1) Open the target process
			IntPtr hProc = OpenProcess(
				PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
				false, processId);
			if (hProc == IntPtr.Zero)
				throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");

			IntPtr allocAddress = IntPtr.Zero;
			IntPtr hThread = IntPtr.Zero;

			try
			{
				// 2) Allocate memory in the remote process for the DLL path
				byte[] dllPathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
				allocAddress = VirtualAllocEx(hProc, IntPtr.Zero, (uint)dllPathBytes.Length,
											  MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
				if (allocAddress == IntPtr.Zero)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed");

				// 3) Write the DLL path into the allocated memory
				if (!WriteProcessMemory(hProc, allocAddress, dllPathBytes, (uint)dllPathBytes.Length, out _))
					throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed");

				// 4) Get address of LoadLibraryW in kernel32.dll
				IntPtr hKernel32 = GetModuleHandle("kernel32.dll");
				if (hKernel32 == IntPtr.Zero)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandle failed");

				IntPtr loadLibraryAddr = GetProcAddress(hKernel32, "LoadLibraryW");
				if (loadLibraryAddr == IntPtr.Zero)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress failed");

				// 5) Create a remote thread that calls LoadLibraryW(dllPath)
				hThread = CreateRemoteThread(hProc, IntPtr.Zero, 0, loadLibraryAddr, allocAddress, 0, out _);
				if (hThread == IntPtr.Zero)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed");

				uint waitResult = WaitForSingleObject(hThread, 10_000);
				switch (waitResult)
				{
					case WAIT_OBJECT_0:
						break;
					case WAIT_TIMEOUT:
						throw new TimeoutException("Timed out waiting for remote LoadLibraryW to complete.");
					case WAIT_FAILED:
						throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed");
					default:
						throw new InvalidOperationException($"Unexpected WaitForSingleObject result: {waitResult}.");
				}

				if (!GetExitCodeThread(hThread, out uint exitCode))
					throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread failed");

				if (exitCode == 0)
					throw new InvalidOperationException("Remote LoadLibraryW returned NULL. Verify the DLL path and architecture.");

				return true;
			}
			finally
			{
				if (hThread != IntPtr.Zero)
					CloseHandle(hThread);

				if (allocAddress != IntPtr.Zero)
					VirtualFreeEx(hProc, allocAddress, 0, MEM_RELEASE);

				CloseHandle(hProc);
			}
		}

		/// <summary>
		/// Finds a process by name (first match) and injects the DLL.
		/// </summary>
		public static bool Inject(string processName, string dllPath)
		{
			var proc = Process.GetProcessesByName(processName);
			if (proc.Length == 0)
				throw new ArgumentException($"No process named '{processName}' is running.");

			return Inject(proc[0].Id, dllPath);
		}
	}

}
