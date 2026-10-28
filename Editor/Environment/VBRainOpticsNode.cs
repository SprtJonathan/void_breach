using Editor.ShaderGraph;
using Sandbox;

namespace VoidBreach.Editor;

[Title( "VB Rain Optics" ), Category( "Void Breach/Weather" ), Icon( "water_drop" )]
public sealed class VBRainOpticsNode : ShaderNode
{
	public bool SurfaceImpact { get; set; }
	[Input( typeof( Vector3 ) ), Hide] public NodeInput Background { get; set; }
	[Output( typeof( Vector2 ) ), Hide]
	public NodeResult.Func Coordinates => compiler =>
	{
		compiler.RegisterInclude( "environment/shaders/vb_rain_optics.hlsl" );
		return new NodeResult( 2, SurfaceImpact
			? "VBRainImpactUv( i.vPositionSs.xy, i.vTextureCoords.xy, i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz, i.vTextureCoords.zw )"
			: "VBRainRefractedUv( i.vPositionSs.xy, i.vTextureCoords.xy, i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz )" );
	};
	[Output( typeof( Vector3 ) ), Hide]
	public NodeResult.Func Color => compiler =>
	{
		compiler.RegisterInclude( "environment/shaders/vb_rain_optics.hlsl" );
		var background = compiler.ResultOrDefault( Background, new Vector3( 0.5f ) );
		return new NodeResult( 3, SurfaceImpact
			? $"VBRainImpactTransmission( {background.Cast( 3 )}, i.vTextureCoords.xy, i.vColor.rgb, i.vTextureCoords.zw )"
			: $"VBRainTransmission( {background.Cast( 3 )}, i.vTextureCoords.xy, i.vColor.rgb )" );
	};
	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Opacity => compiler =>
	{
		compiler.RegisterInclude( "environment/shaders/vb_rain_optics.hlsl" );
		return new NodeResult( 1, SurfaceImpact ? "1.0" : "RainOpticalOpacity" );
	};
}

[Title( "VB Rain Impact Mask" ), Category( "Void Breach/Weather" ), Icon( "water_drop" )]
public sealed class VBRainImpactMaskNode : ShaderNode
{
	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Result => compiler =>
	{
		compiler.RegisterInclude( "environment/shaders/vb_rain_optics.hlsl" );
		return new NodeResult( 1, "VBRainImpactMask( i.vTextureCoords.xy, i.vTextureCoords.zw )" );
	};
}
