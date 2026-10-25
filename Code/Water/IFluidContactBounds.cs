namespace VoidBreach.Water;

/// <summary>Optional geometry supplied by systems with virtual contacts (suspension, scripted limbs, etc.).
/// Discovery and wave emission still belong to the universal water tracker.</summary>
public interface IFluidContactBounds
{
	bool TryGetFluidContactBounds( out BBox bounds );
}
