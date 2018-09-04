using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化.DSP {
	public sealed class Logarithm2 : ILogarithm {
		static readonly double Log2 = Math.Log(2);

		public double ILog(double y) {
			return Math.Pow(2, y) - 1;
		}

		public double Log(double x) {
			return Math.Log(x + 1) / Log2;
		}
	}
}
