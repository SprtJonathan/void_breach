namespace VoidBreach.Water;

/// <summary>Optional tuning only. Ordinary objects are discovered without this component.</summary>
[Title( "VB Fluid Interaction Profile" ), Category( "Void Breach/Water" )]
public sealed class VBFluidInteractionProfile : Component
{
	[Property] public bool SuppressInteractions { get; set; }
	[Property, Range( 0, 4 )] public float StrengthScale { get; set; } = 1;
	[Property, Range( 0.25f, 4 )] public float RadiusScale { get; set; } = 1;
	[Property, Range( 0, 50000 ), Description( "Zero uses the physical mass or an estimate from bounds." )] public float MassOverride { get; set; }
}
