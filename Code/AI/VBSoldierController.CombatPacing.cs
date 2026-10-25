using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[Property, Group( "Weapon" ), Range( 0f, 2f )] public float AimReactionTime { get; set; } = 0.65f;
	[Property, Group( "Weapon" ), Range( 1f, 20f )] public float AimTrackingSpeed { get; set; } = 4f;
	[Property, Group( "Weapon" ), Range( 1, 8 )] public int BurstShotLimit { get; set; } = 3;
	[Property, Group( "Weapon" ), Description( "Use the held weapon's native NPC burst rests. Disable to use BurstRestMin/Max as minimum pauses." )]
	public bool UseWeaponBurstTiming { get; set; } = false;
	[Property, Group( "Weapon" )] public float BurstRestMin { get; set; } = 1f;
	[Property, Group( "Weapon" )] public float BurstRestMax { get; set; } = 1.8f;
	[Property, Group( "Weapon" )] public float AimMemoryTime { get; set; } = 18f;
	private float _lastVisualContact;
	[Sync( SyncFlags.FromHost )] private TimeUntil FireReady { get; set; }
	[Sync( SyncFlags.FromHost )] private GameObject _aimTarget { get; set; }

	private void UpdateCombatAim( GameObject target )
	{
		_lastVisualContact = Time.Now;
		var observed = Perception.LastKnownPosition + Perception.TargetOffset;
		if ( _aimTarget != target )
		{
			_holdPositionUntil = Time.Now + MathF.Max( 0f, InitialAssessmentTime ) + RandomRange( 0f, 0.4f );
			_aimTarget = target;
			AimPosition = observed;
			FireReady = MathF.Max( 0f, AimReactionTime ) + RandomRange( 0f, 0.2f );
			_burstRemaining = 0;
		}
		else AimPosition = Vector3.Lerp( AimPosition, observed, Math.Clamp( AimTrackingSpeed * Time.Delta, 0f, 1f ) );
	}
}
