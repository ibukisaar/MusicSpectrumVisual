using Saar.WPF.Ex;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace 音乐可视化 {
	unsafe public class Spectrum3DVisual : WaveCaptureDrawing {
		static readonly int ScreenWidth = (int)SystemParameters.PrimaryScreenWidth;
		static readonly int ScreenHeight = (int)SystemParameters.PrimaryScreenHeight;

		static readonly (double Alpha, double R, double G, double B)[] IntensityColorTable = {
			(0.00, 0.000, 0.000, 0.000),
			(0.13, 0.000, 0.000, 0.315),
			(0.30, 0.431, 0.000, 0.500),
			(0.61, 0.943, 0.000, 0.000),
			(0.73, 1.000, 0.612, 0.000),
			(0.78, 1.000, 0.791, 0.000),
			(0.91, 1.000, 1.000, 0.591),
			(1.00, 1.000, 1.000, 1.000),
		};
		const int ColorN = 65536;
		static readonly uint[] ColorTable = new uint[ColorN];

		static uint Double3ToColor(double r, double g, double b) {
			if (r < 0 || g < 0 || b < 0 || r > 1 || g > 1 || b > 1) throw null;
			return (uint)(((byte)Math.Round(r * 255) << 16) | ((byte)Math.Round(g * 255) << 8) | (byte)Math.Round(b * 255)) | 0xff000000u;
		}

		static uint ToColor(double a, (double Alpha, double R, double G, double B)[] table) {
			if (a <= table[0].Alpha) return Double3ToColor(table[0].R, table[0].G, table[0].B);
			for (int i = 1; i < table.Length; i++) {
				if (table[i].Alpha >= a) {
					var start = table[i - 1].Alpha;
					var end = table[i].Alpha;
					var lerpfrac = (a - start) / (end - start);
					var r = table[i - 1].R * (1 - lerpfrac) + table[i].R * lerpfrac;
					var g = table[i - 1].G * (1 - lerpfrac) + table[i].G * lerpfrac;
					var b = table[i - 1].B * (1 - lerpfrac) + table[i].B * lerpfrac;
					return Double3ToColor(r, g, b);
				}
			}
			var last = table.Length - 1;
			return Double3ToColor(table[last].R, table[last].G, table[last].B);
		}

		static Spectrum3DVisual() {
			for (int i = 0; i < ColorN; i++) {
				ColorTable[i] = ToColor(i / (double)(ColorN - 1), IntensityColorTable);
			}
		}

		private WriteableBitmap bitmap;
		private ILogarithm logarithm = DSP.Decade.Default;
		private FixedQueue<(double[] Left, double[] Right)> cache;
		private double[] absoluteThresholdOfHearing;
		private DateTime fpsTimer = DateTime.Now;
		private int frames = 0;
		private double prevFPS = 0;
		private TextBlock cpuLabel;

		public Spectrum3DVisual() : base(false, true) {
			Loaded += delegate {
				absoluteThresholdOfHearing = FFTTools.GetAbsoluteThresholdOfHearing(FFTSize, Capture.WaveFormat.SampleRate);
				cpuLabel = FindName("cpuLabel") as TextBlock;
			};
		}

		unsafe protected override void DrawSpectrum(DrawingContext dc, (double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)[] drawData) {
			if (bitmap == null) return;

			var sw = new System.Diagnostics.Stopwatch();
			sw.Restart();

			var capture = Capture;
			var fftSize = FFTSize;
			var fftComplexCount = FFTTools.GetComplexCount(fftSize);
			var sampleRate = capture.WaveFormat.SampleRate;
			int cutFrequencyLength = FFTTools.CutFrequencyLength(fftSize, MinimumFrequency, MaximumFrequency, sampleRate, fftComplexCount);
			var currDrawData = new (double[] Left, double[] Right)[drawData.Length];
			for (int i = 0; i < drawData.Length; i++) {
				var (_, _, left, right) = drawData[i];
				Span<double> tempMagnitudes = stackalloc double[fftComplexCount];
				double[] newLeft = new double[cutFrequencyLength], newRight = new double[cutFrequencyLength];

				FFTTools.Abs(fftSize, left, tempMagnitudes, true, FFTWindow);
				//FFTTools.ToDBForAbsoluteThresholdOfHearing(absoluteThresholdOfHearing, tempMagnitudes, MaxDB);
				FFTTools.CutFrequency<double>(fftSize, MinimumFrequency, MaximumFrequency, sampleRate, tempMagnitudes, newLeft);
				FFTTools.ToDB(newLeft, MaxDB);
				//FFTTools.Scale(newLeft, 1 / MaxDB);

				FFTTools.Abs(fftSize, right, tempMagnitudes, true, FFTWindow);
				//FFTTools.ToDBForAbsoluteThresholdOfHearing(absoluteThresholdOfHearing, tempMagnitudes, MaxDB);
				FFTTools.CutFrequency<double>(fftSize, MinimumFrequency, MaximumFrequency, sampleRate, tempMagnitudes, newRight);
				FFTTools.ToDB(newRight, MaxDB);
				//FFTTools.Scale(newRight, 1 / MaxDB);

				cache.Add((newLeft, newRight));
				currDrawData[i] = (newLeft, newRight);
			}

			int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
			uint* p = (uint*)bitmap.BackBuffer.ToPointer();
			bitmap.Lock();

			new ReadOnlySpan<uint>(p + currDrawData.Length, w * h - currDrawData.Length).CopyTo(new Span<uint>(p, w * h));
			int xOffset = w - currDrawData.Length;
			UpdateBitmap(p, xOffset, currDrawData);

			bitmap.AddDirtyRect(new Int32Rect(w - cache.Count, 0, cache.Count, h));
			bitmap.Unlock();

			dc.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));

			sw.Stop();
			frames++;

			var now = DateTime.Now;
			if (now - fpsTimer > TimeSpan.FromSeconds(0.5)) {
				prevFPS = frames / (now - fpsTimer).TotalSeconds;
				fpsTimer = now;
				frames = 0;
			}

			if (cpuLabel != null) {
				cpuLabel.Text = $"CPU -> FPS: {prevFPS:00.0}, 渲染耗时: {sw.Elapsed.TotalMilliseconds:000.00} ms";
			}
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
			base.OnRenderSizeChanged(sizeInfo);

			if (designMode) return;

			int w = (int)RenderSize.Width;
			int h = (int)RenderSize.Height;
			bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
			if (cache == null) {
				cache = new FixedQueue<(double[] Left, double[] Right)>(w);
			} else {
				cache.Resize(w);
			}

			uint* p = (uint*)bitmap.BackBuffer.ToPointer();
			bitmap.Lock();
			UpdateBitmap(p, w - cache.Count, cache);
			bitmap.AddDirtyRect(new Int32Rect(w - cache.Count, 0, cache.Count, h));
			bitmap.Unlock();
		}

		private void UpdateBitmap(uint* pixels, int x, IReadOnlyCollection<(double[] Left, double[] Right)> data) {
			int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
			int h2 = h / 2;

			double* halfHeightData = stackalloc double[h];
			double minFrequency = MinimumFrequency, maxFrequency = MaximumFrequency;

			fixed (uint* colorTable = ColorTable) {
				int i = x;
				foreach (var (left, right) in data) {
					if (i >= w) break;
					uint* px = pixels + i;
					FFTTools.Logarithm(right, minFrequency, maxFrequency, new Span<double>(halfHeightData, h2), logarithm);
					FFTTools.Logarithm(left, minFrequency, maxFrequency, new Span<double>(halfHeightData + h2, h - h2), logarithm);
					for (int y = h - 1; y >= 0; y--, px += w) {
						//int b = (int)(halfHeightData[y] * 256);
						//if (b < 0) b = 0; else if (b > 255) b = 255;
						//*px = 0xFFFFFFU | (uint)(b << 24);
						int pixel = (int)(halfHeightData[y] * ColorN);
						if (pixel < 0) pixel = 0; else if (pixel >= ColorN) pixel = ColorN - 1;
						*px = colorTable[pixel];
					}
					i++;
				}
			}
		}
	}
}
