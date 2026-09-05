using Sandbox;

/// <summary>
/// Bare-handed melee weapon built on the native s&amp;box combat weapon flow.
/// The native short-range bullet trace provides prediction, host validation,
/// damage delivery and impact effects while the official arms model animates it.
/// </summary>
[Title( "Void Breach Fists" )]
[Category( "Void Breach/Weapons" )]
[Icon( "sports_mma" )]
public sealed class VBFistsWeapon : BaseCombatWeapon, IVBWeaponCarryItem
{
	public int CarrySize => 0;
	public VBWeaponSlotCategory SlotCategory => VBWeaponSlotCategory.Melee;
	public bool IsWeaponSlotPlaceholder => true;

	[Property, Group( "Melee" ), Range( 1f, 256f ), Step( 1f )]
	public float MeleeDistance { get; set; } = 80f;

	[Property, Group( "Melee" ), Range( 0f, 32f ), Step( 1f )]
	public float MeleeRadius { get; set; } = 8f;

	[Property, Group( "Melee" ), Range( 0f, 100f ), Step( 1f )]
	public float MeleeDamage { get; set; } = 20f;

	[Property, Group( "Melee" ), Range( 0f, 2000f ), Step( 25f )]
	public float MeleeForce { get; set; } = 500f;

	private readonly TagSet _damageTags = new( new[] { "melee", "blunt" } );

	public override void PrimaryAttack()
	{
		ShootEffects();
		ShootBullet( MeleeDistance, MeleeRadius, MeleeDamage, MeleeForce, _damageTags );
	}

	protected override void OnUpdate()
	{
		// Only the deployed item is allowed to drive the shared character
		// animgraph. This matters when the same fists item represents either
		// empty logical weapon slot.
		if ( !IsActive )
			return;

		base.OnUpdate();
	}

	protected override bool OnDrop() => false;
}
