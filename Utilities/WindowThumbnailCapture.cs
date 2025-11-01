using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingSize = System.Drawing.Size;

namespace Orbit.Utilities
{
	/// <summary>
	/// Utility class for capturing window thumbnails using Win32 PrintWindow API
	/// </summary>
	public static class WindowThumbnailCapture
	{
		[DllImport("user32.dll")]
		private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

		[DllImport("user32.dll")]
		private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

		[DllImport("user32.dll")]
		private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr WindowFromPoint(POINT pt);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

		[DllImport("dwmapi.dll", PreserveSig = true)]
		private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

		private const uint GA_ROOT = 2;
		private const int DWMWA_CLOAKED = 14;

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		private const uint PW_CLIENTONLY = 0x00000001;
		private const uint PW_RENDERFULLCONTENT = 0x00000002;

		/// <summary>
		/// Captures a thumbnail of the specified window handle
		/// </summary>
		/// <param name="hWnd">Window handle to capture</param>
		/// <param name="maxWidth">Maximum width for thumbnail (maintains aspect ratio)</param>
		/// <param name="maxHeight">Maximum height for thumbnail (maintains aspect ratio)</param>
		/// <returns>BitmapSource thumbnail or null if capture fails</returns>
		public static BitmapSource CaptureWindow(IntPtr hWnd, int maxWidth = 320, int maxHeight = 240)
		{
			if (hWnd == IntPtr.Zero)
				return null;

			try
			{
				// Get the window's client area dimensions
				if (!GetClientRect(hWnd, out RECT rect))
					return null;

				int width = rect.Right - rect.Left;
				int height = rect.Bottom - rect.Top;

				if (width <= 0 || height <= 0)
					return null;

				// Create a bitmap to hold the window image
				using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				using var graphics = Graphics.FromImage(bitmap);

				IntPtr hdc = graphics.GetHdc();
				bool captured = false;
				try
				{
					// Capture the window content
					captured = PrintWindow(hWnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
				}
				finally
				{
					graphics.ReleaseHdc(hdc);
				}

				if (!captured)
				{
					// Fallback to a CopyFromScreen capture if PrintWindow fails (often happens with GPU surfaces)
					using var fallback = TryCaptureFallback(hWnd, width, height);
					if (fallback == null)
					{
						return null;
					}

					return ScaleAndConvert(fallback, maxWidth, maxHeight);
				}

				return ScaleAndConvert(bitmap, maxWidth, maxHeight);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to capture window thumbnail: {ex.Message}");
				return null;
			}
		}

		private static BitmapSource ScaleAndConvert(Bitmap source, int maxWidth, int maxHeight)
		{
			if (source == null)
				throw new ArgumentNullException(nameof(source));

			double scaleX = (double)maxWidth / source.Width;
			double scaleY = (double)maxHeight / source.Height;
			double scale = Math.Min(scaleX, scaleY);

			int thumbnailWidth = Math.Max(1, (int)(source.Width * scale));
			int thumbnailHeight = Math.Max(1, (int)(source.Height * scale));

			using var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, DrawingPixelFormat.Format32bppArgb);
			using var thumbGraphics = Graphics.FromImage(thumbnail);
			thumbGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
			thumbGraphics.DrawImage(source, 0, 0, thumbnailWidth, thumbnailHeight);

			return ConvertToBitmapSource(thumbnail);
		}

		private static Bitmap? TryCaptureFallback(IntPtr hWnd, int width, int height)
		{
			try
			{
				if (!IsWindowVisible(hWnd))
					return null;

				if (IsWindowCloaked(hWnd))
					return null;

				var clientOrigin = new POINT { X = 0, Y = 0 };
				if (!ClientToScreen(hWnd, ref clientOrigin))
					return null;

				if (width <= 0 || height <= 0)
					return null;

				var samplePoint = new POINT
				{
					X = clientOrigin.X + Math.Max(0, width / 2),
					Y = clientOrigin.Y + Math.Max(0, height / 2)
				};

				if (!PointBelongsToWindow(hWnd, samplePoint))
					return null;

				var bitmap = new Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);
				using var graphics = Graphics.FromImage(bitmap);
				graphics.CopyFromScreen(clientOrigin.X, clientOrigin.Y, 0, 0, new DrawingSize(width, height), CopyPixelOperation.SourceCopy);
				return bitmap;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Fallback thumbnail capture failed: {ex.Message}");
				return null;
			}
		}

		private static bool PointBelongsToWindow(IntPtr window, POINT screenPoint)
		{
			if (window == IntPtr.Zero)
				return false;

			var hit = WindowFromPoint(screenPoint);
			if (hit == IntPtr.Zero)
				return false;

			if (hit == window)
				return true;

			if (IsChild(window, hit))
				return true;

			return false;
		}

		private static bool IsWindowCloaked(IntPtr window)
		{
			if (window == IntPtr.Zero)
				return false;

			try
			{
				int cloaked;
				var hr = DwmGetWindowAttribute(window, DWMWA_CLOAKED, out cloaked, sizeof(int));
				return hr == 0 && cloaked != 0;
			}
			catch (DllNotFoundException)
			{
				return false;
			}
			catch (EntryPointNotFoundException)
			{
				return false;
			}

			return false;
		}

		/// <summary>
		/// Converts a System.Drawing.Bitmap to WPF BitmapSource
		/// </summary>
		private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
		{
			var hBitmap = bitmap.GetHbitmap();
			try
			{
				var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
					hBitmap,
					IntPtr.Zero,
					Int32Rect.Empty,
					BitmapSizeOptions.FromEmptyOptions());

				// Freeze for cross-thread access
				bitmapSource.Freeze();
				return bitmapSource;
			}
			finally
			{
				DeleteObject(hBitmap);
			}
		}

		[DllImport("gdi32.dll")]
		private static extern bool DeleteObject(IntPtr hObject);
	}
}
