#ifndef VB_CLOUD_SCREEN_HLSL
#define VB_CLOUD_SCREEN_HLSL
struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic(PosXyz); >;
	float2 vTexCoord : TEXCOORD0 < Semantic(LowPrecisionUv); >;
};
struct PixelInput
{
	float2 uv : TEXCOORD0;
	#if PROGRAM == VFX_PROGRAM_VS
		float4 vPositionPs : SV_Position;
	#else
		float4 vPositionSs : SV_Position;
	#endif
};
#endif
