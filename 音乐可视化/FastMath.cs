using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化 {
	unsafe public static class FastMath {
		const int LogTableLevel = 14;
		static readonly double[] LogTable = new double[1 << LogTableLevel];

		public const double Log2_E = 1.4426950408889634073599246810018921374;
		public const double Log2_10 = 3.3219280948873623478703194294893901758;

		static FastMath() {
			const int N = 1 << LogTableLevel;
			for (int i = 0; i < N; i++) {
				LogTable[i] = Math.Log(1 + (double)i / N, 2);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Log2(double x) {
			const int N = 1 << LogTableLevel;
			ulong t = *(ulong*)&x;
			int exp = (int)(t >> 52) - 0x3ff;
			return LogTable[(t >> (52 - LogTableLevel)) & (N - 1)] + exp;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Log(double x) => Log2(x) / Log2_E;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Log10(double x) => Log2(x) / Log2_10;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Sqrt(double x) {
			long t = *(long*)&x;
			t = 0x1ff7a3be9bb1a200L + (t >> 1);
			var a = *(double*)&t;
			return (a + x / a) / 2;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Sqrt(float x) {
			int t = *(int*)&x;
			t = 0x1fbd1df4 + (t >> 1);
			var a = *(float*)&t;
			return (a + x / a) / 2;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Pow(double x, double y) => Math.Exp(y * Log2(x) / Log2_E);
	}
}
