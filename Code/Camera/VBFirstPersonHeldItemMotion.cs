using System;
using Sandbox;

/// <summary>
/// Applies shared first-person movement and look inertia to the currently held
/// item after the native inventory item has placed its viewmodel.
/// </summary>
[Title( "First Person Held Item Motion" )]
[Category( "Void Breach/Camera" )]
[Icon( "animation" )]
public sealed class VBFirstPersonHeldItemMotion : Component, ICameraModifier
{
	[Property, Group( "Movement" )]
	public bool EnableMovement { get; set; } = true;

	[Property, Group( "Movement" )]
	public Vector3 WalkPositionAmplitude { get; set; } = new( 0.08f, 0.12f, 0.12f );

	[Property, Group( "Movement" )]
	public Vector3 WalkRotationAmplitude { get; set; } = new( 0.05f, 0.05f, 0.04f );

	[Property, Group( "Movement" ), Range( 1f, 3f ), Step( 0.05f )]
	public float RunMovementMultiplier { get; set; } = 1.15f;

	[Property, Group( "Movement" ), Range( 0f, 0.25f ), Step( 0.01f )]
	public float AdsMovementScale { get; set; } = 0.25f;

	[Property, Group( "Look Inertia" )]
	public bool EnableLookInertia { get; set; } = true;

	[Property, Group( "Look Inertia" ), Range( 0f, 0.25f ), Step( 0.01f )]
	public float LookInertiaStrength { get; set; } = 0.05f;

	[Property, Group( "Look Inertia" ), Range( 0.1f, 3f ), Step( 0.05f )]
	public float LookInertiaMaxAngle { get; set; } = 0.75f;

	[Property, Group( "Look Inertia" ), Range( 1f, 40f ), Step( 0.5f )]
	public float LookInertiaReturnSpeed { get; set; } = 14f;

	[Property, Group( "Look Inertia" ), Range( 0f, 0.2f ), Step( 0.005f )]
	public float LookInertiaPositionScale { get; set; } = 0.025f;

	[Property, Group( "Look Inertia" ), Range( 0f, 0.25f ), Step( 0.01f )]
	public float AdsLookInertiaScale { get; set; } = 0.1f;

	[Property, Group( "Pitch Depth" )]
	public bool EnablePitchDepth { get; set; } = true;

	[Property, Group( "Pitch Depth" ), Range( 0f, 3f ), Step( 0.05f )]
	public float PitchDepthOffset { get; set; } = 0.5f;

	[Property, Group( "Pitch Depth" ), Range( 0f, 1f ), Step( 0.05f )]
	public float AdsPitchDepthScale { get; set; } = 0f;

	[RequireComponent]
	private PlayerController Controller { get; set; }

	[RequireComponent]
	private BaseInventoryComponent Inventory { get; set; }

	private GameObject _lookInertiaViewModel;
	private Rotation _laggedCameraRotation;
	private bool _hasLaggedCameraRotation;

	int ICameraModifier.CameraOrder => 250;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		if ( Application.IsDedicatedServer || IsProxy || Controller.ThirdPerson )
		{
			ResetLookInertia();
			return;
		}

		if ( Inventory.ActiveItem is not IVBFirstPersonHeldItem heldItem
			|| !heldItem.FirstPersonViewModel.IsValid() )
		{
			ResetLookInertia();
			return;
		}

		var viewModel = heldItem.FirstPersonViewModel;
		var aimAmount = Math.Clamp( heldItem.FirstPersonMotionAimAmount, 0f, 1f );

		ApplyLookInertia( camera, viewModel, aimAmount );
		ApplyPitchDepth( camera, viewModel, aimAmount );
		ApplyMovement( camera, viewModel, aimAmount );
	}

	private void ApplyLookInertia( CameraComponent camera, GameObject viewModel, float aimAmount )
	{
		if ( !EnableLookInertia )
		{
			ResetLookInertia();
			return;
		}

		if ( !_hasLaggedCameraRotation || _lookInertiaViewModel != viewModel )
		{
			_lookInertiaViewModel = viewModel;
			_laggedCameraRotation = camera.WorldRotation;
			_hasLaggedCameraRotation = true;
			return;
		}

		var deltaTime = Math.Clamp( Time.Delta, 0f, 0.1f );
		var followBlend = 1f - MathF.Exp( -MathF.Max( LookInertiaReturnSpeed, 0.01f ) * deltaTime );
		_laggedCameraRotation = Rotation.Slerp( _laggedCameraRotation, camera.WorldRotation, followBlend );

		var cameraLag = Rotation.Difference( camera.WorldRotation, _laggedCameraRotation );
		var adsScale = MathX.Lerp( 1f, AdsLookInertiaScale, aimAmount );
		var maxAngle = MathF.Max( LookInertiaMaxAngle, 0f );
		var appliedPitch = SoftLimit( cameraLag.Pitch() * LookInertiaStrength, maxAngle ) * adsScale;
		var appliedYaw = SoftLimit( cameraLag.Yaw() * LookInertiaStrength, maxAngle ) * adsScale;
		var localPositionOffset = new Vector3(
			0f,
			-appliedYaw * LookInertiaPositionScale,
			appliedPitch * LookInertiaPositionScale
		);

		viewModel.WorldPosition += camera.WorldRotation * localPositionOffset;
		viewModel.WorldRotation *= Rotation.From( appliedPitch, appliedYaw, 0f );
	}

	private void ApplyPitchDepth( CameraComponent camera, GameObject viewModel, float aimAmount )
	{
		if ( !EnablePitchDepth )
			return;

		var adsScale = MathX.Lerp( 1f, AdsPitchDepthScale, aimAmount );
		var verticalLook = Math.Clamp( camera.WorldRotation.Forward.z, -1f, 1f );
		viewModel.WorldPosition += camera.WorldRotation.Forward
			* (verticalLook * PitchDepthOffset * adsScale);
	}

	private void ApplyMovement( CameraComponent camera, GameObject viewModel, float aimAmount )
	{
		if ( !EnableMovement )
			return;

		var locomotion = Components.Get<VBFirstPersonHeadbob>();
		if ( !locomotion.IsValid() || locomotion.MovementStrength <= 0.0001f )
			return;

		var runScale = MathX.Lerp( 1f, RunMovementMultiplier, locomotion.RunAmount );
		var strength = locomotion.MovementStrength
			* runScale
			* MathX.Lerp( 1f, AdsMovementScale, aimAmount );
		var lateralWave = MathF.Sin( locomotion.MovementPhase );
		var verticalWave = -MathF.Cos( locomotion.MovementPhase * 2f );
		var localPositionOffset = new Vector3(
			verticalWave * WalkPositionAmplitude.x,
			lateralWave * WalkPositionAmplitude.y,
			-verticalWave * WalkPositionAmplitude.z
		);

		viewModel.WorldPosition += camera.WorldRotation * (localPositionOffset * strength);
		viewModel.WorldRotation *= Rotation.From(
			verticalWave * WalkRotationAmplitude.x * strength,
			lateralWave * WalkRotationAmplitude.y * strength,
			-lateralWave * WalkRotationAmplitude.z * strength
		);
	}

	private void ResetLookInertia()
	{
		_lookInertiaViewModel = null;
		_hasLaggedCameraRotation = false;
	}

	private static float SoftLimit( float value, float limit )
	{
		if ( limit <= 0f )
			return 0f;

		return limit * MathF.Tanh( value / limit );
	}
}
