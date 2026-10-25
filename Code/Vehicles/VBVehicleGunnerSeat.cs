using Sandbox;

[Title( "Vehicle Gunner Seat" ), Category( "Void Breach/Vehicles" ), Icon( "military_tech" )]
public sealed class VBVehicleGunnerSeat : VBVehicleSeat
{
	[Property] public VBVehicleTurret Turret { get; set; }
	protected override bool IsSeatAvailable => Turret.IsValid() && Turret.Active && Turret.Access != VBVehicleTurretAccess.Driver;
	protected override string EntryToken => "#vb.vehicle.gunner.enter";
}
