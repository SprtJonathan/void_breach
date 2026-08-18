using System;
using Sandbox;
using Sandbox.Rendering;

/// <summary>
/// Arme de combat Void Breach avec notifications locales configurables
/// pour les états de munitions du chargeur.
/// </summary>
[Title( "Void Breach Combat Weapon" )]
[Category( "Void Breach/Weapons" )]
public sealed class VBCombatWeapon : BaseCombatWeapon
{
	/// <summary>
	/// Active ou désactive le zoom de caméra lorsque l'arme est utilisée en vue FPS.
	/// </summary>
	[Property, Group( "Aim Down Sights - First Person" )]
	public bool EnableFirstPersonAimZoom { get; set; } = true;

	/// <summary>
	/// Réduction du champ de vision en FPS. Une faible valeur conserve
	/// un zoom très léger, adapté au pistolet.
	/// </summary>
	[Property, Group( "Aim Down Sights - First Person" ), Range( 0f, 20f ), Step( 0.5f )]
	public float FirstPersonAimFieldOfViewReduction { get; set; } = 3f;

	/// <summary>
	/// Correction locale appliquée au viewmodel pendant l'ADS.
	/// X avance l'arme, Y la déplace vers la gauche et Z la monte à l'écran.
	/// </summary>
	[Property, Group( "Aim Down Sights - First Person" )]
	public Vector3 FirstPersonAimPositionOffset { get; set; } = Vector3.Zero;

	/// <summary>
	/// Correction angulaire locale appliquée au viewmodel pendant l'ADS.
	/// </summary>
	[Property, Group( "Aim Down Sights - First Person" )]
	public Angles FirstPersonAimRotationOffset { get; set; } = Angles.Zero;

	/// <summary>
	/// Active ou désactive indépendamment le zoom de caméra en vue TPS.
	/// </summary>
	[Property, Group( "Aim Down Sights - Third Person" )]
	public bool EnableThirdPersonAimZoom { get; set; } = true;

	/// <summary>
	/// Réduction du champ de vision en TPS.
	/// </summary>
	[Property, Group( "Aim Down Sights - Third Person" ), Range( 0f, 40f ), Step( 0.5f )]
	public float ThirdPersonAimFieldOfViewReduction { get; set; } = 10f;

	/// <summary>
	/// Vitesse de transition entre la visée à la hanche et la visée épaulée.
	/// </summary>
	[Property, Group( "Aim Down Sights" ), Range( 1f, 30f ), Step( 0.5f )]
	public float AimTransitionSpeed { get; set; } = 12f;

	/// <summary>
	/// Progression visuelle de l'ADS, de 0 (hanche) à 1 (visée).
	/// </summary>
	public float AimAmount => _aimAmount;

	/// <summary>
	/// Indique si l'arme est suffisamment épaulée pour être considérée en ADS.
	/// </summary>
	public bool IsAiming => _aimAmount >= 0.5f;

	/// <summary>
	/// Son joué lorsque le chargeur franchit le seuil de munitions faibles.
	/// </summary>
	[Property, Group( "Ammo Feedback" )]
	public SoundEvent LowAmmoSound { get; set; }

	/// <summary>
	/// Son joué lorsque le dernier tir vide le chargeur.
	/// </summary>
	[Property, Group( "Ammo Feedback" )]
	public SoundEvent MagazineDepletedSound { get; set; }

	/// <summary>
	/// Fraction du chargeur à partir de laquelle les munitions sont considérées faibles.
	/// </summary>
	[Property, Group( "Ammo Feedback" ), Range( 0.05f, 0.95f ), Step( 0.05f )]
	public float LowAmmoThreshold { get; set; } = 0.25f;

	private float _aimAmount;
	private bool _wantsToAim;

	/// <summary>
	/// Nombre de cartouches correspondant au seuil configuré pour ce chargeur.
	/// </summary>
	public int LowAmmoCount => GetLowAmmoCount();

	/// <summary>
	/// Met à jour l'ADS en continu tant que le clic droit est maintenu.
	/// </summary>
	protected override void Think()
	{
		base.Think();

		_wantsToAim = IsHeld
			&& !IsReloading
			&& Input.Down( "Attack2" )
			&& !IsOwnerSprinting();
		var target = _wantsToAim ? 1f : 0f;
		var transition = Math.Clamp( AimTransitionSpeed * Time.Delta, 0f, 1f );

		_aimAmount = MathX.Lerp( _aimAmount, target, transition );

		UpdateViewModelAimParameters();
		SyncMuzzleMarkerToAttachment();
	}

	/// <summary>
	/// Fait partir la trajectoire physique du canon, tout en la faisant converger
	/// vers le point regardé par la caméra.
	/// </summary>
	public override Ray AimRay
	{
		get
		{
			var cameraRay = base.AimRay;

			if ( !IsHeld || !TryGetMuzzleAttachment( out var muzzle ) )
				return cameraRay;

			var range = MathF.Max( Ballistics.Range, 1f );
			var cameraTrace = BulletTrace( cameraRay, range, Ballistics.Radius ).Run();
			var direction = cameraTrace.EndPosition - muzzle.Position;

			if ( direction.LengthSquared <= 0.0001f )
				direction = cameraRay.Forward;

			return new Ray( muzzle.Position, direction.Normal );
		}
	}

	/// <summary>
	/// Utilise l'attache muzzle du modèle actif, y compris pour les traceurs et effets.
	/// </summary>
	public override Transform GetMuzzleTransform()
	{
		if ( TryGetMuzzleAttachment( out var muzzle ) )
			return muzzle;

		return base.GetMuzzleTransform();
	}

	/// <summary>
	/// Réduit progressivement le champ de vision pendant l'ADS.
	/// </summary>
	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );

		if ( _aimAmount <= 0f )
			return;

		var isThirdPerson = Owner.IsValid() && Owner.ThirdPerson;
		var fieldOfViewReduction = isThirdPerson
			? EnableThirdPersonAimZoom ? ThirdPersonAimFieldOfViewReduction : 0f
			: EnableFirstPersonAimZoom ? FirstPersonAimFieldOfViewReduction : 0f;

		view.FieldOfView = MathF.Max(
			1f,
			view.FieldOfView - fieldOfViewReduction * _aimAmount
		);
	}

	/// <summary>
	/// Pilote le renderer du viewmodel FPS une fois celui-ci réellement créé et placé.
	/// </summary>
	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		base.PlaceViewModel( camera, in view );

		if ( ViewModel.IsValid() && _aimAmount > 0f )
		{
			ViewModel.WorldPosition += camera.WorldRotation
				* (FirstPersonAimPositionOffset * _aimAmount);

			var aimRotation = Rotation.From( FirstPersonAimRotationOffset );
			ViewModel.WorldRotation *= Rotation.Slerp( Rotation.Identity, aimRotation, _aimAmount );
		}

		UpdateViewModelAimParameters();
	}

	/// <summary>
	/// Dessine un réticule compact ambre inspiré de Half-Life.
	/// </summary>
	public override void DrawCrosshair( HudPainter hud, Vector2 center )
	{
		if ( _aimAmount >= 0.92f && Owner.IsValid() && !Owner.ThirdPerson )
			return;

		var spread = CurrentSpread.Length;
		var gap = Math.Clamp( MathX.Lerp( 8f, 3f, _aimAmount ) + spread * 1.5f, 3f, 18f );
		const float length = 6f;
		var color = CanPrimaryAttack()
			? new Color( 1f, 0.72f, 0.22f, 1f )
			: new Color( 1f, 0.28f, 0.12f, 1f );
		var shadow = Color.Black.WithAlpha( 0.82f );

		hud.SetBlendMode( BlendMode.Normal );
		DrawCrosshairTicks( hud, center, gap, length, 3.5f, shadow );
		DrawCrosshairTicks( hud, center, gap, length, 1.5f, color );
		hud.DrawCircle( center, new Vector2( 4f, 4f ), shadow );
		hud.DrawCircle( center, new Vector2( 2f, 2f ), color );
	}

	/// <summary>
	/// Tire puis évalue les munitions restantes sur le client qui contrôle l'arme.
	/// </summary>
	public override void PrimaryAttack()
	{
		SyncMuzzleMarkerToAttachment();

		var previousClip = Clip1;

		base.PrimaryAttack();

		if ( !UsesPrimaryClip || previousClip <= Clip1 )
			return;

		if ( Clip1 <= 0 )
		{
			PlayLocalFeedback( MagazineDepletedSound );
			return;
		}

		var lowAmmoCount = LowAmmoCount;

		if ( lowAmmoCount > 0 && previousClip > lowAmmoCount && Clip1 <= lowAmmoCount )
		{
			PlayLocalFeedback( LowAmmoSound );
		}
	}

	private int GetLowAmmoCount()
	{
		if ( PrimaryClipSize <= 1 )
			return 0;

		var count = (int)MathF.Ceiling( PrimaryClipSize * LowAmmoThreshold );
		return Math.Clamp( count, 1, PrimaryClipSize - 1 );
	}

	private void PlayLocalFeedback( SoundEvent sound )
	{
		if ( sound is null || !Owner.IsValid() || Owner.IsProxy )
			return;

		Sound.Play( sound );
	}

	private bool TryGetMuzzleAttachment( out Transform muzzle )
	{
		muzzle = default;

		var renderer = WeaponModel?.Renderer;
		if ( !renderer.IsValid() )
			return false;

		var attachment = renderer.GetAttachment( "muzzle" )
			?? renderer.GetAttachment( "barrel" );

		if ( !attachment.HasValue )
			return false;

		muzzle = attachment.Value;
		return true;
	}

	private void SyncMuzzleMarkerToAttachment()
	{
		if ( !TryGetMuzzleAttachment( out var muzzle ) )
			return;

		var marker = WeaponModel?.MuzzleGameObject;
		if ( marker.IsValid() )
			marker.WorldTransform = muzzle;
	}

	private void UpdateViewModelAimParameters()
	{
		var renderer = GetFirstPersonWeaponRenderer();
		if ( !renderer.IsValid() )
			return;

		// Les armes first-person Facepunch utilisent cet enum public pour
		// passer de la pose à la hanche (0) à la mire métallique (1).
		renderer.Set( "ironsights", _wantsToAim ? 1 : 0 );
	}

	private bool IsOwnerSprinting()
	{
		if ( !Owner.IsValid() )
			return false;

		var stamina = Owner.Components.Get<VBStaminaComponent>();
		if ( !stamina.IsValid() )
			return false;

		return stamina.IsSprinting
			|| (stamina.CanSprint && stamina.IsSprintRequested());
	}

	private SkinnedModelRenderer GetFirstPersonWeaponRenderer()
	{
		if ( !ViewModel.IsValid() )
			return null;

		var weaponModel = ViewModel.Components.Get<BaseWeaponModel>( FindMode.InSelf );
		if ( weaponModel.IsValid() && weaponModel.Renderer.IsValid() )
			return weaponModel.Renderer;

		return ViewModel.Components.Get<SkinnedModelRenderer>( FindMode.InSelf );
	}

	private static void DrawCrosshairTicks(
		HudPainter hud,
		Vector2 center,
		float gap,
		float length,
		float width,
		Color color )
	{
		hud.DrawLine( center + Vector2.Left * (gap + length), center + Vector2.Left * gap, width, color );
		hud.DrawLine( center - Vector2.Left * (gap + length), center - Vector2.Left * gap, width, color );
		hud.DrawLine( center + Vector2.Up * (gap + length), center + Vector2.Up * gap, width, color );
		hud.DrawLine( center - Vector2.Up * (gap + length), center - Vector2.Up * gap, width, color );
	}
}
