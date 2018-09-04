using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化 {
	unsafe public static class Ex {
		public delegate void BufferHandler<TFrom, TTo>(TFrom* src, TTo* dst, int length) where TFrom : unmanaged where TTo : unmanaged;
		public delegate void UnfairRefBufferHandler<TFrom, TTo>(TFrom* src, TTo* dst) where TFrom : unmanaged where TTo : unmanaged;
		public delegate void UnfairBufferHandler<TFrom, TTo>(ReadOnlySpan<TFrom> src, Span<TTo> dst) where TFrom : unmanaged where TTo : unmanaged;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Handle<TFrom, TTo>(this ReadOnlySpan<TFrom> src, Span<TTo> dst, int length, BufferHandler<TFrom, TTo> handler) where TFrom : unmanaged where TTo : unmanaged {
			if (src.Length < length) throw new ArgumentOutOfRangeException(nameof(src));
			if (dst.Length < length) throw new ArgumentOutOfRangeException(nameof(dst));
			fixed (TFrom* @in = src) {
				if (MemoryMarshal.Cast<TFrom, byte>(src).Overlaps(MemoryMarshal.Cast<TTo, byte>(dst), out int offset) && offset > 0) {
					TTo* @out = stackalloc TTo[length];
					handler(@in, @out, length);
					new ReadOnlySpan<TTo>(@out, length).CopyTo(dst);
				} else {
					fixed (TTo* @out = dst) {
						handler(@in, @out, length);
					}
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Handle<TFrom, TTo>(this ReadOnlySpan<TFrom> src, Span<TTo> dst, UnfairRefBufferHandler<TFrom, TTo> handler) where TFrom : unmanaged where TTo : unmanaged {
			fixed (TFrom* @in = src) {
				if (MemoryMarshal.Cast<TFrom, byte>(src).Overlaps(MemoryMarshal.Cast<TTo, byte>(dst))) {
					TTo* @out = stackalloc TTo[dst.Length];
					handler(@in, @out);
					new ReadOnlySpan<TTo>(@out, dst.Length).CopyTo(dst);
				} else {
					fixed (TTo* @out = dst) {
						handler(@in, @out);
					}
				}
			}
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Handle<TFrom, TTo>(this ReadOnlySpan<TFrom> src, Span<TTo> dst, UnfairBufferHandler<TFrom, TTo> handler) where TFrom : unmanaged where TTo : unmanaged {
			if (MemoryMarshal.Cast<TFrom, byte>(src).Overlaps(MemoryMarshal.Cast<TTo, byte>(dst))) {
				Span<TTo> @out = stackalloc TTo[dst.Length];
				handler(src, @out);
				@out.CopyTo(dst);
			} else {
				handler(src, dst);
			}
		}
	}
}
