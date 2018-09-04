using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace 音乐可视化 {
	public class SpectrumVisual : WaveCaptureDrawing {
		private ILogarithm log = new DSP.Spectrum3DLog();
		private Brush foreground = Brushes.White;
		private Pen drawPen;

		public SpectrumVisual() : base(false, true) {
			drawPen = new Pen {
				Thickness = 1,
				Brush = Brushes.LightGreen,
				LineJoin = PenLineJoin.Round
			};
			drawPen.Freeze();
		}


		protected override void DrawSpectrum(DrawingContext dc, (double[] LeftWave, double[] RightWave, Complex[] LeftFFT, Complex[] RightFFT)[] drawData) {
			var capture = Capture;
			var fftSize = FFTSize;
			var fftComplexCount = FFTTools.GetComplexCount(fftSize);
			var sampleRate = capture.WaveFormat.SampleRate;
			int cutFrequencyLength = FFTTools.CutFrequencyLength(fftSize, MinimumFrequency, MaximumFrequency, sampleRate, fftComplexCount);
			var (_, _, left, right) = drawData[drawData.Length - 1];
			Span<double> leftMagnitudes = stackalloc double[fftComplexCount];
			Span<double> rightMagnitudes = stackalloc double[fftComplexCount];
			Span<double> cutLeft = leftMagnitudes.Slice(0, cutFrequencyLength);
			Span<double> cutRight = rightMagnitudes.Slice(0, cutFrequencyLength);
			Span<double> viewLeft = cutLeft.Slice(0, (int)RenderSize.Width);
			Span<double> viewRight = cutRight.Slice(0, (int)RenderSize.Width);

			FFTTools.Abs(fftSize, left, leftMagnitudes, true, FFTWindow);
			FFTTools.CutFrequency(fftSize, MinimumFrequency, MaximumFrequency, sampleRate, leftMagnitudes, cutLeft);
			FFTTools.Logarithm(cutLeft, MinimumFrequency, MaximumFrequency, viewLeft, log);
			FFTTools.ToDB(viewLeft, MaxDB);
			FFTTools.Scale(viewLeft, RenderSize.Height / 2);

			FFTTools.Abs(fftSize, right, rightMagnitudes, true, FFTWindow);
			FFTTools.CutFrequency(fftSize, MinimumFrequency, MaximumFrequency, sampleRate, rightMagnitudes, cutRight);
			FFTTools.Logarithm(cutRight, MinimumFrequency, MaximumFrequency, viewRight, log);
			FFTTools.ToDB(viewRight, MaxDB);
			FFTTools.Scale(viewRight, RenderSize.Height / 2);

			var geometry = new StreamGeometry();
			using (var sgc = geometry.Open()) {
				sgc.BeginFigure(new Point(0, RenderSize.Height / 2), true, true);
				for (int i = 0; i < viewLeft.Length; i++) {
					double v = Math.Max(0, viewLeft[i]);
					sgc.LineTo(new Point(i, RenderSize.Height / 2 - v), true, true);
				}
				sgc.LineTo(new Point(RenderSize.Width - 1, RenderSize.Height / 2), true, true);
				for (int i = viewRight.Length - 1; i >= 0; i--) {
					double v = Math.Max(0, viewRight[i]);
					sgc.LineTo(new Point(i, RenderSize.Height / 2 + v), true, true);
				}
			}
			geometry.Freeze();
			var guidelineSet = new GuidelineSet(null, new[] { RenderSize.Height / 2 + .5 });
			guidelineSet.Freeze();
			dc.PushGuidelineSet(guidelineSet);
			dc.DrawGeometry(foreground, drawPen, geometry);
			dc.Pop();
		}
	}
}
