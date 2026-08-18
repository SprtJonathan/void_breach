FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	VrForward();
	Depth();
}

COMMON
{
	#define S_SPECULAR 1
	#include "common/shared.hlsl"

	float RiverFlowSpeed < Attribute( "RiverFlowSpeed" ); Default( 1.35f ); >;
	float RiverSurfaceAmplitude < Attribute( "RiverSurfaceAmplitude" ); Default( 3.5f ); >;
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		float edgeAttenuation = saturate( 1.0f - abs( i.vTexCoord.x * 2.0f - 1.0f ) );
		float wave = sin( i.vTexCoord.y * 6.28318f - g_flTime * RiverFlowSpeed * 2.4f );
		wave += sin( i.vTexCoord.y * 11.7f - g_flTime * RiverFlowSpeed * 3.6f + i.vTexCoord.x * 4.0f ) * 0.45f;
		i.vPositionOs.z += wave * RiverSurfaceAmplitude * edgeAttenuation;

		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState( CullMode, NONE );

	float4 RiverShallowColor < Attribute( "RiverShallowColor" ); Default4( 0.12f, 0.34f, 0.28f, 1.0f ); >;
	float4 RiverDeepColor < Attribute( "RiverDeepColor" ); Default4( 0.015f, 0.105f, 0.12f, 1.0f ); >;
	float4 RiverFoamColor < Attribute( "RiverFoamColor" ); Default4( 0.72f, 0.82f, 0.75f, 1.0f ); >;
	float RiverRoughness < Attribute( "RiverRoughness" ); Default( 0.16f ); >;
	float RiverBankFoam < Attribute( "RiverBankFoam" ); Default( 0.9f ); >;
	float RiverRippleStrength < Attribute( "RiverRippleStrength" ); Default( 0.8f ); >;

	float4 RiverObstacle0 < Attribute( "RiverObstacle0" ); >;
	float4 RiverObstacle1 < Attribute( "RiverObstacle1" ); >;
	float4 RiverObstacle2 < Attribute( "RiverObstacle2" ); >;
	float4 RiverObstacle3 < Attribute( "RiverObstacle3" ); >;
	float4 RiverObstacle4 < Attribute( "RiverObstacle4" ); >;
	float4 RiverObstacle5 < Attribute( "RiverObstacle5" ); >;
	float4 RiverObstacle6 < Attribute( "RiverObstacle6" ); >;
	float4 RiverObstacle7 < Attribute( "RiverObstacle7" ); >;

	float4 RiverRipple0 < Attribute( "RiverRipple0" ); >;
	float4 RiverRipple1 < Attribute( "RiverRipple1" ); >;
	float4 RiverRipple2 < Attribute( "RiverRipple2" ); >;
	float4 RiverRipple3 < Attribute( "RiverRipple3" ); >;
	float4 RiverRipple4 < Attribute( "RiverRipple4" ); >;
	float4 RiverRipple5 < Attribute( "RiverRipple5" ); >;
	float4 RiverRipple6 < Attribute( "RiverRipple6" ); >;
	float4 RiverRipple7 < Attribute( "RiverRipple7" ); >;

	void AddObstacleWake(
		float4 obstacle,
		float2 worldPosition,
		float2 flowDirection,
		float2 sideDirection,
		inout float2 normalOffset,
		inout float foam )
	{
		if ( obstacle.z <= 0.0f || obstacle.w <= 0.0f )
		{
			return;
		}

		float2 delta = worldPosition - obstacle.xy;
		float radius = max( obstacle.z, 1.0f );
		float distanceToObstacle = length( delta );
		float proximity = 1.0f - smoothstep( radius * 0.75f, radius * 1.8f, distanceToObstacle );
		float downstream = dot( delta, flowDirection );
		float sideways = dot( delta, sideDirection );
		float wakeLength = saturate( downstream / (radius * 5.0f) );
		float wakeWidth = 1.0f - smoothstep( radius * 0.35f, radius * 2.2f, abs( sideways ) );
		float wakeMask = step( 0.0f, downstream ) * (1.0f - wakeLength) * wakeWidth;
		float wakeWave = sin( sideways / radius * 7.0f - downstream / radius * 3.4f - g_flTime * 5.0f );
		float strength = obstacle.w;

		normalOffset += normalize( delta + float2( 0.001f, 0.001f ) ) * proximity * strength * 0.18f;
		normalOffset += sideDirection * wakeWave * wakeMask * strength * 0.12f;
		foam += proximity * strength * 0.75f;
		foam += wakeMask * saturate( wakeWave * 0.5f + 0.5f ) * strength * 0.42f;
	}

	void AddRipple(
		float4 ripple,
		float2 worldPosition,
		inout float2 normalOffset,
		inout float foam )
	{
		if ( ripple.z <= 0.0f )
		{
			return;
		}

		float age = g_flTime - ripple.w;
		float lifetime = 2.6f;
		if ( age < 0.0f || age > lifetime )
		{
			return;
		}

		float2 delta = worldPosition - ripple.xy;
		float distanceToRipple = length( delta );
		float ringRadius = ripple.z + age * 115.0f;
		float ring = 1.0f - smoothstep( 8.0f, 28.0f, abs( distanceToRipple - ringRadius ) );
		float fade = saturate( 1.0f - age / lifetime );
		float strength = ring * fade * RiverRippleStrength;

		normalOffset += normalize( delta + float2( 0.001f, 0.001f ) ) * strength * 0.16f;
		foam += strength * 0.16f;
	}

	float4 MainPs( PixelInput i ) : SV_Target
	{
		Material material = Material::Init( i );
		float3 worldPosition = g_vCameraPositionWs + i.vPositionWithOffsetWs;
		float2 flowDirection = normalize( i.vTangentUWs.xy + float2( 0.0001f, 0.0001f ) );
		float2 sideDirection = float2( -flowDirection.y, flowDirection.x );
		float across = saturate( i.vTextureCoords.x );
		float along = i.vTextureCoords.y;
		float depth = saturate( i.vTextureCoords.z );
		float edgeDistance = abs( across * 2.0f - 1.0f );

		float streamA = sin( along * 8.0f - g_flTime * RiverFlowSpeed * 4.0f + across * 5.0f );
		float streamB = sin( along * 15.0f - g_flTime * RiverFlowSpeed * 6.7f - across * 9.0f );
		float2 normalOffset = flowDirection * streamA * 0.045f + sideDirection * streamB * 0.03f;
		float foam = smoothstep( 0.78f, 0.98f, edgeDistance ) * RiverBankFoam;

		AddObstacleWake( RiverObstacle0, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle1, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle2, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle3, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle4, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle5, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle6, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );
		AddObstacleWake( RiverObstacle7, worldPosition.xy, flowDirection, sideDirection, normalOffset, foam );

		AddRipple( RiverRipple0, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple1, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple2, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple3, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple4, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple5, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple6, worldPosition.xy, normalOffset, foam );
		AddRipple( RiverRipple7, worldPosition.xy, normalOffset, foam );

		float3 normal = normalize( i.vNormalWs + float3( normalOffset, 0.0f ) );
		float bankShallowing = smoothstep( 0.55f, 1.0f, edgeDistance );
		float perceivedDepth = saturate( depth * (1.0f - bankShallowing * 0.72f) );
		float3 waterColor = lerp( RiverShallowColor.rgb, RiverDeepColor.rgb, pow( perceivedDepth, 0.72f ) );
		foam = saturate( foam );
		waterColor = lerp( waterColor, RiverFoamColor.rgb, foam * 0.82f );

		material.Albedo = waterColor;
		material.Normal = normal;
		material.Roughness = lerp( RiverRoughness, 0.46f, foam );
		material.Metalness = 0.0f;
		material.AmbientOcclusion = 1.0f;
		material.Transmission = lerp( float3( 0.28f, 0.38f, 0.24f ), float3( 0.04f, 0.1f, 0.12f ), perceivedDepth );
		material.Emission = RiverFoamColor.rgb * foam * 0.045f;
		material.Opacity = 1.0f;

		if ( DepthNormals::WantsDepthNormals() )
		{
			return DepthNormals::Output( material.Normal, material.Roughness, 1.0f );
		}

		float4 color = ShadingModelStandard::Shade( i, material );
		color.rgb = Fog::Apply( worldPosition, i.vPositionSs.xy, color.rgb );
		return color;
	}
}
