using Sandbox;

/// <summary>
/// Bare-handed placeholder built on the native s&amp;box combat weapon flow.
/// This component only adds Void Breach inventory policy and blunt damage tags;
/// animation, ballistics, networking and effects remain native.
/// </summary>
[Title( "Void Breach Fists" )]
[Category( "Void Breach/Weapons" )]
[Icon( "sports_mma" )]
public sealed class VBFistsWeapon : BaseCombatWeapon, IVBWeaponCarryItem, IVBFirstPersonHeldItem
{
	public int CarrySize => 0;
	public VBWeaponSlotCategory SlotCategory => VBWeaponSlotCategory.Melee;
	public bool IsWeaponSlotPlaceholder => true;
	public GameObject FirstPersonViewModel => ViewModel;
	public float FirstPersonMotionAimAmount => 0f;

	private readonly TagSet _damageTags = new( new[] { "melee", "blunt" } );

	public override void PrimaryAttack()
	{
		if ( IsHeld && !TakePrimaryAmmo( 1 ) )
			return;

		var traces = ShootBullets(
			Ballistics.Pellets,
			CurrentSpread,
			Ballistics.Range,
			Ballistics.Radius,
			Ballistics.Damage,
			Ballistics.Force,
			_damageTags
		);

		TimeSinceShoot = 0f;
		var effects = new ShotEffect[traces.Length];

		for ( var index = 0; index < traces.Length; index++ )
		{
			var trace = traces[index];
			effects[index] = new ShotEffect(
				trace.EndPosition,
				trace.Hit,
				trace.Normal,
				trace.GameObject,
				trace.Surface,
				null,
				index > 0
			);
		}

		ShootEffects( effects );
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
