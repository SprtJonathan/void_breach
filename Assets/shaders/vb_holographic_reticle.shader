HEADER
{
	Description = "Void Breach collimated holographic reticle: emissive, unlit, no scene texture reads";
	Version = 1;
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	VrForward();
}
COMMON
{
	#define S_TRANSLUCENT 1
	#define S_RENDER_BACKFACES 1
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 sightRay : TEXCOORD12;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		// Use the actual render transform, including late viewmodel/camera movement.
		// The Facepunch holographic sight's optical axis is local +X, with +Z up.
		float3x4 objectToWorld = GetTransformMatrix( i.nInstanceTransformID, i.nBoneIndex.x );
		float3 forward = normalize( mul( objectToWorld, float4( 1, 0, 0, 0 ) ) );
		float3 right = normalize( mul( objectToWorld, float4( 0, -1, 0, 0 ) ) );
		float3 up = normalize( mul( objectToWorld, float4( 0, 0, 1, 0 ) ) );
		float3 ray = o.vPositionWs - g_vCameraPositionWs;
		o.sightRay = float3( dot( ray, forward ), dot( ray, right ), dot( ray, up ) );
		return FinalizeVertex( o );
	}
}
PS
{
	BoolAttribute( translucent, true );
	BoolAttribute( DoNotCastShadows, true );
	BoolAttribute( NoZPrepass, true );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	// Premultiplied emission keeps the thin red strokes saturated even against
	// a bright sky. Zero-mask pixels leave the glass/background untouched.
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, INV_SRC_ALPHA );

	CreateInputTexture2D( TextureReticle, Srgb, 8, "", "_color", "Reticle", Default3( 0, 0, 0 ) );
	Texture2D g_tReticle < Channel( RGB, Box( TextureReticle ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	SamplerState ReticleSampler < Filter( MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	float3 g_vReticleColor < UiType( Color ); Default3( 1, 0, 0 ); UiGroup( "Reticle" ); >;
	float g_flReticleBrightness < Default( 3.5 ); Range( 0.1, 12 ); UiGroup( "Reticle" ); >;
	float g_flReticleAngularScale < Default( 8 ); Range( 1, 100 ); UiGroup( "Reticle" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// Angular projection collimates the reticle at infinity: moving the eye
		// moves it across the glass while its direction stays on the optical axis.
		// The lens mesh clips it naturally; no screen color, depth or bindless lookup.
		if ( i.sightRay.x <= 0.001 ) return 0;
		float2 slope = float2( i.sightRay.y, -i.sightRay.z ) / max( i.sightRay.x, 0.001 );
		float2 uv = 0.5 + slope * max( g_flReticleAngularScale, 0.001 );
		// Boost the thin source strokes so filtered subpixel lines stay legible.
		float mask = saturate( g_tReticle.Sample( ReticleSampler, uv ).r * 4 );
		return float4( g_vReticleColor * max( g_flReticleBrightness, 0 ) * mask, mask );
	}
}
