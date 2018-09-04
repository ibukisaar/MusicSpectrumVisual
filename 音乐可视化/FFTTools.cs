using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace 音乐可视化 {
	unsafe public static class FFTTools {
		// 20 / ln(10)
		public const double LOG_2_DB = 8.6858896380650365530225783783321;

		// ln(10) / 20
		public const double DB_2_LOG = 0.11512925464970228420089957273422;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetComplexCount(int fftSize) => fftSize / 2 + 1;

		/// <summary>
		/// FFT结果取模
		/// </summary>
		/// <param name="fftSize">fft大小</param>
		/// <param name="src">输入缓冲区</param>
		/// <param name="dst">输出缓冲区</param>
		/// <param name="sqrt">true则进行sqrt运算</param>
		public static void Abs(int fftSize, ReadOnlySpan<Complex> src, Span<double> dst, bool sqrt = true, DSP.Window window = null) {
			src.Handle(dst, GetComplexCount(fftSize), (@in, @out, length) => {
				double scale0 = window != null ? window.Scale : 1.0 / fftSize;
				double scale = window != null ? 2 * window.Scale : 2.0 / fftSize;

				if (sqrt) {
					@out[0] = @in[0].Magnitude * scale0;
					for (int i = 1; i < length; i++) {
						@out[i] = @in[i].Magnitude * scale;
					}
				} else {
					@out[0] = (@in[0].Real * @in[0].Real + @in[0].Imaginary * @in[0].Imaginary) * scale0;
					for (int i = 1; i < length; i++) {
						@out[i] = (@in[i].Real * @in[i].Real + @in[i].Imaginary * @in[i].Imaginary) * scale;
					}
				}
			});
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double DB(double x, double maxDB) {
			// double r = FastMath.Log2(x) * (LOG_2_DB / FastMath.Log2_E) / maxDB + 1;
			double r = Math.Log(x) * LOG_2_DB / maxDB + 1;
			// double r = Math.Log(x * MAX_DB_CONST_VALUE) * LOG_2_DB;
			return r < 0 ? 0 : r;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void ToDB(Span<double> data, double maxDB = 60) {
			//for (int i = 0; i < data.Length; i++) {
			//	data[i] = DB(data[i], maxDB);
			//}

			double scale = LOG_2_DB / FastMath.Log2_E / maxDB;
			for (int i = 0; i < data.Length; i++) {
				data[i] = FastMath.Log2(data[i]) * scale + 1;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Scale(Span<double> data, double scale) {
			for (int i = 0; i < data.Length; i++) {
				data[i] *= scale;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double GetFrequency(int fftSize, int frequencyIndex, int sampleRate) {
			return (double)frequencyIndex / fftSize * sampleRate;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetFrequencyIndex(int fftSize, double frequency, int sampleRate) {
			return (int)Math.Round(frequency * fftSize / sampleRate);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int CutFrequencyLength(int fftSize, double minFrequency, double maxFrequency, int sampleRate, int maxLength) {
			int minIndex = Math.Max(GetFrequencyIndex(fftSize, minFrequency, sampleRate), 0);
			int maxIndex = Math.Min(GetFrequencyIndex(fftSize, maxFrequency, sampleRate) + 1, maxLength);
			return maxIndex - minIndex;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void CutFrequency<T>(int fftSize, double minFrequency, double maxFrequency, int sampleRate, ReadOnlySpan<T> src, Span<T> dst) {
			int minIndex = Math.Max(GetFrequencyIndex(fftSize, minFrequency, sampleRate), 0);
			int maxIndex = Math.Min(GetFrequencyIndex(fftSize, maxFrequency, sampleRate) + 1, src.Length);
			if (maxIndex > minIndex) src.Slice(minIndex, maxIndex - minIndex).CopyTo(dst);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static double Interpolation(ReadOnlySpan<double> data, double index) {
			if (index <= 0) return data[0];
			if (index >= data.Length - 1) return data[data.Length - 1];
			double dBegin = Math.Floor(index);
			double dEnd = Math.Ceiling(index);
			int iBegin = (int)dBegin, iEnd = (int)dEnd;
			return (data[iEnd] - data[iBegin]) * (index - dBegin) + data[iBegin];
		}

		private static double Max(ReadOnlySpan<double> data, double index1, double index2) {
			int ix1 = Math.Max((int)Math.Ceiling(index1), 0);
			int ix2 = Math.Min((int)Math.Floor(index2), data.Length - 1);

			double maxValue = Interpolation(data, index1);
			fixed (double* p = data) {
				for (int i = ix1; i <= ix2; i++) {
					if (p[i] > maxValue) maxValue = p[i];
				}
			}

			return Math.Max(maxValue, Interpolation(data, index2));
		}

		struct LogarithmCache {
			public ILogarithm Log;
			public int Width;
			public double[] Data;
			public double MinFrequency, MaxFrequency;
			public double MinFrequencyLog, MaxFrequencyLog;
		}

		static LogarithmCache logarithmCache;

		public static void Logarithm(ReadOnlySpan<double> src, double srcMinFrequency, double srcMaxFrequency, Span<double> dst, ILogarithm log) {
			src.Handle(dst, (ReadOnlySpan<double> @in, Span<double> @out) => {
				var srcWidth = @in.Length;
				var dstWidth = @out.Length;
				if (log != null) {
					var logChanged = logarithmCache.Log != log;
					if (logChanged) logarithmCache.Log = log;

					if (logChanged || logarithmCache.MinFrequency != srcMinFrequency || logarithmCache.MaxFrequency != srcMaxFrequency) {
						logarithmCache.MinFrequency = srcMinFrequency;
						logarithmCache.MaxFrequency = srcMaxFrequency;
						logarithmCache.MinFrequencyLog = log.Log(srcMinFrequency);
						logarithmCache.MaxFrequencyLog = log.Log(srcMaxFrequency);
					}

					if (logChanged || logarithmCache.Width != dstWidth) {
						double scale = (srcWidth - 1) / (srcMaxFrequency - srcMinFrequency);
						double minMel = logarithmCache.MinFrequencyLog;
						double maxMel = logarithmCache.MaxFrequencyLog;
						double mscale = (maxMel - minMel) / dstWidth;
						var cache = new double[dstWidth + 1];
						for (int i = 0; i <= dstWidth; i++) {
							cache[i] = (log.ILog(i * mscale + minMel) - srcMinFrequency) * scale;
						}
						logarithmCache.Width = dstWidth;
						logarithmCache.Data = cache;
					}

					fixed (double* cache = logarithmCache.Data) {
						for (int i = 0; i < dstWidth; i++) {
							@out[i] = Max(@in, cache[i], cache[i + 1]);
						}
					}
				} else {
					double scale = (double)(srcWidth - 1) / dstWidth;
					for (int i = 0; i < dstWidth; i++) {
						double x1 = i * scale;
						double x2 = x1 + scale;
						@out[i] = Max(@in, x1, x2);
					}
				}
			});
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static double AbsoluteThresholdOfHearing(double f) {
			f /= 1000;
			return 3.64 * Math.Pow(f, -0.8) - 6.5 * Math.Exp(-0.6 * (f - 3.3) * (f - 3.3)) + 0.001 * (f * f) * (f * f);
		}

		public static double[] GetAbsoluteThresholdOfHearing(int fftSize, int sampleRate) {
			int complexCount = GetComplexCount(fftSize);
			double[] result = new double[complexCount];
			result[0] = double.PositiveInfinity;
			for (int i = 1; i < complexCount; i++) {
				double f = GetFrequency(fftSize, i, sampleRate);
				result[i] = AbsoluteThresholdOfHearing(f);
			}
			return result;
		}

		public static void ToDBForAbsoluteThresholdOfHearing(ReadOnlySpan<double> thresholdOfHearing, Span<double> data, double maxDB) {
			for (int i = 0; i < data.Length; i++) {
				data[i] = Math.Max(Math.Log(data[i]) * LOG_2_DB - thresholdOfHearing[i] + maxDB, 0);
			}
		}
	}
}
