using System;
using System.Collections.Generic;
using VoidBreach.Water;

namespace VoidBreach.Ocean;

public sealed partial class VBFFTOcean
{
	private sealed class Contact
	{
		public PhysicsBody Body;
		public Collider Collider;
		public ModelRenderer Renderer;
		public GameObject Object;
		public VBFluidInteractionProfile Profile;
		public VBWaterInteractor Legacy;
		public Component[] ContactSources;
		public readonly List<BBox> VirtualContacts = new( 8 );
		public BBox Previous;
		public float Seen, LastEmission;
		public bool Initialized, Touching;
		public Vector3 Trail;
	}
	private readonly Dictionary<PhysicsBody, Contact> _bodyContacts = new();
	private readonly Dictionary<Collider, Contact> _colliderContacts = new();
	private readonly List<Collider> _removeColliders = new();
	private readonly Dictionary<ModelRenderer, Contact> _renderContacts = new();
	private readonly List<PhysicsBody> _removeBodies = new();
	private readonly List<ModelRenderer> _removeRenderers = new();
	private readonly List<Contact> _activeContacts = new();
	private readonly List<VBFFTOcean> _interactionSurfaces = new();
	private readonly HashSet<GameObject> _physicalObjects = new();
	private readonly Dictionary<(int, int), float> _contactCells = new();
	private readonly Queue<Guid> _receivedEvents = new();
	private readonly HashSet<Guid> _receivedEventSet = new();
	private readonly List<Vector4> _networkStamps = new( 192 );
	private float _nextNetworkFlush;
	private float _nextDiscovery, _nextRenderDiscovery;
	public int TrackedInteractionCount => _bodyContacts.Count + _colliderContacts.Count + _renderContacts.Count;
	public int AutomaticImpactCount { get; private set; }
	public int DroppedInteractionCount { get; private set; }
	private bool NetworkInteractions => ReplicateInteractions && Networking.IsActive && Network.Active;
	private Contact NewContact( GameObject go ) => new()
	{
		Object = go, Profile = go.GetComponentInParent<VBFluidInteractionProfile>(),
		Legacy = go.GetComponentInParent<VBWaterInteractor>(), Seen = Time.Now,
		ContactSources = go.GetComponentsInChildren<Component>( includeSelf: true ).Where( x => x is IFluidContactBounds ).Take( 8 ).ToArray()
	};
	/// <summary>Broadphase trigger notifications seed tracking immediately; there is no per-entity effect component.</summary>
	public void TrackInteractionCollider( Collider collider )
	{
		if ( !Interactions || !Game.IsPlaying || !collider.IsValid() || collider.IsTrigger ) return;
		var rigidbody = collider.Rigidbody;
		if ( rigidbody.IsValid() ) TrackInteractionBody( rigidbody.PhysicsBody );
		else
		{
			if ( _colliderContacts.TryGetValue( collider, out var existing ) ) { existing.Seen = Time.Now; return; }
			if ( TrackedInteractionCount >= Math.Clamp( InteractionTrackingBudget, 16, 512 ) ) return;
			var contact = NewContact( collider.GameObject ); contact.Collider = collider; _colliderContacts.Add( collider, contact );
		}
	}
	private void TrackInteractionBody( PhysicsBody body )
	{
		if ( body is null || !body.IsValid || !body.GameObject.IsValid() || !body.Enabled ) return;
		if ( body.Component is Collider c && (c.IsTrigger || !c.Active) ) return;
		if ( body.GameObject.Tags.Has( "water" ) || body.GameObject.Tags.Has( "water_surface" ) ) return;
		// Deep submerged/departed bodies must not occupy the contact budget forever.
		var bounds = body.GetBounds();
		if ( _domain.Sample( DomainTransform.PointToLocal( bounds.Center ), out var baseHeight, out _, out _ ) )
		{
			var surface = WorldPosition.z + baseHeight; var padding = Math.Clamp( WaveHeight, 0, 5 ) * FftOceanSpectrum.UnitsPerMeter * 2 + 128;
			if ( bounds.Maxs.z < surface - padding || bounds.Mins.z > surface + padding + 512 ) return;
		}
		if ( _bodyContacts.TryGetValue( body, out var existing ) ) { existing.Seen = Time.Now; return; }
		if ( TrackedInteractionCount >= Math.Clamp( InteractionTrackingBudget, 16, 512 ) ) { DroppedInteractionCount++; return; }
		var contact = NewContact( body.GameObject ); contact.Body = body; _bodyContacts.Add( body, contact );
	}
	private void DetectInteractions( float delta, Vector3 center )
	{
		if ( NetworkInteractions && !Networking.IsHost ) return;
		var range = Math.Clamp( InteractionDistance, 256, 16000 );
		if ( !NetworkInteractions && Scene.Camera.IsValid() && (Scene.Camera.WorldPosition - center).LengthSquared > range * range ) return;
		if ( Time.Now >= _nextDiscovery )
		{
			_nextDiscovery = Time.Now + 0.1f;
			var extents = new Vector3( range, range, MathF.Max( 512, WaveHeight * FftOceanSpectrum.UnitsPerMeter * 2 + 128 ) );
			// Broadphase BODY overlap, not a giant narrowphase box trace against every map triangle.
			// Limiting RunAll() after it returns does not bound the native trace's work or allocations.
			var examined = 0;
			foreach ( var candidate in Scene.FindInPhysics( new BBox( center - extents, center + extents ) ) )
			{
				if ( !candidate.IsValid() || candidate.Tags.Has( "water" ) || candidate.Tags.Has( "water_surface" ) ) continue;
				foreach ( var body in candidate.GetComponents<Rigidbody>() ) if ( body.Active ) TrackInteractionBody( body.PhysicsBody );
				foreach ( var collider in candidate.GetComponents<Collider>() ) if ( collider.Active ) TrackInteractionCollider( collider );
				foreach ( var model in candidate.GetComponents<ModelPhysics>() )
					foreach ( var limb in model.Bodies ) if ( limb.Component.IsValid() ) TrackInteractionBody( limb.Component.PhysicsBody );
				if ( ++examined >= Math.Clamp( InteractionTrackingBudget, 16, 512 ) * 2 ) break;
			}
		}
		_physicalObjects.Clear();
		foreach ( var pair in _bodyContacts ) if ( pair.Key.IsValid ) _physicalObjects.Add( pair.Value.Object );
		foreach ( var pair in _colliderContacts ) if ( pair.Key.IsValid() ) _physicalObjects.Add( pair.Value.Object );
		if ( Time.Now >= _nextRenderDiscovery )
		{
			_nextRenderDiscovery = Time.Now + 1;
			// Fallback for visible scripted objects without ANY collider. Never a scene traversal per frame.
			foreach ( var renderer in Scene.GetAll<ModelRenderer>() )
			{
				if ( !renderer.Active || _physicalObjects.Contains( renderer.GameObject ) || renderer.GameObject.Tags.Has( "water_surface" ) ) continue;
				if ( renderer.GetComponentInParent<Rigidbody>().IsValid() || renderer.GetComponentInParent<ModelPhysics>().IsValid()
					|| renderer.GetComponentInParent<Collider>().IsValid() || renderer.GetComponentsInChildren<Collider>().Any( x => x.Active && !x.IsTrigger ) ) continue;
				if ( (renderer.Bounds.Center - center).LengthSquared > range * range ) continue;
				if ( _renderContacts.TryGetValue( renderer, out var contact ) ) { contact.Seen = Time.Now; continue; }
				if ( TrackedInteractionCount >= Math.Clamp( InteractionTrackingBudget, 16, 512 ) ) break;
				contact = NewContact( renderer.GameObject ); contact.Renderer = renderer; _renderContacts.Add( renderer, contact );
			}
		}
		_activeContacts.Clear(); _removeBodies.Clear(); _removeRenderers.Clear(); _removeColliders.Clear();
		foreach ( var pair in _bodyContacts )
		{
			if ( !pair.Key.IsValid || !pair.Key.Enabled || !pair.Value.Object.IsValid() || !pair.Value.Object.Enabled || Time.Now - pair.Value.Seen > 2 ) _removeBodies.Add( pair.Key );
			else _activeContacts.Add( pair.Value );
		}
		foreach ( var pair in _renderContacts )
		{
			if ( !pair.Key.IsValid() || !pair.Key.Active || _physicalObjects.Contains( pair.Value.Object ) || Time.Now - pair.Value.Seen > 2 ) _removeRenderers.Add( pair.Key );
			else _activeContacts.Add( pair.Value );
		}
		foreach ( var pair in _colliderContacts )
		{
			if ( !pair.Key.IsValid() || !pair.Key.Active || pair.Key.Rigidbody.IsValid() || Time.Now - pair.Value.Seen > 2 ) _removeColliders.Add( pair.Key );
			else _activeContacts.Add( pair.Value );
		}
		foreach ( var collider in _removeColliders ) _colliderContacts.Remove( collider );
		foreach ( var body in _removeBodies ) _bodyContacts.Remove( body );
		foreach ( var renderer in _removeRenderers ) _renderContacts.Remove( renderer );
		_contactCells.Clear();
		_interactionSurfaces.Clear();
		Scene.GetAll( _interactionSurfaces );
		foreach ( var contact in _activeContacts ) UpdateContact( contact, MathF.Max( delta, 0.001f ) );
	}

	private void UpdateContact( Contact c, float delta )
	{
		var bounds = c.Body is not null ? c.Body.GetBounds() : c.Collider.IsValid() ? c.Collider.GetWorldBounds() : c.Renderer.Bounds;
		var p = bounds.Center; var size = bounds.Size;
		if ( !float.IsFinite( p.x + p.y + p.z + size.x + size.y + size.z ) ) return;
		var movement = c.Initialized ? (p - c.Previous.Center) / delta : c.Body?.Velocity ?? Vector3.Zero;
		if ( !c.Initialized ) c.Trail = p;
		var wasInitialized = c.Initialized;
		var previous = c.Initialized ? c.Previous : new BBox( bounds.Mins - movement * delta, bounds.Maxs - movement * delta );
		c.Previous = bounds; c.Initialized = true;
		if ( c.Profile.IsValid() && c.Profile.Active && c.Profile.SuppressInteractions ) { c.Touching = false; return; }
		// Teleports must not paint a trail through the whole map.
		if ( movement.Length * delta > 1024 ) { c.Touching = false; c.Trail = p; return; }
		// Keep observing transform-driven objects, but do not evaluate the Fourier
		// surface at every footprint of every stationary map collider, every tick.
		// The first observation still initializes its actual contact state.
		var minimumSpeed = Math.Clamp( InteractionMinimumSpeed, 1, 100 );
		if ( wasInitialized && movement.LengthSquared < minimumSpeed * minimumSpeed ) { c.Trail = p; return; }
		c.VirtualContacts.Clear();
		foreach ( var component in c.ContactSources )
		{
			if ( component.IsValid() && component.Active && component is IFluidContactBounds source && source.TryGetFluidContactBounds( out var virtualBounds ) )
				c.VirtualContacts.Add( virtualBounds );
		}
		var fallbackMass = Math.Clamp( size.x * size.y * size.z * 0.0005f, 2, 1000 );
		var mass = c.Body is not null && c.Body.MotionEnabled ? c.Body.Mass : fallbackMass;
		if ( c.Profile.IsValid() && c.Profile.Active && c.Profile.MassOverride > 0 ) mass = c.Profile.MassOverride;
		var radiusScale = c.Profile.IsValid() && c.Profile.Active ? Math.Clamp( c.Profile.RadiusScale, 0.25f, 4 ) : 1;
		var strengthScale = c.Profile.IsValid() && c.Profile.Active ? Math.Clamp( c.Profile.StrengthScale, 0, 4 ) : 1;
		var radius = Math.Clamp( MathF.Min( size.x, size.y ) * 0.4f * radiusScale, 8, Math.Clamp( InteractionMaximumRadius, 8, 256 ) );
		// Sample across a large footprint rather than forcing at a vehicle's centre alone.
		var bodyFootprints = Math.Clamp( (int)MathF.Ceiling( MathF.Max( size.x, size.y ) / MathF.Max( radius * 2, 32 ) ), 1, 4 );
		var footprints = bodyFootprints + c.VirtualContacts.Count;
		var touching = false; var emitted = false; var emittedKind = FluidImpactKind.Wake;
		for ( var point = 0; point < footprints; point++ )
		{
			var offset = bodyFootprints == 1 ? 0 : (point / (float)(bodyFootprints - 1) - 0.5f) * 0.7f;
			var contactBounds = point < bodyFootprints ? bounds : c.VirtualContacts[point - bodyFootprints];
			var contactRadius = point < bodyFootprints ? radius : Math.Clamp( MathF.Min( contactBounds.Size.x, contactBounds.Size.y ) * 0.5f, 8, Math.Clamp( InteractionMaximumRadius, 8, 256 ) );
			var probe = point < bodyFootprints ? p + (size.x > size.y ? new Vector3( size.x * offset, 0, 0 ) : new Vector3( 0, size.y * offset, 0 )) : contactBounds.Center;
			if ( !TrySample( probe, out var sample ) || contactBounds.Maxs.z < sample.Bottom ) continue;
			// Resolve only liquid volumes physically overlapping the object's vertical span.
			var superseded = false;
			foreach ( var other in _interactionSurfaces )
			{
				if ( other == this || !other.Active || !other.Interactions || other.Priority < Priority ) continue;
				if ( !other.TrySample( probe, out var higher ) || higher.Height < contactBounds.Mins.z - 4 || higher.Bottom > contactBounds.Maxs.z ) continue;
				if ( other.Priority > Priority || higher.Height > sample.Height ) { superseded = true; break; }
			}
			if ( superseded ) continue;
			var previousBottom = contactBounds.Mins.z + previous.Center.z - p.z;
			var previousTop = contactBounds.Maxs.z + previous.Center.z - p.z;
			var crosses = FluidContactMath.Crossed( previousBottom, previousTop, contactBounds.Mins.z, contactBounds.Maxs.z, sample.Height );
			var near = contactBounds.Mins.z <= sample.Height + 3 && contactBounds.Maxs.z >= sample.Height - 8;
			touching |= near;
			if ( !near && !crosses ) continue;
			var impact = !c.Touching || crosses;
			var kind = impact ? (movement.z > 0 ? FluidImpactKind.Exit : FluidImpactKind.Entry) : FluidImpactKind.Wake;
			var flow = sample.Velocity.WithZ( 0 );
			var relative = movement - flow;
			// No perpetual ripples from static scenery, current or Fourier query noise alone.
			if ( movement.LengthSquared < minimumSpeed * minimumSpeed ) continue;
			if ( !impact && Time.Now - c.LastEmission < 1f / Math.Clamp( InteractionFrequency, 15, 60 ) ) continue;
			var immersion = Math.Clamp( (sample.Height - contactBounds.Mins.z) / MathF.Max( contactBounds.Size.z, 1 ), 0, 1 );
			var strength = FluidContactMath.Strength( relative.WithZ( 0 ).Length, relative.z, mass, immersion, impact, InteractionVelocitySensitivity ) * strengthScale;
			if ( strength < 0.1f ) continue;
			var end = probe.WithZ( sample.Height );
			var start = impact ? end : c.Trail + (probe - p);
			var samples = impact ? 1 : FluidContactMath.TrailSamples( (end - start).WithZ( 0 ).Length, contactRadius );
			for ( var trail = 1; trail <= samples; trail++ )
			{
				var position = Vector3.Lerp( start, end, trail / (float)samples );
				if ( !TrySample( position, out var at ) ) continue;
				position.z = at.Height;
				var key = ((int)MathF.Floor( position.x / 16 ), (int)MathF.Floor( position.y / 16 ));
				if ( _contactCells.TryGetValue( key, out var previousStrength ) && previousStrength >= strength ) continue;
				_contactCells[key] = strength;
				if ( EmitAutomaticImpact( position, contactRadius, strength / samples / MathF.Sqrt( footprints ), kind, relative ) ) { emitted = true; emittedKind = kind; }
			}
		}
		if ( emitted ) { c.LastEmission = Time.Now; c.Trail = p; if ( c.Legacy.IsValid() ) c.Legacy.RecordUniversalImpact( emittedKind ); }
		if ( !touching ) c.Trail = p;
		c.Touching = touching;
	}

	private bool EmitAutomaticImpact( Vector3 position, float radius, float strength, FluidImpactKind kind, Vector3 velocity )
	{
		if ( NetworkInteractions ) return EmitReplicatedSurfaceImpact( position, radius, strength, kind, velocity );
		var accepted = EmitSurfaceImpact( position, radius, strength, kind, velocity );
		if ( accepted ) AutomaticImpactCount++;
		return accepted;
	}
	/// <summary>Host-only explicit gameplay API. Ordinary local effects can keep using EmitSurfaceImpact.</summary>
	public bool EmitReplicatedSurfaceImpact( Vector3 position, float radius, float strength, FluidImpactKind kind = FluidImpactKind.Custom, Vector3 velocity = default )
	{
		if ( NetworkInteractions && !Networking.IsHost ) return false;
		if ( !NetworkInteractions ) return EmitSurfaceImpact( position, radius, strength, kind, velocity );
		if ( !Interactions || !Active || _networkStamps.Count >= 192 || !ReserveImpactBudget()
			|| !float.IsFinite( position.x + position.y + position.z + radius + strength + velocity.x + velocity.y + velocity.z ) ) return false;
		EnsureDomain();
		if ( !_domain.Sample( DomainTransform.PointToLocal( position ), out _, out _, out _ ) ) return false;
		// Never depend on the round trip of our own presentation RPC for host rendering.
		if ( !EmitSurfaceImpact( position, radius, strength, kind, velocity ) ) return false;
		AutomaticImpactCount++;
		_networkStamps.Add( new Vector4( position.x, position.y, position.z, Math.Clamp( radius, 8, Math.Clamp( InteractionMaximumRadius, 8, 256 ) ) ) );
		_networkStamps.Add( new Vector4( Math.Clamp( strength, -160, 160 ), (float)kind, velocity.x, velocity.y ) );
		_networkStamps.Add( new Vector4( velocity.z, 0, 0, 0 ) );
		return true;
	}
	private void FlushNetworkInteractions()
	{
		if ( _networkStamps.Count == 0 || Time.Now < _nextNetworkFlush ) return;
		_nextNetworkFlush = Time.Now + 1f / Math.Clamp( InteractionFrequency, 15, 60 );
		// One bounded event batch per water/tick, rather than an RPC per footprint sample.
		BroadcastInteractions( Guid.NewGuid(), _networkStamps.ToArray() ); _networkStamps.Clear();
	}
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void BroadcastInteractions( Guid id, Vector4[] stamps )
	{
		if ( Networking.IsHost ) return; // Already applied on the sending host.
		if ( stamps is null || stamps.Length > 192 || stamps.Length % 3 != 0 ) return;
		if ( !_receivedEventSet.Add( id ) ) return;
		_receivedEvents.Enqueue( id );
		while ( _receivedEvents.Count > 128 ) _receivedEventSet.Remove( _receivedEvents.Dequeue() );
		for ( var i = 0; i < stamps.Length; i += 3 )
		{
			var p = stamps[i]; var v = stamps[i + 1];
			var kind = (FluidImpactKind)Math.Clamp( (int)v.y, 0, (int)FluidImpactKind.Explosion );
			if ( EmitSurfaceImpact( new Vector3( p.x, p.y, p.z ), p.w, v.x, kind, new Vector3( v.z, v.w, stamps[i + 2].x ) ) ) AutomaticImpactCount++;
		}
	}
	private float _sendBudgetReset;
	private int _sendBudget;
	private bool ReserveImpactBudget()
	{
		if ( Time.Now >= _sendBudgetReset ) { _sendBudgetReset = Time.Now + 1f / Math.Clamp( InteractionFrequency, 15, 60 ); _sendBudget = 0; }
		return _sendBudget++ < Math.Clamp( InteractionBudget, 1, 64 );
	}
	private void ClearTrackedInteractions()
	{
		_bodyContacts.Clear(); _colliderContacts.Clear(); _removeColliders.Clear(); _renderContacts.Clear(); _activeContacts.Clear(); _interactionSurfaces.Clear(); _contactCells.Clear();
		_physicalObjects.Clear(); _removeBodies.Clear(); _removeRenderers.Clear();
		_receivedEvents.Clear(); _receivedEventSet.Clear();
		_networkStamps.Clear();
	}
	protected override void DrawGizmos()
	{
		if ( !DebugInteractions ) return;
		Gizmo.Draw.Color = Color.Cyan;
		foreach ( var field in new[] { _field, _coarseField } )
		{
			var origin = new Vector3( field.Origin.x, field.Origin.y, WorldPosition.z );
			Gizmo.Draw.LineBBox( new BBox( origin, origin + new Vector3( field.Extent, field.Extent, 2 ) ) );
		}
		foreach ( var contact in _activeContacts )
		{
			Gizmo.Draw.Color = contact.Touching ? Color.Green : Color.Yellow;
			Gizmo.Draw.LineBBox( contact.Previous ); Gizmo.Draw.LineSphere( contact.Trail, 8 );
		}
		Gizmo.Draw.WorldText( $"Contacts {TrackedInteractionCount} / impacts {AutomaticImpactCount} / GPU steps {InteractionGpuDispatches} / dropped {DroppedInteractionCount}", new Transform( WorldPosition + Vector3.Up * 64 ) );
	}
}
