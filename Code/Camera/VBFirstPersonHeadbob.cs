using System;
using Sandbox;

/// <summary>
/// Ajoute un headbob local et progressif après le placement de la caméra native.
/// L'effet est strictement limité à la vue à la première personne.
/// </summary>
[Title( "First Person Headbob" )]
[Category( "Void Breach/Camera" )]
[Icon( "visibility" )]
public sealed class VBFirstPersonHeadbob : Component, PlayerController.IEvents
{
	[RequireComponent]
	public PlayerController Controller { get; set; }

	/// <summary>
	/// Active le déplacement de la caméra pendant la marche et la course.
	/// </summary>
	[Property, Group( "Position" )]
	public bool EnablePositionBob { get; set; } = true;

	/// <summary>
	/// Amplitude du balancement gauche/droite, en unités s&box.
	/// </summary>
	[Property, Group( "Position" ), Range( 0f, 3f ), Step( 0.05f )]
	public float HorizontalAmplitude { get; set; } = 0.35f;

	/// <summary>
	/// Amplitude du déplacement haut/bas, en unités s&box.
	/// </summary>
	[Property, Group( "Position" ), Range( 0f, 3f ), Step( 0.05f )]
	public float VerticalAmplitude { get; set; } = 0.6f;

	/// <summary>
	/// Active les faibles rotations qui accompagnent chaque pas.
	/// </summary>
	[Property, Group( "Rotation" )]
	public bool EnableRotationBob { get; set; } = true;

	[Property, Group( "Rotation" ), Range( 0f, 2f ), Step( 0.05f )]
	public float PitchAmplitude { get; set; } = 0.12f;

	[Property, Group( "Rotation" ), Range( 0f, 2f ), Step( 0.05f )]
	public float YawAmplitude { get; set; } = 0.1f;

	[Property, Group( "Rotation" ), Range( 0f, 2f ), Step( 0.05f )]
	public float RollAmplitude { get; set; } = 0.2f;

	/// <summary>
	/// Nombre de cycles de marche par seconde.
	/// </summary>
	[Property, Group( "Rhythm" ), Range( 0.25f, 4f ), Step( 0.05f )]
	public float WalkFrequency { get; set; } = 1.7f;

	/// <summary>
	/// Accélération du rythme lorsque la vitesse approche celle de course.
	/// </summary>
	[Property, Group( "Rhythm" ), Range( 1f, 2.5f ), Step( 0.05f )]
	public float RunFrequencyMultiplier { get; set; } = 1.3f;

	/// <summary>
	/// Renforcement du mouvement lorsque la vitesse approche celle de course.
	/// </summary>
	[Property, Group( "Rhythm" ), Range( 1f, 2.5f ), Step( 0.05f )]
	public float RunAmplitudeMultiplier { get; set; } = 1.15f;

	/// <summary>
	/// Vitesse horizontale minimale avant le début du headbob.
	/// </summary>
	[Property, Group( "Response" ), Range( 0f, 100f ), Step( 1f )]
	public float MinimumMovementSpeed { get; set; } = 10f;

	/// <summary>
	/// Rapidité avec laquelle l'effet apparaît et revient au repos.
	/// </summary>
	[Property, Group( "Response" ), Range( 1f, 30f ), Step( 0.5f )]
	public float BlendSpeed { get; set; } = 10f;

	/// <summary>
	/// Part d'amplitude conservée lorsque l'arme atteint complètement l'ADS.
	/// </summary>
	[Property, Group( "Response" ), Range( 0f, 1f ), Step( 0.05f )]
	public float AimAmplitudeScale { get; set; } = 0.3f;

	private BaseInventoryComponent _inventory;
	private float _phase;
	private float _movementStrength;

	protected override void OnStart()
	{
		_inventory = Components.Get<BaseInventoryComponent>();
	}

	/// <summary>
	/// Applique l'effet une fois que PlayerController a terminé son propre placement.
	/// </summary>
	void PlayerController.IEvents.PostCameraSetup( CameraComponent camera )
	{
		if ( IsProxy || !camera.IsValid() )
			return;

		Controller ??= Components.Get<PlayerController>();

		if ( !Controller.IsValid() )
			return;

		if ( Controller.ThirdPerson )
		{
			_movementStrength = 0f;
			return;
		}

		var deltaTime = Math.Clamp( Time.Delta, 0f, 0.1f );
		var horizontalSpeed = Controller.Velocity.WithZ( 0f ).Length;
		var walkSpeed = MathF.Max( Controller.WalkSpeed, MinimumMovementSpeed + 1f );
		var runSpeed = MathF.Max( Controller.RunSpeed, walkSpeed + 1f );

		var targetStrength = Controller.IsOnGround
			? InverseLerpClamped( MinimumMovementSpeed, walkSpeed, horizontalSpeed )
			: 0f;
		var blend = 1f - MathF.Exp( -MathF.Max( BlendSpeed, 0.01f ) * deltaTime );
		_movementStrength = MathX.Lerp( _movementStrength, targetStrength, blend );

		if ( _movementStrength <= 0.0001f )
			return;

		var runAmount = InverseLerpClamped( walkSpeed, runSpeed, horizontalSpeed );
		var frequency = WalkFrequency * MathX.Lerp( 1f, RunFrequencyMultiplier, runAmount );
		_phase = WrapPhase( _phase + deltaTime * frequency * MathF.PI * 2f );

		var aimScale = GetAimScale();
		var speedScale = MathX.Lerp( 1f, RunAmplitudeMultiplier, runAmount );
		var strength = _movementStrength * speedScale * aimScale;
		var lateralWave = MathF.Sin( _phase );
		var verticalWave = -MathF.Cos( _phase * 2f );
		var cameraRotation = camera.WorldRotation;

		if ( EnablePositionBob )
		{
			var positionOffset = cameraRotation.Right * (lateralWave * HorizontalAmplitude)
				+ cameraRotation.Up * (verticalWave * VerticalAmplitude);

			camera.WorldPosition += positionOffset * strength;
		}

		if ( EnableRotationBob )
		{
			var rotationOffset = Rotation.From(
				verticalWave * PitchAmplitude * strength,
				lateralWave * YawAmplitude * strength,
				-lateralWave * RollAmplitude * strength
			);

			camera.WorldRotation *= rotationOffset;
		}
	}

	private float GetAimScale()
	{
		_inventory ??= Components.Get<BaseInventoryComponent>();

		if ( _inventory?.ActiveItem is not VBCombatWeapon weapon )
			return 1f;

		return MathX.Lerp( 1f, AimAmplitudeScale, weapon.AimAmount );
	}

	private static float InverseLerpClamped( float min, float max, float value )
	{
		if ( max <= min )
			return value >= max ? 1f : 0f;

		return Math.Clamp( (value - min) / (max - min), 0f, 1f );
	}

	private static float WrapPhase( float phase )
	{
		var fullCycle = MathF.PI * 2f;
		return phase % fullCycle;
	}
}
