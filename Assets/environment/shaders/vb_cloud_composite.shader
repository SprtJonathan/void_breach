HEADER
{
	Description = "Void Breach cloud upsample and HDR composition";
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
	RenderState(BlendEnable, true);
	RenderState(SrcBlend, ONE);
	RenderState(DstBlend, SRC_ALPHA);
	RenderState(SrcBlendAlpha, ZERO);
	RenderState(DstBlendAlpha, ONE);
	Texture2D CloudLayerTexture < Attribute("CloudLayerTexture"); SrgbRead(false); >;
	float4 MainPs(PixelInput i) : SV_Target0
	{
		// Full-resolution sky mask: low-resolution clouds never bleed over opaque
		// silhouettes. This ground-view renderer deliberately leaves geometry alone.
		float depth = Depth::GetNormalized(i.uv / g_vInvViewportSize);
		if (depth > 0.00001) return float4(0, 0, 0, 1);
		return CloudLayerTexture.SampleLevel(g_sBilinearClamp, i.uv, 0);
	}
}
