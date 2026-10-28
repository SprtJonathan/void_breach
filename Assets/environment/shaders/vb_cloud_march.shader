HEADER
{
	Description = "Void Breach volumetric cloud integration (HDR)";
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
	#include "environment/shaders/vb_cloud_density.hlsl"
	int CloudResolutionDivisor < Attribute("CloudResolutionDivisor"); Default(2); >;
	int CloudSteps < Attribute("CloudSteps"); Default(48); >;
	int CloudLightSteps < Attribute("CloudLightSteps"); Default(8); >;

	bool CloudSceneOccluded(float2 uv)
	{
		uint width,height,levels;
		g_tDepthChain.GetDimensions(0,width,height,levels);
		// Minimum reverse-Z depth covers the entire tile, including small sky
		// holes. A one-pixel guard protects silhouettes during bilinear upsampling.
		uint mip=(uint)ceil(log2(CloudResolutionDivisor+2.0));
		uint tileWidth,tileHeight;
		g_tDepthChain.GetDimensions(mip,tileWidth,tileHeight,levels);
		float2 pixel=uv*float2(width,height);
		float span=CloudResolutionDivisor*0.5+1;
		float tileSize=exp2((float)mip);
		int2 a=clamp((int2)floor((pixel-span)/tileSize),0,int2(tileWidth,tileHeight)-1);
		int2 b=clamp((int2)floor((pixel+span)/tileSize),0,int2(tileWidth,tileHeight)-1);
		float depth=min(min(g_tDepthChain.Load(int3(a,mip)).r,g_tDepthChain.Load(int3(b.x,a.y,mip)).r),
			min(g_tDepthChain.Load(int3(a.x,b.y,mip)).r,g_tDepthChain.Load(int3(b,mip)).r));
		return Depth::Normalize(depth)>0;
	}

	float CloudLight(float3 world, float shadowJitter)
	{
		if (CloudSunDirection.z <= 0.01) return 0;
		float distance = min((CloudLayer.x + CloudLayer.y - world.z) / CloudSunDirection.z, CloudLayer.y * 4);
		int count = clamp(CloudLightSteps, 1, 12);
		float optical = 0;
		[loop] for (int j = 0; j < count; j++)
		{
			float a = (float)j / count;
			float b = (float)(j + 1) / count;
			float stepLength = (b * b - a * a) * max(distance, 0);
			float alongRay = lerp(a * a, b * b, 0.35 + frac(shadowJitter + j * 0.618033989) * 0.3) * max(distance, 0);
			optical += CloudDensity(world + CloudSunDirection * alongRay, j < 2) * stepLength * 0.0009;
		}
		// Three scattering orders share one optical-depth query. Later orders use
		// broader diffusion, preserving bright rims and depth inside thick banks.
		return exp(-optical) + exp(-optical * 0.3) * 0.32 + exp(-optical * 0.08) * 0.1;
	}
	float CloudPhase(float cosine, float g)
	{
		return (1 - g * g) / pow(max(1 + g * g - 2 * g * cosine, 0.01), 1.5);
	}
	float4 MainPs(PixelInput i) : SV_Target0
	{
		if (CloudSceneOccluded(i.uv)) return float4(0,0,0,1);
		float3 ray = CloudViewRay(i.uv);
		float entry, exit;
		if (!CloudInterval(g_vCameraPositionWs, ray, entry, exit)) return float4(0, 0, 0, 1);
		int count = clamp(CloudSteps, 8, 96);
		float stepLength = (exit - entry) / count;
		// Stable spatial jitter; no history buffer or temporal ghosting.
		float jitter = frac(52.9829189 * frac(dot(floor(i.vPositionSs.xy), float2(0.06711056, 0.00583715))));
		float cosine = dot(ray, CloudSunDirection);
		float phase = 0.15 + 0.35 * CloudPhase(cosine, 0.6) + 0.08 * CloudPhase(cosine, -0.25);
		float transmittance = 1;
		float3 scattering = 0;
		[loop] for (int j = 0; j < count; j++)
		{
			float distance = entry + (j + jitter) * stepLength;
			float3 world = g_vCameraPositionWs + ray * distance;
			float density = CloudDensity(world, true);
			if (density <= 0.001) continue;
			float extinction = 1 - exp(-density * stepLength * 0.0009);
			float height = saturate((world.z - CloudLayer.x) / CloudLayer.y);
			float ambientOcclusion = exp(-density * 1.4);
			float powder = 1 - exp(-density * 3);
			float3 light = CloudAmbient * lerp(0.65, 1.3, height) * ambientOcclusion
				+ CloudSunColor * CloudLight(world, frac(jitter + j * 0.754877666)) * phase * 0.3 * lerp(0.65 + powder * 0.35, 1, saturate(cosine));
			// Blend into atmospheric haze in the distance, preserving transmitted sky.
			float haze = 1 - exp(-distance / 100000.0);
			light = lerp(light, CloudAmbient * 1.6, haze * 0.65);
			scattering += transmittance * extinction * light;
			transmittance *= 1 - extinction;
			if (transmittance < 0.015) break;
		}
		float fade = 1 - smoothstep(CloudMaximumDistance * 0.65, CloudMaximumDistance, entry);
		return float4(scattering * fade, lerp(1, transmittance, fade));
	}
}
