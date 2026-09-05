using Sandbox;

/// <summary>
/// Exposes the first-person presentation of any held item to player-level
/// presentation systems. Weapons, fists and the future PDA share this contract.
/// </summary>
public interface IVBFirstPersonHeldItem
{
	GameObject FirstPersonViewModel { get; }
	float FirstPersonMotionAimAmount { get; }
}
