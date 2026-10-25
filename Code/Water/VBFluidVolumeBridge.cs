using System;
using VoidBreach.Ocean;

namespace VoidBreach.Water;

/// <summary>Registers bodies before the native volume's fixed update, including bodies present at load.</summary>
[Title( "VB Fluid Volume Bridge" ), Category( "Void Breach/Water" )]
public sealed class VBFluidVolumeBridge : Component, Component.ITriggerListener
{
	public VBFFTOcean Surface { get; set; }
	public Collider VolumeCollider { get; set; }
	private float _nextReconcile;
	private void Track( Collider collider )
	{
		if ( !Game.IsPlaying || !Surface.IsValid() || !collider.IsValid() || collider.IsTrigger ) return;
		Surface.TrackInteractionCollider( collider );
		var body = collider.Rigidbody;
		if ( !body.IsValid() || !body.MotionEnabled ) return;
		if ( body.GetComponent<PlayerController>().IsValid() )
		{
			var interactor = body.GetComponent<VBWaterInteractor>( true );
			if ( !interactor.IsValid() )
			{
				interactor = body.AddComponent<VBWaterInteractor>();
				interactor.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
			}
			if ( interactor.Enabled ) interactor.RegisterNativeBody( body );
			return; // Native swimming owns movement; the surface's universal tracker owns effects.
		}
		var contact = body.GetComponent<VBFluidBody>( true );
		if ( !contact.IsValid() ) contact = body.AddComponent<VBFluidBody>();
		contact.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
		if ( !contact.Enabled || contact.Body != body ) contact.Register( body );
	}
	public void OnTriggerEnter( Collider other ) => Track( other );
	public void OnTriggerExit( Collider other )
	{
		// Keep the tracked previous bounds until the next update to detect a swept surface exit.
	}
	protected override void OnFixedUpdate()
	{
		if ( !Game.IsPlaying || !VolumeCollider.IsValid() ) return;
		// New contacts are immediate through OnTriggerEnter. Poll only to recover
		// disabled/replaced components on colliders which already overlap the volume.
		if ( Time.Now < _nextReconcile ) return;
		_nextReconcile = Time.Now + 0.25f;
		foreach ( var collider in VolumeCollider.Touching ) Track( collider );
	}
}
