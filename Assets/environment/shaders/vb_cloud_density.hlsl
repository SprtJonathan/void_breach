#ifndef VB_CLOUD_DENSITY_HLSL
#define VB_CLOUD_DENSITY_HLSL
// Shared by the sky and projected shadows. The padded 16x8 atlas stores 128
// periodic volume slices, avoiding runtime noise generation and GPU allocations.
Texture2D g_tCloudNoiseAtlas < Attribute("CloudNoiseAtlas"); SrgbRead(false); >;
Texture2D g_tCloudWeatherMap < Attribute("CloudWeatherMap"); SrgbRead(false); >;
SamplerState CloudWeatherSampler < Filter(MIN_MAG_MIP_LINEAR); AddressU(WRAP); AddressV(WRAP); >;
float4 CloudLayer < Attribute("CloudLayer"); >; // base, thickness, shape scale, density
float4 CloudWeather < Attribute("CloudWeather"); >; // coverage, erosion, XY advection
float3 CloudSeed < Attribute("CloudSeed"); >;
float3 CloudSunDirection < Attribute("CloudSunDirection"); >;
float3 CloudSunColor < Attribute("CloudSunColor"); >;
float3 CloudAmbient < Attribute("CloudAmbient"); >;
float CloudMaximumDistance < Attribute("CloudMaximumDistance"); Default(160000); >;

float2 CloudAtlasUv(float2 xy, float slice)
{
	float2 tile = float2(fmod(slice, 16.0), floor(slice / 16.0));
	return (tile * 130.0 + 1.5 + frac(xy) * 128.0) / float2(2080.0, 1040.0);
}
float4 CloudNoise(float3 p)
{
	float z = frac(p.z) * 128.0;
	float s = floor(z);
	float4 a = g_tCloudNoiseAtlas.SampleLevel(g_sBilinearClamp, CloudAtlasUv(p.xy, s), 0);
	float4 b = g_tCloudNoiseAtlas.SampleLevel(g_sBilinearClamp, CloudAtlasUv(p.xy, fmod(s + 1.0, 128.0)), 0);
	return lerp(a, b, frac(z));
}
float CloudDensity(float3 world, bool detail)
{
	float height = (world.z - CloudLayer.x) / CloudLayer.y;
	if (height <= 0 || height >= 1 || CloudWeather.x <= 0) return 0;
	float3 p = (world - float3(CloudWeather.zw, 0)) / CloudLayer.z + CloudSeed;
	float4 weather = g_tCloudWeatherMap.SampleLevel(CloudWeatherSampler, p.xy * 0.125, 0);
	float coverage = smoothstep(1 - CloudWeather.x - 0.18, 1 - CloudWeather.x + 0.18, weather.r);
	if (coverage < 0.005) return 0;
	float top = lerp(0.65, 1.0, weather.g);
	if (height >= top) return 0;
	float profile = smoothstep(0, 0.08, height) * (1 - smoothstep(top * 0.8, top, height));
	// Low-frequency weather deforms independent, rotated three-dimensional billows.
	p.xy = float2(p.x * 0.75 + p.y * 0.5, p.y * 0.75 - p.x * 0.5);
	p.xy += (weather.ba - 0.5) * 0.8 + height * float2(0.12, -0.08);
	float4 noise = CloudNoise(p);
	float threshold = 0.52 - coverage * 0.17 + pow(height / top, 3) * 0.2;
	float billows = dot(noise.gba, float3(0.625, 0.25, 0.125));
	float shape = lerp(noise.r, billows, 0.35);
	float density = saturate((shape - threshold) * 4) * profile * coverage;
	if (detail && density > 0)
	{
		float4 erosion = CloudNoise(p * 3.0 + float3(0.13, 0.27, 0.41));
		float wisps = 1 - dot(erosion.gba, float3(0.5, 0.3, 0.2));
		float erosionAmount = CloudWeather.y * 0.85 * lerp(wisps, 1 - wisps, saturate(height * 4));
		density = saturate((density - erosionAmount) / max(1 - erosionAmount, 0.01));
	}
	return saturate(density * 2) * CloudLayer.w;
}
float3 CloudViewRay(float2 uv)
{
	float4 position = mul(g_matProjectionToWorld, float4(uv.x * 2 - 1, 1 - uv.y * 2, 0.5, 1));
	return normalize(position.xyz / max(position.w, 1e-10));
}
bool CloudInterval(float3 origin, float3 direction, out float entry, out float exit)
{
	entry = 0; exit = 0;
	if (abs(direction.z) < 0.0001) return false;
	float a = (CloudLayer.x - origin.z) / direction.z;
	float b = (CloudLayer.x + CloudLayer.y - origin.z) / direction.z;
	entry = max(min(a, b), 0);
	exit = min(max(a, b), CloudMaximumDistance);
	return exit > entry;
}
#endif
