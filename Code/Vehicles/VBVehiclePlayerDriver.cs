using System;
using Sandbox;

/// <summary>Local player input and camera. Seating/movement/animation use native SitMoveMode.</summary>
[Title( "Vehicle Player Driver" ), Category( "Void Breach/Vehicles" ), Icon( "sports_motorsports" )]
public sealed partial class VBVehiclePlayerDriver : Component, PlayerController.IEvents, IScenePhysicsEvents
{
	[Property, InputAction] public string HandbrakeAction { get; set; } = "jump";
	[Property, InputAction] public string TurboAction { get; set; } = "vehicleturbo";
	[Property, Group( "Input" )] public bool UsePlayerInput { get; set; } = true;
	[Property, Range( 5f, 50f )] public float SendRate { get; set; } = 25f;
	[Property] public Vector3 ChaseOffset { get; set; } = new( -230f, 0f, 110f );
	[Property, InputAction] public string TurretToggleAction { get; set; } = "vehicleweapon";
	public VBVehicleDriverSeat Seat => ActiveSeat as VBVehicleDriverSeat;
	public VBVehicleSeat ActiveSeat => ResolveAssignedSeat();
	public bool IsSeated => ActiveSeat.IsValid();
	public bool IsDriving => Seat.IsValid();
	public bool IsTurretAiming { get; private set; }
	public VBVehicleSeat AlternateSeat => ActiveSeat?.Vehicle?.GetComponent<VBVehicleTurret>() is { } turret
		? (IsDriving ? turret.GunnerSeat : turret.DriverSeat) : null;
	public bool CanChangeSeat => ActiveSeat.IsValid() && ActiveSeat.CanTransferTo( _player, AlternateSeat );
	private VBVehicleSeat _lastSeat;
	private PlayerController _player;
	private BaseInventoryComponent _inventory;
	private VBHealthComponent _health;
	private bool _inventoryCaptured, _inventoryWasEnabled, _inputWasEnabled, _thirdPersonWas;
	private float _nextSend;
	private uint _nextInputSequence, _lastInputSequence;
	private Guid _lastInputOwner;
	private readonly Dictionary<PhysicsBody, bool> _seatedBodyCollisions = new();
	private readonly Dictionary<Rigidbody, Transform> _seatedBonePose = new();
	private ModelPhysics[] _seatedModels;

	/// <summary>
	/// MotionEnabled=false leaves native ModelPhysics bones as solid keyframed
	/// bodies. Suppress their contacts before seating, on every peer, without
	/// disabling the renderer or changing the replicated component enabled state.
	/// </summary>
	internal void SuspendSeatedBodyCollisions()
	{
		_seatedModels ??= GetComponentsInChildren<ModelPhysics>( includeDisabled: true, includeSelf: true ).ToArray();
		foreach ( var model in _seatedModels )
		{
			if ( !model.IsValid() ) continue;
			foreach ( var bone in model.Bodies )
			{
				var body = bone.Component.IsValid() ? bone.Component.PhysicsBody : null;
				if ( !body.IsValid() ) continue;
				if ( !_seatedBodyCollisions.ContainsKey( body ) ) _seatedBodyCollisions.Add( body, body.EnableSolidCollisions );
				_seatedBonePose[bone.Component] = WorldTransform.ToLocal( body.Transform );
				body.EnableSolidCollisions = false;
			}
		}
	}

	internal void RestoreSeatedBodyCollisions()
	{
		// Exit teleports the player root, but ModelPhysics normally smooth-moves
		// keyframed bones on the next physics step. Teleport their cached local
		// pose first: re-enabling contacts at the old seat position would hit the
		// chassis at an artificial high speed and damage the exiting player.
		foreach ( var pair in _seatedBonePose )
		{
			if ( !pair.Key.IsValid() || !pair.Key.PhysicsBody.IsValid() ) continue;
			var target = WorldTransform.ToWorld( pair.Value );
			pair.Key.WorldTransform = target;
			var body = pair.Key.PhysicsBody;
			body.Transform = target;
			if ( !pair.Key.MotionEnabled )
			{
				body.Velocity = Vector3.Zero;
				body.AngularVelocity = Vector3.Zero;
			}
		}
		foreach ( var pair in _seatedBodyCollisions )
			if ( pair.Key.IsValid() ) pair.Key.EnableSolidCollisions = pair.Value;
		_seatedBodyCollisions.Clear();
		_seatedBonePose.Clear();
		_seatedModels = null;
	}

	private void UpdateSeatedBodyCollisions()
	{
		if ( IsSeated ) SuspendSeatedBodyCollisions();
		else if ( _seatedModels is not null ) RestoreSeatedBodyCollisions();
	}

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		UpdateSeatedBodyCollisions();
		ResolveHeavyVehicleContacts();
	}

	/// <summary>Called after the native walking motor adds velocity, before contact solving.</summary>
	internal void ResolveHeavyVehicleContacts()
	{
		if ( IsProxy || IsSeated || !_player.IsValid() || !_player.Body.IsValid() || !_player.Body.Active
			|| !_player.Body.PhysicsBody.IsValid() || IsIncapacitated || _player.WishVelocity.IsNearZeroLength ) return;
		// The native character motor continuously adds walking velocity. Resolve
		// an approaching heavy chassis before the solver, so this motor does not
		// become an unlimited pushing force against a parked truck.
		var velocity = _player.Body.Velocity;
		if ( velocity.IsNearZeroLength ) return;
		var hit = Scene.Trace.Body( _player.Body.PhysicsBody, _player.WorldPosition + velocity * Scene.FixedDelta + velocity.Normal * 4f )
			.WithTag( "vehicle" ).IgnoreGameObjectHierarchy( _player.GameObject ).Run();
		if ( !hit.Hit || hit.Normal.z > 0.65f || !hit.GameObject.IsValid() ) return;
		var car = hit.GameObject.GetComponentInParent<VBVehicleController>( includeSelf: true );
		if ( !car.IsValid() || !car.IsConfigured || car.GroundedWheels < 2 || car.Body.Mass < _player.BodyMass * 4f
			|| car.Body.Velocity.Length > VBVehicleController.KmhToUnits( 8f ) ) return;
		if ( Vector3.Dot( _player.WishVelocity, hit.Normal ) >= 0f ) return;
		var relative = velocity - car.Body.GetVelocityAtPoint( _player.WorldPosition );
		if ( car.IsProxy ) relative = velocity - car.LinearVelocity
			- Vector3.Cross( car.AngularVelocity, _player.WorldPosition - car.WorldTransform.PointToWorld( car.Body.MassCenter ) );
		float approach = Vector3.Dot( relative, hit.Normal );
		if ( approach < 0f ) _player.Body.Velocity -= hit.Normal * approach;
	}

	protected override void OnStart()
	{
		_player = GetComponent<PlayerController>();
		_inventory = GetComponent<BaseInventoryComponent>();
		_health = GetComponent<VBHealthComponent>();
	}

	protected override void OnUpdate()
	{
		ReconcileSeatBinding();
		UpdateSeatedBodyCollisions();
		if ( _player.IsValid() )
		{
			var mounted = ActiveSeat?.Vehicle?.GetComponent<VBVehicleTurret>();
			IsTurretAiming = mounted.IsValid() && mounted.CanOperate( _player );
			// Inventory visibility is presentation on every peer, including a new
			// observer. Only the input and camera code below is owner-local.
			UpdateControlPresentation();
		}
		if ( IsProxy || !_player.IsValid() ) return;
		var seat = ActiveSeat;
		if ( seat != _lastSeat )
		{
			if ( seat.IsValid() ) _player.ThirdPerson = false;
			_lastSeat = seat;
		}
		UpdateVehicleCameraMotion();
		if ( !IsSeated || IsIncapacitated ) { IsTurretAiming = false; return; }
		var turret = seat.Vehicle.GetComponent<VBVehicleTurret>();
		IsTurretAiming = turret.IsValid() && turret.CanOperate( _player );
		if ( UsePlayerInput && Input.Pressed( TurretToggleAction ) && CanChangeSeat )
		{
			seat.RequestSeatChange( _player, AlternateSeat );
			return;
		}
		if ( !UsePlayerInput || Time.Now < _nextSend ) return;
		_nextSend = Time.Now + 1f / Math.Clamp( SendRate, 5f, 50f );
		var movement = Input.AnalogMove;
		TransmitControls( new VBVehicleControls( movement.x, -movement.y, 0f, Input.Down( HandbrakeAction ), Input.Down( TurboAction ) ),
			GetTurretAim( turret ), IsTurretAiming, IsTurretAiming && Input.Down( "attack1" ) );
	}

	private Angles GetTurretAim( VBVehicleTurret turret )
	{
		// First person uses this frame's eyes, avoiding feedback from the previous
		// rendered camera. Third person converges toward its camera's centre.
		var camera = Scene.Camera;
		if ( !IsTurretAiming || !turret.IsValid() )
			return _player.EyeTransform.Rotation.Angles();
		var view = IsDriving && _player.ThirdPerson && _player.UseCameraControls && camera.IsValid()
			? camera.View.Transform : GetVehicleFirstPersonView( _player.EyeTransform );
		if ( !IsDriving ) view.Position = turret.CameraPosition;
		return turret.GetAimForView( view );
	}

	internal void TransmitControls( VBVehicleControls controls, Angles aim, bool turretEnabled, bool fire )
	{
		var seat = ActiveSeat;
		if ( IsProxy || !seat.IsValid() ) return;
		var relativeAim = (seat.Vehicle.WorldRotation.Inverse * aim.ToRotation()).Angles();
		SendControls( ++_nextInputSequence, SeatingRevision, controls, relativeAim, turretEnabled, fire );
	}

	[Rpc.Host( NetFlags.OwnerOnly | NetFlags.Unreliable )]
	internal void SendControls( uint sequence, uint seatRevision, VBVehicleControls controls, Angles relativeAim, bool turretEnabled, bool fire )
	{
		if ( !Networking.IsHost ) return;
		_player ??= GetComponent<PlayerController>();
		var seat = ActiveSeat;
		if ( !_player.IsValid() || _player.Network.Owner != Rpc.Caller || !seat.IsValid() || !seat.Active
			|| seat.Occupant != _player || seatRevision != SeatingRevision ) return;
		if ( _lastInputOwner != Rpc.Caller.Id ) { _lastInputOwner = Rpc.Caller.Id; _lastInputSequence = 0; }
		if ( !IsNewer( sequence, _lastInputSequence ) ) return;
		_lastInputSequence = sequence;
		if ( seat is VBVehicleDriverSeat driver ) driver.SubmitPlayerControls( _player, controls );
		if ( turretEnabled && float.IsFinite( relativeAim.pitch ) && float.IsFinite( relativeAim.yaw ) && float.IsFinite( relativeAim.roll ) ) seat.Vehicle.GetComponent<VBVehicleTurret>()?.SubmitControls( _player,
			(seat.Vehicle.WorldRotation * relativeAim.ToRotation()).Angles(), fire );
	}

	private bool IsIncapacitated => _health.IsValid() && (_health.IsDowned || _health.IsDead);

	void PlayerController.IEvents.PreInput()
	{
		if ( !IsProxy && _player.IsValid() ) UpdateControlPresentation();
	}

	private void UpdateControlPresentation()
	{
		// SitMoveMode re-enables its colliders on exit. Incapacitated players must
		// remain under their life-state controller until revived.
		if ( IsIncapacitated && !IsSeated )
		{
			_player.UseInputControls = false;
			if ( _player.Body.IsValid() ) _player.Body.MotionEnabled = false;
			if ( _player.ColliderObject.IsValid() ) _player.ColliderObject.Enabled = false;
		}
		if ( IsSeated )
		{
			if ( !_inventoryCaptured )
			{
				_inventoryWasEnabled = !_inventory.IsValid() || _inventory.Enabled;
				_inputWasEnabled = _player.UseInputControls;
				_thirdPersonWas = _player.ThirdPerson;
				_inventoryCaptured = true;
			}
			// Look/use remain native; move/jump/traversal inputs belong exclusively to the vehicle.
			_player.UseInputControls = false;
			if ( _inventory.IsValid() )
			{
				_inventory.Enabled = false;
				foreach ( var weapon in _inventory.Items.OfType<BaseCombatWeapon>() )
					if ( weapon.ViewModel.IsValid() ) weapon.ViewModel.Enabled = false;
			}
			if ( !IsProxy && IsIncapacitated && Time.Now >= _nextSend )
			{
				_nextSend = Time.Now + 0.25f;
				ActiveSeat.RequestExit( _player );
			}
			return;
		}
		if ( !_inventoryCaptured ) return;
		if ( !IsIncapacitated )
		{
			_player.UseInputControls = _inputWasEnabled;
			_player.ThirdPerson = _thirdPersonWas;
			if ( _inventory.IsValid() )
			{
				_inventory.Enabled = _inventoryWasEnabled;
				foreach ( var weapon in _inventory.Items.OfType<BaseCombatWeapon>() )
					if ( weapon.ViewModel.IsValid() ) weapon.ViewModel.Enabled = true;
			}
		}
		_inventoryCaptured = false;
	}

	protected override void OnDisabled()
	{
		if ( !IsSeated ) RestoreSeatedBodyCollisions();
		if ( IsProxy || !_player.IsValid() ) return;
		var seat = ActiveSeat;
		if ( seat.IsValid() ) seat.RequestExit( _player );
		if ( !IsSeated ) UpdateControlPresentation();
	}

	protected override void OnDestroy() => RestoreSeatedBodyCollisions();
}
