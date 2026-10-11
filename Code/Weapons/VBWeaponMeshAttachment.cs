using Sandbox;

/// <summary>Parents an imported weapon mesh to an existing animated weapon bone.</summary>
[Title( "Weapon Mesh Attachment" )]
[Category( "Void Breach/Weapons" )]
public sealed class VBWeaponMeshAttachment : Component
{
	[Property] public SkinnedModelRenderer AnimationSource { get; set; }
	[Property] public string BoneName { get; set; } = "weapon_root";

	private bool _attached;

	protected override void OnStart() => AttachToBone();

	protected override void OnUpdate()
	{
		if ( !_attached ) AttachToBone();
	}

	private void AttachToBone()
	{
		if ( !AnimationSource.IsValid() || string.IsNullOrWhiteSpace( BoneName ) ) return;
		var bone = AnimationSource.GetBoneObject( BoneName );
		if ( !bone.IsValid() ) return;

		// Keep the authored LOCAL pose: this is the designer's grip adjustment.
		GameObject.SetParent( bone, keepWorldPosition: false );
		_attached = true;
	}
}
