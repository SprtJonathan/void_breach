using System;
using Sandbox;
using VoidBreach.Water;

public sealed partial class VBVehicleController : IFluidBuoyancyResponse
{
	[Property, Group( "Water" ), Description( "Road vehicles gradually lose trapped-air buoyancy. Disable for sealed/amphibious hulls." )]
	public bool FloodInWater { get; set; } = true;
	[Property, Group( "Water" ), Range( 1f, 120f ), Description( "Seconds to fill at full hull immersion; partial immersion fills more slowly. Shallow wheel contact does not flood the chassis." )]
	public float FloodingTime { get; set; } = 12f;
	[Property, Group( "Water" ), Range( 0f, 1f ), Description( "Fraction of native displaced volume remaining after flooding; does not change the water's density or drag." )]
	public float FloodedBuoyancy { get; set; } = 0.05f;
	[Sync( SyncFlags.FromHost )] public float WaterFill { get; private set; }

	float IFluidBuoyancyResponse.UpdateBuoyancy( float submergedFraction, float delta )
	{
		if ( !FloodInWater ) return 1f;
		// Filled water is retained on leaving the volume or changing seats. A wave
		// trough or trigger re-entry must not magically restore an airtight hull.
		if ( Networking.IsHost && submergedFraction > 0.08f )
			WaterFill = Math.Clamp( WaterFill + Math.Clamp( delta, 0f, 0.1f ) * Math.Clamp( submergedFraction, 0f, 1f )
				/ Math.Clamp( FloodingTime, 1f, 120f ), 0f, 1f );
		return 1f + (Math.Clamp( FloodedBuoyancy, 0f, 1f ) - 1f) * WaterFill;
	}
}
