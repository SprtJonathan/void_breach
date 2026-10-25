using Sandbox;

/// <summary>A prefab-authored accessory mount, shared by view, held and pickup models.</summary>
[Title( "Weapon Attachment" )]
[Category( "Void Breach/Weapons" )]
public sealed class VBWeaponAttachment : Component, Component.ExecuteInEditor
{
	[Property] public ModelRenderer MountRenderer { get; set; }
	[Property] public ModelRenderer AttachmentRenderer { get; set; }

	/// <summary>Leave empty for a static world model. Animated mounts follow this bone.</summary>
	[Property] public string BoneName { get; set; } = "weapon_root";

	/// <summary>Pose relative to the bone, or the model origin when BoneName is empty.</summary>
	[Property] public Vector3 MountPosition { get; set; }
	[Property] public Angles MountRotation { get; set; }
	[Property] public float MountScale { get; set; } = 1f;
	[Property] public bool WorldPickupOnly { get; set; }

	protected override void OnUpdate() => UpdateMount();
	protected override void OnPreRender() => UpdateMount();

	protected override void OnDisabled()
	{
		if ( AttachmentRenderer.IsValid() ) AttachmentRenderer.Enabled = false;
	}

	private void UpdateMount()
	{
		if ( !AttachmentRenderer.IsValid() ) return;
		ApplySafeReticle();
		// Prop can recreate its renderer after a pickup/drop cycle.
		if ( !MountRenderer.IsValid() && GameObject.Parent.IsValid() )
			MountRenderer = GameObject.Parent.GetComponent<ModelRenderer>( includeDisabled: true );
		var item = WorldPickupOnly ? GetComponentInParent<BaseInventoryItem>() : null;
		if ( WorldPickupOnly && item.IsValid() && item.Inventory.IsValid() )
		{
			AttachmentRenderer.Enabled = false;
			return;
		}
		if ( !MountRenderer.IsValid() || !MountRenderer.Active )
		{
			AttachmentRenderer.Enabled = false;
			return;
		}

		var anchor = MountRenderer.GameObject;
		var previewMount = MountRenderer.WorldTransform;
		if ( !string.IsNullOrWhiteSpace( BoneName ) )
		{
			if ( MountRenderer is not SkinnedModelRenderer animated )
			{
				AttachmentRenderer.Enabled = false;
				return;
			}
			if ( Scene.IsEditor )
			{
				if ( !animated.TryGetBoneTransform( BoneName, out previewMount ) ) return;
			}
			else
			{
				// MP5 viewmodels do not create bone objects by default.
				if ( !animated.CreateBoneObjects ) animated.CreateBoneObjects = true;
				anchor = animated.GetBoneObject( BoneName );
				if ( !anchor.IsValid() )
				{
					AttachmentRenderer.Enabled = false;
					return;
				}
			}
		}

		var pose = new Transform( MountPosition, Rotation.From( MountRotation ), MountScale );
		if ( Scene.IsEditor )
		{
			// Keep the authored prefab hierarchy intact while previewing it.
			WorldTransform = previewMount.ToWorld( pose );
		}
		else
		{
			// Follow the native bone hierarchy. Sampling world-space bones before
			// camera placement leaves the sight behind when the viewmodel moves.
			if ( GameObject.Parent != anchor ) GameObject.SetParent( anchor, keepWorldPosition: false );
			LocalTransform = pose;
		}
		AttachmentRenderer.Enabled = true;
		AttachmentRenderer.RenderType = MountRenderer.RenderType;

		// Native weapon/camera code changes these locally when switching perspectives.
		// Preserve the accessory's own material flags (notably translucent sight glass).
		var source = MountRenderer.SceneObject;
		var target = AttachmentRenderer.SceneObject;
		if ( !source.IsValid() || !target.IsValid() ) return;
		target.Flags.ViewModelLayer = source.Flags.ViewModelLayer;
		target.Flags.ExcludeGameLayer = source.Flags.ExcludeGameLayer;
		target.Flags.OverlayLayer = source.Flags.OverlayLayer;
	}

	private void ApplySafeReticle()
	{
		var materials = AttachmentRenderer.Materials;
		for ( int i = 0; i < materials.Count; i++ )
		{
			var source = materials.GetOverride( i ) ?? materials.GetOriginal( i );
			if ( source?.ShaderName != "shaders/glass_scope.shader" ) continue;

			// This cloud shader faults in Vulkan when explosions are rendered (Aftermath
			// MMU dumps). Replace only the reticle, leaving the housing and glass intact.
			materials.SetOverride( i, Material.Load( "materials/weapons/holographic_reticle.vmat" ) );
		}
	}
}
