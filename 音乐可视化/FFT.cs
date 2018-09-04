using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using Saar.FFmpeg.FFTW;
using System.Runtime.CompilerServices;
using System.Numerics;

namespace 音乐可视化 {
	unsafe public sealed class FFT : DisposableObject {
		public enum ComplexType {
			Double, Float
		}

		private class ReaderWriterLock {
			internal ReaderWriterLockSlim lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

			internal struct Unlocker : IDisposable {
				private Action unlocker;

				public Unlocker(Action unlocker) => this.unlocker = unlocker ?? throw new ArgumentNullException(nameof(unlocker));

				public void Dispose() => unlocker();
			}

			public Unlocker WriteLock() {
				lockSlim.EnterWriteLock();
				return new Unlocker(lockSlim.ExitWriteLock);
			}

			public Unlocker ReadLock() {
				lockSlim.EnterReadLock();
				return new Unlocker(lockSlim.ExitReadLock);
			}
		}

		private static readonly Dictionary<(int FFTSize, fftw_direction Dir, ComplexType Type), FFT> fftMap = new Dictionary<(int FFTSize, fftw_direction Dir, ComplexType Type), FFT>();

		static FFT Create(int fftSize, fftw_direction dir, ComplexType complexType) {
			if (fftMap.TryGetValue((fftSize, dir, complexType), out var result)) {
				return result;
			}
			result = new FFT(fftSize, dir, complexType);
			fftMap.Add((fftSize, dir, complexType), result);
			return result;
		}

		public static FFT CreateFFT(int fftSize, ComplexType complexType = ComplexType.Double)
			=> Create(fftSize, fftw_direction.Forward, complexType);

		public static FFT CreateIFFT(int fftSize, ComplexType complexType = ComplexType.Double)
			=> Create(fftSize, fftw_direction.Backward, complexType);

		public IntPtr Plan => plan.Plan;
		public IntPtr Input => plan.Input;
		public IntPtr Output => plan.Output;
		public int FFTSize { get; }
		public int FFTComplexCount => FFTSize / 2 + 1;
		public ComplexType Type { get; }
		
		private (IntPtr Plan, IntPtr Input, IntPtr Output) plan;
		private Task<(IntPtr Plan, IntPtr Input, IntPtr Output)> measureTask;
		private readonly int elementSize;
		private readonly Func<int, IntPtr> fftwAlloc;
		private readonly Action<IntPtr> fftwFree;
		private readonly Func<int, IntPtr, IntPtr, fftw_direction, fftw_flags, IntPtr> fftwCreatePlan;
		private readonly Action<IntPtr> fftwDestroyPlan;
		private readonly Action<IntPtr> fftwExecute;

		private FFT(int fftSize, fftw_direction dir, ComplexType complexType = ComplexType.Double) {
			FFTSize = fftSize;
			Type = complexType;

			switch (complexType) {
				case ComplexType.Double:
					fftwAlloc = _fftSize => fftw.alloc_complex((IntPtr)_fftSize);
					fftwFree = fftw.free;
					fftwCreatePlan = fftw.dft_1d;
					fftwDestroyPlan = fftw.destroy_plan;
					fftwExecute = fftw.execute;
					elementSize = sizeof(double) * 2;
					break;
				case ComplexType.Float:
					fftwAlloc = _fftSize => fftwf.alloc_complex((IntPtr)_fftSize);
					fftwFree = fftwf.free;
					fftwCreatePlan = fftwf.dft_1d;
					fftwDestroyPlan = fftwf.destroy_plan;
					fftwExecute = fftwf.execute;
					elementSize = sizeof(float) * 2;
					break;
				default: throw new ArgumentException("超出预期值", nameof(complexType));
			}
			{
				var input = fftwAlloc(fftSize * elementSize);
				var output = fftwAlloc(fftSize * elementSize);
				var plan = fftwCreatePlan(fftSize, input, output, dir, fftw_flags.Estimate);
				this.plan = (plan, input, output);
			}
			measureTask = Task.Run(() => {
				var input = fftwAlloc(FFTSize * elementSize);
				var output = fftwAlloc(FFTSize * elementSize);
				var result = fftwCreatePlan(fftSize, input, output, dir, fftw_flags.Measure);
				return (result, input, output);
			});
		}

		private void TrySwitchPlan() {
			if (measureTask != null && measureTask.IsCompleted) {
				var (newPlan, newInput, newOutput) = measureTask.Result;
				new ReadOnlySpan<byte>((void*)plan.Input, FFTSize * elementSize).CopyTo(new Span<byte>((void*)newInput, FFTSize * elementSize));
				new ReadOnlySpan<byte>((void*)plan.Output, FFTSize * elementSize).CopyTo(new Span<byte>((void*)newOutput, FFTSize * elementSize));
				DestroyPlan(plan);
				plan = (newPlan, newInput, newOutput);
				measureTask = null;
			}
		}

		private void DestroyPlan((IntPtr Plan, IntPtr Input, IntPtr Output) plan) {
			fftwDestroyPlan(plan.Plan);
			fftwFree(plan.Input);
			fftwFree(plan.Output);
		}

		public void Execute() {
			if (Plan == null) throw new InvalidOperationException("对象已释放。");
			TrySwitchPlan();
			fftwExecute(Plan);
		}

		protected override void Dispose(bool disposing) {
			if (plan.Plan != IntPtr.Zero) {
				if (measureTask != null) {
					Task.Run(() => {
						measureTask.Wait();
						DestroyPlan(measureTask.Result);
					});
				}
				DestroyPlan(plan);
				plan = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			}
		}

		public void WriteInput<T>(ReadOnlySpan<T> src) where T : unmanaged {
			var bytes = MemoryMarshal.AsBytes(src);
			bytes.CopyTo(new Span<byte>((void*)Input, FFTSize * elementSize));
		}

		public void WriteComplexToInput(ReadOnlySpan<Complex> srcComplices) {
			if (elementSize == sizeof(double) * 2) {
				WriteInput(srcComplices);
			} else {
				var count = FFTSize;
				ref Complex src = ref Unsafe.AsRef(srcComplices[0]);
				var dst = (float*)Input;
				for (int i = 0; i < count; i++) {
					var c = Unsafe.Add(ref src, i);
					dst[i * 2] = (float)c.Real;
					dst[i * 2 + 1] = (float)c.Imaginary;
				}
			}
		}

		public void WriteRealToInput(ReadOnlySpan<double> srcReals) {
			var dst = (double*)Input;
			var count = FFTSize;
			if (elementSize == sizeof(double) * 2) {
				ref double src = ref Unsafe.AsRef(srcReals[0]);
				for (int i = 0; i < count; i++) {
					dst[i * 2] = Unsafe.Add(ref src, i);
					dst[i * 2 + 1] = 0;
				}
			} else {
				ref double src = ref Unsafe.AsRef(srcReals[0]);
				for (int i = 0; i < count; i++) {
					dst[i * 2] = (float)Unsafe.Add(ref src, i);
					dst[i * 2 + 1] = 0;
				}
			}
		}

		public void ReadOutput<T>(Span<T> dst) where T : unmanaged {
			var bytes = MemoryMarshal.AsBytes(dst);
			new ReadOnlySpan<byte>((void*)Output, FFTComplexCount * elementSize).CopyTo(bytes);
		}

		public void ReadComplexFromOutput(Span<Complex> dstComplices) {
			if (elementSize == sizeof(double) * 2) {
				ReadOutput(dstComplices);
			} else {
				var count = FFTComplexCount;
				var src = (Complex*)Output;
				ref Complex dst = ref dstComplices[0];
				for (int i = 0; i < count; i++) {
					Unsafe.Add(ref dst, i) = src[i];
				}
			}
		}

		public void ReadMagnitudeFromOutput(Span<double> dstMagnitudes) {
			var count = FFTSize;
			if (elementSize == sizeof(double) * 2) {
				var src = (Complex*)Output;
				ref double dst = ref dstMagnitudes[0];
				for (int i = 0; i < count; i++) {
					Unsafe.Add(ref dst, i) = src[i].Magnitude;
				}
			} else {
				var src = (float*)Output;
				ref double dst = ref dstMagnitudes[0];
				for (int i = 0; i < count; i++) {
					Unsafe.Add(ref dst, i) = Math.Sqrt(src[2 * i] * src[2 * i] + src[2 * i + 1] + src[2 * i + 1]);
				}
			}
		}

	}
}
