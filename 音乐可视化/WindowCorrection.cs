using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace 音乐可视化 {
	public static class WindowCorrection {
		[DllImport("user32")]
		internal static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);
		[DllImport("user32")]
		internal static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
		public class MONITORINFO {
			public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
			public RECT rcMonitor = new RECT();
			public RECT rcWork = new RECT();
			public int dwFlags = 0;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT {
			public int left;
			public int top;
			public int right;
			public int bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT {
			public int x;
			public int y;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct MINMAXINFO {
			public POINT ptReserved;
			public POINT ptMaxSize;
			public POINT ptMaxPosition;
			public POINT ptMinTrackSize;
			public POINT ptMaxTrackSize;
		}

		public static void CompatibilityMaximizedNoneWindow(Window window) {
			var wiHelper = new WindowInteropHelper(window);
			IntPtr handle = wiHelper.Handle;
			HwndSource.FromHwnd(handle).AddHook(CompatibilityMaximizedNoneWindowProc);
		}

		private static IntPtr CompatibilityMaximizedNoneWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
			switch (msg) {
				case 0x0024:    // WM_GETMINMAXINFO
					MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

					// Adjust the maximized size and position
					// to fit the work area of the correct monitor
					// int MONITOR_DEFAULTTONEAREST = 0x00000002;
					var monitor = MonitorFromWindow(hwnd, 0x00000002);
					if (monitor != IntPtr.Zero) {
						MONITORINFO monitorInfo = new MONITORINFO();
						GetMonitorInfo(monitor, monitorInfo);
						RECT rcWorkArea = monitorInfo.rcWork;
						RECT rcMonitorArea = monitorInfo.rcMonitor;
						mmi.ptMaxPosition.x =
							  Math.Abs(rcWorkArea.left - rcMonitorArea.left);
						mmi.ptMaxPosition.y =
							  Math.Abs(rcWorkArea.top - rcMonitorArea.top);
						mmi.ptMaxSize.x =
							  Math.Abs(rcWorkArea.right - rcWorkArea.left);
						mmi.ptMaxSize.y =
							  Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
					}
					Marshal.StructureToPtr(mmi, lParam, true);
					handled = true;
					break;
			}
			return IntPtr.Zero;
		}
	}
}
