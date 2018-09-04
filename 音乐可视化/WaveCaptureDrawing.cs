using NAudio.Wave;
using Saar.WPF.Ex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace 音乐可视化 {
	public abstract class WaveCaptureDrawing : FrameworkElement {
		//static SpectrumDrawing() {
		//	DropShadowEffect effect = new DropShadowEffect {
		//		Opacity = 1,
		//		Color = Colors.White,
		//		BlurRadius = 10,
		//		RenderingBias = RenderingBias.Quality,
		//		ShadowDepth = 0
		//	};
		//	effect.Freeze();
		//	EffectProperty.OverrideMetadata(typeof(SpectrumDrawing), new UIPropertyMetadata(effect));
		//}

		private class SharedStreamObserver : IDisposable {
			public delegate void FFTCompletedHandler(ReadOnlySpan<double> leftWave, ReadOnlySpan<double> rightWave, ReadOnlySpan<Complex> leftFFT, ReadOnlySpan<Complex> rightFFT);

			private IWaveIn capture;
			private FFT fft;
			private DSP.Window fftWindow;
			private StreamObserver<float> streamObserver;
			private Complex[] leftFFTData, rightFFTData;

			public event FFTCompletedHandler FFTCompleted;

			public int RefCount { get; set; }

			public SharedStreamObserver(IWaveIn capture, int fftSize, double moveSpeed, DSP.Window fftWindow) {
				this.capture = capture;
				this.fftWindow = fftWindow;
				fft = FFT.CreateFFT(fftSize);
				leftFFTData = new Complex[fft.FFTComplexCount];
				rightFFTData = new Complex[fft.FFTComplexCount];
				streamObserver = new StreamObserver<float>(fftSize, (int)(fftSize / moveSpeed), 2, true);
				streamObserver.Completed += StreamObserver_Completed;
				capture.DataAvailable += Capture_DataAvailable;
			}

			unsafe static void ReadSingleChannel(ReadOnlySpan<float> src, Span<double> dst) {
				fixed (float* @in = src) { // 禁止下标越界检查
					for (int i = 0; i < dst.Length; i++) {
						dst[i] = @in[i * 2];
					}
				}
			}

			private void StreamObserver_Completed(float[] data) {
				Span<double> left = stackalloc double[fft.FFTSize];
				Span<double> right = stackalloc double[fft.FFTSize];

				ReadSingleChannel(data, left);
				if (fftWindow != null) {
					Span<double> temp = stackalloc double[fft.FFTSize];
					fftWindow.Apply(left, temp);
					fft.WriteRealToInput(temp);
				} else {
					fft.WriteRealToInput(left);
				}
				fft.Execute();
				fft.ReadOutput<Complex>(leftFFTData);

				ReadSingleChannel(data.AsSpan(1), right);
				if (fftWindow != null) {
					Span<double> temp = stackalloc double[fft.FFTSize];
					fftWindow.Apply(right, temp);
					fft.WriteRealToInput(temp);
				} else {
					fft.WriteRealToInput(right);
				}
				fft.Execute();
				fft.ReadOutput<Complex>(rightFFTData);

				FFTCompleted?.Invoke(left, right, leftFFTData, rightFFTData);
			}

			private void Capture_DataAvailable(object sender, WaveInEventArgs e) {
				ReadOnlySpan<float> data = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
				streamObserver.Write(data);
			}

			public void Dispose() {
				capture.DataAvailable -= Capture_DataAvailable;
				fft.Dispose();
			}
		}

		protected class ResetMetadata : PropertyMetadata {
			public ResetMetadata() : base() { }
			public ResetMetadata(object defaultValue) : base(defaultValue) { }
			public ResetMetadata(PropertyChangedCallback callback) : base(callback) { }
			public ResetMetadata(object defaultValue, PropertyChangedCallback callback) : base(defaultValue, callback) { }
			public ResetMetadata(object defaultValue, PropertyChangedCallback callback, CoerceValueCallback coerceValueCallback) : base(defaultValue, callback, coerceValueCallback) { }
		}

		private static readonly Dictionary<(IWaveIn Capture, int FFTSize, double MoveSpeed, DSP.Window FFTWindow), SharedStreamObserver> SharedStreamObservers = new Dictionary<(IWaveIn Capture, int FFTSize, double MoveSpeed, DSP.Window FFTWindow), SharedStreamObserver>();
		private static readonly LoopbackCapture DefaultCapture = new LoopbackCapture(20);
		private const int DefaultFFTSize = 8192;

		public static readonly DependencyProperty WaveLengthProperty = DP.Register(new PropertyMetadata(0, WaveLengthChanged));
		public static readonly DependencyProperty FFTSizeProperty = DP.Register(new ResetMetadata(DefaultFFTSize, CaptureChanged));
		public static readonly DependencyProperty MoveSpeedProperty = DP.Register(new ResetMetadata(8d, CaptureChanged));
		public static readonly DependencyProperty FFTWindowProperty = DP.Register(new ResetMetadata(new DSP.BlackmanNuttallWindow(DefaultFFTSize), CaptureChanged));
		public static readonly DependencyProperty MinimumFrequencyProperty = DP.Register(new ResetMetadata(50d));
		public static readonly DependencyProperty MaximumFrequencyProperty = DP.Register(new ResetMetadata(20000d));
		public static readonly DependencyProperty MaxDBProperty = DP.Register(new ResetMetadata(130d));
		public static readonly DependencyProperty CaptureProperty = DP.Register(new ResetMetadata(DefaultCapture, CaptureChanged));
		public static readonly DependencyProperty PeriodProperty = DP.Register(new PropertyMetadata(20, PeriodChanged));

		private static void WaveLengthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			var @this = (WaveCaptureDrawing)d;
			var newWaveLength = (int)e.NewValue;
			if (newWaveLength < 0) @this.WaveLength = 0;
			@this.ResizeWaveArray();
		}

		private static void CaptureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			var @this = (WaveCaptureDrawing)d;
			@this.UnregisterFFT();
			@this.RegisterFFT();
		}

		private static void PeriodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			var @this = d as WaveCaptureDrawing;
			@this.timer.Change(0, (int)e.NewValue);
		}

		public int WaveLength {
			get => this.GetValue<int>(WaveLengthProperty);
			set => SetValue(WaveLengthProperty, value);
		}

		public int FFTSize {
			get => this.GetValue<int>(FFTSizeProperty);
			set => SetValue(FFTSizeProperty, value);
		}

		public double MoveSpeed {
			get => this.GetValue<double>(MoveSpeedProperty);
			set => SetValue(MoveSpeedProperty, value);
		}

		public DSP.Window FFTWindow {
			get => this.GetValue<DSP.Window>(FFTWindowProperty);
			set => SetValue(FFTWindowProperty, value);
		}

		public double MinimumFrequency {
			get => this.GetValue<double>(MinimumFrequencyProperty);
			set => SetValue(MinimumFrequencyProperty, value);
		}

		public double MaximumFrequency {
			get => this.GetValue<double>(MaximumFrequencyProperty);
			set => SetValue(MaximumFrequencyProperty, value);
		}

		public double MaxDB {
			get => this.GetValue<double>(MaxDBProperty);
			set => SetValue(MaxDBProperty, value);
		}

		public IWaveIn Capture {
			get => this.GetValue<IWaveIn>(CaptureProperty);
			set => SetValue(CaptureProperty, value);
		}

		public int Period {
			get => this.GetValue<int>(PeriodProperty);
			set => SetValue(PeriodProperty, value);
		}

		public bool IsDrawWave => drawWave;
		public bool IsDrawSpectrum => drawSpectrum;

		private object waveLock = new object();
		protected bool designMode;
		private QueueArray<(double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)> cache, drawCache;
		private Timer timer;
		private DrawingVisual drawingWaveCache = new DrawingVisual(), drawingFFTCache = new DrawingVisual();
		private bool drawWave, drawSpectrum;
		private float[] leftWave, rightWave, drawLeftWave, drawRightWave;
		private (IWaveIn Capture, int FFTSize, double MoveSpeed, DSP.Window FFTWindow) currRegisterInfo;

		public WaveCaptureDrawing(bool drawWave, bool drawSpectrum) {
			if (!drawWave && !drawSpectrum) throw new ArgumentException("至少要使用一种绘制方案");
			this.drawWave = drawWave;
			this.drawSpectrum = drawSpectrum;
			designMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(this);
			if (designMode) return;

			var events = EventManager.GetRoutedEvents();
			foreach (var e in events) {
				EventManager.RegisterClassHandler(GetType(), e, (RoutedEventHandler)delegate {
					System.Diagnostics.Debug.Print($"{e.Name}");
				});
			}

			timer = new Timer(delegate {
				try {
					Dispatcher.Invoke(InvalidateVisual);
				} catch (TaskCanceledException) {
					timer.Dispose();
					UnregisterFFT();
				}
			});

			Loaded += delegate {
				RegisterCaptureEvent();
				ResizeWaveArray();
				RegisterFFT();

				if (this.drawSpectrum) {
					int fftSize = FFTSize;
					int fftComplexCount = FFTTools.GetComplexCount(fftSize);
					cache = new QueueArray<(double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)>(
						() => (new double[fftSize], new double[fftSize], new Complex[fftComplexCount], new Complex[fftComplexCount])
						);
					drawCache = new QueueArray<(double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)>(
						() => (new double[fftSize], new double[fftSize], new Complex[fftComplexCount], new Complex[fftComplexCount])
						, false);
				}

				timer.Change(0, Period);
			};
		}

		private void RegisterFFT(IWaveIn capture, int fftSize, double moveSpeed, DSP.Window fftWindow) {
			if (!drawSpectrum || capture == null || currRegisterInfo.Capture != null) return;
			if (!SharedStreamObservers.TryGetValue((capture, fftSize, moveSpeed, fftWindow), out var newObserver)) {
				newObserver = new SharedStreamObserver(capture, fftSize, moveSpeed, fftWindow);
				SharedStreamObservers.Add((capture, fftSize, moveSpeed, fftWindow), newObserver);
				if (capture == DefaultCapture) DefaultCapture.StartRecording();
			}
			newObserver.RefCount++;
			newObserver.FFTCompleted += CaptureFFTCompleted;
			currRegisterInfo = (capture, fftSize, moveSpeed, fftWindow);
		}

		private void RegisterFFT() => RegisterFFT(Capture, FFTSize, MoveSpeed, FFTWindow);

		private void UnregisterFFT() {
			var (capture, fftSize, moveSpeed, fftWindow) = currRegisterInfo;
			if (!drawSpectrum || capture == null) return;
			var observer = SharedStreamObservers[(capture, fftSize, moveSpeed, fftWindow)];
			observer.FFTCompleted -= CaptureFFTCompleted;
			observer.RefCount--;
			if (observer.RefCount == 0) {
				observer.Dispose();
				SharedStreamObservers.Remove((capture, fftSize, moveSpeed, fftWindow));
				if (capture == DefaultCapture) DefaultCapture.StopRecording();
			}
			currRegisterInfo = default;
		}

		private void ResizeWaveArray() {
			if (!drawWave) return;
			var waveLength = WaveLength;
			lock (waveLock) {
				if (waveLength > 0) {
					leftWave = new float[waveLength];
					rightWave = new float[waveLength];
				} else {
					leftWave = null;
					rightWave = null;
				}
			}
		}

		private void RegisterCaptureEvent() {
			if (!drawWave) return;
			var capture = Capture;
			if (capture == null) return;

			capture.DataAvailable += (sender, e) => {
				ReadOnlySpan<float> data = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
				var length = data.Length / 2;
				lock (waveLock) {
					if (leftWave == null) return;
					if (leftWave.Length > length) {
						leftWave.AsSpan(length).CopyTo(leftWave);
						rightWave.AsSpan(length).CopyTo(rightWave);
						for (int dstOffset = leftWave.Length - length, i = 0; i < length; i++) {
							leftWave[dstOffset + i] = data[2 * i];
							rightWave[dstOffset + i] = data[2 * i + 1];
						}
					} else {
						for (int srcOffset = length - leftWave.Length, i = 0; i < leftWave.Length; i++) {
							leftWave[i] = data[2 * (srcOffset + i)];
							rightWave[i] = data[2 * (srcOffset + i) + 1];
						}
					}
				}
			};
		}

		private void CaptureFFTCompleted(ReadOnlySpan<double> leftWave, ReadOnlySpan<double> rightWave, ReadOnlySpan<Complex> leftFFT, ReadOnlySpan<Complex> rightFFT) {
			using (cache.WriteLock()) {
				var (newLeftWave, newRightWave, newLeftFFT, newRightFFT) = cache.Write();
				leftWave.CopyTo(newLeftWave);
				rightWave.CopyTo(newRightWave);
				leftFFT.CopyTo(newLeftFFT);
				rightFFT.CopyTo(newRightFFT);
			}
		}

		protected virtual void ResetCapture() { }

		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
			base.OnPropertyChanged(e);

			if (e.Property.DefaultMetadata is ResetMetadata) {
				ResetCapture();
			}
		}


		protected virtual void DrawWave(DrawingContext dc, ReadOnlySpan<float> left, ReadOnlySpan<float> right) { }
		protected virtual void DrawSpectrum(DrawingContext dc, (double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)[] drawData) { }

		protected override void OnRender(DrawingContext dc) {
			base.OnRender(dc);

			if (designMode) return;

			if (drawWave) {
				lock (waveLock) {
					if (leftWave == null) goto StartFFT;
					if (drawLeftWave == null || drawLeftWave.Length != leftWave.Length) {
						drawLeftWave = new float[leftWave.Length];
						drawRightWave = new float[leftWave.Length];
					}
					leftWave.AsSpan().CopyTo(drawLeftWave);
					rightWave.AsSpan().CopyTo(drawRightWave);
				}
				using (var newDC = drawingWaveCache.RenderOpen()) {
					DrawWave(newDC, drawLeftWave, drawRightWave);
				}
			}

			StartFFT:
			if (drawSpectrum) {
				if (cache == null) goto StartRender;
				using (cache.ReadLock()) {
					if (cache.Count == 0) {
						goto StartRender;
					}
					do {
						var (oldLeftWave, oldRightWave, oldLeftFFT, oldRightFFT) = cache.Read();
						var (newLeftWave, newRightWave, newLeftFFT, newRightFFT) = drawCache.Write();
						oldLeftWave.AsSpan().CopyTo(newLeftWave);
						oldRightWave.AsSpan().CopyTo(newRightWave);
						oldLeftFFT.AsSpan().CopyTo(newLeftFFT);
						oldRightFFT.AsSpan().CopyTo(newRightFFT);
					} while (cache.Count > 0);
				}

				var drawData = new (double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)[drawCache.Count];
				for (int i = 0; i < drawData.Length; i++) {
					drawData[i] = drawCache.Read();
				}

				using (var newDC = drawingFFTCache.RenderOpen()) {
					DrawSpectrum(newDC, drawData);
				}
			}

			StartRender:
			if (drawWave) dc.DrawDrawing(drawingWaveCache.Drawing);
			if (drawSpectrum) dc.DrawDrawing(drawingFFTCache.Drawing);
		}
	}
}
