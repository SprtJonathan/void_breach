using System;
using Sandbox;

/// <summary>Driver intent, independent of keyboard, AI, drivetrain and physics.</summary>
public readonly record struct VBVehicleControls( float Throttle, float Steering, float Brake, bool Handbrake, bool Turbo = false )
{
	public static VBVehicleControls Parked => new( 0f, 0f, 0f, true );
	public VBVehicleControls Sanitized() => new(
		float.IsFinite( Throttle ) ? Math.Clamp( Throttle, -1f, 1f ) : 0f,
		float.IsFinite( Steering ) ? Math.Clamp( Steering, -1f, 1f ) : 0f,
		float.IsFinite( Brake ) ? Math.Clamp( Brake, 0f, 1f ) : 1f, Handbrake, Turbo );
}

/// <summary>Implement on a Component. Called on the host at the physics timestep.</summary>
public interface IVBVehicleControlSource
{
	VBVehicleControls GetVehicleControls( VBVehicleController vehicle );
}

public enum VBVehicleDriveLayout { Front, Rear, All }
