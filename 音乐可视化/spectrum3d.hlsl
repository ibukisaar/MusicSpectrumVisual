
cbuffer CB : register(b0) {
	int outWidth;
	float minFreq;
	float maxFreq;
	float maxDB;
	float scale;
};

cbuffer CB2 : register(b1) {
	int fftSize;
	int sampleRate;
};

#define LOG_2_DB 8.6858896380650365530225783783321

Texture2D<float2> fftData : register(t0);
RWTexture1DArray<float> outAmplitudeData : register(u0);
Texture1DArray<float> amplitudeData : register(t1);
RWTexture2D<uint> outImage : register(u1);
sampler Sampler : register(s0);

float GetFreqIndex(float frequency) {
	return frequency * fftSize / sampleRate;
}

float SpecILog(float y) {
	return pow(10, y);
}

float SpecLog(float x) {
	return log10(x);
}

float ToDB(float x) {
	return max(0, log(x) * LOG_2_DB + maxDB) / maxDB;
	//return 1 + log10(clamp(x, 1e-6, 1)) / 6;
}

float MaxInRange(float x1, float x2, uint y, uint channelIndex) {
	int complexCount = fftSize / 2 + 1;
	float maxValue = amplitudeData.SampleLevel(Sampler, float2((x1 + .5) / complexCount, y * 2 + channelIndex), 0);
	for (int i = floor(x1 + .5); i < x2 + .5; i++) {
		maxValue = max(maxValue, amplitudeData[uint2(i, y * 2 + channelIndex)]);
	}
	maxValue = max(maxValue, amplitudeData.SampleLevel(Sampler, float2((x2 + .5) / complexCount, y * 2 + channelIndex), 0));
	return maxValue;
}

uint FloatToUInt(float x, float maxValue) {
	return (uint)floor(clamp(x, 0, 1) * maxValue + 0.5f);
}

uint Float3ToColor(float3 x) {
	return FloatToUInt(x[2], 255) | (FloatToUInt(x[1], 255) << 8) | (FloatToUInt(x[0], 255) << 16) | (255 << 24);
}

float3 YUV2RGB(float3 yuv) {
	float r = yuv[0] + 1.402 * yuv[2];
	float g = yuv[0] - 0.344136 * yuv[1] - 0.714136 * yuv[2];
	float b = yuv[0] + 1.772 * yuv[1];
	return float3(r, g, b);
}

//static const float4 color_table[] = {
//	float4(0.000, 0.000, 0.000, 0.00),
//	float4(0.000, 0.000, 0.315, 0.13),
//	float4(0.431, 0.000, 0.500, 0.30),
//	float4(0.943, 0.000, 0.000, 0.61),
//	float4(1.000, 0.612, 0.000, 0.73),
//	float4(1.000, 0.791, 0.000, 0.78),
//	float4(1.000, 1.000, 0.591, 0.91),
//	float4(1.000, 1.000, 1.000, 1.00),
//};

static const float4 yuv_table[] = {
	float4(                 0,                  0,                   0,    0),
	float4(.03587126228984074,  .1573300977624594, -.02548747583751842, 0.13),
	float4(.18572281794568020,  .1772436246393981,  .17475554840414750, 0.30),
	float4(.28184980583656130, -.1593064119945782,  .47132074554608920, 0.60),
	float4(.65830621175547810, -.3716070802232764,  .24352759331252930, 0.73),
	float4(.76318535758242900, -.4307467689263783,  .16866496622310430, 0.78),
	float4(.95336363636363640, -.2045454545454546,  .03313636363636363, 0.91),
	float4(                 1,                  0,                   0,    1),
};

uint ToColor(float x) {
	// return FloatToUInt(x[0], 255) | (FloatToUInt(x[1], 255) << 8) | (FloatToUInt(x[2], 255) << 16) | (255 << 24);
	// return 0xffffff | (FloatToUInt(x, 255) << 24);

	if (x <= 0) return Float3ToColor(YUV2RGB(yuv_table[0].rgb));
	for (int i = 1; i < 8; i++) {
		if (yuv_table[i].a >= x) {
			float a = (x - yuv_table[i - 1].a) / (yuv_table[i].a - yuv_table[i - 1].a);
			float3 yuv = yuv_table[i - 1].rgb * (1 - a) + yuv_table[i].rgb * a;
			return Float3ToColor(YUV2RGB(yuv));
		}
	}
	return Float3ToColor(YUV2RGB(yuv_table[7].rgb));
}

[numthreads(2, 1, 1)]
void FFTDataToAmplitude(uint3 dtid : SV_DispatchThreadID, uint channelIndex : SV_GroupIndex) {
	uint x = dtid.x / 2, y = dtid.y;
	float amplitude;
	if (x == 0) {
		amplitude = length(fftData[uint2(x, y * 2 + channelIndex)]) * scale;
	} else {
		amplitude = length(fftData[uint2(x, y * 2 + channelIndex)]) * scale * 2;
	}

	outAmplitudeData[uint2(x, y * 2 + channelIndex)] = ToDB(amplitude);
}

[numthreads(1, 1, 1)]
void AmplitudeToImage(uint3 dtid : SV_DispatchThreadID) {
	int i = dtid.x, j = dtid.y;
	int channelIndex = 0;
	int dstWidth = outWidth / 2;
	int complexCount = fftSize / 2 + 1;
	if (i >= dstWidth) {
		i -= dstWidth;
		dstWidth = outWidth - dstWidth;
		channelIndex = 1;
	}
	float minIndex = GetFreqIndex(minFreq);
	float maxIndex = GetFreqIndex(maxFreq);
	float srcWidth = maxIndex - minIndex;
	float srcScale = srcWidth / (maxFreq - minFreq);
	float minLogFreq = SpecLog(minFreq);
	float maxLogFreq = SpecLog(maxFreq);
	float logScale = (maxLogFreq - minLogFreq) / dstWidth;
	float x1 = max((SpecILog(i * logScale + minLogFreq) - minFreq) * srcScale + minIndex, 0);
	float x2 = min((SpecILog((i + 1) * logScale + minLogFreq) - minFreq) * srcScale + minIndex, complexCount - 1);
	outImage[uint2(dtid.x, j)] = ToColor(MaxInRange(x1, x2, j, channelIndex));
}