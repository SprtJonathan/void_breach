namespace VoidBreach.Water;

/// <summary>Optional body policy; changes lift inside the existing native buoyancy call, not surface effects.</summary>
public interface IFluidBuoyancyResponse
{
	/// <returns>Displaced-fluid density multiplier. Called once per owned body physics step.</returns>
	float UpdateBuoyancy( float submergedFraction, float delta );
}
