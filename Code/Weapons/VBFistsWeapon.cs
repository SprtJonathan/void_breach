using Sandbox;

/// <summary>
/// Bare-handed placeholder built on the native s&amp;box combat weapon flow.
/// Adds inventory policy, blunt damage tags and a relaxed third-person idle.
/// Punch gestures, ballistics, networking and viewmodel effects remain native.
/// </summary>
[Title( "Void Breach Fists" )]
[Category( "Void Breach/Weapons" )]
[Icon( "sports_mma" )]
public sealed class VBFistsWeapon : VBTraversalCombatWeapon, IVBWeaponCarryItem, IVBFirstPersonHeldItem
{
	public int CarrySize => 0;
	public VBWeaponSlotCategory SlotCategory => VBWeaponSlotCategory.Melee;
	public bool IsWeaponSlotPlaceholder => true;
	public GameObject FirstPersonViewModel => ViewModel;
	public float FirstPersonMotionAimAmount => 0f;
	private VBSoldierController NpcHolder => Inventory.IsValid() ? Inventory.GetComponent<VBSoldierController>() : null;
	protected override GameObject Attacker => NpcHolder.IsValid() ? NpcHolder.GameObject : base.Attacker;
	public override Ray AimRay => NpcHolder.IsValid() ? NpcHolder.MeleeAimRay : base.AimRay;

	private readonly TagSet _damageTags = new( new[] { "melee", "blunt" } );
	private const float PunchPoseDuration = 0.6f;
	private TimeSince _timeSincePunch = 999f;
	public bool IsPunchPoseActive => IsActive && _timeSincePunch < PunchPoseDuration;

	public override void PrimaryAttack()
	{
		if ( IsTraversalBlockingActions ) return;
		var npc = NpcHolder;
		if ( npc.IsValid() && (!Networking.IsHost || !IsActive || !npc.CanFight) ) return;
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
		{
			_timeSincePunch = 999f;
			return;
		}

		base.OnUpdate();
		var npc = NpcHolder;
		if ( npc.IsValid() && npc.CanFight && npc.BodyRenderer.IsValid() ) UpdateBodyAnimations( npc.BodyRenderer );
	}

	protected override void UpdateBodyAnimations( SkinnedModelRenderer renderer )
	{
		base.UpdateBodyAnimations( renderer );
		if ( IsTraversalBlockingActions ) _timeSincePunch = 999f;
		renderer.Set( "holdtype", _timeSincePunch < PunchPoseDuration ? HoldType : "none" );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		var npc = NpcHolder;
		if ( !shot.NoEvents && npc.IsValid() && npc.BodyRenderer.IsValid() )
		{
			_timeSincePunch = 0f;
			UpdateBodyAnimations( npc.BodyRenderer );
			npc.BodyRenderer.Set( "b_attack", true );
		}
		// The native shot callback runs on every peer, including for a missed punch.
		// Select the punch branch before the native b_attack trigger is sent.
		if ( !shot.NoEvents && IsActive && IsHeld && !IsTraversalBlockingActions
			&& HolderRenderer.IsValid() )
		{
			_timeSincePunch = 0f;
			UpdateBodyAnimations( HolderRenderer );
		}
		base.OnShootEffects( shot );
	}

	protected override void OnDisabled()
	{
		_timeSincePunch = 999f;
		base.OnDisabled();
	}

	protected override bool OnDrop() => false;
}
