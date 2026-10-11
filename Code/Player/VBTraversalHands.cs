using System;
using Sandbox;

/// <summary>Cosmetic native Citizen hand IK; never moves the player or decides whether a grab succeeds.</summary>
[Title( "Traversal Hands IK" ), Category( "Void Breach/Player" ), Icon( "pan_tool" )]
public sealed class VBTraversalHands : Component, ICameraModifier
{
	[Property] public bool EnableHands { get; set; } = true;
	[Property] public bool FaceLedge { get; set; } = true;
	[Property, Range( 8f, 48f )] public float HandSpacing { get; set; } = 28f;
	[Property] public Vector3 GripOffset { get; set; } = new( 3f, 0f, 1f );
	[Property] public Angles LeftHandRotation { get; set; } = new( 0f, 0f, -90f );
	[Property] public Angles RightHandRotation { get; set; } = new( 0f, 0f, 90f );
	[Property, Range( 0f, 0.5f )] public float BlendInTime { get; set; } = 0.12f;
	[Property, Range( 0f, 0.5f )] public float BlendOutTime { get; set; } = 0.15f;
	/// <summary>Release gradually as the feet approach the top during the climb.</summary>
	[Property, Range( 8f, 64f )] public float ReleaseHeight { get; set; } = 36f;
	/// <summary>Show the actual character arms instead of the held item's separate FPS arms during a grip.</summary>
	[Property] public bool HideHeldViewModel { get; set; } = true;
	public bool IsShowingFirstPersonHands => !IsProxy && Enabled && EnableHands && HideHeldViewModel && _hasPose && _blend > 0f
		&& _controller.IsValid() && !_controller.ThirdPerson && !_controller.HideBodyInFirstPerson;

	private PlayerController _controller;
	private VBMoveModeTraversal _traversal;
	private BaseInventoryComponent _inventory;
	private Dresser _dresser;
	private SkinnedModelRenderer _renderer;
	private GameObject _hiddenViewModel;
	private bool _viewModelWasEnabled;
	private Transform _neutralLeft, _neutralRight, _grip;
	private float _blend;
	private bool _hasPose;
	private bool _headHidden;

	int ICameraModifier.CameraOrder => 300;
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view ) { }
	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		if ( IsProxy ) return;
		var viewModel = _inventory.IsValid() && _inventory.ActiveItem is IVBFirstPersonHeldItem held
			? held.FirstPersonViewModel : null;
		bool hide = IsShowingFirstPersonHands;
		if ( !hide || _hiddenViewModel != viewModel ) RestoreViewModel();
		if ( !hide || !viewModel.IsValid() ) return;
		if ( !_hiddenViewModel.IsValid() ) { _hiddenViewModel = viewModel; _viewModelWasEnabled = viewModel.Enabled; }
		viewModel.Enabled = false;
	}

	internal void UpdateHands()
	{
		if ( Application.IsDedicatedServer || Scene.IsEditor ) return;
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>();
		if ( !_traversal.IsValid() ) _traversal = Components.Get<VBMoveModeTraversal>();
		if ( !_inventory.IsValid() ) _inventory = Components.Get<BaseInventoryComponent>();
		var renderer = _controller.IsValid() ? _controller.Renderer : null;
		if ( renderer != _renderer ) { ClearHands(); _renderer = renderer; }
		if ( !_renderer.IsValid() || !_traversal.IsValid() ) return;

		float target = EnableHands && _controller.Enabled && _traversal.Enabled && _traversal.IsTraversing ? 1f : 0f;
		if ( target > 0f )
		{
			_grip = _traversal.Grip;
			if ( _traversal.State is VBTraversalState.Climbing or VBTraversalState.Vaulting )
				target *= Math.Clamp( (_grip.Position.z - WorldPosition.z) / MathF.Max( ReleaseHeight, 1f ), 0f, 1f );
		}
		if ( target > 0f && !_hasPose )
		{
			if ( !_renderer.TryGetBoneTransform( "hand_L", out var left ) || !_renderer.TryGetBoneTransform( "hand_R", out var right ) ) return;
			_neutralLeft = _renderer.WorldTransform.ToLocal( left );
			_neutralRight = _renderer.WorldTransform.ToLocal( right );
			_hasPose = true;
		}
		if ( !_hasPose ) return;
		float duration = target > _blend ? BlendInTime : BlendOutTime;
		_blend = duration <= 0f ? target : MathX.Approach( _blend, target, MathF.Max( Time.Delta, 0f ) / duration );
		if ( _blend <= 0f ) { ClearHands(); return; }
		float weight = _blend * _blend * (3f - 2f * _blend);
		if ( FaceLedge && target > 0f )
		{
			_renderer.WorldRotation = Rotation.Slerp( _renderer.WorldRotation, _grip.Rotation, weight );
			_renderer.SetLookDirection( "aim_body", _grip.Rotation.Forward, weight );
		}
		ApplyHand( "hand_left", _neutralLeft, -1f, LeftHandRotation, weight );
		ApplyHand( "hand_right", _neutralRight, 1f, RightHandRotation, weight );
	}

	private void ApplyHand( string chain, Transform neutral, float side, Angles rotation, float weight )
	{
		var rest = _renderer.WorldTransform.ToWorld( neutral );
		var position = _grip.PointToWorld( GripOffset + Vector3.Right * (side * MathF.Max( HandSpacing, 0f ) * 0.5f) );
		var targetRotation = _grip.Rotation * Rotation.From( rotation );
		_renderer.SetIk( chain, new Transform( Vector3.Lerp( rest.Position, position, weight ), Rotation.Slerp( rest.Rotation, targetRotation, weight ) ) );
	}

	private void RestoreViewModel()
	{
		if ( _hiddenViewModel.IsValid() ) _hiddenViewModel.Enabled = _viewModelWasEnabled;
		_hiddenViewModel = null;
	}
	internal bool IsHeadClothing( ModelRenderer renderer )
	{
		if ( !IsShowingFirstPersonHands || !renderer.Model.IsValid() ) return false;
		if ( !_dresser.IsValid() ) _dresser = Components.Get<Dresser>();
		if ( !_dresser.IsValid() ) return false;
		foreach ( var entry in _dresser.Clothing )
		{
			var clothing = entry.Clothing;
			if ( !clothing.IsValid() || clothing.Category is not (Clothing.ClothingCategory.Hat or Clothing.ClothingCategory.HatCap
				or Clothing.ClothingCategory.Hair or Clothing.ClothingCategory.Facial or Clothing.ClothingCategory.Eyewear or Clothing.ClothingCategory.EarringStud) ) continue;
			string model = renderer.Model.Name;
			if ( string.Equals( model, clothing.Model, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( model, clothing.HumanAltModel, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( model, clothing.HumanAltFemaleModel, StringComparison.OrdinalIgnoreCase ) ) return true;
		}
		return false;
	}
	internal void UpdateFirstPersonHead()
	{
		bool hide = IsShowingFirstPersonHands;
		if ( (!_headHidden && !hide) || !_renderer.IsValid() || !_renderer.SceneModel.IsValid() || !_renderer.Model.IsValid() ) return;
		foreach ( var part in _renderer.Model.Parts.All )
		{
			if ( !string.Equals( part.Name, "Head", StringComparison.OrdinalIgnoreCase ) || part.Choices.Count < 2 ) continue;
			// Native Citizen models reserve the final Head choice for an empty mesh.
			// Change only the local render object; component bodygroups remain intact in snapshots.
			_renderer.SceneModel.SetBodyGroup( part.Name, hide ? part.Choices.Count - 1 : _renderer.GetBodyGroup( part.Name ) );
		}
		_headHidden = hide;
	}
	private void ClearHands()
	{
		if ( _hasPose && _renderer.IsValid() ) { _renderer.ClearIk( "hand_left" ); _renderer.ClearIk( "hand_right" ); }
		_blend = 0f; _hasPose = false;
		UpdateFirstPersonHead();
		RestoreViewModel();
	}
	protected override void OnDisabled() => ClearHands();
	protected override void OnDestroy() => ClearHands();
}

/// <summary>Apply after native controller/weapon animation parameters, before the next animation evaluation.</summary>
public sealed class VBTraversalHandsSystem : GameObjectSystem<VBTraversalHandsSystem>
{
	public VBTraversalHandsSystem( Scene scene ) : base( scene ) => Listen( Stage.FinishUpdate, 100, UpdateHands, "Traversal hand IK" );
	private void UpdateHands()
	{
		if ( Scene.IsEditor || Application.IsDedicatedServer ) return;
		foreach ( var hands in Scene.GetAllComponents<VBTraversalHands>() )
			if ( hands.Enabled && hands.GameObject.Active ) hands.UpdateHands();
	}
}
