using NAudio.CoreAudioApi;
using NAudio.Wave;
using Saar.FFmpeg.FFTW;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化 {
	public sealed class WaveCapture : DisposableObject {
		static MMDevice defaultDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

		// public static WaveCapture DefaultCapture { get; } = new WaveCapture();

		private readonly object @lock = new object();
		private int fftSize = 1024 * 2;
		private int fftComplexCount;
		private double moveSpeed;
		private IWaveIn capture;
		private FFT fft;
		private StreamObserver<float> observer;
		private DSP.Window fftWindow;
		
		public FFT FFT => fft;
		
		public WaveFormat Format => capture.WaveFormat;

		public event EventHandler<WaveInEventArgs> DataAvailable {
			add { capture.DataAvailable += value; }
			remove { capture.DataAvailable -= value; }
		}

		public delegate void FFTCompletedHandle(Complex[] left, Complex[] right);

		private FFTCompletedHandle fftCompleted;
		public event FFTCompletedHandle FFTCompleted {
			add {
				if (fft == null) {
					fft = FFT.CreateFFT(fftSize);
					CreateObserver();
				}
				fftCompleted += value;
			}
			remove {
				fftCompleted -= value;
				if (fftCompleted == null) {
					fft.Dispose();
					fft = null;
				}
			}
		}

		private void CreateObserver() {
			var fftDataLeft = new Complex[fftComplexCount];
			var fftDataRight = new Complex[fftComplexCount];
			observer = new StreamObserver<float>(fftSize, (int)(fftSize / moveSpeed), 2, true);
			observer.Completed += multiChannelData => {
				lock (@lock) {
					Span<double> tempIn = stackalloc double[fftSize];

					ReadSingleChannel(ref multiChannelData[0], tempIn);
					fftWindow?.Apply(tempIn, tempIn);
					fft.WriteRealToInput(tempIn);
					fft.Execute();
					fft.ReadOutput<Complex>(fftDataLeft);

					ReadSingleChannel(ref multiChannelData[1], tempIn);
					fftWindow?.Apply(tempIn, tempIn);
					fft.WriteRealToInput(tempIn);
					fft.Execute();
					fft.ReadOutput<Complex>(fftDataRight);

					fftCompleted?.Invoke(fftDataLeft, fftDataRight);
				}
			};
		}

		public WaveCapture() : this(new WasapiCapture(defaultDevice, true, 1) {
			ShareMode = AudioClientShareMode.Shared
		}, 2048, 16, new DSP.BlackmanNuttallWindow(2048)) { }

		public WaveCapture(IWaveIn capture, int fftSize, double moveSpeed, DSP.Window window = null) {
			this.capture = capture;
			capture.DataAvailable += (sender, e) => {
				observer?.Write(MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded)));
			};
			Reset(fftSize, moveSpeed, window);
		}

		public void Reset(int fftSize, double moveSpeed, DSP.Window window) {
			lock (@lock) {
				this.fftSize = fftSize;
				fftComplexCount = fftSize / 2 + 1;
				this.moveSpeed = moveSpeed;
				fftWindow = window;
				if (fft != null) {
					fft = FFT.CreateFFT(fftSize);
					CreateObserver();
				}
			}
		}

		static void ReadSingleChannel(ref float first, Span<double> dst) {
			for (int i = 0; i < dst.Length; i++) {
				dst[i] = Unsafe.Add(ref first, i * 2);
			}
		}

		public void Start() {
			try {
				capture.StartRecording();
			} catch { }
		}

		public void Stop() => capture.StopRecording();

		protected override void Dispose(bool disposing) {
			if (disposing) {
				if (capture != null) {
					capture.StopRecording();
					capture.Dispose();
					capture = null;
				}
			}
		}
	}
}
