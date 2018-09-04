using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化.DSP {
	public sealed class Decade : ILogarithm {
		public static readonly Decade Default = new Decade();

		private Decade() { }

		public double ILog(double y) {
			return Math.Pow(10, y);
		}

		public double Log(double x) {
			return Math.Log10(x);
		}
	}
}
