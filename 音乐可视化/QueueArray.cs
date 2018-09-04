using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace 音乐可视化 {
	public class QueueArray<T> : IEnumerable<T> {
		private class Scope : IDisposable {
			private ReaderWriterLockSlim lockSlim;
			private bool isRead;

			public Scope(ReaderWriterLockSlim lockSlim, bool isRead) {
				this.lockSlim = lockSlim;
				this.isRead = isRead;
				if (isRead) {
					lockSlim.EnterReadLock();
				} else {
					lockSlim.EnterWriteLock();
				}
			}

			void IDisposable.Dispose() {
				if (isRead) {
					lockSlim.ExitReadLock();
				} else {
					lockSlim.ExitWriteLock();
				}
			}
		}

		public delegate void WriteRefHandler(ref T value);

		private ReaderWriterLockSlim lockSlim;
		private Queue<T> freeBuffer = new Queue<T>();
		private Queue<T> queue = new Queue<T>();
		private Func<T> ctor;

		public QueueArray(Func<T> ctor, bool threadSafe = true) {
			this.ctor = ctor;
			if (threadSafe) lockSlim = new ReaderWriterLockSlim();
		}

		public IDisposable ReadLock() => new Scope(lockSlim, true);

		public IDisposable WriteLock() => new Scope(lockSlim, false);

		public T Write() {
			var newItem = freeBuffer.Count > 0 ? freeBuffer.Dequeue() : ctor();
			queue.Enqueue(newItem);
			return newItem;
		}

		public T Read() {
			var item = queue.Dequeue();
			freeBuffer.Enqueue(item);
			return item;
		}

		public int Count => queue.Count;

		public IEnumerator<T> GetEnumerator() => queue.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
