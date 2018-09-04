using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 音乐可视化 {
	public class FixedQueue<T> : IReadOnlyCollection<T> {
		private Queue<T> data;
		private int maxCount;

		public FixedQueue(int maxCount) {
			data = new Queue<T>(maxCount);
			this.maxCount = maxCount;
		}

		public void Resize(int maxCount) {
			this.maxCount = maxCount;
			while (data.Count > maxCount) {
				data.Dequeue();
			}
		}

		public int Count => data.Count;

		public IEnumerator<T> GetEnumerator() => data.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public void Add(T item) {
			data.Enqueue(item);
			if (data.Count > maxCount) {
				data.Dequeue();
			}
		}
	}
}
