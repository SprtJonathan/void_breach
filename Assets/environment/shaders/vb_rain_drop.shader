
HEADER
{
	Description = "VB rain: transmitted scene light and local refraction";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif

	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define S_UV2 1
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool vFrontFacing : SV_IsFrontFace;
	#endif
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{

		PixelInput i = ProcessVertex( v );
		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );
		return FinalizeVertex( i );

	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "environment/shaders/vb_rain_optics.hlsl"
	#include "environment/shaders/vb_rain_cloud_mask.hlsl"
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );

	BoolAttribute( bWantsFBCopyTexture, true );
	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture"); SrgbRead( false ); >;

	float2 MapSceneColorCoords( float2 vInput, float2 modes )
	{
		float2 result;

		// X
		if ( modes.x == 1 ) // Mirror
		{
			float xx = abs( vInput.x );
			result.x = (fmod( floor( xx ), 2.0 ) == 0.0) ? frac( xx ) : 1.0 - frac( xx );
		}
		else if ( modes.x == 2 ) // Clamp
		{
			result.x = clamp( vInput.x, 0.0, 1.0 );
		}
		else if ( modes.x == 3 ) // Border
		{
			result.x = (vInput.x < 0.0 || vInput.x > 1.0) ? 0.5 : vInput.x;
		}
		else if ( modes.x == 4 ) // MirrorOnce
		{
	        float xx = abs( vInput.x );
			float floorX = floor( xx );
			if ( floorX < 1.0 )
			{
				result.x = frac( xx );
			}
			else if ( floorX < 2.0 )
			{
				result.x = 1.0 - frac( xx );
			}
			else
			{
				result.x = vInput.x;
			}
		}
		else // Wrap by default
		{
			result.x = vInput.x;
		}

		// Y
		if ( modes.y == 1 ) // Mirror
		{
			float yy = abs( vInput.y );
			result.y = (fmod( floor( yy ), 2.0 ) == 0.0) ? frac( yy ) : 1.0 - frac( yy );
		}
		else if ( modes.y == 2 ) // Clamp
		{
			result.y = clamp( vInput.y, 0.0, 1.0 );
		}
		else if ( modes.y == 3 ) // Border
		{
			result.y = (vInput.y < 0.0 || vInput.y > 1.0) ? 0.5 : vInput.y;
		}
		else if ( modes.y == 4 ) // MirrorOnce
		{
			float yy = abs( vInput.y );
			float floorY = floor( yy );
			if ( floorY < 1.0 )
			{
				result.y = frac( yy );
			}
			else if ( floorY < 2.0 )
			{
				result.y = 1.0 - frac( yy );
			}
			else
			{
				result.y = vInput.y;
			}
		}
		else // Wrap by default
		{
			result.y = vInput.y;
		}

		return result;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{


		float2 l_0 = VBRainRefractedUv( i.vPositionSs.xy, i.vTextureCoords.xy, i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz );
		float3 l_1 = g_tFrameBufferCopyTexture.Sample( g_sAniso, MapSceneColorCoords( l_0, float2(2,2) )).rgb;
		float3 l_2 = VBRainTransmission( l_1, i.vTextureCoords.xy, i.vColor.rgb );
		float2 l_3 = i.vTextureCoords.xy * float2( 1, 1 );
		float2 l_4 = float2( 0.5, 0.5 );
		float2 l_5 = l_3 - l_4;
		float l_6 = length( l_5 );
		float l_7 = l_6 * 2;
		float l_8 = 1 - l_7;
		float l_9 = saturate( l_8 );
		float l_10 = i.vColor.a;
		float l_11 = l_9 * l_10;
		float l_12 = VBRainCloudMask( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz );
		float l_13 = l_11 * l_12;
		float l_14 = RainOpticalOpacity;
		float l_15 = l_13 * l_14;


		return float4( l_2, l_15 );
	}
}
