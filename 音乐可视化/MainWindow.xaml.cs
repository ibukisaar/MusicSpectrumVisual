using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NAudio.Wave;

namespace 音乐可视化 {
	/// <summary>
	/// MainWindow.xaml 的交互逻辑
	/// </summary>
	public partial class MainWindow : CoolWindow {
		public MainWindow() {
			InitializeComponent();
			Loaded += delegate { WindowCorrection.CompatibilityMaximizedNoneWindow(this); };
		}
		
		private void CloseWindow_Executed(object sender, ExecutedRoutedEventArgs e) {
			SystemCommands.CloseWindow(this);
		}
	}
}
