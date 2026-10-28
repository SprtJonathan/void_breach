#ifndef VB_RAIN_CLOUD_MASK_HLSL
#define VB_RAIN_CLOUD_MASK_HLSL
#include "environment/shaders/vb_cloud_density.hlsl"
float3 RainVelocity < Attribute("RainVelocity"); Default3(0,0,-1800); >;

// Sparse sampling of the exact animated cloud density field. Conservative:
// a narrow cloud missed by all samples stays dry, never invent a rainy bank.
// No screen depth or camera exposure mask: exterior rain remains visible indoors.
float VBRainCloudMask(float3 world)
{
	if (CloudLayer.w <= 0 || CloudWeather.x <= 0) return 0;
	// The same continuous deck is actually present in CloudDensity. Avoid all
	// detailed noise reads in the common map-wide rain case.
	if (CloudRainDeck >= 0.999) return 1;
	float density = 0;
	[unroll] for (int sample = 0; sample < 4; sample++)
	{
		float z = CloudLayer.x + CloudLayer.y * (0.12 + sample * 0.17);
		float3 source = world - RainVelocity * (max(z - world.z, 0) / max(-RainVelocity.z, 1));
		source.z = z;
		density = max(density, CloudDensity(source, true));
	}
	return smoothstep(0.015, 0.08, density);
}
#endif
