namespace VoidBreach.River;

/// <summary>
/// Defines the position and local width of one point along a RiverWater spline.
/// </summary>
[Title( "River Control Point" )]
[Category( "Void Breach/River" )]
[Icon( "timeline" )]
public sealed class RiverControlPoint : Component, Component.ExecuteInEditor
{
	[Property, Range( 64f, 2048f )]
	public float Width { get; set; } = 420f;

	protected override void OnValidate()
	{
		Width = Width.Clamp( 64f, 2048f );
	}
}
