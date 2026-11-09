using System;
using Sandbox;

public enum VBVehicleTurretAccess { Driver, Gunner, DriverAndGunner }

/// <summary>Host-authoritative mounted gun. Aim commands never contain a victim or damage value.</summary>
[Title( "Vehicle Turret" ), Category( "Void Breach/Vehicles" ), Icon( "military_tech" )]
public sealed class VBVehicleTurret : Component
{
	[Property] public VBVehicleController Vehicle { get; set; }
	[Property] public VBVehicleDriverSeat DriverSeat { get; set; }
	[Property] public VBVehicleGunnerSeat GunnerSeat { get; set; }
	[Property] public VBVehicleTurretAccess Access { get; set; } = VBVehicleTurretAccess.DriverAndGunner;
	[Property, Group( "Gun" )] public Vector3 YawPivot { get; set; } = new( -8.19f, 0f, 52.7f );
	[Property, Group( "Gun" )] public Vector3 PitchPivot { get; set; } = new( 20.7f, 0f, 60.64f );
	[Property, Group( "Gun" )] public Vector3 MuzzleOffset { get; set; } = new( 80f, 0f, 74.72f );
	[Property, Group( "Gun" )] public Vector3 CameraOffset { get; set; } = new( 0f, 0f, 32f );
	[Property, Group( "Gun" )] public float Damage { get; set; } = 55f;
	[Property, Group( "Gun" )] public float ShotsPerSecond { get; set; } = 8f;
	[Property, Group( "Gun" )] public float Range { get; set; } = 10000f;
	[Property, Group( "Gun" )] public float TurnSpeed { get; set; } = 180f;
	[Property, Group( "Rig" ), Range( 1f, 60f )] public float VisualResponse { get; set; } = 32f;
	[Property, Group( "Gun" ), Description( "Degrees: X (negative) is maximum elevation, Y (positive) is maximum downward aim." )]
	public Vector2 PitchLimits { get; set; } = new( -35f, 45f );
	[Property, Group( "Gun" )] public SoundEvent FireSound { get; set; }
	[Property, Group( "Rig" )] public SkinnedModelRenderer Renderer { get; set; }
	[Property, Group( "Rig" )] public string YawBone { get; set; }
	[Property, Group( "Rig" )] public string PitchBone { get; set; }
	[Property, Group( "Rig" )] public float BonePivotScale { get; set; } = 1f;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float Yaw { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float Pitch { get; private set; }
	[Sync( SyncFlags.FromHost )] public PlayerController Operator { get; private set; }
	private PlayerController _commandPlayer;
	private Angles _aim;
	private bool _fire;
	private float _lastInput = float.NegativeInfinity, _nextShot;
	private readonly List<(int Index, Transform Bind, bool Elevates)> _bones = new();
	private Model _model;
	private Vector3 _yawBindPivot, _pitchBindPivot;
	private float _visualYaw, _visualPitch;
	private TimeSince _timeSinceHudShot = 999f;
	public float HudShotAge => _timeSinceHudShot;

	public bool CanOperate( PlayerController player )
	{
		if ( !Active || !Vehicle.IsValid() || !player.IsValid() ) return false;
		var health = player.GetComponent<VBHealthComponent>();
		if ( health.IsValid() && (health.IsDead || health.IsDowned) ) return false;
		if ( Access != VBVehicleTurretAccess.Driver && GunnerSeat.IsValid() && GunnerSeat.Occupant.IsValid() )
			return GunnerSeat.Occupant == player && GunnerSeat.GetOccupant() == player;
		return Access != VBVehicleTurretAccess.Gunner && DriverSeat.IsValid()
			&& DriverSeat.Driver == player && DriverSeat.GetOccupant() == player;
	}

	internal void SubmitControls( PlayerController player, Angles worldAim, bool fire )
	{
		if ( !Networking.IsHost || !CanOperate( player ) || !float.IsFinite( worldAim.pitch ) || !float.IsFinite( worldAim.yaw ) ) return;
		var relative = (Vehicle.WorldRotation.Inverse * Rotation.From( new Angles( worldAim.pitch, worldAim.yaw, 0f ) )).Angles();
		_aim = new( Math.Clamp( relative.pitch, PitchLimits.x, PitchLimits.y ), Wrap( relative.yaw ), 0f );
		_commandPlayer = player; _fire = fire; _lastInput = Time.Now;
	}

	internal void ClearPlayerCommands( PlayerController player )
	{
		if ( !Networking.IsHost || _commandPlayer != player ) return;
		_commandPlayer = null; _fire = false; _lastInput = float.NegativeInfinity; Operator = null;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Vehicle.IsValid() || Vehicle.IsProxy ) return;
		if ( Time.Now - _lastInput > 0.35f || !CanOperate( _commandPlayer ) ) { Operator = null; _fire = false; return; }
		Operator = _commandPlayer;
		float step = Math.Clamp( TurnSpeed, 1f, 720f ) * Scene.FixedDelta;
		Yaw += Math.Clamp( Wrap( _aim.yaw - Yaw ), -step, step );
		Pitch += Math.Clamp( _aim.pitch - Pitch, -step, step );
		// Shots follow the actual barrel; holding fire while turning cannot shoot through the chassis.
		if ( _fire && Time.Now >= _nextShot ) Fire();
	}

	private static float Wrap( float angle ) => ((angle + 180f) % 360f + 360f) % 360f - 180f;
	public Rotation GunRotation => Vehicle.WorldRotation * Rotation.From( new Angles( Pitch, Yaw, 0f ) );
	public Vector3 MuzzlePosition => Vehicle.WorldTransform.PointToWorld( YawPivot + Rotation.FromYaw( Yaw )
		* (PitchPivot - YawPivot + Rotation.FromPitch( Pitch ) * (MuzzleOffset - PitchPivot)) );
	public Vector3 CameraPosition => Vehicle.WorldTransform.PointToWorld( YawPivot + CameraOffset );

	/// <summary>The same physical barrel ray is used for shots and the HUD impact marker.</summary>
	public SceneTraceResult TraceBarrel() => Scene.Trace.Ray( MuzzlePosition,
		MuzzlePosition + GunRotation.Forward * Math.Clamp( Range, 100f, 50000f ) )
		.IgnoreGameObjectHierarchy( Vehicle.GameObject ).UseHitboxes( true )
		.WithoutTags( new[] { "trigger", "water" } ).Run();

	/// <summary>Resolve parallax from fixed pivots, without feeding the rotating muzzle back into the aim.</summary>
	public Angles GetAimForView( Transform view )
	{
		if ( !Vehicle.IsValid() ) return view.Rotation.Angles();
		var trace = Scene.Trace.Ray( view.Position, view.Position + view.Rotation.Forward * Range )
			.IgnoreGameObjectHierarchy( Vehicle.GameObject ).UseHitboxes( true )
			.WithoutTags( new[] { "trigger", "water" } ).Run();
		// A compressed chase camera can see a wall behind the gun. Do not turn the
		// barrel backwards to reach a point already behind its fixed aiming pivot.
		var pivot = Vehicle.WorldTransform.PointToWorld( YawPivot );
		if ( Vector3.Dot( trace.EndPosition - pivot, view.Rotation.Forward ) <= 0f ) return view.Rotation.Angles();
		return GetAimForPoint( trace.EndPosition, view.Rotation.Angles() );
	}

	public Angles GetAimForPoint( Vector3 worldPoint, Angles fallback )
	{
		if ( !Vehicle.IsValid() ) return fallback;
		var point = Vehicle.WorldTransform.PointToLocal( worldPoint );
		var relativeSight = (Vehicle.WorldRotation.Inverse * fallback.ToRotation()).Angles();
		var flat = (point - YawPivot).WithZ( 0f );
		float yaw = relativeSight.yaw;
		float sideways = MuzzleOffset.y - YawPivot.y;
		if ( flat.Length > MathF.Abs( sideways ) + 1f )
			yaw = (MathF.Atan2( flat.y, flat.x ) - MathF.Asin( sideways / flat.Length )) * 180f / MathF.PI;
		var yawRotation = Rotation.FromYaw( yaw );
		var pitchOrigin = YawPivot + yawRotation * (PitchPivot - YawPivot);
		var delta = yawRotation.Inverse * (point - pitchOrigin);
		float distance = MathF.Sqrt( delta.x * delta.x + delta.z * delta.z );
		float barrelOffset = MuzzleOffset.z - PitchPivot.z;
		float pitch = relativeSight.pitch;
		if ( distance > MathF.Abs( barrelOffset ) + 1f && delta.x > 0f )
		{
			float angle = MathF.Asin( barrelOffset / distance ) - MathF.Atan2( delta.z, delta.x );
			// A point behind the muzzle cannot be hit along the barrel's forward ray.
			// Keep sight direction for that close range, rather than alternating yaw.
			if ( delta.x * MathF.Cos( angle ) - delta.z * MathF.Sin( angle ) >= MuzzleOffset.x - PitchPivot.x )
				pitch = angle * 180f / MathF.PI;
		}
		pitch = Math.Clamp( pitch, PitchLimits.x, PitchLimits.y );
		return (Vehicle.WorldRotation * Rotation.From( new Angles( pitch, yaw, 0f ) )).Angles();
	}

	private void Fire()
	{
		_nextShot = Time.Now + 1f / Math.Clamp( ShotsPerSecond, 0.5f, 30f );
		var muzzle = MuzzlePosition;
		var clear = Scene.Trace.Ray( CameraPosition, muzzle ).IgnoreGameObjectHierarchy( Vehicle.GameObject ).Run();
		if ( clear.Hit || clear.StartedSolid ) return;
		var trace = TraceBarrel();
		if ( trace.Hit && trace.GameObject.IsValid() )
		{
			var health = trace.GameObject.GetComponentInParent<VBHealthComponent>( includeSelf: true );
			var force = GunRotation.Forward * 1600f;
			if ( health.IsValid() ) health.TakeDamage( VBDamageInfo.FromBullet( Math.Clamp( Damage, 0f, 1000f ),
				Operator.GameObject, GameObject, trace.EndPosition, force, trace.Hitbox?.Tags.Has( "head" ) == true, 0.25f ) );
			else if ( trace.GameObject.GetComponentInParent<Component.IDamageable>( includeSelf: true ) is { } damageable )
				damageable.OnDamage( new DamageInfo( Damage, Operator.GameObject, GameObject ) { Position = trace.EndPosition } );
			if ( trace.Body.IsValid() && trace.Body.BodyType == PhysicsBodyType.Dynamic && !health.IsValid() )
				trace.Body.ApplyImpulseAt( trace.EndPosition, force );
		}
		VBAiSoundStimulus.Emit( muzzle, 3000f, Operator.GameObject, VBAiSoundKind.Gunshot );
		ShowShot( muzzle, trace.EndPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void ShowShot( Vector3 start, Vector3 end )
	{
		_timeSinceHudShot = 0f;
		// This presentation RPC already runs once on each peer, just like handheld shot effects.
		VoidBreach.Water.FluidImpacts.Segment( Scene, start, end, radius: 18, strength: 140 );
		var effect = new GameObject( false, "Mounted gun tracer" ) { Flags = GameObjectFlags.NotSaved, NetworkMode = NetworkMode.Never };
		effect.WorldPosition = start;
		var tracer = effect.AddComponent<TracerEffect>();
		tracer.EndPoint = end;
		tracer.DistancePerSecond = 12000f; tracer.Length = 160f; tracer.StartDistance = 0f;
		tracer.CastShadows = false;
		effect.AddComponent<TemporaryEffect>();
		effect.Enabled = true;
		if ( FireSound is not null ) Sound.Play( FireSound, start );
	}

	protected override void OnPreRender()
	{
		if ( !Vehicle.IsValid() ) return;
		if ( string.IsNullOrWhiteSpace( YawBone ) || string.IsNullOrWhiteSpace( PitchBone ) ) return;
		if ( !Renderer.IsValid() || Renderer.Model is null || Renderer.SceneObject is not SceneModel sceneModel ) return;
		var model = Renderer.Model;
		if ( _model != model )
		{
			_model = model; _bones.Clear();
			_visualYaw = Yaw; _visualPitch = Pitch;
			var yaw = model.Bones.GetBone( YawBone ); var pitch = model.Bones.GetBone( PitchBone );
			if ( yaw is null || pitch is null ) { Log.Warning( "Turret rig bones not found." ); return; }
			_yawBindPivot = model.GetBoneTransform( yaw.Index ).Position * BonePivotScale;
			_pitchBindPivot = model.GetBoneTransform( pitch.Index ).Position * BonePivotScale;
			for ( int i = 0; i < model.BoneCount; i++ )
			{
				bool inYaw = false, inPitch = false;
				for ( int ancestor = i; ancestor >= 0; ancestor = model.GetBoneParent( ancestor ) )
				{ inYaw |= ancestor == yaw.Index; inPitch |= ancestor == pitch.Index; }
				if ( inYaw ) _bones.Add( (i, model.GetBoneTransform( i ), inPitch) );
			}
		}
		float blend = 1f - MathF.Exp( -Math.Clamp( VisualResponse, 1f, 60f ) * MathF.Max( Time.Delta, 0f ) );
		_visualYaw += Wrap( Yaw - _visualYaw ) * blend;
		_visualPitch += (Pitch - _visualPitch) * blend;
		var yawRotation = Rotation.FromYaw( _visualYaw ); var pitchRotation = Rotation.FromPitch( _visualPitch );
		foreach ( var bone in _bones )
		{
			var delta = yawRotation * (bone.Elevates ? pitchRotation : Rotation.Identity);
			var pivot = bone.Elevates ? _pitchBindPivot : _yawBindPivot;
			var position = _yawBindPivot + yawRotation * (pivot - _yawBindPivot
				+ (bone.Elevates ? pitchRotation : Rotation.Identity) * (bone.Bind.Position - pivot));
			sceneModel.SetBoneOverride( bone.Index, new Transform( position, delta * bone.Bind.Rotation, bone.Bind.Scale ) );
		}
	}

	protected override void OnValidate()
	{
		_model = null;
		ShotsPerSecond = Math.Clamp( ShotsPerSecond, 0.5f, 30f );
		Damage = Math.Clamp( Damage, 0f, 1000f );
		Range = Math.Clamp( Range, 100f, 50000f );
		TurnSpeed = Math.Clamp( TurnSpeed, 1f, 720f );
		PitchLimits = new( Math.Clamp( PitchLimits.x, -85f, 0f ), Math.Clamp( PitchLimits.y, 0f, 85f ) );
	}
}
