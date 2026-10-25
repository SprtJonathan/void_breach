using System;
using Sandbox;

/// <summary>A suspension hardpoint. Its child visual has no collider or Rigidbody.</summary>
[Title( "Vehicle Wheel" ), Category( "Void Breach/Vehicles" ), Icon( "tire_repair" )]
public sealed class VBVehicleWheel : Component, VoidBreach.Water.IFluidContactBounds
{
	[Property] public GameObject Visual { get; set; }
	[Property, Group( "Rigged Visual" )] public SkinnedModelRenderer WheelRenderer { get; set; }
	[Property, Group( "Rigged Visual" )] public string WheelBone { get; set; }
	[Property, Group( "Rigged Visual" ), Description( "Pivot correction for legacy imported rigs whose bind bones use different units from the mesh. Normally 1." )]
	public float BonePivotScale { get; set; } = 1f;
	[Property, Range( 4f, 40f )] public float Radius { get; set; } = 14f;
	[Property, Range( 2f, 30f )] public float SuspensionTravel { get; set; } = 14f;
	[Property, Range( 1f, 5f )] public float SuspensionFrequency { get; set; } = 2f;
	[Property, Range( 0.1f, 2f )] public float DampingRatio { get; set; } = 0.85f;
	[Property, Group( "Bump Stop" ), Range( 0f, 10f )] public float BumpStopStrength { get; set; }
	[Property, Group( "Bump Stop" ), Range( 0.4f, 0.95f )] public float BumpStopStartFraction { get; set; } = 0.65f;
	[Property, Range( 0.05f, 1f )] public float ProbeRadiusFraction { get; set; } = 1f;
	[Property, Range( 0.1f, 3f )] public float Grip { get; set; } = 1.15f;
	[Property, Range( 0.1f, 20f )] public float LateralResponse { get; set; } = 7f;
	[Property] public bool UseSurfaceFriction { get; set; } = true;
	private VBVehicleController Vehicle => GetComponentInParent<VBVehicleController>();
	private float _suspensionLength = 14f, _steeringAngle, _rollingSpeed, _spin;
	public float SuspensionLength { get => IsProxy && Vehicle.IsValid() ? Vehicle.GetWheelPresentation( this ).x : _suspensionLength; private set => _suspensionLength = value; }
	public float SteeringAngle { get => IsProxy && Vehicle.IsValid() ? Vehicle.GetWheelPresentation( this ).y : _steeringAngle; private set => _steeringAngle = value; }
	public float RollingSpeed { get => IsProxy && Vehicle.IsValid() ? Vehicle.GetWheelPresentation( this ).z : _rollingSpeed; private set => _rollingSpeed = value; }
	private bool _grounded;
	public bool Grounded { get => IsProxy && Vehicle.IsValid() ? Vehicle.IsWheelGrounded( this ) : _grounded; private set => _grounded = value; }
	internal Vector3 Presentation => new( _suspensionLength, _steeringAngle, _rollingSpeed );
	internal float AdvanceSpin( float dt ) => _spin += _rollingSpeed / MathF.Max( Radius, 0.1f ) * (180f / MathF.PI) * dt;
	public float NormalLoad { get; internal set; }
	public float Compression => SuspensionTravel - SuspensionLength;
	internal SceneTraceResult Contact { get; private set; }
	internal Vector3 ContactPoint { get; private set; }
	internal Vector3 RelativeVelocity { get; private set; }
	internal Vector3 Forward { get; private set; }
	internal Vector3 Right { get; private set; }
	private Transform _visualRest;
	private GameObject _capturedVisual;
	private float _roll;
	private Model _rigModel;
	private string _rigBoneName;
	private int _rigBoneIndex = -1;
	private Transform _bindBone;
	private bool _parkingAnchored;
	private PhysicsBody _parkingBody;
	private Vector3 _parkingAnchor;
	public bool TryGetFluidContactBounds( out BBox bounds )
	{
		var radius = MathF.Max( Radius, 1 );
		var center = WorldPosition - WorldRotation.Up * SuspensionLength;
		bounds = new BBox( center - Vector3.One * radius, center + Vector3.One * radius );
		return Active;
	}

	internal void Sample( VBVehicleController vehicle, float steerAngle )
	{
		SteeringAngle = steerAngle;
		var up = vehicle.WorldRotation.Up;
		float probe = Radius * ProbeRadiusFraction;
		// Start above full compression. A hardpoint below the road after a sharp
		// transition must recover its contact instead of staying StartedSolid.
		float recovery = Radius + SuspensionTravel + 8f;
		Contact = Scene.Trace.Sphere( probe, WorldPosition + up * recovery,
			WorldPosition - up * (SuspensionTravel + Radius - probe) )
			.IgnoreGameObjectHierarchy( vehicle.GameObject )
			.WithoutTags( new[] { "trigger", "water", "player", "zombie", "npc_ragdoll" } ).Run();
		// A wall can overlap the tyre-sized sweep at its starting position even
		// when the chassis is resting correctly on the road. It is not a support.
		// A central ray can still find the road; preserve its radius projection.
		if ( !IsSupport( Contact, up ) )
		{
			var floor = Scene.Trace.Ray( WorldPosition + up * recovery,
				WorldPosition - up * (SuspensionTravel + Radius) )
				.IgnoreGameObjectHierarchy( vehicle.GameObject )
				.WithoutTags( new[] { "trigger", "water", "player", "zombie", "npc_ragdoll" } )
				.RunAll().Where( hit => IsSupport( hit, up ) ).OrderBy( hit => hit.Distance ).FirstOrDefault();
			if ( floor.Hit ) { Contact = floor; probe = 0f; }
		}
		float normalUp = Vector3.Dot( Contact.Normal, up );
		Grounded = IsSupport( Contact, up );
		NormalLoad = 0f;
		// Small probes need slope projection; using radius difference directly
		// places the visible tyre inside a slope when the chassis is not aligned.
		float length = Grounded ? Contact.Distance - recovery - (Radius - probe) / normalUp : SuspensionTravel;
		Grounded &= length <= SuspensionTravel + 0.1f;
		SuspensionLength = Grounded ? Math.Clamp( length, -recovery, SuspensionTravel ) : SuspensionTravel;
		if ( !Grounded ) { RollingSpeed = 0f; _parkingAnchored = false; return; }
		ContactPoint = WorldPosition - up * SuspensionLength - Contact.Normal * Radius;
		var groundVelocity = Contact.Body.IsValid() ? Contact.Body.GetVelocityAtPoint( ContactPoint ) : Vector3.Zero;
		if ( Contact.Collider.IsValid() ) groundVelocity += Contact.Collider.SurfaceVelocity;
		RelativeVelocity = vehicle.Body.GetVelocityAtPoint( ContactPoint ) - groundVelocity;
		var desiredForward = (vehicle.WorldRotation * Rotation.FromYaw( steerAngle )).Forward;
		Forward = (desiredForward - Contact.Normal * Vector3.Dot( desiredForward, Contact.Normal )).Normal;
		Right = Vector3.Cross( Forward, Contact.Normal ).Normal;
		RollingSpeed = Vector3.Dot( RelativeVelocity, Forward );
		float supportedMass = vehicle.Body.Mass * 0.25f;
		float omega = 2f * MathF.PI * SuspensionFrequency;
		float spring = supportedMass * omega * omega;
		float damper = 2f * supportedMass * omega * DampingRatio;
		// Velocity damping avoids derivative spikes when a wheel first meets the road.
		float suspensionForce = Compression * spring - Vector3.Dot( RelativeVelocity, up ) * damper;
		float bumpCompression = MathF.Max( Compression - SuspensionTravel * BumpStopStartFraction, 0f );
		suspensionForce += spring * Math.Clamp( BumpStopStrength, 0f, 10f ) * bumpCompression * bumpCompression / SuspensionTravel;
		NormalLoad = Math.Clamp( suspensionForce, 0f, supportedMass * Scene.PhysicsWorld.Gravity.Length * 5f );
	}

	private bool IsSupport( SceneTraceResult hit, Vector3 suspensionUp )
	{
		var gravity = Scene.PhysicsWorld.Gravity;
		var groundUp = gravity.IsNearZeroLength ? Vector3.Up : -gravity.Normal;
		return hit.Hit && !hit.StartedSolid && Vector3.Dot( hit.Normal, suspensionUp ) > 0.25f
			&& Vector3.Dot( hit.Normal, groundUp ) > 0.25f;
	}

	internal void ApplyContactForces( VBVehicleController vehicle, float driveForce, float brake, bool handbrake, float dt )
	{
		if ( !Grounded || NormalLoad <= 0f || vehicle.IsRecovering ) return;
		var normal = Contact.Normal;
		// Equal/opposite forces make moving platforms and loose props react to the car.
		ApplyPair( vehicle.Body, normal * NormalLoad );
		float mass = vehicle.Body.Mass * 0.25f;
		float longitudinal = Vector3.Dot( RelativeVelocity, Forward );
		float lateral = Vector3.Dot( RelativeVelocity, Right );
		float resistance = vehicle.RollingResistance + (MathF.Abs( driveForce ) < 1f ? vehicle.EngineBraking : 0f);
		float braking = vehicle.BrakeAcceleration * brake + (handbrake ? vehicle.HandbrakeAcceleration : 0f);
		float stopForce = MathF.Min( MathF.Abs( longitudinal ) * mass / dt, mass * (resistance + braking) );
		float longitudinalForce = driveForce - MathF.Sign( longitudinal ) * stopForce;
		float lateralForce = -lateral * mass * MathF.Min( LateralResponse, 1f / dt );
		bool parking = vehicle.CurrentControls.Handbrake && MathF.Abs( vehicle.CurrentControls.Throttle ) < 0.01f;
		if ( !parking || _parkingBody != Contact.Body ) _parkingAnchored = false;
		if ( parking && !_parkingAnchored && vehicle.Body.Velocity.Length < VBVehicleController.KmhToUnits( 5f ) )
		{
			_parkingBody = Contact.Body;
			_parkingAnchor = _parkingBody.IsValid() ? _parkingBody.Transform.PointToLocal( ContactPoint ) : ContactPoint;
			_parkingAnchored = true;
		}
		if ( _parkingAnchored )
		{
			var anchor = _parkingBody.IsValid() ? _parkingBody.Transform.PointToWorld( _parkingAnchor ) : _parkingAnchor;
			if ( Contact.Collider.IsValid() )
			{
				anchor += Contact.Collider.SurfaceVelocity * dt;
				_parkingAnchor = _parkingBody.IsValid() ? _parkingBody.Transform.PointToLocal( anchor ) : anchor;
			}
			var displacement = ContactPoint - anchor;
			// Static tyre friction is a bounded spring around the road contact.
			// It holds a parked heavy car against a walking character without
			// freezing the chassis or resisting an unlimited collision force.
			const float stiffness = 144f;
			longitudinalForce -= Vector3.Dot( displacement, Forward ) * mass * stiffness;
			lateralForce = -mass * (lateral * MathF.Min( 24f, 1f / dt ) + Vector3.Dot( displacement, Right ) * stiffness);
		}
		float surfaceGrip = UseSurfaceFriction && Contact.Surface is not null ? Math.Clamp( Contact.Surface.Friction, 0.05f, 2f ) : 1f;
		float availableGrip = NormalLoad * Grip * surfaceGrip * (handbrake && !_parkingAnchored ? vehicle.HandbrakeGripScale : 1f);
		// One friction circle: cornering consumes the same grip budget as braking/acceleration.
		var tireForce = Forward * longitudinalForce + Right * lateralForce;
		if ( tireForce.Length > availableGrip )
		{
			tireForce = tireForce.Normal * availableGrip;
			// Once traction breaks, slide the anchor instead of tethering the car
			// back to its old parking spot after a strong collision.
			if ( _parkingAnchored ) _parkingAnchor = _parkingBody.IsValid()
				? _parkingBody.Transform.PointToLocal( ContactPoint ) : ContactPoint;
		}
		ApplyPair( vehicle.Body, tireForce );
	}

	private void ApplyPair( Rigidbody body, Vector3 force )
	{
		body.ApplyForceAt( ContactPoint, force );
		if ( Contact.Body.IsValid() && Contact.Body.BodyType == PhysicsBodyType.Dynamic )
			Contact.Body.ApplyForceAt( ContactPoint, -force );
	}

	protected override void OnPreRender()
	{
		// Keep the phase authoritative, including on a dedicated server without
		// rendering. An unwrapped phase interpolates through full revolutions.
		_roll = IsProxy && Vehicle.IsValid() ? Vehicle.GetWheelSpin( this ) : _spin;
		if ( WheelRenderer.IsValid() && !string.IsNullOrWhiteSpace( WheelBone ) )
		{
			UpdateRiggedWheel();
			return;
		}
		if ( !Visual.IsValid() ) return;
		if ( _capturedVisual != Visual ) { _visualRest = Visual.LocalTransform; _capturedVisual = Visual; }
		Visual.LocalPosition = _visualRest.Position + Vector3.Down * SuspensionLength;
		Visual.LocalRotation = Rotation.FromYaw( SteeringAngle ) * Rotation.FromAxis( Vector3.Left, _roll ) * _visualRest.Rotation;
	}

	private void UpdateRiggedWheel()
	{
		var model = WheelRenderer.Model;
		if ( model is null || model.IsError || WheelRenderer.SceneObject is not SceneModel sceneModel ) return;
		if ( _rigModel != model || _rigBoneName != WheelBone )
		{
			_rigModel = model; _rigBoneName = WheelBone;
			var bone = model.Bones.GetBone( WheelBone );
			_rigBoneIndex = bone is null ? -1 : bone.Index;
			if ( _rigBoneIndex < 0 ) { Log.Warning( $"Vehicle wheel bone '{WheelBone}' not found." ); return; }
			_bindBone = model.GetBoneTransform( _rigBoneIndex );
		}
		if ( _rigBoneIndex < 0 ) return;
		var pivot = _bindBone.Position * BonePivotScale;
		var target = WheelRenderer.WorldTransform.PointToLocal( WorldPosition - WorldRotation.Up * SuspensionLength );
		var worldDelta = Rotation.FromAxis( WorldRotation.Up, SteeringAngle ) * Rotation.FromAxis( WorldRotation.Left, _roll );
		var localDelta = WheelRenderer.WorldRotation.Inverse * worldDelta * WheelRenderer.WorldRotation;
		// Rotate about the visible wheel centre, preserving the imported inverse-bind transform.
		// This also handles legacy assets such as greg.humvee (mesh units = bind units / 50).
		var boneTransform = new Transform( target + localDelta * (_bindBone.Position - pivot), localDelta * _bindBone.Rotation, _bindBone.Scale );
		sceneModel.SetBoneOverride( _rigBoneIndex, boneTransform );
	}

	protected override void OnValidate()
	{
		Radius = Math.Clamp( Radius, 4f, 40f );
		SuspensionTravel = Math.Clamp( SuspensionTravel, 2f, 30f );
		SuspensionFrequency = Math.Clamp( SuspensionFrequency, 1f, 5f );
		DampingRatio = Math.Clamp( DampingRatio, 0.1f, 2f );
		BumpStopStrength = Math.Clamp( BumpStopStrength, 0f, 10f );
		BumpStopStartFraction = Math.Clamp( BumpStopStartFraction, 0.4f, 0.95f );
		ProbeRadiusFraction = Math.Clamp( ProbeRadiusFraction, 0.05f, 1f );
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Yellow;
		// DrawGizmos already uses this hardpoint's transform. World coordinates
		// here would apply its translation a second time in the prefab editor.
		Gizmo.Draw.Line( Vector3.Zero, Vector3.Down * SuspensionTravel );
		Gizmo.Draw.LineSphere( Vector3.Down * SuspensionTravel, Radius );
	}
}
