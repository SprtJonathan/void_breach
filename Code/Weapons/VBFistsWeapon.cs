using Sandbox;

/// <summary>
/// Bare-handed melee weapon built on the native s&amp;box combat weapon flow.
/// The native short-range bullet trace provides prediction, host validation,
/// damage delivery and impact effects while the official arms model animates it.
/// </summary>
[Title( "Void Breach Fists" )]
[Category( "Void Breach/Weapons" )]
[Icon( "sports_mma" )]
public sealed class VBFistsWeapon : BaseCombatWeapon, IVBIncapacitatedItemPresentation
{
	[Property, Group( "Melee" ), Range( 1f, 256f ), Step( 1f )]
	public float MeleeDistance { get; set; } = 80f;

	[Property, Group( "Melee" ), Range( 0f, 32f ), Step( 1f )]
	public float MeleeRadius { get; set; } = 8f;

	[Property, Group( "Melee" ), Range( 0f, 100f ), Step( 1f )]
	public float MeleeDamage { get; set; } = 20f;

	[Property, Group( "Melee" ), Range( 0f, 2000f ), Step( 25f )]
	public float MeleeForce { get; set; } = 500f;

	private readonly TagSet _damageTags = new( new[] { "melee", "blunt" } );
	private bool _holderIncapacitated;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		ApplyViewModelVisibility();
	}

	public override void PrimaryAttack()
	{
		ShootEffects();
		ShootBullet( MeleeDistance, MeleeRadius, MeleeDamage, MeleeForce, _damageTags );
	}

	protected override bool OnDrop() => false;

	public void SetHolderIncapacitated( bool incapacitated )
	{
		_holderIncapacitated = incapacitated;
		ApplyViewModelVisibility();
	}

	private void ApplyViewModelVisibility()
	{
		if ( ViewModel.IsValid() )
			ViewModel.Enabled = !_holderIncapacitated;
	}
}
