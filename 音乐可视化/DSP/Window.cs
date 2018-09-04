using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化.DSP {
	public abstract class Window {
		protected double? scale = null;
		internal protected double[] window;

		public double Scale => scale ?? (scale = 1 / window.Sum()).Value;

		public Window(int fftSize) => window = new double[fftSize];

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		unsafe public void Apply(ReadOnlySpan<double> src, Span<double> dst) {
			src.Handle(dst, window.Length, (@in, @out, length) => {
				fixed (double* win = window) {
					for (int i = 0; i < length; i++) {
						@out[i] = @in[i] * win[i];
					}
				}
			});
		}
	}
}
