using Orbit.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Orbit.Views
{
	/// <summary>
	/// Interaction logic for WindowManipulatorView.xaml
	/// </summary>
	public partial class WindowManipulatorView : MahApps.Metro.Controls.MetroWindow
	{
		[DllImport("user32.dll", SetLastError = true)]
		internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		private double _windowWidth = 800;
		public double WindowWidth
		{
			get => _windowWidth;
			set => SetProperty(ref _windowWidth, value);
		}

		private void SetProperty(ref double windowWidth, double value)
		{
			throw new NotImplementedException();
		}

		private double _windowHeight = 600;
		public double WindowHeight
		{
			get => _windowHeight;
			set => SetProperty(ref _windowHeight, value);
		}


		public WindowManipulatorView()
		{
			InitializeComponent();
			this.DataContext = new WindowManipulatorViewModel();
		}

		private void MinimizeWindow(object sender, RoutedEventArgs e)
		{
			// Implement the logic to minimize the window here
			this.WindowState = WindowState.Minimized;
		}

		private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			// If the left mouse button is pressed, don't proceed with resize
			if (System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed) return;

			if (RSForm.rs2client != null)
			{
				if (sliderHeight != null && sliderWidth != null)
				{
					MoveWindow(RSForm.hWndDocked, 0, 0, (int)sliderWidth.Value, (int)sliderHeight.Value, true);
				}
			}
		}

		private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{           // If the left mouse button is pressed, don't proceed with resize
			if (System.Windows.Input.Mouse.LeftButton == MouseButtonState.Pressed) return;

			if (RSForm.rs2client != null)
			{
				if (sliderHeight != null && sliderWidth != null)
				{
					MoveWindow(RSForm.hWndDocked, 0, 0, (int)sliderWidth.Value, (int)sliderHeight.Value, true);
				}
			}
		}
	}
}
