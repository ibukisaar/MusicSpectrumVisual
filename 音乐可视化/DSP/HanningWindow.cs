using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化.DSP {
	public sealed class HanningWindow : Window {
		public HanningWindow(int fftSize) : base(fftSize) {
			double a = 2 * Math.PI / (fftSize - 1);
			for (int i = 0; i < fftSize; i++) {
				window[i] = 0.5 * (1 - Math.Cos(a * i));
			}
		}
	}
}
