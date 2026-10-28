using Editor.ShaderGraph;
using Sandbox;

namespace VoidBreach.Editor;

/// <summary>Editor-only ShaderGraph bridge; generated shaders have no dependency on editor code.</summary>
[Title( "VB Rain Cloud Mask" ), Category( "Void Breach/Weather" ), Icon( "rainy" )]
public sealed class VBRainCloudMaskNode : ShaderNode
{
	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Result => compiler =>
	{
		if ( !compiler.IsNotPreview ) return new NodeResult( 1, "1.0" );
		compiler.RegisterInclude( "environment/shaders/vb_rain_cloud_mask.hlsl" );
		return new NodeResult( 1, "VBRainCloudMask( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz )" );
	};
}
