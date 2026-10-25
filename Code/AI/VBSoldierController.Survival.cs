using System;
using Sandbox;
using Sandbox.Navigation;

public sealed partial class VBSoldierController
{
	[Property, Group( "Survival" )] public bool AvoidVisibleHazards { get; set; } = true;
	[Property, Group( "Survival" )] public float HazardReactionTime { get; set; } = 0.2f;
	[Property, Group( "Survival" )] public float VehicleLookAhead { get; set; } = 1.5f;
	private readonly Dictionary<GameObject, SeenHazard> _seenHazards = new();
	private float _nextHazardSense, _nextEscapePlan, _evadeUntil;
	private bool _evading, _survivalActive;
	private Vector3 _escapeDestination;
	private Vector3[] _nearbyThreatPositions = Array.Empty<Vector3>();
	private readonly record struct SeenHazard( Vector3 Position, Vector3 Velocity, float Radius, float SeenAt,
		float ExpiresAt, float ReactAt, bool Vehicle );

	private void SenseHazards()
	{
		if ( Time.Now < _nextHazardSense ) return;
		_nextHazardSense = Time.Now + 0.15f;
		foreach ( var stale in _seenHazards.Where( x => !x.Key.IsValid() || Time.Now > x.Value.ExpiresAt ).Select( x => x.Key ).ToArray() ) _seenHazards.Remove( stale );
		foreach ( var grenade in Scene.GetAll<VBNpcGrenade>() )
		{
			if ( !grenade.IsLive || !Perception.CanSeePoint( grenade.WorldPosition, grenade.GameObject ) ) continue;
			var velocity = grenade.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
			Observe( grenade.GameObject, grenade.WorldPosition, velocity, grenade.BlastRadius + 64f, grenade.RemainingFuse, false );
		}
		foreach ( var car in Scene.GetAll<VBVehicleController>() )
		{
			if ( !car.Body.IsValid() || !car.Body.PhysicsBody.IsValid() ) continue;
			var velocity = car.Body.IsProxy ? car.LinearVelocity : car.Body.Velocity;
			if ( velocity.WithZ( 0f ).Length < 140f || !Perception.CanSeePoint( car.Body.PhysicsBody.MassCenter, car.GameObject ) ) continue;
			var bounds = car.Body.PhysicsBody.GetBounds();
			float radius = MathF.Max( bounds.Size.x, bounds.Size.y ) * 0.5f + Agent.Radius + 24f;
			Observe( car.GameObject, car.WorldPosition, velocity, radius, 0.65f, true );
		}
		_nearbyThreatPositions = Scene.GetAll<VBHealthComponent>().Where( h => !h.IsDead
			&& Vector3.DistanceBetween( h.WorldPosition, WorldPosition ) < Threats.CloseThreatRange
			&& Perception.CanSee( h.GameObject ) ).Select( h => h.WorldPosition ).ToArray();
	}

	private void Observe( GameObject source, Vector3 position, Vector3 velocity, float radius, float memory, bool vehicle )
	{
		float reaction = _seenHazards.TryGetValue( source, out var known ) ? known.ReactAt : Time.Now + MathF.Max( 0f, HazardReactionTime );
		_seenHazards[source] = new SeenHazard( position, velocity, radius, Time.Now, Time.Now + memory, reaction, vehicle );
	}

	/// <summary>Distance from a seen danger corridor. Hidden objects use the last observation only.</summary>
	private float HazardClearance( SeenHazard hazard, Vector3 point )
	{
		float elapsed = MathF.Max( 0f, Time.Now - hazard.SeenAt );
		if ( hazard.Vehicle )
		{
			var position = hazard.Position + hazard.Velocity * elapsed;
			return VBSurvivalMath.VehicleClearance( position, hazard.Velocity, hazard.Radius, point, VehicleLookAhead, Agent.Height + 48f );
		}
		// Keep both last contact and short ballistic prediction: a seen flying
		// grenade can become dangerous before it reaches the soldier's feet.
		float flight = MathF.Min( elapsed + 0.35f, MathF.Max( 0f, hazard.ExpiresAt - hazard.SeenAt ) );
		return VBSurvivalMath.GrenadeClearance( hazard.Position, hazard.Velocity, Scene.PhysicsWorld.Gravity, flight, hazard.Radius, point );
	}

	private bool TickSurvival()
	{
		if ( !AvoidVisibleHazards || MoveSpeed <= 0f ) return false;
		SenseHazards();
		SeenHazard? danger = null;
		float clearance = 0f;
		foreach ( var hazard in _seenHazards.Values )
		{
			if ( Time.Now < hazard.ReactAt || Time.Now > hazard.ExpiresAt ) continue;
			float value = HazardClearance( hazard, WorldPosition );
			if ( value < clearance ) { danger = hazard; clearance = value; }
		}
		var nearby = _nearbyThreatPositions;
		bool surrounded = nearby.Length >= 3;
		Vector3 threat = danger?.Position ?? (surrounded ? nearby.Aggregate( Vector3.Zero, (sum, point) => sum + point ) / nearby.Length : WorldPosition);
		bool urgent = danger.HasValue || surrounded;
		if ( !urgent && (!_evading || Time.Now > _evadeUntil || Vector3.DistanceBetween( WorldPosition, _escapeDestination ) < 28f) )
		{ if ( _evading ) StopMoving(); _evading = _survivalActive = false; return false; }
		if ( !_survivalActive )
		{
			EndCrouchedFire();
			_survivalActive = true;
			ClearCover(); ClearTacticalMove(); _burstRemaining = 0;
			_hasTraversalGoal = false;
			// Interrupt committed attacks before their impact/release; ammo is not spent on an aborted throw.
			_meleePending = false; MeleeTarget = null; _grenadeRelease = 0f;
			_nextEscapePlan = 0f;
		}
		State = VBSoldierState.Evade;
		Agent.MaxSpeed = danger.HasValue ? MathF.Max( MoveSpeed, EmergencyMoveSpeed ) : MathF.Max( 0f, MoveSpeed );
		if ( urgent && Time.Now >= _nextEscapePlan )
		{
			_nextEscapePlan = Time.Now + 0.45f;
			if ( !FindEscape( danger, nearby, threat, out var point ) )
			{
				var away = (WorldPosition - threat).WithZ( 0f ).Normal;
				if ( away.IsNearZeroLength ) away = -WorldRotation.Forward;
				foreach ( float angle in new[] { 0f, 45f, -45f, 90f, -90f } )
					if ( TryEscapeDrop( Rotation.FromYaw( angle ) * away ) ) { _evading = false; return true; }
				StopMoving();
			}
			else if ( TryMove( point, allowTraversal: false ) )
			{ _escapeDestination = point; _evading = true; _evadeUntil = Time.Now + 0.75f; }
		}
		if ( danger.HasValue ) { ReleaseFireSlot(); Face( Agent.WishVelocity ); }
		else
		{
			var target = Perception.CurrentTarget;
			if ( Perception.TargetVisible && Perception.IsViableTarget( target ) )
			{ SelectCombatWeapon(); UpdateCombatAim( target ); FaceCombatAim(); TickFire( target ); }
		}
		return true;
	}

	private bool FindEscape( SeenHazard? danger, Vector3[] nearby, Vector3 threat, out Vector3 best )
	{
		best = default;
		float Score( Vector3 point )
		{
			float value = danger.HasValue ? HazardClearance( danger.Value, point ) : 300f;
			foreach ( var hazard in _seenHazards.Values )
				if ( Time.Now >= hazard.ReactAt && Time.Now <= hazard.ExpiresAt ) value = MathF.Min( value, HazardClearance( hazard, point ) );
			foreach ( var position in nearby ) value = MathF.Min( value, Vector3.DistanceBetween( point, position ) - 100f );
			return Math.Clamp( value, -600f, 600f );
		}
		float baseline = Score( WorldPosition ), bestScore = baseline + 24f;
		var away = (WorldPosition - threat).WithZ( 0f ).Normal;
		if ( away.IsNearZeroLength ) away = -WorldRotation.Forward;
		foreach ( float distance in new[] { 180f, 320f, 440f } )
		foreach ( float angle in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f } )
		{
			var candidate = WorldPosition + Rotation.FromYaw( angle ) * away * distance;
			var nav = Scene.NavMesh.GetClosestPoint( candidate, 48f );
			if ( !nav.HasValue || MathF.Abs( nav.Value.z - WorldPosition.z ) > 24f ) continue;
			candidate = nav.Value;
			float score = Score( candidate ) - Vector3.DistanceBetween( candidate, WorldPosition ) * 0.06f;
			if ( score <= bestScore ) continue;
			var path = Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = candidate, Agent = Agent } );
			if ( path.Status != NavMeshPathStatus.Complete || path.Points is not { Count: > 1 } ) continue;
			// A safe endpoint reached via the grenade/car's path is not an escape.
			bool safer = true;
			for ( int i = 1; i < path.Points.Count; i++ )
			{
				var from = path.Points[i - 1].Position; var to = path.Points[i].Position;
				int samples = Math.Clamp( (int)MathF.Ceiling( Vector3.DistanceBetween( from, to ) / 48f ), 1, 64 );
				for ( int sample = 1; sample <= samples; sample++ )
					if ( Score( Vector3.Lerp( from, to, sample / (float)samples ) ) < baseline - 16f ) { safer = false; break; }
				if ( !safer ) break;
			}
			if ( !safer || !TraversalGeometry.PathClear( WorldPosition, WorldPosition + (path.Points[1].Position - WorldPosition).ClampLength( 64f ) ) ) continue;
			best = candidate; bestScore = score;
		}
		return bestScore > baseline + 24f;
	}
}
