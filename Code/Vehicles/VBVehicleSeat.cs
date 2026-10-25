using System;
using Sandbox;
using Sandbox.Movement;

/// <summary>Native chair lifecycle plus a host-side driver command buffer.</summary>

public abstract partial class VBVehicleSeat : BaseChair, Component.IPressable, ISitTarget, IVBInteractionPromptProvider
{
	[Property] public VBVehicleController Vehicle { get; set; }
	[Property, Range( 32f, 300f )] public float EntryDistance { get; set; } = 160f;
	[Property, Range( 0f, 30f )] public float MaximumEntrySpeedKmh { get; set; } = 8f;
	// Retained for existing prefab serialization; leaving is never speed-gated.
	[Property, Hide] public float MaximumExitSpeedKmh { get; set; } = 10f;
	[Property, Range( 0.1f, 2f )] public float InputTimeout { get; set; } = 0.35f;
	public PlayerController Occupant
	{
		get => Vehicle.IsValid() && Vehicle.SeatOccupants.TryGetValue( Id, out var player ) ? player : null;
		private set
		{
			if ( !Networking.IsHost || !Vehicle.IsValid() ) return;
			if ( value.IsValid() ) Vehicle.SeatOccupants[Id] = value; else Vehicle.SeatOccupants.Remove( Id );
		}
	}
	private float _entryTime;
	protected virtual bool IsSeatAvailable => true;
	protected virtual string EntryToken => "#vb.vehicle.enter";
	protected virtual void OnCommandsCleared() { }

	public override Transform CalculateEyeTransform( PlayerController player )
	{
		if ( player.GetComponent<VBVehiclePlayerDriver>()?.IsTurretAiming == true )
			return new Transform( GetEyeTransform().Position, Vehicle.WorldRotation * player.EyeAngles.ToRotation() );
		return base.CalculateEyeTransform( player );
	}

	public override void UpdatePlayerAnimator( PlayerController player, SkinnedModelRenderer renderer )
	{
		var yawRange = YawRange;
		if ( player.GetComponent<VBVehiclePlayerDriver>()?.IsTurretAiming == true ) YawRange = new( -180f, 180f );
		base.UpdatePlayerAnimator( player, renderer );
		YawRange = yawRange;
	}
	// A driver seat on the chassis must not mistake a gunner for its own occupant.
	public new PlayerController GetOccupant() => SeatPosition.IsValid()
		? SeatPosition.GetComponentInChildren<PlayerController>( includeSelf: true ) : null;

	protected override void OnStart()
	{
		base.OnStart();
		if ( !Vehicle.IsValid() ) Vehicle = GetComponentInParent<VBVehicleController>( includeSelf: true );
	}

	public override bool CanEnter( PlayerController player )
	{
		if ( !Active || GetOccupant().IsValid() || Occupant.IsValid() || !Vehicle.IsValid() || !Vehicle.Active || !Vehicle.IsConfigured || Vehicle.IsOverturned || Vehicle.IsRecovering
			|| !IsSeatAvailable || !player.IsValid() ) return false;
		if ( !player.GetComponent<VBVehiclePlayerDriver>().IsValid() || !player.GetComponent<SitMoveMode>().IsValid()
			|| player.GetComponentInParent<ISitTarget>() is not null ) return false;
		var health = player.GetComponent<VBHealthComponent>();
		if ( health.IsValid() && (health.IsDead || health.IsDowned) ) return false;
		var traversal = player.GetComponent<VBMoveModeTraversal>();
		return !(traversal.IsValid() && traversal.IsTraversing)
			&& (player.WorldPosition - WorldPosition).Length <= EntryDistance
			&& Vehicle.Body.Velocity.Length <= VBVehicleController.KmhToUnits( MaximumEntrySpeedKmh );
	}

	public override bool CanLeave( PlayerController player )
	{
		return player.IsValid() && Vehicle.IsValid();
	}

	// PlayerController.StartPressing resolves the first IPressable on the selected
	// GameObject again. The chassis seat must route roof use to recovery as well,
	// otherwise it captures E even when the hovered component was recovery.
	private VBVehicleRecovery RecoveryInteraction => Vehicle.IsValid() && Vehicle.IsOverturned
		? Vehicle.GetComponent<VBVehicleRecovery>() : null;

	bool Component.IPressable.CanPress( Component.IPressable.Event e ) => RecoveryInteraction.IsValid()
		? RecoveryInteraction.CanPress( e ) : base.CanPress( e );

	public override Component.IPressable.Tooltip? GetTooltip( Component.IPressable.Event e ) => RecoveryInteraction.IsValid()
		? RecoveryInteraction.GetTooltip( e ) : base.GetTooltip( e );

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		if ( RecoveryInteraction.IsValid() ) return RecoveryInteraction.Press( e );
		if ( e.Source is not PlayerController player || !CanEnter( player ) ) return false;
		RequestEntry( player );
		return true;
	}

	[Rpc.Host]
	private void RequestEntry( PlayerController player )
	{
		if ( !player.IsValid() || player.Network.Owner != Rpc.Caller || !CanEnter( player ) ) return;
		var visibility = Scene.Trace.Ray( player.EyePosition, WorldPosition + Vector3.Up * 24f )
			.IgnoreGameObjectHierarchy( player.GameObject ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( visibility.Hit || visibility.StartedSolid ) return;
		// Reserve immediately, before the owning client replicates its new parent.
		Occupant = player;
		_entryTime = Time.Now;
		OnCommandsCleared();
		player.GetComponent<VBVehiclePlayerDriver>().AssignSeatOnHost( this );
	}

	public bool CanTransferTo( PlayerController player, VBVehicleSeat destination )
	{
		if ( !player.IsValid() || !Active || Occupant != player || GetOccupant() != player
			|| !destination.IsValid() || destination == this || !destination.Active || !destination.SeatPosition.IsValid()
			|| !Vehicle.IsValid() || !Vehicle.Active || Vehicle.IsOverturned || Vehicle.IsRecovering || destination.Vehicle != Vehicle || !destination.IsSeatAvailable
			|| destination.Occupant.IsValid() || destination.GetOccupant().IsValid() || Time.Now - _entryTime < 0.3f ) return false;
		var health = player.GetComponent<VBHealthComponent>();
		return !health.IsValid() || (!health.IsDead && !health.IsDowned);
	}

	[Rpc.Host]
	public void RequestSeatChange( PlayerController player, VBVehicleSeat destination )
	{
		if ( !player.IsValid() || player.Network.Owner != Rpc.Caller || !CanTransferTo( player, destination ) ) return;
		// Reserve on the host before reparenting on the owner. Remain in SitMoveMode:
		// no exit teleport, no capsule restoration and no frame of walking physics.
		destination.Occupant = player;
		destination._entryTime = Time.Now;
		destination.OnCommandsCleared();
		Occupant = null;
		OnCommandsCleared();
		Vehicle.GetComponent<VBVehicleTurret>()?.ClearPlayerCommands( player );
		player.GetComponent<VBVehiclePlayerDriver>().AssignSeatOnHost( destination );
	}

	void ISitTarget.AskToLeave( PlayerController player ) => RequestExit( player );

	[Rpc.Host]
	public void RequestExit( PlayerController player )
	{
		if ( !player.IsValid() || player.Network.Owner != Rpc.Caller || Occupant != player ) return;
		ReleaseOccupant( player, FindExitPosition( player ) );
	}

	private void ReleaseOccupant( PlayerController player, Vector3 position )
	{
		var eyes = CalculateEyeTransform( player ).Rotation;
		var velocity = Vehicle.IsValid() && Vehicle.Body.IsValid() ? Vehicle.Body.GetVelocityAtPoint( position ) : Vector3.Zero;
		Occupant = null;
		OnCommandsCleared();
		Vehicle.GetComponent<VBVehicleTurret>()?.ClearPlayerCommands( player );
		player.GetComponent<VBVehiclePlayerDriver>( includeDisabled: true )?.AssignSeatOnHost( null,
			new Transform( position, eyes, player.WorldScale ), velocity );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost ) return;
		if ( !Occupant.IsValid() )
		{
			Occupant = null;
			OnCommandsCleared();
		}
		else if ( !Occupant.Active || Occupant.GetComponent<VBVehiclePlayerDriver>()?.ActiveSeat != this
			|| Networking.IsActive && (Occupant.Network.Owner is null || !Occupant.Network.Owner.IsActive) )
		{
			var player = Occupant;
			ReleaseOccupant( player, FindExitPosition( player ) );
		}
	}

	protected override void OnDisabled()
	{
		if ( !Game.IsPlaying || !Networking.IsHost || !Vehicle.IsValid() || !Occupant.IsValid() ) return;
		var player = Occupant;
		ReleaseOccupant( player, FindExitPosition( player ) );
	}


	public VBInteractionPromptState GetInteractionPromptState( GameObject viewer )
	{
		var player = viewer.IsValid() ? viewer.GetComponent<PlayerController>() : null;
		return new VBInteractionPromptState( player.IsValid() && CanEnter( player ), EntryToken, "use", "#vb.vehicle.name" );
	}
}
