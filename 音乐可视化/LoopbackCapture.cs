using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace 音乐可视化 {
	public class LoopbackCapture : WasapiCapture {
		public LoopbackCapture(int audioBufferMillisecondsLength)
				: base(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice(), false, audioBufferMillisecondsLength) { }

		protected override AudioClientStreamFlags GetAudioClientStreamFlags() {
			return AudioClientStreamFlags.Loopback;
		}
	}
}
