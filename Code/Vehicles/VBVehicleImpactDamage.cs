using System;
using Sandbox;

/// <summary>Host collision damage; uses VB health so NPC knockdown/death ragdolls receive the impulse.</summary>
[Title( "Vehicle Impact Damage" ), Category( "Void Breach/Vehicles" ), Icon( "car_crash" )]
public sealed class VBVehicleImpactDamage : Component, Component.ICollisionListener, IScenePhysicsEvents
{
	[Property] public VBVehicleController Vehicle { get; set; }
	[Property, Range( 1f, 50f )] public float MinimumSpeedKmh { get; set; } = 8f;
	[Property, Range( 1f, 100f )] public float LethalSpeedKmh { get; set; } = 35f;
	[Property, Range( 1f, 500f )] public float DamageAtLethalSpeed { get; set; } = 120f;
	[Property, Range( 1f, 1000f )] public float MaximumDamage { get; set; } = 300f;
	[Property, Range( 0.05f, 5f )] public float TargetCooldown { get; set; } = 0.75f;
	[Property, Range( 10f, 300f )] public float VictimMass { get; set; } = 70f;
	[Property, Range( 0.1f, 1f )] public float ImpulseTransfer { get; set; } = 0.45f;
	[Property] public bool DamagePlayers { get; set; } = true;
	private readonly Dictionary<VBHealthComponent, float> _lastHit = new();
	private float _nextPrune;
	private Vector3 _velocityBeforePhysics;

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( !Networking.IsHost || !Active || !Vehicle.IsValid() || !Vehicle.IsConfigured || Vehicle.Body.IsProxy || Vehicle.IsRecovering ) return;
		_velocityBeforePhysics = Vehicle.Body.Velocity;
		if ( _velocityBeforePhysics.Length < VBVehicleController.KmhToUnits( MinimumSpeedKmh ) ) return;
		var movement = _velocityBeforePhysics * Scene.FixedDelta;
		// Sweep the actual chassis, not an oversized damage radius. World blockers
		// remain in the sweep so a zombie behind a wall is never hit through it.
		var hits = Scene.Trace.Body( Vehicle.Body.PhysicsBody, Vehicle.WorldPosition + movement + movement.Normal * 3f )
			.IgnoreGameObjectHierarchy( Vehicle.GameObject ).WithoutTags( new[] { "trigger", "water" } ).RunAll().OrderBy( x => x.Fraction );
		foreach ( var hit in hits )
		{
			if ( !hit.GameObject.IsValid() ) continue;
			var health = hit.GameObject.GetComponentInParent<VBHealthComponent>( includeSelf: true );
			var ragdoll = health.IsValid() ? health.GetComponent<VBNpcRagdollController>() : null;
			if ( !ragdoll.IsValid() )
			{
				if ( Vector3.Dot( hit.Normal, movement.Normal ) < -0.2f ) break;
				continue;
			}
			if ( health.IsDead || ragdoll.IsRagdolled ) continue;
			var obstruction = Scene.Trace.Ray( Vehicle.Body.PhysicsBody.MassCenter, health.WorldPosition + Vector3.Up * 32f )
				.IgnoreGameObjectHierarchy( Vehicle.GameObject ).IgnoreGameObjectHierarchy( health.GameObject )
				.WithoutTags( new[] { "trigger", "water" } ).Run();
			if ( obstruction.Hit || obstruction.StartedSolid ) continue;
			float closingSpeed = MathF.Max( 0f, -Vector3.Dot( _velocityBeforePhysics, hit.Normal ) );
			if ( hit.StartedSolid ) closingSpeed = _velocityBeforePhysics.Length;
			if ( ApplyHit( health, hit.EndPosition, closingSpeed ) )
			{
				// A finite victim absorbs a small amount of momentum. Do not restore
				// velocity after collisions: that would let the car drive through walls.
				Vehicle.Body.ApplyImpulse( -_velocityBeforePhysics.Normal * (VictimMass * closingSpeed * ImpulseTransfer) );
			}
		}
	}

	protected override void OnStart()
	{
		if ( !Vehicle.IsValid() ) Vehicle = GetComponent<VBVehicleController>();
		if ( Vehicle.IsValid() && Vehicle.Body.IsValid() && Networking.IsHost )
		{
			Vehicle.Body.CollisionEventsEnabled = true;
			// This component provides speed-scaled VB damage and impulses. Native impact damage would double it.
			Vehicle.Body.EnableImpactDamage = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Vehicle.IsValid() || !Vehicle.Body.IsValid() ) return;
		_velocityBeforePhysics = Vehicle.Body.Velocity;
		if ( Time.Now < _nextPrune ) return;
		_nextPrune = Time.Now + 5f;
		foreach ( var target in _lastHit.Keys.Where( x => !x.IsValid() || Time.Now - _lastHit[x] > TargetCooldown ).ToArray() ) _lastHit.Remove( target );
	}

	public void OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || !Active || !Vehicle.IsValid() || Vehicle.Body.IsProxy || Vehicle.IsRecovering ) return;
		var other = collision.Other.GameObject;
		if ( !other.IsValid() ) return;
		var health = other.GetComponentInParent<VBHealthComponent>( includeSelf: true );
		if ( !health.IsValid() || health.IsDead || health.GameObject.IsDescendant( GameObject ) ) return;
		if ( !DamagePlayers && health.GetComponent<PlayerController>().IsValid() ) return;
		// An animated or falling victim can have a high contact speed against a
		// parked car. Only the moving vehicle's share can count as a run-over.
		float speed = MathF.Min( MathF.Abs( collision.Contact.NormalSpeed ), _velocityBeforePhysics.Length );
		var ragdoll = health.GetComponent<VBNpcRagdollController>();
		if ( ragdoll.IsValid() && ragdoll.IsRagdolled && speed < VBVehicleController.KmhToUnits( LethalSpeedKmh ) ) return;
		ApplyHit( health, collision.Contact.Point, speed );
	}

	private bool ApplyHit( VBHealthComponent health, Vector3 point, float speed )
	{
		float minimum = VBVehicleController.KmhToUnits( MinimumSpeedKmh );
		if ( !float.IsFinite( speed ) || speed < minimum ) return false;
		if ( _lastHit.TryGetValue( health, out var last ) && Time.Now - last < TargetCooldown ) return false;
		_lastHit[health] = Time.Now;
		float lethal = MathF.Max( minimum + 1f, VBVehicleController.KmhToUnits( LethalSpeedKmh ) );
		float amount = MathF.Min( MaximumDamage, DamageAtLethalSpeed * (speed * speed - minimum * minimum) / (lethal * lethal - minimum * minimum) );
		var occupant = Vehicle.ControlSource as VBVehicleDriverSeat;
		var attacker = occupant.IsValid() ? occupant.GetOccupant()?.GameObject : null;
		var direction = _velocityBeforePhysics.Normal;
		if ( direction.IsNearZeroLength ) direction = (health.WorldPosition - WorldPosition).Normal;
		var impulse = (direction + Vector3.Up * 0.12f).Normal * (VictimMass * MathF.Min( speed, VBVehicleController.KmhToUnits( 100f ) ) * ImpulseTransfer);
		var damage = VBDamageInfo.Create( amount, VBDamageType.Blunt, attacker.IsValid() ? attacker : GameObject, GameObject );
		damage.Position = point;
		damage.Force = impulse;
		health.TakeDamage( damage );
		var ragdoll = health.GetComponent<VBNpcRagdollController>();
		// Low-speed bumps still knock NPCs down; the ordinary recovery controller
		// decides when they can stand up. Health keeps its usual damage rules.
		if ( !health.GodMode && !health.IsDead && ragdoll.IsValid() && !ragdoll.IsRagdolled ) ragdoll.BeginRecoverableRagdoll( impulse );
		VBAiSoundStimulus.Emit( point, 800f, GameObject, VBAiSoundKind.Impact );
		return true;
	}
}
