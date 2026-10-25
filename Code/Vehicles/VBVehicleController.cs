using System;
using Sandbox;

/// <summary>Host-owned chassis. No input polling and no transform/velocity steering.</summary>
[Title( "Four Wheel Vehicle" ), Category( "Void Breach/Vehicles" ), Icon( "directions_car" )]
public sealed partial class VBVehicleController : Component
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public VBVehicleWheel FrontLeft { get; set; }
	[Property] public VBVehicleWheel FrontRight { get; set; }
	[Property] public VBVehicleWheel RearLeft { get; set; }
	[Property] public VBVehicleWheel RearRight { get; set; }
	[Property, Description( "Component implementing IVBVehicleControlSource (player seat or future AI)." )]
	public Component ControlSource { get; set; }
	[Property, Group( "Drivetrain" )] public VBVehicleDriveLayout DriveLayout { get; set; } = VBVehicleDriveLayout.All;
	[Property, Group( "Drivetrain" ), Range( 10f, 200f )] public float TopSpeedKmh { get; set; } = 110f;
	[Property, Group( "Drivetrain" ), Range( 5f, 60f )] public float ReverseSpeedKmh { get; set; } = 25f;
	[Property, Group( "Drivetrain" ), Range( 10f, 600f )] public float EngineAcceleration { get; set; } = 240f;
	[Property, Group( "Drivetrain" ), Range( 10f, 400f )] public float EnginePowerKw { get; set; } = 95f;
	[Property, Group( "Drivetrain" ), Range( 0f, 100f )] public float EngineBraking { get; set; } = 18f;
	[Property, Group( "Turbo" )] public bool EnableTurbo { get; set; }
	[Property, Group( "Turbo" ), Range( 1f, 3f )] public float TurboTorqueMultiplier { get; set; } = 1.55f;
	[Property, Group( "Turbo" ), Range( 1f, 3f )] public float TurboPowerMultiplier { get; set; } = 1.5f;
	[Property, Group( "Turbo" ), Range( 1f, 2f )] public float TurboTopSpeedMultiplier { get; set; } = 1.25f;
	[Sync( SyncFlags.FromHost )] public bool IsTurboActive { get; private set; }
	[Property, Group( "Off Road" )] public bool AutomaticLowGear { get; set; }
	[Property, Group( "Off Road" ), Description( "Bias drive torque toward grounded driven wheels, within their tyre grip. Useful for off-road differentials." )]
	public bool RedistributeDriveTorque { get; set; }
	[Property, Group( "Off Road" ), Range( 1f, 5f )] public float LowGearTorqueRatio { get; set; } = 3f;
	[Property, Group( "Off Road" ), Range( 5f, 60f )] public float LowGearSpeedKmh { get; set; } = 20f;
	[Property, Group( "Off Road" ), Range( 5f, 45f )] public float LowGearInclineDegrees { get; set; } = 12f;
	[Sync( SyncFlags.FromHost )] public bool IsLowGear { get; private set; }
	[Property, Group( "Brakes" ), Range( 50f, 1200f )] public float BrakeAcceleration { get; set; } = 520f;
	[Property, Group( "Brakes" ), Range( 50f, 1600f )] public float HandbrakeAcceleration { get; set; } = 850f;
	[Property, Group( "Brakes" ), Range( 0.1f, 1f )] public float HandbrakeGripScale { get; set; } = 0.6f;
	[Property, Group( "Steering" ), Range( 5f, 60f )] public float MaximumSteerAngle { get; set; } = 33f;
	[Property, Group( "Steering" ), Range( 1f, 20f )] public float SteeringResponse { get; set; } = 8f;
	[Property, Group( "Steering" ), Range( 0.1f, 1f )] public float HighSpeedSteerScale { get; set; } = 0.3f;
	[Property, Group( "Steering" ), Range( 1f, 20f )] public float PedalResponse { get; set; } = 6f;
	[Property, Group( "Stability" ), Range( 0f, 2f )] public float AntiRollStrength { get; set; } = 0.4f;
	[Property, Group( "Resistance" ), Range( 0f, 40f )] public float RollingResistance { get; set; } = 8f;
	[Property, Group( "Resistance" ), Range( 0f, 0.001f )] public float AerodynamicDrag { get; set; } = 0.000025f;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float SpeedKmh { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Direction { get; private set; } = 1;
	public VBVehicleControls CurrentControls { get; private set; }
	[Sync( SyncFlags.FromHost )] public int GroundedWheels { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public Vector3 LinearVelocity { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public Vector3 AngularVelocity { get; private set; }
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, PlayerController> SeatOccupants { get; set; } = new();
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public Vector3 FrontLeftPresentation { get; private set; } = new( 16f, 0f, 0f );
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public Vector3 FrontRightPresentation { get; private set; } = new( 16f, 0f, 0f );
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public Vector3 RearLeftPresentation { get; private set; } = new( 16f, 0f, 0f );
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public Vector3 RearRightPresentation { get; private set; } = new( 16f, 0f, 0f );
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float FrontLeftSpin { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float FrontRightSpin { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float RearLeftSpin { get; private set; }
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float RearRightSpin { get; private set; }
	[Sync( SyncFlags.FromHost )] public int GroundedMask { get; private set; }
	public bool IsConfigured => Body.IsValid() && FrontLeft.IsValid() && FrontRight.IsValid() && RearLeft.IsValid() && RearRight.IsValid();
	public bool IsRecovering => GetComponent<VBVehicleRecovery>()?.IsRecovering == true;
	[Property, Group( "Recovery" ), Range( -0.95f, 0f )] public float OverturnedUpDotThreshold { get; set; } = -0.2f;
	public bool IsOverturned
	{
		get
		{
			var gravity = Scene.PhysicsWorld.Gravity;
			var up = gravity.IsNearZeroLength ? Vector3.Up : -gravity.Normal;
			return Vector3.Dot( WorldRotation.Up, up ) < OverturnedUpDotThreshold;
		}
	}
	private float _steering, _throttle;
	private bool _warned;
	public const float UnitsPerMetre = 39.3700787f;
	public static float KmhToUnits( float kmh ) => kmh / 3.6f * UnitsPerMetre;

	protected override void OnStart()
	{
		if ( !Body.IsValid() ) Body = GetComponent<Rigidbody>();
		if ( !ControlSource.IsValid() ) ControlSource = GetComponentInChildren<VBVehicleDriverSeat>();
		EnsureHostNetworking();
		if ( !IsConfigured ) Log.Warning( $"Vehicle '{GameObject.Name}' requires a Rigidbody and four wheel references." );
	}

	private void EnsureHostNetworking()
	{
		if ( Networking.IsHost && Game.IsPlaying )
		{
			// Scene-snapshot objects never stream their changing transforms. This
			// also repairs older placed prefab instances which override the mode.
			if ( !Network.Active ) GameObject.NetworkSpawn( new NetworkSpawnOptions { Owner = Connection.Host, OrphanedMode = NetworkOrphaned.Host, OwnerTransfer = OwnerTransfer.Fixed, AlwaysTransmit = true } );
			if ( Network.Owner is not null && Network.Owner != Connection.Host ) Network.AssignOwnership( Connection.Host );
			Network.SetOrphanedMode( NetworkOrphaned.Host ); Network.SetOwnerTransfer( OwnerTransfer.Fixed );
			Network.AlwaysTransmit = true;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !IsConfigured || !Body.Active || !Body.MotionEnabled ) return;
		if ( !Network.Active || Network.Owner is not null && Network.Owner != Connection.Host ) EnsureHostNetworking();
		if ( Body.IsProxy )
		{
			if ( !_warned ) { Log.Warning( "VBVehicleController requires host ownership. Spawn the vehicle on the host without transferring it to the driver." ); _warned = true; }
			return;
		}
		float dt = MathF.Max( Scene.FixedDelta, 0.001f );
		LinearVelocity = Body.Velocity;
		AngularVelocity = Body.AngularVelocity;
		var controls = !IsRecovering && ControlSource.IsValid() && ControlSource.Active && ControlSource is IVBVehicleControlSource source
			? source.GetVehicleControls( this ).Sanitized() : VBVehicleControls.Parked;
		CurrentControls = controls;
		float speed = Vector3.Dot( Body.Velocity, WorldRotation.Forward );
		SpeedKmh = Body.Velocity.Length / UnitsPerMetre * 3.6f;
		float pedal = controls.Throttle;
		float brake = controls.Brake;
		// Opposite pedal brakes first; reversing cannot instantly invert the car's velocity.
		// Uphill throttle must develop torque even if gravity has started rolling
		// the car backwards; treating rollback as a direction change stalls it.
		bool pullingUphill = AutomaticLowGear && MathF.Abs( pedal ) > 0.01f
			&& WorldRotation.Forward.z * MathF.Sign( pedal ) > MathF.Sin( LowGearInclineDegrees * MathF.PI / 180f );
		if ( pedal * speed < -20f && !pullingUphill ) { brake = MathF.Max( brake, MathF.Abs( pedal ) ); pedal = 0f; }
		if ( pedal != 0f ) Direction = pedal > 0f ? 1 : -1;
		_throttle += (pedal - _throttle) * (1f - MathF.Exp( -PedalResponse * dt ));
		if ( brake > 0f || controls.Handbrake ) _throttle = 0f;
		float speedFraction = Math.Clamp( MathF.Abs( speed ) / KmhToUnits( TopSpeedKmh ), 0f, 1f );
		float steerLimit = MaximumSteerAngle * (1f + (HighSpeedSteerScale - 1f) * speedFraction);
		_steering += (controls.Steering * steerLimit - _steering) * (1f - MathF.Exp( -SteeringResponse * dt ));
		GetFrontSteerAngles( out var left, out var right );
		FrontLeft.Sample( this, left ); FrontRight.Sample( this, right );
		RearLeft.Sample( this, 0f ); RearRight.Sample( this, 0f );
		BalanceAxle( FrontLeft, FrontRight ); BalanceAxle( RearLeft, RearRight );
		GroundedWheels = (FrontLeft.Grounded ? 1 : 0) + (FrontRight.Grounded ? 1 : 0) + (RearLeft.Grounded ? 1 : 0) + (RearRight.Grounded ? 1 : 0);
		GroundedMask = (FrontLeft.Grounded ? 1 : 0) | (FrontRight.Grounded ? 2 : 0) | (RearLeft.Grounded ? 4 : 0) | (RearRight.Grounded ? 8 : 0);
		FrontLeftPresentation = FrontLeft.Presentation; FrontRightPresentation = FrontRight.Presentation;
		RearLeftPresentation = RearLeft.Presentation; RearRightPresentation = RearRight.Presentation;
		FrontLeftSpin = FrontLeft.AdvanceSpin( dt ); FrontRightSpin = FrontRight.AdvanceSpin( dt );
		RearLeftSpin = RearLeft.AdvanceSpin( dt ); RearRightSpin = RearRight.AdvanceSpin( dt );
		IsTurboActive = EnableTurbo && controls.Turbo && _throttle > 0.01f && pedal > 0f
			&& brake <= 0f && !controls.Handbrake && GroundedWheels >= 2;
		float turboTorque = IsTurboActive ? Math.Clamp( TurboTorqueMultiplier, 1f, 3f ) : 1f;
		float turboPower = IsTurboActive ? Math.Clamp( TurboPowerMultiplier, 1f, 3f ) : 1f;
		float powerForce = EnginePowerKw * turboPower * 1000f * UnitsPerMetre * UnitsPerMetre / MathF.Max( MathF.Abs( speed ), 100f );
		IsLowGear = AutomaticLowGear && GroundedWheels >= 2 && MathF.Abs( _throttle ) > 0.01f
			&& WorldRotation.Forward.z * MathF.Sign( _throttle ) > MathF.Sin( LowGearInclineDegrees * MathF.PI / 180f )
			&& SpeedKmh < LowGearSpeedKmh;
		float torqueRatio = IsLowGear ? Math.Clamp( LowGearTorqueRatio, 1f, 5f ) : 1f;
		float force = MathF.Min( Body.Mass * EngineAcceleration * torqueRatio * turboTorque, powerForce ) * _throttle;
		float limit = KmhToUnits( _throttle >= 0f ? TopSpeedKmh * (IsTurboActive ? Math.Clamp( TurboTopSpeedMultiplier, 1f, 2f ) : 1f) : ReverseSpeedKmh );
		// Smooth torque fade, not a velocity clamp. Gravity and collisions may exceed this speed.
		force *= Math.Clamp( (limit - MathF.Abs( speed )) / (limit * 0.12f), 0f, 1f );
		bool driveFront = DriveLayout != VBVehicleDriveLayout.Rear;
		bool driveRear = DriveLayout != VBVehicleDriveLayout.Front;
		int drivenContacts = (driveFront ? (FrontLeft.Grounded ? 1 : 0) + (FrontRight.Grounded ? 1 : 0) : 0)
			+ (driveRear ? (RearLeft.Grounded ? 1 : 0) + (RearRight.Grounded ? 1 : 0) : 0);
		float forcePerWheel = force / (RedistributeDriveTorque ? Math.Max( drivenContacts, 1 ) : DriveLayout == VBVehicleDriveLayout.All ? 4 : 2);
		float frontForce = driveFront ? forcePerWheel : 0f;
		float rearForce = driveRear ? forcePerWheel : 0f;
		FrontLeft.ApplyContactForces( this, frontForce, brake, false, dt ); FrontRight.ApplyContactForces( this, frontForce, brake, false, dt );
		RearLeft.ApplyContactForces( this, rearForce, brake, controls.Handbrake, dt ); RearRight.ApplyContactForces( this, rearForce, brake, controls.Handbrake, dt );
		Body.ApplyForce( -Body.Velocity * Body.Velocity.Length * (Body.Mass * AerodynamicDrag) );
	}

	internal Vector3 GetWheelPresentation( VBVehicleWheel wheel ) => wheel == FrontLeft ? FrontLeftPresentation
		: wheel == FrontRight ? FrontRightPresentation : wheel == RearLeft ? RearLeftPresentation : RearRightPresentation;
	internal float GetWheelSpin( VBVehicleWheel wheel ) => wheel == FrontLeft ? FrontLeftSpin
		: wheel == FrontRight ? FrontRightSpin : wheel == RearLeft ? RearLeftSpin : RearRightSpin;
	internal bool IsWheelGrounded( VBVehicleWheel wheel ) => (GroundedMask & (wheel == FrontLeft ? 1 : wheel == FrontRight ? 2 : wheel == RearLeft ? 4 : 8)) != 0;

	private void GetFrontSteerAngles( out float left, out float right )
	{
		left = right = -_steering; // Positive driver steering means right; positive Source yaw means left.
		if ( MathF.Abs( _steering ) < 0.1f ) return;
		float wheelbase = ((FrontLeft.WorldPosition + FrontRight.WorldPosition - RearLeft.WorldPosition - RearRight.WorldPosition) * 0.5f).Length;
		float track = (FrontLeft.WorldPosition - FrontRight.WorldPosition).Length;
		if ( wheelbase < 1f ) return;
		float radius = wheelbase / MathF.Tan( MathF.Abs( _steering ) * MathF.PI / 180f );
		float inner = MathF.Atan( wheelbase / MathF.Max( radius - track * 0.5f, 1f ) ) * 180f / MathF.PI;
		float outer = MathF.Atan( wheelbase / (radius + track * 0.5f) ) * 180f / MathF.PI;
		left = -MathF.Sign( _steering ) * (_steering > 0f ? outer : inner);
		right = -MathF.Sign( _steering ) * (_steering > 0f ? inner : outer);
	}

	private void BalanceAxle( VBVehicleWheel left, VBVehicleWheel right )
	{
		if ( !left.Grounded || !right.Grounded ) return;
		float maxTransfer = MathF.Min( left.NormalLoad, right.NormalLoad );
		float transfer = Math.Clamp( (left.Compression - right.Compression) * Body.Mass * AntiRollStrength * 10f, -maxTransfer, maxTransfer );
		left.NormalLoad += transfer;
		right.NormalLoad -= transfer;
	}

	protected override void OnValidate()
	{
		TopSpeedKmh = Math.Clamp( TopSpeedKmh, 10f, 200f ); ReverseSpeedKmh = Math.Clamp( ReverseSpeedKmh, 5f, 60f );
		EngineAcceleration = Math.Clamp( EngineAcceleration, 10f, 600f ); EnginePowerKw = Math.Clamp( EnginePowerKw, 10f, 400f );
		MaximumSteerAngle = Math.Clamp( MaximumSteerAngle, 5f, 60f ); SteeringResponse = Math.Clamp( SteeringResponse, 1f, 20f );
		PedalResponse = Math.Clamp( PedalResponse, 1f, 20f ); AntiRollStrength = Math.Clamp( AntiRollStrength, 0f, 2f );
	}
}
