using System;
using System.Collections.Generic;

namespace VoidBreach.Water;

/// <summary>
/// Builds a calm, reactive water surface and configures the native WaterVolume
/// for buoyancy, drag and fluid density. Place this component at the waterline.
/// </summary>
[Title( "Stagnant Water" )]
[Category( "Void Breach/Water" )]
[Icon( "water" )]
public sealed class StagnantWater : Component, Component.ExecuteInEditor, Component.ITriggerListener
{
	private const string ShaderPath = "stagnant_water/shaders/stagnant_water.shader";
	private const string DefaultReferenceTexturePath = "stagnant_water/textures/stagnant_water_reference.png";
	private const int MaxRipples = 8;

	[RequireComponent]
	public BoxCollider VolumeCollider { get; set; }

	[RequireComponent]
	public WaterVolume PhysicsVolume { get; set; }

	[Property, Group( "Geometry" )]
	public Vector2 SurfaceSize { get; set; } = new( 1200f, 1200f );

	[Property, Group( "Geometry" ), Range( 16f, 2048f )]
	public float Depth { get; set; } = 180f;

	[Property, Group( "Geometry" ), Range( 2, 128 )]
	public int Resolution { get; set; } = 48;

	[Property, Group( "Rendering" ), Description( "Optional material override. Leave empty to use the included stagnant-water material made from the reference image." )]
	public Material MaterialOverride { get; set; }

	[Property, Group( "Rendering" )]
	public Color ShallowColor { get; set; } = new( 0.20f, 0.24f, 0.22f, 1f );

	[Property, Group( "Rendering" )]
	public Color DeepColor { get; set; } = new( 0.035f, 0.07f, 0.065f, 1f );

	[Property, Group( "Rendering" )]
	public Color ReflectionColor { get; set; } = new( 0.64f, 0.72f, 0.78f, 1f );

	[Property, Group( "Rendering" ), Range( 0.01f, 1f )]
	public float Roughness { get; set; } = 0.105f;

	[Property, Group( "Rendering" ), Range( 0.05f, 1f )]
	public float Opacity { get; set; } = 0.68f;

	[Property, Group( "Reference Image" ), Range( 0f, 1f )]
	public float ReferenceInfluence { get; set; } = 0.42f;

	[Property, Group( "Reference Image" ), Range( 0.0001f, 0.02f )]
	public float ReferenceScale { get; set; } = 0.00115f;

	[Property, Group( "Reference Image" ), Range( 0f, 4f )]
	public float TextureNormalStrength { get; set; } = 0.75f;

	[Property, Group( "Surface Motion" ), Range( 0f, 12f )]
	public float SurfaceAmplitude { get; set; } = 1.65f;

	[Property, Group( "Surface Motion" ), Range( 16f, 1024f )]
	public float SurfaceScale { get; set; } = 210f;

	[Property, Group( "Surface Motion" ), Range( 0f, 4f )]
	public float SurfaceSpeed { get; set; } = 0.42f;

	[Property, Group( "Interaction" ), Range( 0f, 4f )]
	public float RippleStrength { get; set; } = 1f;

	[Property, Group( "Interaction" ), Range( 0.2f, 6f )]
	public float RippleLifetime { get; set; } = 2.8f;

	[Property, Group( "Interaction" ), Range( 16f, 512f )]
	public float RippleSpeed { get; set; } = 105f;

	[Property, Group( "Interaction" ), Range( 0.05f, 1f )]
	public float RippleInterval { get; set; } = 0.16f;

	[Property, Group( "Interaction" ), Range( 0f, 512f )]
	public float MinimumRippleSpeed { get; set; } = 14f;

	[Property, Group( "Physics" ), Range( 0f, 15000f )]
	public float FluidDensity { get; set; } = 1000f;

	[Property, Group( "Physics" ), Range( 0f, 1f )]
	public float LinearDrag { get; set; } = 0.18f;

	[Property, Group( "Physics" ), Range( 0f, 2f )]
	public float AngularDrag { get; set; } = 0.55f;

	private readonly Ripple[] _ripples = new Ripple[MaxRipples];
	private readonly Dictionary<Collider, TrackedCollider> _trackedColliders = new();
	private readonly List<Collider> _staleColliders = new();
	private ModelRenderer _renderer;
	private Model _generatedModel;
	private Material _defaultMaterial;
	private Texture _referenceTexture;
	private Vector2 _builtSize;
	private int _builtResolution;
	private float _builtSurfaceAmplitude;
	private int _nextRipple;

	private readonly record struct Ripple( Vector3 Position, float Radius, float BornTime );

	private sealed class TrackedCollider
	{
		public Vector3 PreviousPosition { get; set; }
		public float LastRippleTime { get; set; }
	}

	protected override void OnEnabled()
	{
		EnsureComponents();
		EnsureSurfaceMesh();
		ApplySettings();
	}

	protected override void OnUpdate()
	{
		EnsureComponents();
		EnsureSurfaceMesh();
		ApplySettings();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Game.IsPlaying )
		{
			return;
		}

		UpdateTrackedColliders();
	}

	protected override void OnValidate()
	{
		SurfaceSize = new Vector2(
			MathF.Max( SurfaceSize.x, 16f ),
			MathF.Max( SurfaceSize.y, 16f )
		);
		Depth = Depth.Clamp( 16f, 2048f );
		Resolution = Resolution.Clamp( 2, 128 );
		SurfaceScale = SurfaceScale.Clamp( 16f, 1024f );
		ReferenceScale = ReferenceScale.Clamp( 0.0001f, 0.02f );
		RippleLifetime = RippleLifetime.Clamp( 0.2f, 6f );
		RippleSpeed = RippleSpeed.Clamp( 16f, 512f );
		RippleInterval = RippleInterval.Clamp( 0.05f, 1f );
	}

	protected override void OnDisabled()
	{
		_trackedColliders.Clear();
	}

	protected override void OnDestroy()
	{
		if ( _renderer.IsValid() && _renderer.Model == _generatedModel )
		{
			_renderer.Model = null;
		}

		_generatedModel = null;
		_trackedColliders.Clear();
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !Game.IsPlaying || !IsExternalCollider( other ) )
		{
			return;
		}

		var position = other.GetWorldBounds().Center;
		_trackedColliders[other] = new TrackedCollider
		{
			PreviousPosition = position,
			LastRippleTime = Time.Now
		};

		var speed = other.GetVelocityAtPoint( position ).Length;
		AddRipple( ProjectToSurface( position ), GetRippleRadius( speed, 34f ) );
	}

	public void OnTriggerExit( Collider other )
	{
		if ( !Game.IsPlaying || !other.IsValid() )
		{
			return;
		}

		if ( _trackedColliders.Remove( other ) )
		{
			var position = other.GetWorldBounds().Center;
			var speed = other.GetVelocityAtPoint( position ).Length;
			AddRipple( ProjectToSurface( position ), GetRippleRadius( speed, 28f ) );
		}
	}

	/// <summary>Adds a circular visual disturbance at a world-space position.</summary>
	public void AddRipple( Vector3 worldPosition, float radius )
	{
		_ripples[_nextRipple] = new Ripple(
			ProjectToSurface( worldPosition ),
			radius.Clamp( 8f, 256f ),
			Time.Now
		);

		_nextRipple = (_nextRipple + 1) % MaxRipples;
	}

	private void EnsureComponents()
	{
		VolumeCollider ??= GetOrAddComponent<BoxCollider>();
		PhysicsVolume ??= GetOrAddComponent<WaterVolume>();
		_renderer ??= GetOrAddComponent<ModelRenderer>();
		if ( !Tags.Has( "water" ) ) Tags.Add( "water" );

		var desiredScale = new Vector3( SurfaceSize.x, SurfaceSize.y, Depth );
		var desiredCenter = new Vector3( 0f, 0f, -Depth * 0.5f );

		if ( !VolumeCollider.IsTrigger ) VolumeCollider.IsTrigger = true;
		if ( !VolumeCollider.Static ) VolumeCollider.Static = true;
		if ( VolumeCollider.Scale != desiredScale ) VolumeCollider.Scale = desiredScale;
		if ( VolumeCollider.Center != desiredCenter ) VolumeCollider.Center = desiredCenter;

		var waterSurface = Surface.FindByName( "water" );
		if ( waterSurface.IsValid() && VolumeCollider.Surface != waterSurface )
		{
			VolumeCollider.Surface = waterSurface;
		}

		PhysicsVolume.FluidDensity = FluidDensity;
		PhysicsVolume.LinearDrag = LinearDrag;
		PhysicsVolume.AngularDrag = AngularDrag;
		PhysicsVolume.FluidVelocity = Vector3.Zero;
		PhysicsVolume.SurfaceOffset = 0f;
		PhysicsVolume.WaveAmplitude = SurfaceAmplitude;
		PhysicsVolume.WaveFrequency = SurfaceSpeed;
	}

	private void EnsureSurfaceMesh()
	{
		if ( !_renderer.IsValid() )
		{
			return;
		}

		if ( !_generatedModel.IsValid()
			|| _builtSize != SurfaceSize
			|| _builtResolution != Resolution
			|| _builtSurfaceAmplitude != SurfaceAmplitude )
		{
			_generatedModel = BuildSurfaceModel();
			_builtSize = SurfaceSize;
			_builtResolution = Resolution;
			_builtSurfaceAmplitude = SurfaceAmplitude;
			_renderer.Model = _generatedModel;
		}

		_defaultMaterial = _defaultMaterial.IsValid() ? _defaultMaterial : Material.FromShader( ShaderPath );
		_renderer.MaterialOverride = MaterialOverride.IsValid() ? MaterialOverride : _defaultMaterial;
	}

	private Model BuildSurfaceModel()
	{
		_defaultMaterial = _defaultMaterial.IsValid() ? _defaultMaterial : Material.FromShader( ShaderPath );

		var verticesPerSide = Resolution + 1;
		var vertices = new Vertex[verticesPerSide * verticesPerSide];
		var indices = new int[Resolution * Resolution * 6];
		var halfSize = SurfaceSize * 0.5f;

		for ( var y = 0; y < verticesPerSide; ++y )
		{
			var v = y / (float)Resolution;

			for ( var x = 0; x < verticesPerSide; ++x )
			{
				var u = x / (float)Resolution;
				var position = new Vector3(
					u * SurfaceSize.x - halfSize.x,
					v * SurfaceSize.y - halfSize.y,
					0f
				);

				vertices[y * verticesPerSide + x] = new Vertex(
					position,
					Vector3.Up,
					Vector3.Forward,
					new Vector4( u, v, 0f, 0f )
				);
			}
		}

		var index = 0;
		for ( var y = 0; y < Resolution; ++y )
		{
			for ( var x = 0; x < Resolution; ++x )
			{
				var bottomLeft = y * verticesPerSide + x;
				var bottomRight = bottomLeft + 1;
				var topLeft = bottomLeft + verticesPerSide;
				var topRight = topLeft + 1;

				indices[index++] = bottomLeft;
				indices[index++] = bottomRight;
				indices[index++] = topRight;
				indices[index++] = topRight;
				indices[index++] = topLeft;
				indices[index++] = bottomLeft;
			}
		}

		var mesh = new Mesh( _defaultMaterial );
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = BBox.FromPositionAndSize(
			Vector3.Zero,
			new Vector3( SurfaceSize.x, SurfaceSize.y, SurfaceAmplitude * 6f + 32f )
		);

		return Model.Builder
			.WithName( "void_breach_stagnant_water" )
			.AddMesh( mesh )
			.Create();
	}

	private void ApplySettings()
	{
		if ( !_renderer.IsValid() )
		{
			return;
		}

		if ( !_referenceTexture.IsValid() )
		{
			_referenceTexture = Texture.Load( DefaultReferenceTexturePath );
		}
		var attributes = _renderer.Attributes;

		if ( _referenceTexture.IsValid() )
		{
			attributes.Set( "StagnantReferenceTexture", _referenceTexture );
		}

		attributes.Set( "StagnantShallowColor", ShallowColor );
		attributes.Set( "StagnantDeepColor", DeepColor );
		attributes.Set( "StagnantReflectionColor", ReflectionColor );
		attributes.Set( "StagnantRoughness", Roughness );
		attributes.Set( "StagnantOpacity", Opacity );
		attributes.Set( "StagnantReferenceInfluence", ReferenceInfluence );
		attributes.Set( "StagnantReferenceScale", ReferenceScale );
		attributes.Set( "StagnantTextureNormalStrength", TextureNormalStrength );
		attributes.Set( "StagnantSurfaceAmplitude", SurfaceAmplitude );
		attributes.Set( "StagnantSurfaceScale", SurfaceScale );
		attributes.Set( "StagnantSurfaceSpeed", SurfaceSpeed );
		attributes.Set( "StagnantRippleStrength", RippleStrength );
		attributes.Set( "StagnantRippleLifetime", RippleLifetime );
		attributes.Set( "StagnantRippleSpeed", RippleSpeed );

		for ( var i = 0; i < MaxRipples; ++i )
		{
			var ripple = _ripples[i];
			attributes.Set(
				$"StagnantRipple{i}",
				new Vector4( ripple.Position.x, ripple.Position.y, ripple.Radius, ripple.BornTime )
			);
		}
	}

	private void UpdateTrackedColliders()
	{
		_staleColliders.Clear();
		var deltaTime = MathF.Max( Scene.FixedDelta, 0.001f );

		foreach ( var pair in _trackedColliders )
		{
			var collider = pair.Key;
			var tracked = pair.Value;

			if ( !collider.IsValid() )
			{
				_staleColliders.Add( collider );
				continue;
			}

			var position = collider.GetWorldBounds().Center;
			var velocity = collider.Rigidbody.IsValid()
				? collider.GetVelocityAtPoint( position )
				: (position - tracked.PreviousPosition) / deltaTime;
			tracked.PreviousPosition = position;

			if ( velocity.Length < MinimumRippleSpeed || Time.Now - tracked.LastRippleTime < RippleInterval )
			{
				continue;
			}

			tracked.LastRippleTime = Time.Now;
			AddRipple( ProjectToSurface( position ), GetRippleRadius( velocity.Length, 20f ) );
		}

		foreach ( var collider in _staleColliders )
		{
			_trackedColliders.Remove( collider );
		}
	}

	private bool IsExternalCollider( Collider other )
	{
		return other.IsValid()
			&& other != VolumeCollider
			&& other.GameObject != GameObject
			&& !other.GameObject.IsDescendant( GameObject );
	}

	private Vector3 ProjectToSurface( Vector3 worldPosition )
	{
		var local = WorldTransform.PointToLocal( worldPosition );
		local.z = 0f;
		return WorldTransform.PointToWorld( local );
	}

	private static float GetRippleRadius( float speed, float minimum )
	{
		return MathF.Max( minimum, 18f + speed * 0.12f ).Clamp( 8f, 256f );
	}
}
