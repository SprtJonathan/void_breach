namespace VoidBreach.River;

/// <summary>
/// Marks a rock, log or other static obstruction that should disturb nearby river water.
/// </summary>
[Title( "River Obstacle" )]
[Category( "Void Breach/River" )]
[Icon( "waves" )]
public sealed class RiverObstacle : Component
{
	[Property, Range( 8f, 512f )]
	public float Radius { get; set; } = 72f;

	[Property, Range( 0f, 3f )]
	public float Strength { get; set; } = 1f;

	protected override void OnValidate()
	{
		Radius = Radius.Clamp( 8f, 512f );
		Strength = Strength.Clamp( 0f, 3f );
	}
}
