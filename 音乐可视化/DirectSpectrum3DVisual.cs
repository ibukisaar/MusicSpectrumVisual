using Saar.WPF.Ex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace 音乐可视化 {
	unsafe public class DirectSpectrum3DVisual : WaveCaptureDrawing {
		static readonly int ScreenWidth = (int)SystemParameters.PrimaryScreenWidth;
		static readonly int ScreenHeight = (int)SystemParameters.PrimaryScreenHeight;

		class Cache {
			private float[] data;

			public int Count { get; private set; }
			public int Width { get; }
			public int Height { get; private set; }

			public Cache(int width, int height) {
				Width = width;
				Height = height;
				data = new float[width * height];
			}

			public void Resize(int newHeight) {
				if (newHeight == Height || newHeight <= 0) return;
				var newData = new float[Width * newHeight];
				if (newHeight < Height && newHeight <= Count) {
					data.AsSpan(Width * (Height - newHeight)).CopyTo(newData);
				} else {
					data.AsSpan(Width * (Height - Count)).CopyTo(newData.AsSpan(Width * (newHeight - Count)));
				}
				data = newData;
				Height = newHeight;
				Count = Math.Min(Count, Height);
			}

			public void Add(ReadOnlySpan<float> data)
				=> Add(data, 1);

			public void Add(ReadOnlySpan<float> data, int addCount) {
				if (Width * addCount != data.Length) throw new ArgumentException();
				if (addCount < Height) {
					this.data.AsSpan(Width * addCount).CopyTo(this.data);
					data.CopyTo(this.data.AsSpan((Height - addCount) * Width));
				} else {
					data.Slice((addCount - Height) * Width).CopyTo(this.data);
				}
				Count = Math.Min(Count + addCount, Height);
			}

			public ref float GetPinnableReference() => ref data[Width * (Height - Count)];

			public static implicit operator Span<float>(Cache @this) => @this.data.AsSpan(@this.Width * (@this.Height - @this.Count));
			public static implicit operator ReadOnlySpan<float>(Cache @this) => @this.data.AsSpan(@this.Width * (@this.Height - @this.Count));
		}

		static DirectSpectrum3DVisual() {
			//MinimumFrequencyProperty.Override(new ResetMetadata(100d));
			//MaxDBProperty.Override(new ResetMetadata(120d));
		}

		private WriteableBitmap bitmap;
		private Spectrum3DCompute compute;
		private FixedQueue<float[]> cache;
		private DateTime fpsTimer = DateTime.Now;
		private int frames = 0;
		private double prevFPS = 0;
		private TextBlock gpuLabel;

		public int DataWidth => FFTTools.GetComplexCount(FFTSize) * 2 * 2;

		public DirectSpectrum3DVisual() : base(false, true) {
			Loaded += delegate {
				gpuLabel = FindName("gpuLabel") as TextBlock;
			};
		}

		static void ComplexToFloat(ReadOnlySpan<Complex> src, Span<float> dst) {
			fixed (float* @out = dst) {
				for (int i = 0; i < src.Length; i++) {
					@out[2 * i] = (float)src[i].Real;
					@out[2 * i + 1] = (float)src[i].Imaginary;
				}
			}
		}

		protected override void DrawSpectrum(DrawingContext dc, (double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)[] drawData) {
			if (bitmap == null) return;

			var sw = new System.Diagnostics.Stopwatch();
			sw.Restart();

			int dataWidth = DataWidth;

			float[][] currDrawData = new float[drawData.Length][];
			for (int i = 0; i < drawData.Length; i++) {
				var (_, _, left, right) = drawData[i];
				float[] tempBuffer = new float[(left.Length + right.Length) * 2];
				ComplexToFloat(right, tempBuffer.AsSpan());
				ComplexToFloat(left, tempBuffer.AsSpan(right.Length * 2));
				currDrawData[i] = tempBuffer;
				cache.Add(tempBuffer);
			}

			int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
			uint* p = (uint*)bitmap.BackBuffer.ToPointer();
			bitmap.Lock();

			new ReadOnlySpan<uint>(p + drawData.Length, w * h - drawData.Length).CopyTo(new Span<uint>(p, w * h - drawData.Length));
			int xOffset = Math.Max(0, w - drawData.Length);
			UpdateBitmap(p, xOffset, currDrawData);

			bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
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

			if (gpuLabel != null) {
				gpuLabel.Text = $"GPU -> FPS: {prevFPS:00.0}, 渲染耗时: {sw.Elapsed.TotalMilliseconds:000.00} ms";
			}
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
			base.OnRenderSizeChanged(sizeInfo);

			if (designMode) return;

			int w = (int)RenderSize.Width;
			int h = (int)RenderSize.Height;
			bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

			if (compute == null) {
				var scale = FFTWindow != null ? FFTWindow.Scale : 1.0 / FFTSize;
				compute = new Spectrum3DCompute(FFTSize, Capture.WaveFormat.SampleRate, h, (float)MinimumFrequency, (float)MaximumFrequency, (float)MaxDB, (float)scale);
			} else {
				compute.OutWidth = h;
			}
			if (cache == null) {
				cache = new FixedQueue<float[]>(w);
			} else {
				cache.Resize(w);
			}

			UpdateBitmap(w - cache.Count, cache.ToArray());
		}

		unsafe private void UpdateBitmap(int x, ReadOnlyMemory<float[]> data) {
			int dataWidth = DataWidth;
			int w = bitmap.PixelWidth, h = bitmap.PixelHeight;

			uint* p = (uint*)bitmap.BackBuffer.ToPointer();
			bitmap.Lock();

			UpdateBitmap(p, x, data);

			bitmap.AddDirtyRect(new Int32Rect(x, 0, w - x, h));
			bitmap.Unlock();
		}

		unsafe private void UpdateBitmap(uint* pixels, int x, ReadOnlyMemory<float[]> data) {
			int dataWidth = DataWidth;
			int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
			int count = w - x;

			for (int i = 0; i < count; i += Spectrum3DCompute.MaxWidth) {
				int width = Math.Min(count - i, Spectrum3DCompute.MaxWidth);
				UpdateBitmap(pixels, x + i, width, data.Slice(i, width));
			}
		}

		unsafe private void UpdateBitmap(uint* pixels, int x, int width, ReadOnlyMemory<float[]> data) {
			int dataWidth = DataWidth;
			int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
			if (width > w - x) throw new ArgumentOutOfRangeException();
			if (width != data.Length) throw new ArgumentException();

			using (var writer = compute.Write()) {
				var dstWidth = writer.Width;
				for (int i = 0; i < data.Length; i++) {
					data.Span[i].AsSpan(0, dataWidth / 2).CopyTo(new Span<float>((float*)writer.Data + (2 * i) * dstWidth, dataWidth / 2));
					data.Span[i].AsSpan(dataWidth / 2).CopyTo(new Span<float>((float*)writer.Data + (2 * i + 1) * dstWidth, dataWidth / 2));
				}
			}
			compute.Run(width);

			using (var reader = compute.Read()) {
				uint* @out = (uint*)reader.Data;
				var srcWidth = reader.Width;
				for (int i = 0; i < width; i++) {
					uint* pxOut = pixels + x + i;
					for (int y = h - 1; y >= 0; y--, pxOut += w) {
						*pxOut = @out[i * srcWidth + y];
					}
				}
			}
		}
	}
}
