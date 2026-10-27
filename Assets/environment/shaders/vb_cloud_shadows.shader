HEADER
{
	Description = "Void Breach approximate cloud shadows on opaque surfaces";
	Version = 1;
}
MODES
{
	Forward();
}
COMMON
{
	#include "postprocess/shared.hlsl"
	#include "environment/shaders/vb_cloud_screen.hlsl"
}
VS
{
	PixelInput MainVs(VertexInput i)
	{
		PixelInput o;
		o.vPositionPs = float4(i.vPositionOs.xy, 0, 1);
		o.uv = i.vTexCoord;
		return o;
	}
}
PS
{
	#include "postprocess/common.hlsl"
	RenderState(CullMode, NONE);
	#include "common/classes/Depth.hlsl"
	#include "Shadows/DirectionalLightShadow.hlsl"
	#include "environment/shaders/vb_cloud_density.hlsl"
	RenderState(BlendEnable, true);
	RenderState(SrcBlend, ZERO);
	RenderState(DstBlend, SRC_COLOR);
	RenderState(SrcBlendAlpha, ZERO);
	RenderState(DstBlendAlpha, ONE);
	float CloudShadowStrength < Attribute("CloudShadowStrength"); Default(0.45); >;
	float CloudShadowDistance < Attribute("CloudShadowDistance"); Default(36000); >;
	float4 MainPs(PixelInput i) : SV_Target0
	{
		float2 pixel = i.uv / g_vInvViewportSize;
		if (Depth::GetNormalized(pixel) <= 0.00001 || CloudSunDirection.z <= 0.05) return 1;
		float3 world = Depth::GetWorldPosition(pixel);
		float distance = length(world - g_vCameraPositionWs);
		if (distance > CloudShadowDistance || world.z >= CloudLayer.x) return 1;
		float3 normal = normalize(cross(ddy(world), ddx(world)));
		float facing = saturate(dot(normal, CloudSunDirection));
		float sunlight = DirectionalLightShadow::GetVisibility(world, float4(pixel, 0, 1));
		if (facing * sunlight < 0.01) return 1;
		float entry = (CloudLayer.x - world.z) / CloudSunDirection.z;
		float stepLength = CloudLayer.y / max(CloudSunDirection.z, 0.15) / 8.0;
		float optical = 0;
		[unroll] for (int j = 0; j < 8; j++)
			optical += CloudDensity(world + CloudSunDirection * (entry + (j + 0.5) * stepLength), true) * stepLength * 0.0009;
		float fade = 1 - smoothstep(CloudShadowDistance * 0.7, CloudShadowDistance, distance);
		float shadow = (1 - exp(-optical)) * CloudShadowStrength * facing * sunlight * fade;
		return float4((1 - shadow).xxx, 1);
	}
}
