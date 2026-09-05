using System;
using Sandbox;

/// <summary>
/// Replaces the PlayerController's narrow use ray with a configurable sphere
/// sweep while preserving the native IPressable interaction lifecycle.
/// </summary>
[Title( "Wide Interaction Controller" )]
[Category( "Void Breach/Interaction" )]
[Icon( "ads_click" )]
public sealed class VBWideInteractionController : Component
{
	[Property, Group( "Input" ), InputAction]
	public string UseInputAction { get; set; } = "use";

	[Property, Group( "Reach" ), Range( 1f, 512f ), Step( 1f )]
	public float InteractionDistance { get; set; } = 140f;

	[Property, Group( "Reach" ), Range( 0f, 64f ), Step( 1f )]
	public float InteractionRadius { get; set; } = 18f;

	[RequireComponent]
	private PlayerController Controller { get; set; }

	private Component _hovered;
	private Component _pressed;

	protected override void OnStart()
	{
		if ( Controller.IsValid() )
			Controller.EnablePressing = false;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || !Controller.IsValid() )
			return;

		var ray = new Ray( Controller.EyePosition, Controller.EyeAngles.Forward );
		UpdatePressed( ray );

		var target = FindTarget( ray );
		UpdateHovered( target, ray );

		if ( !string.IsNullOrWhiteSpace( UseInputAction )
			&& Input.Pressed( UseInputAction ) )
		{
			StartPressing( target, ray );
		}
	}

	protected override void OnDisabled()
	{
		StopPressing( CurrentRay );
		UpdateHovered( null, CurrentRay );
	}

	private Ray CurrentRay => Controller.IsValid()
		? new Ray( Controller.EyePosition, Controller.EyeAngles.Forward )
		: new Ray( WorldPosition, WorldRotation.Forward );

	private Component FindTarget( Ray ray )
	{
		var endPosition = ray.Project( MathF.Max( InteractionDistance, 1f ) );
		var trace = Scene.Trace
			.Sphere( MathF.Max( InteractionRadius, 0f ), ray.Position, endPosition )
			.IgnoreGameObjectHierarchy( GameObject )
			.HitTriggers();

		foreach ( var result in trace.RunAll() )
		{
			var hitObject = result.Collider?.GameObject ?? result.GameObject;
			if ( !hitObject.IsValid() )
				continue;

			var interactionEvent = CreateEvent( ray );
			foreach ( var pressable in hitObject.GetComponentsInParent<Component.IPressable>(
				includeDisabled: false,
				includeSelf: true
			) )
			{
				if ( pressable is Component component
					&& component.IsValid()
					&& pressable.CanPress( interactionEvent ) )
				{
					return component;
				}
			}

			if ( result.Collider.IsValid() && !result.Collider.IsTrigger )
				break;
		}

		return null;
	}

	private void UpdateHovered( Component target, Ray ray )
	{
		var interactionEvent = CreateEvent( ray );

		if ( target == _hovered )
		{
			if ( _hovered is Component.IPressable currentPressable )
				currentPressable.Look( interactionEvent );

			return;
		}

		if ( _hovered is Component.IPressable previousPressable )
			previousPressable.Blur( interactionEvent );

		_hovered = target;

		if ( _hovered is Component.IPressable nextPressable )
		{
			nextPressable.Hover( interactionEvent );
			nextPressable.Look( interactionEvent );
		}
	}

	private void StartPressing( Component target, Ray ray )
	{
		StopPressing( ray );

		if ( target is not Component.IPressable pressable )
			return;

		var interactionEvent = CreateEvent( ray );
		if ( !pressable.CanPress( interactionEvent )
			|| !pressable.Press( interactionEvent ) )
			return;

		_pressed = target;
	}

	private void UpdatePressed( Ray ray )
	{
		if ( !_pressed.IsValid() || _pressed is not Component.IPressable pressable )
		{
			_pressed = null;
			return;
		}

		if ( string.IsNullOrWhiteSpace( UseInputAction )
			|| !Input.Down( UseInputAction )
			|| !IsWithinReach( _pressed )
			|| !pressable.Pressing( CreateEvent( ray ) ) )
		{
			StopPressing( ray );
		}
	}

	private bool IsWithinReach( Component target )
	{
		if ( !target.IsValid() || !Controller.IsValid() )
			return false;

		var bounds = target.GameObject.GetBounds();
		var nearestPoint = bounds.Size.LengthSquared > 0.001f
			? bounds.ClosestPoint( Controller.EyePosition )
			: target.WorldPosition;

		return Vector3.DistanceBetween( Controller.EyePosition, nearestPoint )
			<= InteractionDistance + InteractionRadius;
	}

	private void StopPressing( Ray ray )
	{
		if ( _pressed is Component.IPressable pressable )
			pressable.Release( CreateEvent( ray ) );

		_pressed = null;
	}

	private Component.IPressable.Event CreateEvent( Ray ray )
	{
		return new Component.IPressable.Event( Controller, ray );
	}
}
