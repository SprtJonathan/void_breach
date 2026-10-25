using System;
using Sandbox;

/// <summary>Vehicle chair with a host-side driving command buffer.</summary>
[Title( "Vehicle Driver Seat" ), Category( "Void Breach/Vehicles" ), Icon( "airline_seat_recline_normal" )]
public sealed class VBVehicleDriverSeat : VBVehicleSeat, IVBVehicleControlSource
{
	public PlayerController Driver => Occupant;
	private PlayerController _commandDriver;
	private VBVehicleControls _commands = VBVehicleControls.Parked;
	private float _lastInputTime = float.NegativeInfinity;
	protected override bool IsSeatAvailable => Vehicle.IsValid() && Vehicle.ControlSource == this;
	protected override void OnCommandsCleared()
	{
		_commandDriver = null;
		_commands = VBVehicleControls.Parked;
		_lastInputTime = float.NegativeInfinity;
	}
	/// <summary>Only the owning player's bridge may deliver remote commands; no client-supplied damage.</summary>
	internal void SubmitPlayerControls( PlayerController player, VBVehicleControls controls )
	{
		if ( !Networking.IsHost || !player.IsValid() || Driver != player || GetOccupant() != player
			|| player.GetComponent<VBVehiclePlayerDriver>()?.Seat != this ) return;
		var health = player.GetComponent<VBHealthComponent>();
		if ( health.IsValid() && (health.IsDead || health.IsDowned) ) return;
		_commandDriver = player;
		_commands = controls.Sanitized();
		_lastInputTime = Time.Now;
	}

	public VBVehicleControls GetVehicleControls( VBVehicleController vehicle )
	{
		var occupant = Driver;
		if ( !Networking.IsHost || vehicle != Vehicle || !occupant.IsValid() || occupant != _commandDriver
			|| Time.Now - _lastInputTime > InputTimeout ) return VBVehicleControls.Parked;
		var health = occupant.GetComponent<VBHealthComponent>();
		if ( health.IsValid() && (health.IsDead || health.IsDowned) ) return VBVehicleControls.Parked;
		return _commands;
	}

}
