using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Orbit.Classes;

namespace Orbit.ViewModels
{
	public class WindowManipulatorViewModel : BaseViewModel
	{
		#region Dll Imports
		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);
		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
		[DllImport("user32.dll", SetLastError = true)]
		internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
		private const int SW_MAXIMIZE = 3;
		private const int SW_MINIMIZE = 6;
		#endregion

		private double _windowWidth = 800;
		public double WindowWidth
		{
			get => _windowWidth;
			set => SetProperty(ref _windowWidth, value);
		}

		private double _windowHeight = 600;
		public double WindowHeight
		{
			get => _windowHeight;
			set => SetProperty(ref _windowHeight, value);
		}


		private IntPtr _windowHandle;
		public IntPtr WindowHandle
		{
			get => _windowHandle;
			set => SetProperty(ref _windowHandle, value);
		}

		private int _rs2ClientID;
		public int Rs2ClientID
		{
			get => _rs2ClientID;
			set => SetProperty(ref _rs2ClientID, value);
		}

		private int _runescapeProcessID;
		public int RunescapeProcessID
		{
			get => _runescapeProcessID;
			set => SetProperty(ref _runescapeProcessID, value);
		}

		private IntPtr _hWndDocked;
		public IntPtr HWndDocked
		{
			get => _hWndDocked;
			set => SetProperty(ref _hWndDocked, value);
		}

		private IntPtr _rsWindow;
		public IntPtr RsWindow
		{
			get => _rsWindow;
			set => SetProperty(ref _rsWindow, value);
		}

		public ICommand SetFocusCommand { get; }
		public ICommand MaximizeCommand { get; }
		public ICommand MinimizeCommand { get; }
		public ICommand RefreshDockingInfoCommand { get; }

		public WindowManipulatorViewModel()
		{
			SetFocusCommand = new RelayCommand(_ => SetFocus());
			MaximizeCommand = new RelayCommand(_ => MaximizeWindow());
			MinimizeCommand = new RelayCommand(_ => MinimizeWindow());
			RefreshDockingInfoCommand = new RelayCommand(_ => RefreshDockingInfo());
			RefreshDockingInfo();
		}

		private void SetFocus()
		{
			if (WindowHandle != IntPtr.Zero)
			{
				SetForegroundWindow(WindowHandle);
			}
		}

		private void MaximizeWindow()
		{
			if (WindowHandle != IntPtr.Zero)
			{
				ShowWindow(WindowHandle, SW_MAXIMIZE);
			}
		}

		private void MinimizeWindow()
		{
			if (WindowHandle != IntPtr.Zero)
			{
				ShowWindow(WindowHandle, SW_MINIMIZE);
			}
		}

		private void RefreshDockingInfo()
		{
			// Assuming RSForm.rs2client, RSForm.runescape, etc., are accessible as static members
			if (RSForm.rs2client != null)
			{
				Rs2ClientID = RSForm.rs2client.Id;
			}

			if (RSForm.runescape != null)
			{
				RunescapeProcessID = RSForm.runescape.Id;
			}

			HWndDocked = RSForm.hWndDocked;
			RsWindow = RSForm.rsWindow;
			WindowHandle = RSForm.hWndDocked;
		}

		private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (RSForm.rs2client != null)
			{
				MoveWindow(HWndDocked, 0, 0, (int)WindowWidth, (int)WindowHeight, true);
			}
		}

		private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (RSForm.rs2client != null)
			{
				MoveWindow(HWndDocked, 0, 0, (int)WindowWidth, (int)WindowHeight, true);
			}
		}
	}
}
