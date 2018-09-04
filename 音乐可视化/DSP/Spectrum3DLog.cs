using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化.DSP {
	public class Spectrum3DLog : ILogarithm {
		const double CutOffFrequency = 440;

		public double ILog(double y) {
			return CutOffFrequency * (Math.Exp(y / 1127) - 1);
		}

		public double Log(double x) {
			return 1127 * Math.Log(1 + x / CutOffFrequency);
		}
	}
}
