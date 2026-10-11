HEADER
{
	Description = "Void Breach underwater absorption, distortion and caustics";
	Version = 1;
}
MODES
{
	Forward();
}
COMMON
{
	#include "postprocess/shared.hlsl"
}
struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic(PosXyz); >;
	float2 vTexCoord : TEXCOORD0 < Semantic(LowPrecisionUv); >;
};
struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;
	#if (PROGRAM == VFX_PROGRAM_VS)
		float4 vPositionPs : SV_Position;
	#endif
	#if (PROGRAM == VFX_PROGRAM_PS)
		float4 vPositionSs : SV_Position;
	#endif
};
VS
{
	PixelInput MainVs(VertexInput i)
	{
		PixelInput o;
		o.vPositionPs=float4(i.vPositionOs.xy,0,1);
		o.vTexCoord=i.vTexCoord;
		return o;
	}
}
PS
{
	#include "postprocess/common.hlsl"
	#include "postprocess/functions.hlsl"
	#include "common/classes/Depth.hlsl"
	Texture2D ColorBuffer < Attribute("ColorBuffer"); SrgbRead(true); >;
	float UnderwaterAmount < Attribute("UnderwaterAmount"); Default(0); >;
	float3 UnderwaterTint < Attribute("UnderwaterTint"); Default3(0.035,0.16,0.13); >;
	float3 UnderwaterWaterColor < Attribute("UnderwaterWaterColor"); Default3(0.018,0.064,0.05); >;
	float UnderwaterVisibility < Attribute("UnderwaterVisibility"); Default(900); >;
	float UnderwaterContrast < Attribute("UnderwaterContrast"); Default(0.9); >;
	float UnderwaterDistortion < Attribute("UnderwaterDistortion"); Default(0.0022); >;
	float UnderwaterDistortionSpeed < Attribute("UnderwaterDistortionSpeed"); Default(0.75); >;
	float UnderwaterSurfaceHeight < Attribute("UnderwaterSurfaceHeight"); Default(0); >;
	float UnderwaterBottomHeight < Attribute("UnderwaterBottomHeight"); Default(-240); >;
	float UnderwaterCausticStrength < Attribute("UnderwaterCausticStrength"); Default(0.18); >;
	float UnderwaterCausticScale < Attribute("UnderwaterCausticScale"); Default(92); >;
	float UnderwaterCausticSpeed < Attribute("UnderwaterCausticSpeed"); Default(0.7); >;
	float UnderwaterTime < Attribute("UnderwaterTime"); Default(0); >;
    #include "water/shaders/vb_caustics.hlsl"
    float Caustics(float2 worldPosition)
    {
        return WaterCausticPattern(worldPosition/max(UnderwaterCausticScale,1),UnderwaterTime*UnderwaterCausticSpeed);
    }

	float4 MainPs(PixelInput i) : SV_Target0
	{
		float2 uv=i.vTexCoord;
		float2 wave=float2(
			sin(uv.y*47+UnderwaterTime*UnderwaterDistortionSpeed*2.1)+sin(uv.y*91-UnderwaterTime*1.3),
			cos(uv.x*53-UnderwaterTime*UnderwaterDistortionSpeed*1.7)+sin(uv.x*83+UnderwaterTime*1.1));
		wave*=UnderwaterDistortion*UnderwaterAmount*0.5;
		float2 distortedUv=clamp(uv+wave,0.001,0.999);
		float4 source=ColorBuffer.SampleLevel(g_sBilinearMirror,distortedUv,0);
		float2 pixel=distortedUv/g_vInvViewportSize;
		float3 world=Depth::GetWorldPosition(pixel);
		float distanceToPixel=length(world-g_vCameraPositionWs);
		float waterDistance=distanceToPixel;
		if (world.z>UnderwaterSurfaceHeight+0.5)
		{
			float denominator=max(world.z-g_vCameraPositionWs.z,0.001);
			waterDistance*=saturate((UnderwaterSurfaceHeight-g_vCameraPositionWs.z)/denominator);
		}
		float fog=1-exp(-waterDistance/max(UnderwaterVisibility,1));
		float3 absorption=max(float3(0.08,0.08,0.08),1-UnderwaterWaterColor*2.2)/max(UnderwaterVisibility,1);
		float3 beer=exp(-waterDistance*absorption);
		float3 color=source.rgb*beer+lerp(UnderwaterWaterColor,UnderwaterTint,0.55)*(1-beer);
		float3 dx=ddx(world);
		float3 dy=ddy(world);
		float3 surfaceNormal=normalize(cross(dx,dy));
		float upward=abs(surfaceNormal.z);
		float belowSurface=step(world.z,UnderwaterSurfaceHeight+2);
		float aboveBottom=step(UnderwaterBottomHeight-8,world.z);
		float causticDepth=exp(-max(UnderwaterSurfaceHeight-world.z,0)/280);
		float caustic=Caustics(world.xy)*UnderwaterCausticStrength*upward*belowSurface*aboveBottom*causticDepth;
		color*=1+caustic*float3(0.75,1,0.72);
		color=lerp(float3(0.5,0.5,0.5),color,UnderwaterContrast);
		float vignette=saturate(1-dot(uv*2-1,uv*2-1)*0.08*UnderwaterAmount);
		color*=vignette;
		return float4(lerp(source.rgb,color,UnderwaterAmount),source.a);
	}
}
