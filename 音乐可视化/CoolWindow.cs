using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using NAudio.Wave;
using Saar.WPF.Ex;

namespace 音乐可视化 {
	[TemplatePart(Name = PART_TitleBarName, Type = typeof(Border))]
	[TemplatePart(Name = PART_CloseWindowButtonName, Type = typeof(Button))]
	[TemplatePart(Name = PART_ToggleWindowButtonName, Type = typeof(Button))]
	[TemplatePart(Name = PART_MinimizeWindowButton, Type = typeof(Button))]
	public class CoolWindow : Window {
		const string PART_TitleBarName = "PART_TitleBar";
		const string PART_CloseWindowButtonName = "PART_CloseWindowButton";
		const string PART_ToggleWindowButtonName = "PART_ToggleWindowButton";
		const string PART_MinimizeWindowButton = "PART_MinimizeWindowButton";

		const int WM_SYSCOMMAND = 0x0112;
		const int SC_MOUSEMOVE = 0xf012;
		const int SC_MOUSEMENU = 0xf090;
		const uint TPM_RETURNCMD = 0x0100;
		const uint TPM_LEFTBUTTON = 0x0;

		[DllImport("user32")]
		static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

		[DllImport("user32")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

		[DllImport("user32")]
		static extern IntPtr GetSystemMenu(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

		[DllImport("user32")]
		static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

		[DllImport("user32")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool IsWindow(IntPtr hwnd);

		private static readonly DependencyPropertyKey IsShowTitlePropertyKey = DP.RegisterReadOnly(new PropertyMetadata(true));
		public static readonly DependencyProperty IsShowTitleProperty = IsShowTitlePropertyKey.DependencyProperty;

		public bool IsShowTitle {
			get => this.GetValue<bool>(IsShowTitleProperty);
			private set => SetValue(IsShowTitlePropertyKey, value);
		}

		static CoolWindow() {
			// DefaultStyleKeyProperty.OverrideMetadata(typeof(CoolWindow), new FrameworkPropertyMetadata(typeof(CoolWindow)));
		}

		private static Geometry GetMinimizeWindowGeometry() {
			var geometry = new RectangleGeometry(new Rect(0, 5, 9, 3));
			geometry.Freeze();
			return geometry;
		}

		private static Geometry GetMaximizeWindowGeometry() {
			var bigRect = new RectangleGeometry(new Rect(0, 0, 9, 9));
			var smallRect = new RectangleGeometry(new Rect(1, 3, 7, 5));
			var geometry = Geometry.Combine(bigRect, smallRect, GeometryCombineMode.Exclude, null);
			geometry.Freeze();
			return geometry;
		}

		private static Geometry GetRestoreWindowGeometry() {
			var bigRect = new RectangleGeometry(new Rect(3, 0, 7, 7));
			var smallRect = new RectangleGeometry(new Rect(4, 2, 5, 4));
			var geometryBack = Geometry.Combine(bigRect, smallRect, GeometryCombineMode.Exclude, null);
			bigRect = new RectangleGeometry(new Rect(0, 3, 7, 7));
			smallRect = new RectangleGeometry(new Rect(1, 5, 5, 4));
			geometryBack = Geometry.Combine(geometryBack, bigRect, GeometryCombineMode.Exclude, null);
			var geometryFore = Geometry.Combine(bigRect, smallRect, GeometryCombineMode.Exclude, null);
			var result = Geometry.Combine(geometryFore, geometryBack, GeometryCombineMode.Union, null);
			result.Freeze();
			return result;
		}

		private static Geometry GetCloseWindowGeometry() {
			const double Width = 11;
			const double Range = 2;
			var geometry = new GeometryGroup();
			geometry.Children.Add(new RectangleGeometry(new Rect(0, (Width - Range) / 2, Width, Range)));
			geometry.Children.Add(new RectangleGeometry(new Rect((Width - Range) / 2, 0, Range, Width)));
			geometry.Transform = new RotateTransform(45, Width / 2, Width / 2);
			geometry.FillRule = FillRule.Nonzero;
			geometry.Freeze();
			return geometry;
		}


		public static Geometry MinimizeWindowGeometry { get; } = GetMinimizeWindowGeometry();
		public static Geometry MaximizeWindowGeometry { get; } = GetMaximizeWindowGeometry();
		public static Geometry RestoreWindowGeometry { get; } = GetRestoreWindowGeometry();
		public static Geometry CloseWindowGeometry { get; } = GetCloseWindowGeometry();


		private Border titleBar;

		public CoolWindow() {
			CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, CloseWindow));
			CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, MinimizeWindow));
			CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, MaximizeWindow));
			CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, RestoreWindow));
		}

		public override void OnApplyTemplate() {
			base.OnApplyTemplate();

			if (GetTemplateChild(PART_TitleBarName) is Border titleBar) {
				this.titleBar = titleBar;
				titleBar.PreviewMouseRightButtonUp += TitleBar_PreviewMouseRightButtonUp;
			}
		}

		private void TitleBar_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
			ShowMenu();
			e.Handled = true;
		}

		private void CloseWindow(object sender, ExecutedRoutedEventArgs e) {
			SystemCommands.CloseWindow(this);
		}

		private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e) {
			SystemCommands.MinimizeWindow(this);
		}

		private void MaximizeWindow(object sender, ExecutedRoutedEventArgs e) {
			SystemCommands.MaximizeWindow(this);
		}

		private void RestoreWindow(object sender, ExecutedRoutedEventArgs e) {
			SystemCommands.RestoreWindow(this);
		}

		public void DragWindow() {
			var wiHelper = new WindowInteropHelper(this);
			IntPtr hwnd = wiHelper.Handle;
			if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
			SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_MOUSEMOVE, IntPtr.Zero);
		}

		public void ShowMenu() {
			var wiHelper = new WindowInteropHelper(this);
			IntPtr hwnd = wiHelper.Handle;
			if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
			IntPtr hmenu = GetSystemMenu(hwnd, false);
			var point = PointToScreen(Mouse.GetPosition(this));
			var cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_LEFTBUTTON, (int)point.X, (int)point.Y, hwnd, IntPtr.Zero);
			if (cmd != 0) {
				PostMessage(hwnd, WM_SYSCOMMAND, (IntPtr)cmd, IntPtr.Zero);
			}
		}

		protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
			base.OnMouseLeftButtonDown(e);
			DragWindow();
		}

		protected override void OnPreviewMouseMove(MouseEventArgs e) {
			base.OnPreviewMouseMove(e);

			//IsShowTitle = false;
			//if (WindowChrome.GetWindowChrome(this) is WindowChrome windowChrome && titleBar != null) {
			//	var titleHeight = windowChrome.CaptionHeight;
			//	if (e.GetPosition(this).Y <= titleBar.ActualHeight * 2) {
			//		IsShowTitle = true;
			//	}
			//}
		}

		protected override void OnMouseLeave(MouseEventArgs e) {
			base.OnMouseLeave(e);
			//IsShowTitle = false;
		}
	}
}
