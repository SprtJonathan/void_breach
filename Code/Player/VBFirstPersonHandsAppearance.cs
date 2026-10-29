using System;
using System.Linq;
using Sandbox;

/// <summary>Dress a bone-merged viewmodel avatar with the player's native Dresser outfit.</summary>
[Title( "First Person Hands Appearance" ), Category( "Void Breach/Player" )]
public sealed class VBFirstPersonHandsAppearance : Component
{
	public PlayerController Player { get; set; }
	private SkinnedModelRenderer _arms;
	private SkinnedModelRenderer _avatar;
	private Dresser _dresser;
	private ulong _originalGroups;
	private int? _outfitHash;

	public static void Attach( GameObject viewModel, PlayerController player )
	{
		if ( !viewModel.IsValid() || !player.IsValid() || player.IsProxy || Application.IsDedicatedServer ) return;
		viewModel.GetOrAddComponent<VBFirstPersonHandsAppearance>().Player = player;
	}

	protected override void OnUpdate()
	{
		if ( !Player.IsValid() || Player.IsProxy || !Player.Renderer.IsValid() ) return;
		var source = Player.GetComponent<Dresser>();
		if ( !source.IsValid() || source.IsDressing ) return;
		if ( !_arms.IsValid() )
		{
			_arms = GameObject.GetComponentsInChildren<SkinnedModelRenderer>( true, true )
				.FirstOrDefault( r => r.Model.IsValid() && r.Model.Parts.All.Any( p => p.Name == "arms" ) );
			if ( !_arms.IsValid() ) return;
			_originalGroups = _arms.BodyGroups;
		}
		var hash = new HashCode();
		hash.Add( Player.Renderer.Model );
		hash.Add( source.Source );
		hash.Add( source.ManualAge ); hash.Add( source.ManualTint );
		hash.Add( source.NeckSize ); hash.Add( source.WaistSize ); hash.Add( source.ChestSize );
		hash.Add( source.HeadShape ); hash.Add( source.NoseSize ); hash.Add( source.ChinSize );
		foreach ( var entry in source.Clothing )
		{
			hash.Add( entry.Clothing ); hash.Add( entry.ItemDefinitionId ); hash.Add( entry.Tint );
		}
		foreach ( var renderer in Player.Renderer.GetComponentsInChildren<SkinnedModelRenderer>( true ) )
		{
			if ( renderer.BoneMergeTarget != Player.Renderer ) continue;
			hash.Add( renderer.Model ); hash.Add( renderer.Tint );
		}
		int current = hash.ToHashCode();
		if ( _outfitHash == current && _avatar.IsValid() ) return;

		var outfit = source.Source switch
		{
			Dresser.ClothingSource.LocalUser => ClothingContainer.CreateFromLocalUser(),
			Dresser.ClothingSource.OwnerConnection => ClothingContainer.CreateFromConnection( Player.Network.Owner ?? Connection.Local, source.RemoveUnownedItems ),
			_ => new ClothingContainer()
		};
		if ( source.Source == Dresser.ClothingSource.Manual ) outfit.AddRange( source.Clothing );
		// Workshop items already installed by the body Dresser may not be in its
		// manual list. Reuse their loaded clothing resources, not generated meshes.
		var worn = Player.Renderer.GetComponentsInChildren<SkinnedModelRenderer>( true )
			.Where( r => r.BoneMergeTarget == Player.Renderer && r.Model.IsValid() ).Select( r => r.Model.Name ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var clothing in ResourceLibrary.GetAll<Clothing>() )
			if ( worn.Contains( clothing.Model ?? "" ) || worn.Contains( clothing.HumanAltModel ?? "" ) || worn.Contains( clothing.HumanAltFemaleModel ?? "" ) )
				if ( !outfit.Has( clothing ) ) outfit.Add( clothing );

		if ( !_avatar.IsValid() )
		{
			var go = new GameObject( true, "Dressed viewmodel avatar" );
			go.SetParent( _arms.GameObject, keepWorldPosition: false );
			go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			_avatar = go.AddComponent<SkinnedModelRenderer>();
			_avatar.Model = Player.Renderer.Model;
			_avatar.BoneMergeTarget = _arms;
			_avatar.UseAnimGraph = false;
			_avatar.RenderType = ModelRenderer.ShadowRenderType.Off;
			_dresser = Dresser.GetOrCreate( _avatar );
			_dresser.ApplyHeightScale = false;
		}
		_avatar.Model = Player.Renderer.Model;
		_dresser.ManualAge = source.ManualAge; _dresser.ManualTint = source.ManualTint;
		_dresser.NeckSize = source.NeckSize; _dresser.WaistSize = source.WaistSize; _dresser.ChestSize = source.ChestSize;
		_dresser.HeadShape = source.HeadShape; _dresser.NoseSize = source.NoseSize; _dresser.ChinSize = source.ChinSize;
		_dresser.Apply( outfit );
		foreach ( var part in _avatar.Model.Parts.All )
			if ( part.Name is "Head" or "Legs" or "Feet" ) _avatar.SetBodyGroup( part.Name, part.Choices.Count - 1 );
		foreach ( var renderer in _avatar.GetComponentsInChildren<SkinnedModelRenderer>() )
		{
			if ( renderer == _avatar ) continue;
			if ( !renderer.Model.Bones.AllBones.Any( b => b.Name.StartsWith( "arm_", StringComparison.Ordinal ) || b.Name is "hand_L" or "hand_R" ) )
			{
				renderer.Enabled = false;
				continue;
			}
			// Merge clothing directly onto the FPS rig: the full body avatar has
			// ragdoll physics which would restore its torso every animation pass.
			renderer.BoneMergeTarget = _arms;
			MaskBodyBones( renderer );
		}
		_arms.SetBodyGroup( "arms", _arms.Model.Parts.Get( "arms" ).Choices.Count - 1 );
		_outfitHash = current;
	}

	private void MaskBodyBones( SkinnedModelRenderer renderer )
	{
		var hidden = new Transform( Vector3.Zero, Rotation.Identity, Vector3.One * 0.0001f );
		foreach ( var bone in renderer.Model.Bones.AllBones )
		{
			if ( _arms.Model.Bones.GetBone( bone.Name ) is not null ) continue;
			if ( bone.Name.StartsWith( "arm_", StringComparison.Ordinal )
				|| bone.Name.StartsWith( "hand_", StringComparison.Ordinal )
				|| bone.Name.StartsWith( "finger_", StringComparison.Ordinal ) ) continue;
			renderer.SetBoneTransform( bone, hidden );
		}
	}

	protected override void OnDisabled()
	{
		if ( _arms.IsValid() ) _arms.BodyGroups = _originalGroups;
		if ( _avatar.IsValid() ) _avatar.GameObject.Destroy();
		_outfitHash = null;
	}
	protected override void OnDestroy() => OnDisabled();
}
