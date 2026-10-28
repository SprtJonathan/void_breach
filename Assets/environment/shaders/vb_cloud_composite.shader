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
	// The fullscreen triangle is at reverse-Z far depth. Native depth testing
	// masks every opaque surface, including late draws and every MSAA sample.
	RenderState(DepthEnable, true);
	RenderState(DepthFunc, GREATER_EQUAL);
	RenderState(BlendEnable, true);
	RenderState(SrcBlend, ONE);
	RenderState(DstBlend, SRC_ALPHA);
	RenderState(SrcBlendAlpha, ZERO);
	RenderState(DstBlendAlpha, ONE);
	Texture2D CloudLayerTexture < Attribute("CloudLayerTexture"); SrgbRead(false); >;
	float4 MainPs(PixelInput i) : SV_Target0
	{
		return CloudLayerTexture.SampleLevel(g_sBilinearClamp, i.uv, 0);
	}
}
