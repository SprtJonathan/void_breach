#ifndef VB_RAIN_OPTICS_HLSL
#define VB_RAIN_OPTICS_HLSL
#include "common/classes/Depth.hlsl"
float RainRefractionPixels < Attribute("RainRefractionPixels"); Default(1.25); >;
float RainOpticalOpacity < Attribute("RainOpticalOpacity"); Default(0.8); >;
float RainLightScattering < Attribute("RainLightScattering"); Default(0.3); >;

float2 VBRainRefractedUv(float2 screen, float2 uv, float3 world)
{
	float2 base = CalculateViewportUv(screen);
	// Curved cylindrical water lens: bounded in pixel units at every resolution.
	float2 bend = float2((uv.x * 2 - 1) * 0.85, (uv.y * 2 - 1) * 0.12);
	float2 refracted = clamp(base + bend * RainRefractionPixels * g_vInvViewportSize, g_vInvViewportSize, 1 - g_vInvViewportSize);
	float2 pixel = refracted / g_vInvViewportSize;
	if (Depth::GetNormalized(pixel) > 0.000001)
	{
		float opaqueDistance = length(Depth::GetWorldPosition(pixel) - g_vCameraPositionWs);
		// Never pull a foreground doorway or wall into an outdoor drop.
		if (opaqueDistance < length(world - g_vCameraPositionWs) - 1) return base;
	}
	return refracted;
}
float3 VBRainTransmission(float3 background, float2 uv, float3 tint)
{
	float edge = pow(abs(uv.x * 2 - 1), 4);
	float glint = pow(saturate(1 - (uv.x * 2 - 1) * (uv.x * 2 - 1)), 2);
	float luminance = dot(background, float3(0.2126, 0.7152, 0.0722));
	// Transmit scene light with slight absorption and restrained edge scattering.
	// No fixed white/emissive streak: local lights, shadow and sky set the response.
	float3 transmission = background * lerp(float3(1,1,1), tint, 0.08) * 0.97;
	float3 reflection = (background + luminance * 0.1) * RainLightScattering * 2 * (0.3 + edge * 0.45 + glint * 0.7);
	return max(transmission + reflection, 0);
}

float VBRainImpactMask(float2 uv, float2 data)
{
	float2 p = uv * 2 - 1;
	if (data.x >= 2) return exp(-dot(p * float2(1,0.8), p * float2(1,0.8)) * 5) * (1 - smoothstep(0.75,1,length(p)));
	float angle = atan2(p.y,p.x);
	float boundary = 0.78 + sin(angle * 7 + data.y * 31) * 0.08 + sin(angle * 13 - data.y * 17) * 0.04;
	float radius = length(p * float2(1, 0.85 + data.y * 0.3));
	// A short irregular wet footprint on solid ground, never an expanding white ring.
	return (1 - smoothstep(boundary * 0.5, boundary, radius)) * exp(-data.x * 3);
}
float2 VBRainImpactUv(float2 screen, float2 uv, float3 world, float2 data)
{
	float2 refracted = VBRainRefractedUv(screen, uv, world);
	float amount = data.x >= 2 ? 0.65 : 0.3 * exp(-data.x * 6);
	return lerp(CalculateViewportUv(screen), refracted, amount);
}
float3 VBRainImpactTransmission(float3 background, float2 uv, float3 tint, float2 data)
{
	if (data.x >= 2) return VBRainTransmission(background, uv, tint);
	// Brief absorption/reflection of the actual underlying ground texture.
	return background * (0.68 + 0.16 * data.x);
}
#endif
