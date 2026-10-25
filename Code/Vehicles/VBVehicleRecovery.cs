using System;
using Sandbox;

/// <summary>Native use interaction for recovering an empty, settled vehicle from its roof.</summary>
[Title( "Vehicle Recovery" ), Category( "Void Breach/Vehicles" ), Icon( "flip_camera_android" )]
public sealed class VBVehicleRecovery : Component, Component.IPressable, IVBInteractionPromptProvider
{
	[Property] public VBVehicleController Vehicle { get; set; }
	[Property, Range( 32f, 240f )] public float ReachDistance { get; set; } = 130f;
	[Property, Range( 0f, 15f )] public float MaximumSpeedKmh { get; set; } = 5f;
	[Property, Range( 0.05f, 5f )] public float MaximumAngularSpeed { get; set; } = 1f;
	[Property, Range( 32f, 512f )] public float GroundProbeDepth { get; set; } = 200f;
	[Property, Range( 1f, 8f )] public float ClearancePadding { get; set; } = 2f;
	[Property, Group( "Motion" ), Range( 1f, 4f )] public float RollDuration { get; set; } = 1.8f;
	[Sync( SyncFlags.FromHost )] public bool IsRecovering { get; private set; }
	private float _nextRecovery;
	private float _startedAt;
	private Rotation _startRotation;
	private Vector3 _rollAxis, _groundPosition;
	private float _rollAngle, _restHeight;
	private Vector3[] _corners;

	protected override void OnStart()
	{
		if ( !Vehicle.IsValid() ) Vehicle = GetComponentInParent<VBVehicleController>( includeSelf: true );
	}

	private Vector3 GroundUp => Scene.PhysicsWorld.Gravity.IsNearZeroLength ? Vector3.Up : -Scene.PhysicsWorld.Gravity.Normal;
	private Collider[] ChassisShapes => Vehicle.GetComponentsInChildren<Collider>()
		.Where( shape => shape.Active && !shape.IsTrigger && shape.Rigidbody == Vehicle.Body ).ToArray();

	public bool CanRecover( PlayerController player )
	{
		if ( !Active || IsRecovering || !Vehicle.IsValid() || !Vehicle.Active || !Vehicle.IsConfigured || !Vehicle.IsOverturned
			|| !Vehicle.Body.Active || !Vehicle.Body.MotionEnabled || !player.IsValid() || Time.Now < _nextRecovery
			|| (Vehicle.IsProxy ? Vehicle.LinearVelocity : Vehicle.Body.Velocity).Length > VBVehicleController.KmhToUnits( MaximumSpeedKmh )
			|| (Vehicle.IsProxy ? Vehicle.AngularVelocity : Vehicle.Body.AngularVelocity).Length > MaximumAngularSpeed ) return false;
		var health = player.GetComponent<VBHealthComponent>();
		if ( health.IsValid() && (health.IsDead || health.IsDowned) ) return false;
		if ( player.GetComponent<VBVehiclePlayerDriver>()?.IsSeated == true ) return false;
		if ( Vehicle.GetComponentsInChildren<VBVehicleSeat>( includeDisabled: true )
			.Any( seat => seat.Occupant.IsValid() || seat.GetOccupant().IsValid() ) ) return false;
		var shapes = ChassisShapes;
		if ( shapes.Length == 0 ) return false;
		var closest = shapes.Select( shape => shape.FindClosestPoint( player.EyePosition ) )
			.OrderBy( point => (point - player.EyePosition).LengthSquared ).First();
		if ( (closest - player.EyePosition).Length > ReachDistance ) return false;
		var sight = Scene.Trace.Ray( player.EyePosition, closest ).IgnoreGameObjectHierarchy( player.GameObject )
			.IgnoreGameObjectHierarchy( Vehicle.GameObject ).WithoutTags( new[] { "trigger", "water" } ).Run();
		return !sight.Hit && !sight.StartedSolid && TryRecoveryPose( shapes, out var pose ) && IsRollClear( shapes, pose, player );
	}

	public bool CanPress( Component.IPressable.Event e ) => e.Source is PlayerController player && CanRecover( player );
	public bool Press( Component.IPressable.Event e )
	{
		if ( e.Source is not PlayerController player || !CanRecover( player ) ) return false;
		RequestRecovery( player );
		return true;
	}

	[Rpc.Host]
	public void RequestRecovery( PlayerController player )
	{
		if ( !Networking.IsHost || !player.IsValid() || player.Network.Owner != Rpc.Caller || Vehicle?.Body?.IsProxy == true
			|| !CanRecover( player ) || !TryRecoveryPose( ChassisShapes, out var pose ) ) return;
		BuildRoll( ChassisShapes, pose, player );
		_startedAt = Time.Now;
		IsRecovering = true;
		Vehicle.Body.Sleeping = false;
	}

	private void BuildRoll( Collider[] shapes, Transform destination, PlayerController player )
	{
		_startRotation = Vehicle.WorldRotation;
		var difference = destination.Rotation * _startRotation.Inverse;
		_rollAxis = new Vector3( difference.x, difference.y, difference.z ).Normal;
		_rollAngle = 2f * MathF.Acos( Math.Clamp( difference.w, -1f, 1f ) ) * 180f / MathF.PI;
		if ( _rollAngle > 180f ) { _rollAxis = -_rollAxis; _rollAngle = 360f - _rollAngle; }
		// At exactly upside down both directions work. Swing the roof away from
		// the person using the vehicle, then check that entire route for obstacles.
		if ( _rollAngle > 175f && Vector3.Dot( Vector3.Cross( _rollAxis, _startRotation.Up ), player.WorldPosition - Vehicle.WorldPosition ) > 0f )
		{
			_rollAxis = -_rollAxis; _rollAngle = 360f - _rollAngle;
		}
		_groundPosition = ProbeGround( Vehicle.WorldPosition ).EndPosition;
		_restHeight = Vector3.Dot( destination.Position - _groundPosition, GroundUp );
		_corners = shapes.SelectMany( shape => shape.LocalBounds.Corners.Select( corner =>
			Vehicle.WorldTransform.PointToLocal( shape.WorldTransform.PointToWorld( corner ) ) ) ).ToArray();
	}

	private Transform RollPose( float progress )
	{
		float eased = progress * progress * (3f - 2f * progress);
		var rotation = Rotation.FromAxis( _rollAxis, _rollAngle * eased ) * _startRotation;
		float lowest = _corners.Min( corner => Vector3.Dot( rotation * (corner * Vehicle.WorldScale), GroundUp ) );
		float height = MathF.Max( _restHeight, -lowest + ClearancePadding * 2f + 1f );
		return new Transform( _groundPosition + GroundUp * height, rotation, Vehicle.WorldScale );
	}

	private bool IsRollClear( Collider[] shapes, Transform destination, PlayerController player )
	{
		BuildRoll( shapes, destination, player );
		for ( int step = 0; step <= 8; step++ )
			if ( !IsChassisClear( shapes, RollPose( step / 8f ) ) ) return false;
		return true;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !IsRecovering ) return;
		if ( !Vehicle.IsValid() || !Vehicle.IsConfigured || !Vehicle.Body.Active || Vehicle.Body.IsProxy || _corners is null ) { FinishRecovery(); return; }
		float elapsed = Time.Now - _startedAt;
		var target = RollPose( Math.Clamp( elapsed / MathF.Max( RollDuration, 1f ), 0f, 1f ) );
		var body = Vehicle.Body;
		var error = target.Rotation * Vehicle.WorldRotation.Inverse;
		float sign = error.w < 0f ? -1f : 1f;
		var axis = new Vector3( error.x, error.y, error.z ) * sign;
		var rotationError = axis.Normal * (2f * MathF.Atan2( axis.Length, MathF.Abs( error.w ) ));
		var acceleration = (rotationError * 55f - body.AngularVelocity * 12f).ClampLength( 18f );
		// Convert the bounded angular acceleration through the actual body's
		// principal inertia. The collision solver remains free to oppose it.
		var inertiaRotation = Vehicle.WorldRotation * body.InertiaTensorRotation;
		var torque = inertiaRotation * ((inertiaRotation.Inverse * acceleration) * body.InertiaTensor);
		body.ApplyTorque( torque );
		var linearAcceleration = ((target.Position - Vehicle.WorldPosition) * 40f - body.Velocity * 12f).ClampLength( 550f );
		body.ApplyForce( (linearAcceleration - Scene.PhysicsWorld.Gravity) * body.Mass );
		// Release the assistance, with no transform or velocity snap. The tyres
		// and gravity finish settling the car back onto the road.
		if ( elapsed >= RollDuration + 0.25f && target.Rotation.Distance( Vehicle.WorldRotation ) < 5f
			&& body.AngularVelocity.Length < 0.25f || elapsed > RollDuration + 2.5f ) FinishRecovery();
	}

	private void FinishRecovery()
	{
		IsRecovering = false;
		_nextRecovery = Time.Now + 0.5f;
	}
	protected override void OnDisabled() { if ( Networking.IsHost ) FinishRecovery(); }

	private SceneTraceResult ProbeGround( Vector3 position )
	{
		var hit = Scene.Trace.Ray( position + GroundUp * 96f, position - GroundUp * GroundProbeDepth )
			.IgnoreGameObjectHierarchy( Vehicle.GameObject ).WithoutTags( new[] { "trigger", "water" } ).Run();
		// A ceiling or an object above the car must not become its destination.
		return hit.Hit && Vector3.Dot( hit.EndPosition - position, GroundUp ) > 4f ? default : hit;
	}
	private bool IsGround( SceneTraceResult hit ) => hit.Hit && !hit.StartedSolid && Vector3.Dot( hit.Normal, GroundUp ) > 0.65f;

	private bool TryRecoveryPose( Collider[] shapes, out Transform pose )
	{
		pose = Vehicle.WorldTransform;
		var up = GroundUp;
		var ground = ProbeGround( Vehicle.WorldPosition );
		if ( !IsGround( ground ) ) return false;
		// A nearby road is not enough: the current roof must already be resting
		// close to it. Do not turn this interaction into an airborne reset.
		float lowest = shapes.SelectMany( shape => shape.LocalBounds.Corners
			.Select( corner => Vector3.Dot( shape.WorldTransform.PointToWorld( corner ), up ) ) ).Min();
		if ( lowest - Vector3.Dot( ground.EndPosition, up ) > 16f ) return false;
		var forward = Vehicle.WorldRotation.Forward;
		forward = (forward - ground.Normal * Vector3.Dot( forward, ground.Normal )).Normal;
		if ( forward.IsNearZeroLength ) return false;
		var rotation = Rotation.LookAt( forward, ground.Normal );
		float lift = 0f;
		foreach ( var wheel in new[] { Vehicle.FrontLeft, Vehicle.FrontRight, Vehicle.RearLeft, Vehicle.RearRight } )
		{
			var local = Vehicle.WorldTransform.PointToLocal( wheel.WorldPosition ) * Vehicle.WorldScale;
			var hardpoint = ground.EndPosition + rotation * local;
			var support = ProbeGround( hardpoint );
			if ( !IsGround( support ) ) return false;
			var extendedCentre = hardpoint - rotation.Up * wheel.SuspensionTravel;
			float required = (wheel.Radius + ClearancePadding - Vector3.Dot( extendedCentre - support.EndPosition, support.Normal ))
				/ Vector3.Dot( up, support.Normal );
			lift = MathF.Max( lift, required );
		}
		pose = new Transform( ground.EndPosition + up * lift, rotation, Vehicle.WorldScale );
		if ( !IsChassisClear( shapes, pose ) ) return false;
		foreach ( var wheel in new[] { Vehicle.FrontLeft, Vehicle.FrontRight, Vehicle.RearLeft, Vehicle.RearRight } )
		{
			var local = Vehicle.WorldTransform.PointToLocal( wheel.WorldPosition );
			var centre = pose.PointToWorld( local ) - rotation.Up * wheel.SuspensionTravel;
			var space = Scene.Trace.Sphere( wheel.Radius, centre, centre ).IgnoreGameObjectHierarchy( Vehicle.GameObject )
				.WithoutTags( new[] { "trigger", "water" } ).Run();
			if ( space.Hit || space.StartedSolid ) return false;
		}
		return true;
	}

	private bool IsChassisClear( Collider[] shapes, Transform pose )
	{
		foreach ( var shape in shapes )
		{
			var target = pose.ToWorld( Vehicle.WorldTransform.ToLocal( shape.WorldTransform ) );
			var extents = shape.LocalBounds.Extents * target.Scale;
			var centre = target.PointToWorld( shape.LocalBounds.Center );
			var space = Scene.Trace.Box( new BBox( -extents, extents ).Grow( ClearancePadding ), centre, centre ).Rotated( target.Rotation )
				.IgnoreGameObjectHierarchy( Vehicle.GameObject ).WithoutTags( new[] { "trigger", "water" } ).Run();
			if ( space.Hit || space.StartedSolid ) return false;
		}
		return true;
	}

	public Component.IPressable.Tooltip? GetTooltip( Component.IPressable.Event e ) => CanPress( e )
		? new Component.IPressable.Tooltip( "#vb.vehicle.recover", "flip_camera_android", "" ) : null;
	public VBInteractionPromptState GetInteractionPromptState( GameObject viewer ) => new(
		viewer.IsValid() && CanRecover( viewer.GetComponent<PlayerController>() ), "#vb.vehicle.recover", "use", "#vb.vehicle.name" );
}
