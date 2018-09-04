using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace 音乐可视化 {
	unsafe public class Spectrum3DCompute : IDisposable {
		struct ParamStruct {
			public int OutWidth;
			public float MinimumFrequency;
			public float MaximumFrequency;
			public float MaxDB;
			public float Scale;
		}

		public const int MaxWidth = 128;
		public const int MaxHeight = 1080;

		private ParamStruct @params;
		private Buffer paramsBuffer, constBuffer;
		private Texture1D outAmplitudeTexture, inAmplitudeTexture;
		private Texture2D inTexture, outTexture, readTexture;
		private SamplerState sampler;
		private ResourceView inView, outAmplitudeView, inAmplitudeView, outView;
		private ComputeShader toAmplitudeShader, toImageShader;

		public Device Device { get; private set; }
		public int FFTSize { get; }
		public int FFTComplexCount => FFTSize / 2 + 1;
		public int SampleRate { get; }

		public int OutWidth {
			get => @params.OutWidth;
			set => SetParam(ref @params.OutWidth, value);
		}

		public float MinimumFrequency {
			get => @params.MinimumFrequency;
			set => SetParam(ref @params.MinimumFrequency, value);
		}

		public float MaximumFrequency {
			get => @params.MaximumFrequency;
			set => SetParam(ref @params.MaximumFrequency, value);
		}

		public float MaxDB {
			get => @params.MaxDB;
			set => SetParam(ref @params.MaxDB, value);
		}

		public float Scale {
			get => @params.Scale;
			set => SetParam(ref @params.Scale, value);
		}

		private void SetParam<T>(ref T field, T value) where T : unmanaged {
			if (!Equals(field, value)) {
				field = value;
				Device.ImmediateContext.UpdateSubresource(ref @params, paramsBuffer);
			}
		}

		public Spectrum3DCompute(Device device, int fftSize, int sampleRate, int outWidth, float minFreq, float maxFreq, float maxDB, float winScale) {
			Device = device;
			FFTSize = fftSize;
			SampleRate = sampleRate;

			Init();

			@params = new ParamStruct {
				OutWidth = outWidth,
				MinimumFrequency = minFreq,
				MaximumFrequency = maxFreq,
				MaxDB = maxDB,
				Scale = winScale,
			};
			Device.ImmediateContext.UpdateSubresource(ref @params, paramsBuffer);
		}

		public Spectrum3DCompute(int fftSize, int sampleRate, int outWidth, float minFreq, float maxFreq, float maxDB, float winScale)
			: this(new Device(DriverType.Hardware), fftSize, sampleRate, outWidth, minFreq, maxFreq, maxDB, winScale) {

		}

		private void Init() {
			constBuffer = Buffer.Create(Device, new int[] { FFTSize, SampleRate }, new BufferDescription {
				SizeInBytes = (sizeof(int) * 2 + 15) & ~15,
				BindFlags = BindFlags.ConstantBuffer,
				Usage = ResourceUsage.Immutable,
			});
			paramsBuffer = new Buffer(Device, new BufferDescription {
				SizeInBytes = (sizeof(ParamStruct) + 15) & ~15,
				BindFlags = BindFlags.ConstantBuffer,
			});
			inTexture = new Texture2D(Device, new Texture2DDescription {
				ArraySize = 1,
				Width = FFTComplexCount,
				Height = MaxWidth * 2,
				Format = SharpDX.DXGI.Format.R32G32_Float, // complex
				BindFlags = BindFlags.ShaderResource,
				MipLevels = 1,
				CpuAccessFlags = CpuAccessFlags.Write,
				Usage = ResourceUsage.Dynamic,
				SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
			});
			outAmplitudeTexture = new Texture1D(Device, new Texture1DDescription {
				ArraySize = MaxWidth * 2,
				Width = FFTComplexCount,
				Format = SharpDX.DXGI.Format.R32_Float,
				BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
				MipLevels = 1,
			});
			inAmplitudeTexture = new Texture1D(Device, new Texture1DDescription {
				ArraySize = MaxWidth * 2,
				Width = FFTComplexCount,
				Format = SharpDX.DXGI.Format.R32_Float,
				BindFlags = BindFlags.ShaderResource,
				MipLevels = 1,
			});
			outTexture = new Texture2D(Device, new Texture2DDescription {
				ArraySize = 1,
				Width = MaxHeight,
				Height = MaxWidth,
				BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
				Format = SharpDX.DXGI.Format.R32_UInt, // image pixel
				MipLevels = 1,
				SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
			});
			readTexture = new Texture2D(Device, new Texture2DDescription {
				ArraySize = 1,
				Width = MaxHeight,
				Height = MaxWidth,
				Format = SharpDX.DXGI.Format.R32_UInt,
				MipLevels = 1,
				CpuAccessFlags = CpuAccessFlags.Read,
				Usage = ResourceUsage.Staging,
				SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
			});
			sampler = new SamplerState(Device, new SamplerStateDescription {
				AddressU = TextureAddressMode.Clamp,
				AddressV = TextureAddressMode.Clamp,
				AddressW = TextureAddressMode.Border,
				BorderColor = Color.Black,
				Filter = Filter.MinMagMipLinear,
			});

			inView = new ShaderResourceView(Device, inTexture);
			outAmplitudeView = new UnorderedAccessView(Device, outAmplitudeTexture);
			inAmplitudeView = new ShaderResourceView(Device, inAmplitudeTexture);
			outView = new UnorderedAccessView(Device, outTexture);

			toAmplitudeShader = new ComputeShader(Device, ShaderBytecode.CompileFromFile("spectrum3d.hlsl", "FFTDataToAmplitude", "cs_5_0"));
			toImageShader = new ComputeShader(Device, ShaderBytecode.CompileFromFile("spectrum3d.hlsl", "AmplitudeToImage", "cs_5_0"));

			Device.ImmediateContext.ComputeShader.SetConstantBuffer(0, paramsBuffer);
			Device.ImmediateContext.ComputeShader.SetConstantBuffer(1, constBuffer);
			Device.ImmediateContext.ComputeShader.SetSampler(0, sampler);
			Device.ImmediateContext.ComputeShader.SetShaderResource(0, inView as ShaderResourceView);
			Device.ImmediateContext.ComputeShader.SetUnorderedAccessView(0, outAmplitudeView as UnorderedAccessView);
			Device.ImmediateContext.ComputeShader.SetShaderResource(1, inAmplitudeView as ShaderResourceView);
			Device.ImmediateContext.ComputeShader.SetUnorderedAccessView(1, outView as UnorderedAccessView);
		}

		public void Dispose() {
			if (Device != null) {
				paramsBuffer.Dispose();
				constBuffer.Dispose();
				inTexture.Dispose();
				outTexture.Dispose();
				readTexture.Dispose();
				inView.Dispose();
				outView.Dispose();
				sampler.Dispose();
				toAmplitudeShader.Dispose();
				Device.Dispose();
				Device = null;
			}
		}

		public IMap Write() => new Writer(Device, inTexture);

		public IMap Read() {
			Device.ImmediateContext.CopyResource(outTexture, readTexture);
			return new Reader(Device, readTexture);
		}

		public void Run(int width) {
			Device.ImmediateContext.ComputeShader.Set(toAmplitudeShader);
			Device.ImmediateContext.Dispatch(FFTComplexCount, width, 1);
			Device.ImmediateContext.CopyResource(outAmplitudeTexture, inAmplitudeTexture);
			Device.ImmediateContext.ComputeShader.Set(toImageShader);
			Device.ImmediateContext.Dispatch(OutWidth, width, 1);
		}

		public interface IMap : IDisposable {
			void* Data { get; }
			int Width { get; }
		}

		private class Reader : IMap {
			private readonly Device device;
			private readonly Resource resource;

			public void* Data { get; }
			public int Width { get; }

			public Reader(Device device, Resource resource) {
				this.device = device;
				this.resource = resource;
				var data = device.ImmediateContext.MapSubresource(resource, 0, MapMode.Read, 0);
				Data = data.DataPointer.ToPointer();
				Width = data.RowPitch / sizeof(float);
			}

			void IDisposable.Dispose() {
				device.ImmediateContext.UnmapSubresource(resource, 0);
			}
		}

		private class Writer : IMap {
			private readonly Device device;
			private readonly Resource resource;

			public void* Data { get; }
			public int Width { get; }

			public Writer(Device device, Resource resource) {
				this.device = device;
				this.resource = resource;
				var data = device.ImmediateContext.MapSubresource(resource, 0, MapMode.WriteDiscard, 0);
				Data = data.DataPointer.ToPointer();
				Width = data.RowPitch / sizeof(float);
			}

			void IDisposable.Dispose() {
				device.ImmediateContext.UnmapSubresource(resource, 0);
			}
		}
	}
}
