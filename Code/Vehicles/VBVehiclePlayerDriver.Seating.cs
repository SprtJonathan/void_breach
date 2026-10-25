using System;
using Sandbox;

/// <summary>One atomic host decision; GUIDs also resolve after a late-join snapshot.</summary>
public readonly record struct VBVehicleSeatBinding( Guid SeatId, uint Revision, Transform ExitPose, NetworkFlags RestoreFlags, Vector3 ExitVelocity = default );

public sealed partial class VBVehiclePlayerDriver
{
	[Sync( SyncFlags.FromHost )] public VBVehicleSeatBinding Seating { get; private set; }
	private VBVehicleSeatBinding _receivedSeating;
	private uint _presentedRevision;
	private VBVehicleSeat _presentedSeat;
	private bool _hasPresentedBinding;
	private VBVehicleSeatBinding CurrentSeating => IsNewer( _receivedSeating.Revision, Seating.Revision ) ? _receivedSeating : Seating;
	internal uint SeatingRevision => CurrentSeating.Revision;
	internal static bool IsNewer( uint value, uint previous ) => unchecked( (int)(value - previous) ) > 0;
	private VBVehicleSeat ResolveAssignedSeat()
	{
		var binding = CurrentSeating;
		return binding.Revision == 0 ? GetComponentInParent<VBVehicleSeat>()
			: binding.SeatId == Guid.Empty ? null : Scene.Directory.FindComponentByGuid( binding.SeatId ) as VBVehicleSeat;
	}

	internal void AssignSeatOnHost( VBVehicleSeat seat, Transform exitPose = default, Vector3 exitVelocity = default )
	{
		if ( !Networking.IsHost ) return;
		var flags = Seating.SeatId == Guid.Empty ? Network.Flags : Seating.RestoreFlags;
		Seating = new( seat.IsValid() ? seat.Id : Guid.Empty, Seating.Revision + 1, exitPose, flags, exitVelocity );
		// Only the host may change flags on a spawned object. While seated, the
		// player has no second transform stream competing with the car's stream.
		Network.Flags = seat.IsValid() ? flags | NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation : flags;
		ApplySeatBinding( Seating );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ApplySeatBinding( VBVehicleSeatBinding binding )
	{
		if ( IsNewer( binding.Revision, _receivedSeating.Revision ) ) _receivedSeating = binding;
		ReconcileSeatBinding();
	}

	private void ReconcileSeatBinding()
	{
		_player ??= GetComponent<PlayerController>();
		if ( !_player.IsValid() ) return;
		var binding = CurrentSeating;
		if ( binding.Revision == 0 ) return;
		bool changed = !_hasPresentedBinding || binding.Revision != _presentedRevision;
		if ( binding.SeatId != Guid.Empty )
		{
			var seat = ResolveAssignedSeat();
			// An actor can arrive before its vehicle during snapshot deserialization.
			// Wait for the GUID, without treating that pending reference as an exit.
			if ( !seat.IsValid() || !seat.SeatPosition.IsValid() ) return;
			SuspendSeatedBodyCollisions();
			_player.Body.Enabled = false;
			_player.ColliderObject.Enabled = false;
			if ( GameObject.Parent != seat.SeatPosition ) GameObject.SetParent( seat.SeatPosition, false );
			using ( Transform.DisableProxy() ) LocalTransform = global::Transform.Zero;
			if ( changed && !IsProxy && !_presentedSeat.IsValid() ) _player.EyeAngles = default;
			_presentedSeat = seat;
		}
		else if ( changed )
		{
			GameObject.SetParent( null, true );
			using ( Transform.DisableProxy() ) WorldTransform = binding.ExitPose;
			if ( !IsProxy ) _player.EyeAngles = binding.ExitPose.Rotation.Angles();
			Network.ClearInterpolation();
			RestoreSeatedBodyCollisions();
			_presentedSeat = null;
			// SitMoveMode performs the normal owner-side restoration. Proxies must
			// also stop retaining disabled colliders from an old seat snapshot.
			if ( !IsIncapacitated )
			{
				_player.Body.Enabled = true; _player.ColliderObject.Enabled = true;
				if ( !IsProxy ) _player.Body.Velocity = binding.ExitVelocity;
			}
		}
		_presentedRevision = binding.Revision; _hasPresentedBinding = true;
	}

	protected override void OnPreRender()
	{
		// Reapply after the network/player update so render interpolation cannot
		// separate a seated actor from the already-interpolated chassis.
		if ( CurrentSeating.SeatId != Guid.Empty ) ReconcileSeatBinding();
	}
}
