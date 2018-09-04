using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers;

namespace 音乐可视化 {
	public sealed class StreamObserver<T> where T : struct {
		public T[] FixedBuffer { get; private set; }
		public int FixedBlocks { get; private set; }
		public int MoveBlocks { get; private set; }
		public int BlockSize { get; private set; }
		public int Offset { get; private set; }

		public event Action<T[]> Completed;

		public StreamObserver(int fixedCount) : this(fixedCount, fixedCount) {
		}

		public StreamObserver(int fixedBlocks, int moveBlocks, int blockSize = 1, bool useEmptyData = false) {
			if (blockSize <= 0) throw new ArgumentException($"{nameof(blockSize)} 不能小于等于0");
			if (moveBlocks <= 0) throw new ArgumentException($"{nameof(moveBlocks)} 不能小于等于0");
			if (fixedBlocks < moveBlocks) throw new ArgumentException($"{nameof(fixedBlocks)} 不能小于 {nameof(moveBlocks)}");

			BlockSize = blockSize;
			FixedBlocks = fixedBlocks;
			MoveBlocks = moveBlocks;
			FixedBuffer = new T[fixedBlocks * blockSize];
			Offset = useEmptyData ? (fixedBlocks - moveBlocks) * blockSize : 0;
		}

		public void Write(ReadOnlySpan<T> data) {
			var fixedCount = FixedBlocks * BlockSize;
			var moveCount = MoveBlocks * BlockSize;
			Span<T> fixedSpan = FixedBuffer;
			while (true) {
				int copyLength = Math.Min(fixedCount - Offset, data.Length);
				if (copyLength == 0) return;
				data.Slice(0, copyLength).CopyTo(fixedSpan.Slice(Offset));
				Offset += copyLength;
				if (Offset == fixedCount) {
					Completed?.Invoke(FixedBuffer);
					Offset = fixedCount - moveCount;
					if (Offset > 0) {
						fixedSpan.Slice(fixedCount - Offset).CopyTo(fixedSpan);
					}
					data = data.Slice(copyLength);
				} else return;
			}
		}
	}
}
