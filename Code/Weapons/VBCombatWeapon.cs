using System;
using Sandbox;
using Sandbox.Rendering;

/// <summary>
/// Arme de combat Void Breach avec notifications locales configurables
/// pour les états de munitions du chargeur.
/// </summary>
[Title( "Void Breach Combat Weapon" )]
[Category( "Void Breach/Weapons" )]
public sealed class VBCombatWeapon : VBTraversalCombatWeapon, IVBWeaponCarryItem, IVBFirstPersonHeldItem, Component.IPressable
{
	/// <summary>
	/// Amount consumed from the shared five-point weapon carrying budget.
	/// </summary>
	[Property, Group( "Inventory" ), Range( 1, 5 )]
	public int CarrySize { get; set; } = 1;

	[Property, Group( "Inventory" )]
	public VBWeaponSlotCategory SlotCategory { get; set; } = VBWeaponSlotCategory.Main;

	public bool IsWeaponSlotPlaceholder => false;

	bool Component.IPressable.CanPress( Component.IPressable.Event e ) =>
		e.Source.IsValid() && (e.Source.GetComponentInParent<VBWeaponInventoryComponent>( includeSelf: true )?.CanSwapWeapon( this ) ?? false);
	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		if ( !((Component.IPressable)this).CanPress( e ) ) return false;
		e.Source.GetComponentInParent<VBWeaponInventoryComponent>( includeSelf: true ).SwapWeapon( this );
		return true;
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		base.OnAdded( inventory );
		// NPC inaccuracy belongs to the shooter, not a permanent modification of
		// the physical weapon a player may recover afterwards.
		if ( inventory.GetComponent<PlayerController>().IsValid() ) SpreadScale = 1f;
	}

	protected override bool OnDrop()
	{
		bool npcHeld = NpcHolder.IsValid();
		var player = Inventory.IsValid() ? Inventory.GetComponent<PlayerController>() : null;
		var eyes = player.IsValid() ? player.EyeTransform : new Transform( WorldPosition + Vector3.Up * 40f, WorldRotation );
		var owner = Inventory.IsValid() ? Inventory.GameObject : GameObject;
		var trace = Scene.Trace.Sphere( 6f, eyes.Position, eyes.Position + eyes.Rotation.Forward * 28f )
			.IgnoreGameObjectHierarchy( owner ).Run();
		if ( !base.OnDrop() ) return false;
		if ( npcHeld ) SpreadScale = 1f;
		WorldPosition = trace.StartedSolid ? eyes.Position : trace.EndPosition - eyes.Rotation.Forward * 3f;
		WorldRotation = Rotation.FromYaw( eyes.Rotation.Yaw() );
		var body = GetComponent<Rigidbody>( includeDisabled: true );
		if ( body.IsValid() ) body.Velocity = eyes.Rotation.Forward * 150f + Vector3.Up * 50f;
		return true;
	}

	/// <summary>Use the prefab muzzle when an animation donor has a different barrel.</summary>
	[Property, Group( "Presentation" )]
	public bool PreferMuzzleMarker { get; set; }

	// Keep serialized property names so existing weapon profiles apply to both players and NPCs.
	[Property, Group( "Third Person Hold" )] public Vector3 NpcHoldPositionOffset { get; set; } = Vector3.Zero;
	[Property, Group( "Third Person Hold" )] public Angles NpcHoldRotationOffset { get; set; } = Angles.Zero;
	[Property, Group( "Third Person Hold" )] public bool NpcUseSupportHand { get; set; }
	[Property, Group( "Third Person Hold" )] public Vector3 NpcSupportHandPosition { get; set; }
	[Property, Group( "Third Person Hold" )] public Angles NpcSupportHandRotation { get; set; }
	public GameObject NpcSupportHandTarget => _npcSupportHand.IsValid() && _npcSupportHand.Active ? _npcSupportHand : null;
	private GameObject _npcSupportHand;

	[ConCmd( "vb_soldier_grip_reload" )]
	public static void ReloadNpcGrips()
	{
		if ( !Game.IsPlaying || !Networking.IsHost || Game.ActiveScene is null ) return;
		foreach ( var path in new[] { "weapons/mp5/mp5.prefab", "weapons/p90/p90.prefab", "weapons/pistol/9mmpistol.prefab" } )
		{
			var prefab = GameObject.GetPrefab( path );
			var profile = prefab.GetComponent<VBCombatWeapon>();
			foreach ( var gun in Game.ActiveScene.GetAllComponents<VBCombatWeapon>() )
			{
				if ( !gun.NpcHolder.IsValid() || gun.GameObject.Name != prefab.Name ) continue;
				gun.NpcHoldPositionOffset = profile.NpcHoldPositionOffset;
				gun.NpcHoldRotationOffset = profile.NpcHoldRotationOffset;
				gun.NpcUseSupportHand = profile.NpcUseSupportHand;
				gun.NpcSupportHandPosition = profile.NpcSupportHandPosition;
				gun.NpcSupportHandRotation = profile.NpcSupportHandRotation;
				if ( gun.IsActive ) gun.CreateWorldModel();
			}
		}
	}

	[ConCmd( "vb_soldier_grip_debug" )]
	public static void DumpNpcGrips()
	{
		if ( Game.ActiveScene is null ) return;
		foreach ( var gun in Game.ActiveScene.GetAllComponents<VBCombatWeapon>() )
		{
			var npc = gun.NpcHolder;
			if ( !npc.IsValid() || !gun.IsActive || !gun.WorldModel.IsValid() || !npc.BodyRenderer.IsValid() ) continue;
			foreach ( var name in new[] { "hand_R", "hand_L", "hold_R" } )
				if ( npc.BodyRenderer.TryGetBoneTransform( name, out var bone ) )
				{
					var local = gun.WorldModel.WorldTransform.ToLocal( bone );
					Log.Info( $"NPC grip {npc.GameObject.Name}/{gun.GameObject.Name} {name}: position={local.Position} angles={local.Rotation.Angles()}" );
				}
		}
	}

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

	/// <summary>Accuracy multiplier while aiming; one preserves the native spread.</summary>
	[Property, Group( "Aim Down Sights" ), Range( 0.1f, 1f )]
	public float AimSpreadMultiplier { get; set; } = 1f;

	/// <summary>Owner's eye-angle kick per successful shot, in degrees.</summary>
	[Property, Group( "Recoil" )]
	public Vector2 RecoilPitch { get; set; } = Vector2.Zero;

	[Property, Group( "Recoil" )]
	public Vector2 RecoilYaw { get; set; } = Vector2.Zero;

	[Property, Group( "Recoil" ), Range( 0f, 1f )]
	public float AimRecoilMultiplier { get; set; } = 1f;

	public override Vector2 CurrentSpread => base.CurrentSpread
		* MathX.Lerp( 1f, AimSpreadMultiplier, _aimAmount )
		* (NpcHolder.IsValid() ? NpcHolder.FiringStanceAccuracyMultiplier : 1f);

	/// <summary>
	/// Progression visuelle de l'ADS, de 0 (hanche) à 1 (visée).
	/// </summary>
	public float AimAmount => _aimAmount;
	public bool SuppressHudCrosshair => _wantsToAim || _aimAmount > 0.01f;
	public GameObject FirstPersonViewModel => ViewModel;
	public float FirstPersonMotionAimAmount => _aimAmount;

	/// <summary>
	/// Indique si l'arme est suffisamment épaulée pour être considérée en ADS.
	/// </summary>
	public bool IsAiming => _aimAmount >= 0.5f;

	private VBSoldierController NpcHolder => Inventory.IsValid() ? Inventory.GetComponent<VBSoldierController>() : null;
	protected override GameObject Attacker => NpcHolder.IsValid() ? NpcHolder.GameObject : base.Attacker;

	private bool HasUnlimitedNpcReserve( BaseAmmoResource ammo )
	{
		var npc = NpcHolder;
		return npc.IsValid() && npc.Sidearm == this && npc.SidearmReserveMagazines == -1
			&& ammo is not null && ammo == PrimaryAmmoType;
	}

	// Only the NPC's reserve is unlimited. Native clip sizes, reloads and fire
	// delays still apply, and dropping the gun restores ordinary player ammo rules.
	protected override int GetReserveAmmo( BaseAmmoResource ammo ) => HasUnlimitedNpcReserve( ammo )
		? int.MaxValue : base.GetReserveAmmo( ammo );

	protected override int TakeReserveAmmo( BaseAmmoResource ammo, int amount ) => HasUnlimitedNpcReserve( ammo )
		? Math.Max( 0, amount ) : base.TakeReserveAmmo( ammo, amount );

	public override bool HasPrimaryAmmo()
	{
		// The native implementation assumes unlimited ammunition without a
		// PlayerController holder. NPCs must inspect the same real clip/pool.
		if ( !NpcHolder.IsValid() ) return base.HasPrimaryAmmo();
		return !UsesAmmo || (UsesPrimaryClip ? Clip1 > 0 : Ammo1 > 0);
	}

	public override bool CanPrimaryAttack()
	{
		var npc = NpcHolder;
		return (!npc.IsValid() || IsActive && npc.CanFight && !npc.IsDynamicallyTraversing) && base.CanPrimaryAttack();
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius )
	{
		// NPC locomotion capsules provide physical blocking. Their animated model
		// hitboxes must receive bullets so head/limb locations survive the trace.
		return base.BulletTrace( ray, distance, radius ).WithoutTags( new[] { "npc_body" } );
	}

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

	/// <summary>
	/// Rayon de gameplay dans lequel un tir peut être entendu par l'IA.
	/// Zéro désactive le stimulus sans couper le son audio du tir.
	/// </summary>
	[Property, Group( "AI Hearing" ), Range( 0f, 5000f ), Step( 50f )]
	public float GunshotAiRadius { get; set; } = 1800f;

	private float _aimAmount;
	private bool _wantsToAim;

	/// <summary>
	/// Nombre de cartouches correspondant au seuil configuré pour ce chargeur.
	/// </summary>
	public int LowAmmoCount => GetLowAmmoCount();

	/// <summary>
	/// Garantit le modèle 3PP sur chaque pair.
	/// </summary>
	protected override void OnUpdate()
	{
		if ( Application.IsDedicatedServer )
			return;

		// A carried item is not necessarily the deployed item. Calling the
		// native update for holstered weapons would let them overwrite the
		// active weapon's hold type, and recreating their world model would
		// leave the wrong weapon in the character's hands.
		if ( !IsActive )
		{
			DestroyWorldModel();
			return;
		}

		base.OnUpdate();
		var npc = NpcHolder;
		if ( npc.IsValid() )
		{
			if ( !WorldModel.IsValid() && npc.BodyRenderer.IsValid() ) CreateWorldModel();
			if ( WorldModel.IsValid() ) WorldModel.Enabled = npc.IsWeaponPoseActive;
			if ( npc.BodyRenderer.IsValid() && npc.IsWeaponPoseActive ) UpdateBodyAnimations( npc.BodyRenderer );
		}

		// Le world model n'est pas un objet réseau : chaque pair doit posséder
		// sa propre instance. Cette vérification couvre notamment l'arrivée
		// tardive de l'ownership ou de l'ActiveItem au spawn.
		if ( IsHeld && !WorldModel.IsValid() )
			CreateWorldModel();
	}

	protected override void CreateWorldModel()
	{
		var npc = NpcHolder;
		var renderer = npc.IsValid() ? npc.BodyRenderer : HolderRenderer;
		DestroyWorldModel();
		if ( Application.IsDedicatedServer || !WorldModelPrefab.IsValid() || !renderer.IsValid() ) return;
		var bone = renderer.GetBoneObject( HoldBone );
		if ( !bone.IsValid() ) return;
		WorldModel = WorldModelPrefab.Clone( new CloneConfig { Parent = bone, StartEnabled = true, Transform = ThirdPersonHoldTransform } );
		WorldModel.LocalTransform = ThirdPersonHoldTransform;
		WorldModel.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		// Human's weapon graph locks the player's support hand to the right
		// hand in the same animation pass. A target on the gun would feed the
		// preceding frame's pose back into that graph.
		if ( npc.IsValid() && NpcUseSupportHand )
		{
			_npcSupportHand = new GameObject( true, "Weapon support hand" );
			_npcSupportHand.SetParent( WorldModel, keepWorldPosition: false );
			_npcSupportHand.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			_npcSupportHand.LocalTransform = new Transform( NpcSupportHandPosition, Rotation.From( NpcSupportHandRotation ) );
		}
	}

	private Transform ThirdPersonHoldTransform
	{
		get
		{
			// The authored world prefab contains grip corrections (notably the USP
			// translation and P90 roll). Transform.Zero discarded those corrections.
			var authored = WorldModelPrefab.LocalTransform;
			authored.Position += NpcHoldPositionOffset;
			authored.Rotation *= Rotation.From( NpcHoldRotationOffset );
			return authored;
		}
	}

	protected override void OnPreRender()
	{
		base.OnPreRender();
		var npc = NpcHolder;
		var renderer = npc.IsValid() ? npc.BodyRenderer : HolderRenderer;
		if ( !IsActive || !WorldModel.IsValid() || !renderer.IsValid() || !WorldModelPrefab.IsValid() ) return;
		var bone = renderer.GetBoneObject( HoldBone );
		if ( !bone.IsValid() ) return;
		// Dresser can rebuild the body/bones after the weapon deploys.
		if ( WorldModel.Parent != bone ) WorldModel.SetParent( bone, keepWorldPosition: false );
		WorldModel.LocalTransform = ThirdPersonHoldTransform;
		if ( _npcSupportHand.IsValid() ) _npcSupportHand.LocalTransform = new Transform( NpcSupportHandPosition, Rotation.From( NpcSupportHandRotation ) );
		if ( npc.IsValid() ) npc.UpdateWeaponHands();
	}

	protected override void DestroyWorldModel()
	{
		_npcSupportHand = null;
		base.DestroyWorldModel();
	}

	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();
		var npc = NpcHolder;
		if ( npc.IsValid() ) npc.BodyRenderer?.Set( "b_reload", true );
	}

	/// <summary>
	/// Met à jour l'ADS en continu tant que le clic droit est maintenu.
	/// </summary>
	protected override void Think()
	{
		base.Think();

		_wantsToAim = IsHeld
			&& !IsTraversalBlockingActions
			&& !IsReloading
			&& Input.Down( "Attack2" )
			&& !IsOwnerSprinting();
		var target = _wantsToAim ? 1f : 0f;
		var transition = Math.Clamp( AimTransitionSpeed * Time.Delta, 0f, 1f );

		_aimAmount = IsTraversalBlockingActions ? 0f : MathX.Lerp( _aimAmount, target, transition );

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
			var npc = NpcHolder;
			if ( npc.IsValid() )
			{
				var origin = GetMuzzleTransform().Position;
				return new Ray( origin, (npc.AimPosition - origin).Normal );
			}
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
		var npc = NpcHolder;
		if ( npc.IsValid() && (!WorldModel.IsValid() || Application.IsDedicatedServer) )
			return new Transform( npc.FallbackMuzzlePosition, npc.WorldRotation );
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
	/// Tire puis évalue les munitions restantes sur le client qui contrôle l'arme.
	/// </summary>
	public override void PrimaryAttack()
	{
		if ( IsTraversalBlockingActions ) return;
		SyncMuzzleMarkerToAttachment();

		var previousClip = Clip1;
		var npc = NpcHolder;
		// Native PrimaryAttack only spends ammunition for PlayerController holders.
		// NPC guns use the same clip/reserve API, with exactly one spend on the host.
		if ( npc.IsValid() && !IsHeld
			&& (!Networking.IsHost || !IsActive || !npc.CanFight || IsReloading || !TakePrimaryAmmo( 1 )) ) return;

		base.PrimaryAttack();

		if ( !UsesPrimaryClip || previousClip <= Clip1 )
			return;

		ApplyShotRecoil();
		ReportGunshotToHost();

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

	/// <summary>
	/// The shooter hears a dry 2D shot. Keep native world audio for other peers,
	/// and let the native presentation pipeline handle impacts and tracers.
	/// </summary>
	protected override void OnShootEffects( ShotEffect shot )
	{
		// Native hitscan leaves Origin null and supplies EndPosition even when nothing solid is hit.
		// NoEvents suppresses shared muzzle/audio events, not individual pellet impacts.
		if ( !Application.IsDedicatedServer && (shot.Hit || shot.HitPosition != default) )
			VoidBreach.Water.FluidImpacts.Segment( Scene, shot.Origin ?? GetMuzzleTransform().Position, shot.HitPosition );
		var npc = NpcHolder;
		if ( !shot.NoEvents && npc.IsValid() ) npc.BodyRenderer?.Set( "b_attack", true );
		if ( Application.IsDedicatedServer || shot.NoEvents
			|| !IsHeld || !Owner.IsValid() || Owner.IsProxy )
		{
			base.OnShootEffects( shot );
			return;
		}

		// Skip only the native event sound/gesture on the local shooter, so
		// there is exactly one shot sound and one model event per shot.
		base.OnShootEffects( shot with { NoEvents = true } );

		if ( AttackSound is not null )
		{
			var sound = Sound.Play( AttackSound );
			if ( sound.IsValid() )
			{
				sound.SpacialBlend = 0f;
				sound.OcclusionEnabled = false;
				sound.DistanceAttenuation = false;
				sound.AirAbsorption = false;
				sound.ReverbEnabled = false;
			}
		}

		HolderRenderer?.Set( "b_attack", true );
		Vector3? hitPoint = shot.Hit || shot.HitPosition != default ? shot.HitPosition : null;
		WeaponModel?.OnAttack( hitPoint, shot.Origin );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void ReportGunshotToHost()
	{
		if ( GunshotAiRadius <= 0f )
			return;

		var source = Owner.IsValid() ? Owner.GameObject.Root : GameObject.Root;
		var position = GetMuzzleTransform().Position;

		if ( source.IsValid() && Vector3.DistanceBetween( position, source.WorldPosition ) > 256f )
			position = source.WorldPosition + Vector3.Up * 48f;

		VBAiSoundStimulus.Emit( position, GunshotAiRadius, source, VBAiSoundKind.Gunshot );
	}

	private int GetLowAmmoCount()
	{
		if ( PrimaryClipSize <= 1 )
			return 0;

		var count = (int)MathF.Ceiling( PrimaryClipSize * LowAmmoThreshold );
		return Math.Clamp( count, 1, PrimaryClipSize - 1 );
	}

	private void ApplyShotRecoil()
	{
		if ( !Owner.IsValid() || Owner.IsProxy )
			return;
		if ( RecoilPitch == Vector2.Zero && RecoilYaw == Vector2.Zero )
			return;

		var scale = MathX.Lerp( 1f, AimRecoilMultiplier, _aimAmount );
		var pitch = MathX.Lerp( RecoilPitch.x, RecoilPitch.y, Game.Random.NextSingle() );
		var yaw = MathX.Lerp( RecoilYaw.x, RecoilYaw.y, Game.Random.NextSingle() );
		var angles = Owner.EyeAngles + new Angles( pitch * scale, yaw * scale, 0f );
		angles.pitch = Math.Clamp( angles.pitch, -89f, 89f );
		Owner.EyeAngles = angles;
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

		var explicitMarker = WeaponModel?.MuzzleGameObject;
		if ( PreferMuzzleMarker && explicitMarker.IsValid() )
		{
			muzzle = explicitMarker.WorldTransform;
			return true;
		}

		var renderer = WeaponModel?.Renderer;
		if ( !renderer.IsValid() )
			return false;

		var attachment = renderer.GetAttachment( "muzzle" )
			?? renderer.GetAttachment( "barrel" );

		if ( !attachment.HasValue )
		{
			// Imported props may have no model attachments. Their prefab still
			// supplies the native weapon model's explicit muzzle marker.
			var marker = WeaponModel?.MuzzleGameObject;
			if ( !marker.IsValid() ) return false;
			muzzle = marker.WorldTransform;
			return true;
		}

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

		// Resolve the native enum by option name; its values are not integer indices.
		renderer.Set( "ironsights", _wantsToAim ? "enabled" : "not_in_ironsights" );
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

}
