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

	static const float OCEAN_PI = 3.14159265359f;

	float2 OceanWindDirection < Attribute( "OceanWindDirection" ); Default2( 1.0f, 0.25f ); >;
	float OceanWaveAmplitude < Attribute( "OceanWaveAmplitude" ); Default( 52.0f ); >;
	float OceanWaveLength < Attribute( "OceanWaveLength" ); Default( 1100.0f ); >;
	float OceanWaveSpeed < Attribute( "OceanWaveSpeed" ); Default( 1.0f ); >;
	float OceanChoppiness < Attribute( "OceanChoppiness" ); Default( 0.72f ); >;

	void AddGerstnerWave(
		inout float3 position,
		inout float3 tangent,
		inout float3 binormal,
		float2 direction,
		float amplitude,
		float wavelength,
		float speed,
		float steepness,
		float time )
	{
		direction = normalize( direction );
		float waveNumber = 2.0f * OCEAN_PI / max( wavelength, 1.0f );
		float angularFrequency = sqrt( 980.0f * waveNumber ) * speed;
		float phase = waveNumber * dot( direction, position.xy ) - angularFrequency * time;
		float sine = sin( phase );
		float cosine = cos( phase );
		float horizontal = steepness * amplitude;
		float derivative = waveNumber * amplitude;

		position.xy += direction * horizontal * cosine;
		position.z += amplitude * sine;

		tangent += float3(
			-horizontal * waveNumber * direction.x * direction.x * sine,
			-horizontal * waveNumber * direction.x * direction.y * sine,
			derivative * direction.x * cosine
		);

		binormal += float3(
			-horizontal * waveNumber * direction.x * direction.y * sine,
			-horizontal * waveNumber * direction.y * direction.y * sine,
			derivative * direction.y * cosine
		);
	}

	void EvaluateOcean( float3 basePosition, float time, out float3 position, out float3 normal, out float crest )
	{
		float2 wind = normalize( OceanWindDirection + float2( 0.0001f, 0.0001f ) );
		float2 direction1 = normalize( float2( wind.x * 0.82f - wind.y * 0.57f, wind.x * 0.57f + wind.y * 0.82f ) );
		float2 direction2 = normalize( float2( wind.x * 0.32f + wind.y * 0.95f, -wind.x * 0.95f + wind.y * 0.32f ) );
		float2 direction3 = normalize( float2( -wind.x * 0.72f - wind.y * 0.69f, wind.x * 0.69f - wind.y * 0.72f ) );

		position = basePosition;
		float3 tangent = float3( 1.0f, 0.0f, 0.0f );
		float3 binormal = float3( 0.0f, 1.0f, 0.0f );

		AddGerstnerWave( position, tangent, binormal, wind, OceanWaveAmplitude, OceanWaveLength, OceanWaveSpeed, OceanChoppiness, time );
		AddGerstnerWave( position, tangent, binormal, direction1, OceanWaveAmplitude * 0.48f, OceanWaveLength * 0.47f, OceanWaveSpeed * 1.12f, OceanChoppiness * 0.78f, time );
		AddGerstnerWave( position, tangent, binormal, direction2, OceanWaveAmplitude * 0.22f, OceanWaveLength * 0.23f, OceanWaveSpeed * 1.28f, OceanChoppiness * 0.55f, time );
		AddGerstnerWave( position, tangent, binormal, direction3, OceanWaveAmplitude * 0.10f, OceanWaveLength * 0.18f, OceanWaveSpeed * 1.55f, OceanChoppiness * 0.35f, time );

		normal = normalize( cross( tangent, binormal ) );
		float maximumHeight = max( OceanWaveAmplitude * 1.8f, 0.001f );
		crest = saturate( position.z / maximumHeight * 0.5f + 0.5f );
	}
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
		PixelInput o = ProcessVertex( i );
		float3 displacedPosition;
		float3 oceanNormal;
		float crest;
		EvaluateOcean( i.vPositionOs.xyz, g_flTime, displacedPosition, oceanNormal, crest );

		float3x4 objectToWorld = GetTransformMatrix( i.nInstanceTransformID );
		o.vPositionWs = mul( objectToWorld, float4( displacedPosition, 1.0f ) );
		o.vNormalWs = normalize( mul( objectToWorld, float4( oceanNormal, 0.0f ) ) );
		o.vPositionPs = Position3WsToPs( o.vPositionWs );
		o.vVertexColor = float4( crest, 0.0f, 0.0f, 1.0f );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState( CullMode, NONE );

	float4 OceanDeepColor < Attribute( "OceanDeepColor" ); Default4( 0.006f, 0.045f, 0.075f, 1.0f ); >;
	float4 OceanShallowColor < Attribute( "OceanShallowColor" ); Default4( 0.025f, 0.22f, 0.26f, 1.0f ); >;
	float4 OceanHorizonColor < Attribute( "OceanHorizonColor" ); Default4( 0.18f, 0.38f, 0.48f, 1.0f ); >;
	float OceanRoughness < Attribute( "OceanRoughness" ); Default( 0.075f ); >;
	float OceanFoamThreshold < Attribute( "OceanFoamThreshold" ); Default( 0.69f ); >;
	float OceanFoamIntensity < Attribute( "OceanFoamIntensity" ); Default( 1.15f ); >;

	float4 MainPs( PixelInput i ) : SV_Target
	{
		Material material = Material::Init( i );
		float3 normal = normalize( i.vNormalWs );
		float3 worldPosition = g_vCameraPositionWs + i.vPositionWithOffsetWs;
		float2 wind = normalize( OceanWindDirection + float2( 0.0001f, 0.0001f ) );
		float2 crossWind = float2( -wind.y, wind.x );
		float rippleA = cos( dot( worldPosition.xy, wind ) * 0.045f - g_flTime * 2.7f );
		float rippleB = cos( dot( worldPosition.xy, crossWind ) * 0.071f + g_flTime * 3.4f );
		normal = normalize( normal + float3( wind * rippleA * 0.055f + crossWind * rippleB * 0.035f, 0.0f ) );
		float3 viewDirection = CalculatePositionToCameraDirWs( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz );
		float fresnel = pow( 1.0f - saturate( dot( normal, viewDirection ) ), 5.0f );
		float crest = saturate( i.vVertexColor.r );
		float slope = saturate( 1.0f - normal.z );
		float foam = smoothstep( OceanFoamThreshold, 1.0f, crest ) * smoothstep( 0.04f, 0.32f, slope );
		foam = saturate( foam * OceanFoamIntensity );

		float3 waterColor = lerp( OceanDeepColor.rgb, OceanShallowColor.rgb, crest * 0.62f );
		waterColor = lerp( waterColor, float3( 0.82f, 0.9f, 0.91f ), foam );

		material.Albedo = waterColor;
		material.Normal = normal;
		material.Roughness = lerp( OceanRoughness, 0.38f, foam );
		material.Metalness = 0.0f;
		material.AmbientOcclusion = 1.0f;
		material.Transmission = float3( 0.16f, 0.28f, 0.32f ) * ( 1.0f - foam );
		material.Emission = OceanHorizonColor.rgb * fresnel * 0.22f + foam.xxx * 0.08f;
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
