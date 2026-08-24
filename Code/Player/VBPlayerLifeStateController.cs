using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

/// <summary>
/// Optional contract for the future team system. Spectating prefers living
/// players with the same non-negative team id, then falls back to everyone.
/// </summary>
public interface IVBTeamMember
{
	int TeamId { get; }
}

/// <summary>
/// Présentation facultative d'un objet conservé en main pendant une mise à
/// terre. Le world model reste attaché au squelette du joueur ; seule la
/// présentation locale incompatible avec cet état (le viewmodel FPS) change.
/// </summary>
public interface IVBIncapacitatedItemPresentation
{
	void SetHolderIncapacitated( bool incapacitated );
}

/// <summary>
/// Presents the authoritative health state as player control, an in-place
/// replicated ragdoll and local downed/death cameras.
/// </summary>
[Title( "Player Life State Controller" )]
[Category( "Void Breach/Player" )]
[Icon( "personal_injury" )]
public sealed class VBPlayerLifeStateController : Component, PlayerController.IEvents
{
	[Property, Group( "Ragdoll" )]
	public ModelPhysics RagdollPhysics { get; set; }

	[Property, Group( "Camera" ), Range( 0f, 100f ), Step( 1f )]
	public float DownedCameraHeight { get; set; } = 20f;

	public GameObject SpectatedPlayer => _spectatedPlayer;
	public bool CanSpectate => GetSpectateCandidates().Count > 0;
	public string SpectatedPlayerName => GetPlayerName( _spectatedPlayer );

	private PlayerController _controller;
	private VBHealthComponent _health;
	private BaseInventoryComponent _inventory;
	private BaseInventoryItem _incapacitatedItem;
	private IVBIncapacitatedItemPresentation _incapacitatedItemPresentation;
	private GameObject _spectatedPlayer;
	private bool _defaultsCaptured;
	private bool _presentationIncapacitated;
	private bool _defaultUseInputControls;
	private bool _defaultUseAnimatorControls;
	private bool _defaultEnablePressing;
	private bool _defaultRendererUseAnimGraph;
	private bool _defaultColliderObjectEnabled;
	private bool _defaultBodyMotionEnabled;
	private bool _defaultRagdollMotionEnabled;
	private bool _defaultInventoryEnabled;
	private Transform _defaultRendererLocalTransform;

	protected override void OnStart()
	{
		ResolveComponents();
		CaptureDefaults();
	}

	protected override void OnUpdate()
	{
		ResolveComponents();

		if ( !_health.IsValid() || !_controller.IsValid() )
			return;

		UpdatePresentation();
		UpdateSpectateTarget();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !_controller.IsValid() || !IsIncapacitated )
			return;

		_controller.WishVelocity = Vector3.Zero;
		StopMovement();
	}

	protected override void OnDestroy()
	{
		if ( !IsProxy )
			Mouse.Visibility = MouseVisibility.Auto;
	}

	void PlayerController.IEvents.PreInput()
	{
		if ( IsProxy || !_controller.IsValid() || !IsIncapacitated )
			return;

		_controller.UseInputControls = false;
		StopMovement();
	}

	void PlayerController.IEvents.PostCameraSetup( CameraComponent camera )
	{
		if ( IsProxy || !camera.IsValid() || !_health.IsValid() )
			return;

		if ( _health.IsDowned )
		{
			PlaceCameraAtRagdoll( camera );
			return;
		}

		if ( !_health.IsDead )
			return;

		if ( IsValidSpectateTarget( _spectatedPlayer ) )
		{
			var targetController = _spectatedPlayer.Components.Get<PlayerController>();
			camera.WorldTransform = targetController.EyeTransform;
			return;
		}

		PlaceCameraAtRagdoll( camera );
	}

	public void SpectateNext()
	{
		if ( IsProxy || !_health.IsValid() || !_health.IsDead )
			return;

		var candidates = GetSpectateCandidates();
		if ( candidates.Count == 0 )
		{
			_spectatedPlayer = null;
			return;
		}

		var currentIndex = candidates.IndexOf( _spectatedPlayer );
		_spectatedPlayer = candidates[(currentIndex + 1) % candidates.Count];
	}

	public void ReturnToMenu()
	{
		if ( IsProxy )
			return;

		Mouse.Visibility = MouseVisibility.Auto;
		Game.Disconnect();
	}

	private bool IsIncapacitated => _health.IsValid() && (_health.IsDowned || _health.IsDead);

	private void ResolveComponents()
	{
		_controller ??= Components.Get<PlayerController>();
		_health ??= Components.Get<VBHealthComponent>();
		_inventory ??= Components.Get<BaseInventoryComponent>();

		if ( !RagdollPhysics.IsValid() && _controller.IsValid() && _controller.Renderer.IsValid() )
			RagdollPhysics = _controller.Renderer.Components.Get<ModelPhysics>();
	}

	private void CaptureDefaults()
	{
		if ( _defaultsCaptured || !_controller.IsValid() )
			return;

		_defaultUseInputControls = _controller.UseInputControls;
		_defaultUseAnimatorControls = _controller.UseAnimatorControls;
		_defaultEnablePressing = _controller.EnablePressing;
		_defaultRendererUseAnimGraph = !_controller.Renderer.IsValid() || _controller.Renderer.UseAnimGraph;
		if ( _controller.Renderer.IsValid() )
			_defaultRendererLocalTransform = _controller.Renderer.GameObject.LocalTransform;
		_defaultColliderObjectEnabled = !_controller.ColliderObject.IsValid() || _controller.ColliderObject.Enabled;
		_defaultBodyMotionEnabled = !_controller.Body.IsValid() || _controller.Body.MotionEnabled;
		_defaultRagdollMotionEnabled = RagdollPhysics.IsValid() && RagdollPhysics.MotionEnabled;
		_defaultInventoryEnabled = !_inventory.IsValid() || _inventory.Enabled;
		_defaultsCaptured = true;
	}

	private Vector3 FindStandPosition()
	{
		var center = RagdollPhysics.IsValid()
			? RagdollPhysics.MassCenter
			: _controller.Renderer.WorldPosition;
		var trace = Scene.Trace
			.Ray( center + Vector3.Up * 32f, center + Vector3.Down * 128f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		return trace.Hit
			? trace.EndPosition + Vector3.Up * 2f
			: center;
	}

	private void PlacePlayerAfterRevive( Vector3 position )
	{
		WorldPosition = position;
		GameObject.Network.ClearInterpolation();

		if ( _controller.IsValid() )
			StopMovement();
	}

	private void UpdatePresentation()
	{
		CaptureDefaults();

		var incapacitated = IsIncapacitated;
		if ( incapacitated != _presentationIncapacitated )
		{
			if ( incapacitated )
				ApplyIncapacitatedPresentation();
			else
				RestoreAlivePresentation();

			_presentationIncapacitated = incapacitated;
		}

		if ( IsProxy )
			return;

		if ( _health.IsDead )
			Mouse.Visibility = MouseVisibility.Visible;
		else if ( _health.IsDowned )
			Mouse.Visibility = MouseVisibility.Hidden;
	}

	private void ApplyIncapacitatedPresentation()
	{
		CaptureIncapacitatedItem();
		_incapacitatedItemPresentation?.SetHolderIncapacitated( true );
		_controller.UseAnimatorControls = false;

		if ( _controller.Renderer.IsValid() )
		{
			if ( RagdollPhysics.IsValid() )
			{
				RagdollPhysics.MotionEnabled = false;
				RagdollPhysics.Renderer = _controller.Renderer;
				RagdollPhysics.Model = _controller.Renderer.Model;
				RagdollPhysics.IgnoreRoot = true;
				RagdollPhysics.CopyBonesFrom( _controller.Renderer, teleport: true );
			}
			else if ( !IsProxy )
			{
				Log.Warning( $"[VBPlayerLifeStateController] Aucun ModelPhysics configuré pour '{GameObject.Name}'." );
			}

			_controller.Renderer.UseAnimGraph = false;

			if ( RagdollPhysics.IsValid() )
				RagdollPhysics.MotionEnabled = true;
		}

		if ( _controller.Body.IsValid() )
			_controller.Body.MotionEnabled = false;

		if ( _controller.ColliderObject.IsValid() )
			_controller.ColliderObject.Enabled = false;

		if ( IsProxy )
			return;

		Input.ReleaseActions();
		_controller.StopPressing();
		_controller.UseInputControls = false;
		_controller.EnablePressing = false;
		StopMovement();

		if ( _inventory.IsValid() )
			_inventory.Enabled = false;
	}

	private void RestoreAlivePresentation()
	{
		var standPosition = !IsProxy ? FindStandPosition() : WorldPosition;

		if ( RagdollPhysics.IsValid() )
			RagdollPhysics.MotionEnabled = _defaultRagdollMotionEnabled;

		if ( _controller.Renderer.IsValid() )
		{
			_controller.Renderer.GameObject.LocalTransform = _defaultRendererLocalTransform;
			_controller.Renderer.UseAnimGraph = _defaultRendererUseAnimGraph;
		}

		_controller.UseAnimatorControls = _defaultUseAnimatorControls;
		_incapacitatedItemPresentation?.SetHolderIncapacitated( false );
		_incapacitatedItem = null;
		_incapacitatedItemPresentation = null;

		if ( !IsProxy )
			PlacePlayerAfterRevive( standPosition );

		if ( _controller.Body.IsValid() )
			_controller.Body.MotionEnabled = _defaultBodyMotionEnabled;

		if ( _controller.ColliderObject.IsValid() )
			_controller.ColliderObject.Enabled = _defaultColliderObjectEnabled;

		if ( IsProxy )
			return;

		_controller.UseInputControls = _defaultUseInputControls;
		_controller.EnablePressing = _defaultEnablePressing;

		if ( _inventory.IsValid() )
			_inventory.Enabled = _defaultInventoryEnabled;

		_spectatedPlayer = null;
		Mouse.Visibility = MouseVisibility.Auto;
	}

	private void CaptureIncapacitatedItem()
	{
		if ( _incapacitatedItem.IsValid() )
			return;

		var activeItem = _inventory.IsValid() ? _inventory.ActiveItem : null;
		if ( !activeItem.IsValid() )
			return;

		_incapacitatedItem = activeItem;
		_incapacitatedItemPresentation = activeItem as IVBIncapacitatedItemPresentation;
	}

	private void PlaceCameraAtRagdoll( CameraComponent camera )
	{
		var anchor = WorldPosition + Vector3.Up * (_controller.BodyHeight * 0.5f);

		if ( RagdollPhysics.IsValid() && RagdollPhysics.MotionEnabled )
			anchor = RagdollPhysics.MassCenter;

		camera.WorldPosition = anchor + Vector3.Up * DownedCameraHeight;
		camera.WorldRotation = Rotation.From( _controller.EyeAngles );
	}

	private void StopMovement()
	{
		if ( !_controller.IsValid() )
			return;

		_controller.WishVelocity = Vector3.Zero;

		if ( _controller.Body.IsValid() )
			_controller.Body.Velocity = Vector3.Zero;
	}

	private void UpdateSpectateTarget()
	{
		if ( IsProxy || !_health.IsDead || _spectatedPlayer is null )
			return;

		if ( !IsValidSpectateTarget( _spectatedPlayer ) )
			SpectateNext();
	}

	private List<GameObject> GetSpectateCandidates()
	{
		if ( !_health.IsValid() || !_health.IsDead )
			return new List<GameObject>();

		var candidates = Scene
			.GetAll<VBHealthComponent>()
			.Where( candidate => candidate.IsValid() )
			.Where( candidate => candidate.GameObject != GameObject )
			.Where( candidate => !candidate.IsDead && !candidate.IsDowned )
			.Select( candidate => candidate.GameObject )
			.OrderBy( GetPlayerName )
			.ToList();

		var ownTeam = GetTeamId( GameObject );
		if ( ownTeam < 0 )
			return candidates;

		var teammates = candidates
			.Where( candidate => GetTeamId( candidate ) == ownTeam )
			.ToList();

		return teammates.Count > 0 ? teammates : candidates;
	}

	private static bool IsValidSpectateTarget( GameObject candidate )
	{
		if ( !candidate.IsValid() )
			return false;

		var health = candidate.Components.Get<VBHealthComponent>();
		var controller = candidate.Components.Get<PlayerController>();
		return health.IsValid()
			&& controller.IsValid()
			&& !health.IsDowned
			&& !health.IsDead;
	}

	private static int GetTeamId( GameObject player )
	{
		if ( !player.IsValid() )
			return -1;

		foreach ( var component in player.Components.GetAll() )
		{
			if ( component is IVBTeamMember teamMember )
				return teamMember.TeamId;
		}

		return -1;
	}

	private static string GetPlayerName( GameObject player )
	{
		if ( !player.IsValid() )
			return string.Empty;

		return player.Network.Owner?.DisplayName ?? player.Name;
	}
}
