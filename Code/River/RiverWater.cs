using System;

namespace VoidBreach.River;

/// <summary>
/// Builds a curved river ribbon, scans the riverbed for depth and drives the interactive river shader.
/// </summary>
[Title( "River Water" )]
[Category( "Void Breach/River" )]
[Icon( "water" )]
public sealed class RiverWater : Component, Component.ExecuteInEditor
{
	private const string ShaderPath = "river/shaders/interactive_river.shader";
	private const int MaxInteractions = 8;

	[Property, Group( "Path" )]
	public List<GameObject> ControlPoints { get; set; } = new();

	[Property, Group( "Path" ), Range( 64f, 2048f )]
	public float DefaultWidth { get; set; } = 420f;

	[Property, Group( "Path" ), Range( 2, 32 )]
	public int SamplesPerSegment { get; set; } = 12;

	[Property, Group( "Path" ), Range( 2, 16 )]
	public int CrossSegments { get; set; } = 8;

	[Property, Group( "Depth" ), Range( 32f, 2048f )]
	public float MaximumDepth { get; set; } = 384f;

	[Property, Group( "Depth" ), Range( 8f, 1024f )]
	public float DeepColorDepth { get; set; } = 240f;

	[Property, Group( "Flow" ), Range( 0f, 8f )]
	public float FlowSpeed { get; set; } = 1.35f;

	[Property, Group( "Flow" ), Range( 16f, 1024f )]
	public float FlowScale { get; set; } = 180f;

	[Property, Group( "Flow" ), Range( 0f, 24f )]
	public float SurfaceAmplitude { get; set; } = 3.5f;

	[Property, Group( "Surface" )]
	public Color ShallowColor { get; set; } = new( 0.12f, 0.34f, 0.28f );

	[Property, Group( "Surface" )]
	public Color DeepColor { get; set; } = new( 0.015f, 0.105f, 0.12f );

	[Property, Group( "Surface" )]
	public Color FoamColor { get; set; } = new( 0.72f, 0.82f, 0.75f );

	[Property, Group( "Surface" ), Range( 0.01f, 0.8f )]
	public float Roughness { get; set; } = 0.16f;

	[Property, Group( "Surface" ), Range( 0f, 3f )]
	public float BankFoam { get; set; } = 0.9f;

	[Property, Group( "Interaction" ), Range( 0f, 3f )]
	public float RippleStrength { get; set; } = 0.8f;

	private readonly List<RiverSample> _centerline = new();
	private readonly RiverRipple[] _ripples = new RiverRipple[MaxInteractions];
	private ModelRenderer _renderer;
	private Model _generatedModel;
	private int _geometryHash;
	private int _nextRipple;

	private readonly record struct RiverSample( Vector3 Position, Vector3 Flow, float Width, float Distance );
	private readonly record struct RiverRipple( Vector3 Position, float Radius, float BornTime );

	protected override void OnEnabled()
	{
		EnsureRiverMesh();
		ApplyShaderSettings();
	}

	protected override void OnUpdate()
	{
		EnsureRiverMesh();
		ApplyShaderSettings();
	}

	protected override void OnValidate()
	{
		DefaultWidth = DefaultWidth.Clamp( 64f, 2048f );
		SamplesPerSegment = SamplesPerSegment.Clamp( 2, 32 );
		CrossSegments = CrossSegments.Clamp( 2, 16 );
		MaximumDepth = MaximumDepth.Clamp( 32f, 2048f );
		DeepColorDepth = DeepColorDepth.Clamp( 8f, MaximumDepth );
		FlowScale = FlowScale.Clamp( 16f, 1024f );
	}

	protected override void OnDestroy()
	{
		if ( _renderer.IsValid() && _renderer.Model == _generatedModel )
		{
			_renderer.Model = null;
		}

		_generatedModel = null;
		_centerline.Clear();
	}

	/// <summary>Adds one transient circular disturbance to the water surface.</summary>
	public void AddRipple( Vector3 worldPosition, float radius )
	{
		_ripples[_nextRipple] = new RiverRipple(
			worldPosition,
			radius.Clamp( 8f, 512f ),
			Time.Now
		);

		_nextRipple = (_nextRipple + 1) % MaxInteractions;
	}

	/// <summary>Finds the closest point on the river surface and checks its width and height.</summary>
	public bool TryGetSurfaceInfo(
		Vector3 worldPosition,
		float verticalTolerance,
		out Vector3 surfacePosition,
		out Vector3 flowDirection )
	{
		surfacePosition = default;
		flowDirection = Vector3.Forward;

		if ( _centerline.Count < 2 )
		{
			return false;
		}

		var query = new Vector2( worldPosition.x, worldPosition.y );
		var bestDistanceSquared = float.MaxValue;
		var bestHalfWidth = 0f;

		for ( var i = 0; i < _centerline.Count - 1; ++i )
		{
			var a = _centerline[i];
			var b = _centerline[i + 1];
			var a2 = new Vector2( a.Position.x, a.Position.y );
			var b2 = new Vector2( b.Position.x, b.Position.y );
			var segment = b2 - a2;
			var lengthSquared = MathF.Max( segment.LengthSquared, 0.001f );
			var fraction = Vector2.Dot( query - a2, segment ) / lengthSquared;
			fraction = fraction.Clamp( 0f, 1f );
			var closest2 = a2 + segment * fraction;
			var distanceSquared = (query - closest2).LengthSquared;

			if ( distanceSquared >= bestDistanceSquared )
			{
				continue;
			}

			bestDistanceSquared = distanceSquared;
			bestHalfWidth = MathX.Lerp( a.Width, b.Width, fraction ) * 0.5f;
			surfacePosition = Vector3.Lerp( a.Position, b.Position, fraction );
			flowDirection = Vector3.Lerp( a.Flow, b.Flow, fraction ).Normal;
		}

		return bestDistanceSquared <= bestHalfWidth * bestHalfWidth
			&& MathF.Abs( worldPosition.z - surfacePosition.z ) <= verticalTolerance;
	}

	private void EnsureRiverMesh()
	{
		_renderer ??= GetOrAddComponent<ModelRenderer>();
		var geometryHash = CalculateGeometryHash();

		if ( _generatedModel.IsValid() && geometryHash == _geometryHash )
		{
			return;
		}

		_geometryHash = geometryHash;
		_generatedModel = BuildRiverModel();
		_renderer.Model = _generatedModel;
		_renderer.MaterialOverride = null;
	}

	private Model BuildRiverModel()
	{
		var points = GetConfiguredControlPoints();
		_centerline.Clear();

		if ( points.Count < 2 )
		{
			return null;
		}

		BuildCenterline( points );

		var verticesAcross = CrossSegments + 1;
		var vertices = new Vertex[_centerline.Count * verticesAcross];
		var indices = new int[(_centerline.Count - 1) * CrossSegments * 6];
		var localUp = WorldTransform.NormalToLocal( Vector3.Up ).Normal;
		var boundsMin = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
		var boundsMax = new Vector3( float.MinValue, float.MinValue, float.MinValue );

		for ( var row = 0; row < _centerline.Count; ++row )
		{
			var sample = _centerline[row];
			var side = Vector3.Cross( Vector3.Up, sample.Flow ).Normal;

			for ( var column = 0; column < verticesAcross; ++column )
			{
				var across = column / (float)CrossSegments;
				var lateral = (across - 0.5f) * sample.Width;
				var worldPosition = sample.Position + side * lateral;
				var depth = SampleNormalizedDepth( worldPosition );
				var localPosition = WorldTransform.PointToLocal( worldPosition );
				var localFlow = WorldTransform.NormalToLocal( sample.Flow ).Normal;

				vertices[row * verticesAcross + column] = new Vertex(
					localPosition,
					localUp,
					localFlow,
					new Vector4( across, sample.Distance / FlowScale, depth, 0f )
				);

				boundsMin = Vector3.Min( boundsMin, localPosition );
				boundsMax = Vector3.Max( boundsMax, localPosition );
			}
		}

		var index = 0;
		for ( var row = 0; row < _centerline.Count - 1; ++row )
		{
			for ( var column = 0; column < CrossSegments; ++column )
			{
				var bottomLeft = row * verticesAcross + column;
				var bottomRight = bottomLeft + 1;
				var topLeft = bottomLeft + verticesAcross;
				var topRight = topLeft + 1;

				indices[index++] = bottomLeft;
				indices[index++] = bottomRight;
				indices[index++] = topRight;
				indices[index++] = topRight;
				indices[index++] = topLeft;
				indices[index++] = bottomLeft;
			}
		}

		var mesh = new Mesh( Material.FromShader( ShaderPath ) );
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );

		var boundsCenter = (boundsMin + boundsMax) * 0.5f;
		var boundsSize = boundsMax - boundsMin + new Vector3( 16f, 16f, SurfaceAmplitude * 4f + 16f );
		mesh.Bounds = BBox.FromPositionAndSize( boundsCenter, boundsSize );

		return Model.Builder
			.WithName( "void_breach_interactive_river" )
			.AddMesh( mesh )
			.Create();
	}

	private void BuildCenterline( IReadOnlyList<GameObject> points )
	{
		var distance = 0f;
		Vector3? previous = null;

		for ( var segment = 0; segment < points.Count - 1; ++segment )
		{
			var p0 = points[Math.Max( segment - 1, 0 )].WorldPosition;
			var p1 = points[segment].WorldPosition;
			var p2 = points[segment + 1].WorldPosition;
			var p3 = points[Math.Min( segment + 2, points.Count - 1 )].WorldPosition;
			var width1 = GetPointWidth( points[segment] );
			var width2 = GetPointWidth( points[segment + 1] );

			for ( var step = 0; step < SamplesPerSegment; ++step )
			{
				var fraction = step / (float)SamplesPerSegment;
				var position = EvaluateCatmullRom( p0, p1, p2, p3, fraction );
				var flow = EvaluateCatmullRomTangent( p0, p1, p2, p3, fraction );
				flow.z = 0f;
				flow = flow.IsNearZeroLength ? Vector3.Forward : flow.Normal;

				if ( previous.HasValue )
				{
					distance += Vector3.DistanceBetween( previous.Value, position );
				}

				_centerline.Add( new RiverSample(
					position,
					flow,
					MathX.Lerp( width1, width2, fraction ),
					distance
				) );

				previous = position;
			}
		}

		var lastPosition = points[^1].WorldPosition;
		var lastFlow = lastPosition - points[^2].WorldPosition;
		lastFlow.z = 0f;
		lastFlow = lastFlow.IsNearZeroLength ? Vector3.Forward : lastFlow.Normal;
		distance += previous.HasValue ? Vector3.DistanceBetween( previous.Value, lastPosition ) : 0f;
		_centerline.Add( new RiverSample( lastPosition, lastFlow, GetPointWidth( points[^1] ), distance ) );
	}

	private float SampleNormalizedDepth( Vector3 surfacePosition )
	{
		var start = surfacePosition + Vector3.Up * 8f;
		var end = surfacePosition + Vector3.Down * MaximumDepth;
		var trace = Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreDynamic()
			.Run();

		if ( !trace.Hit )
		{
			return 1f;
		}

		var depth = MathF.Max( surfacePosition.z - trace.EndPosition.z, 0f );
		return (depth / MathF.Max( DeepColorDepth, 1f )).Clamp( 0f, 1f );
	}

	private int CalculateGeometryHash()
	{
		var points = GetConfiguredControlPoints();
		var hash = 17;
		hash = hash * 31 + SamplesPerSegment;
		hash = hash * 31 + CrossSegments;
		hash = hash * 31 + DefaultWidth.GetHashCode();
		hash = hash * 31 + MaximumDepth.GetHashCode();
		hash = hash * 31 + DeepColorDepth.GetHashCode();
		hash = hash * 31 + FlowScale.GetHashCode();
		hash = hash * 31 + points.Count;

		foreach ( var point in points )
		{
			if ( !point.IsValid() )
			{
				hash *= 31;
				continue;
			}

			hash = hash * 31 + point.Id.GetHashCode();
			hash = hash * 31 + point.WorldPosition.GetHashCode();
			hash = hash * 31 + GetPointWidth( point ).GetHashCode();
		}

		return hash;
	}

	private List<GameObject> GetConfiguredControlPoints()
	{
		var configured = ControlPoints.Where( point => point.IsValid() ).ToList();
		if ( configured.Count >= 2 )
		{
			return configured;
		}

		return GameObject.Children
			.Where( child => child.GetComponent<RiverControlPoint>().IsValid() )
			.ToList();
	}

	private float GetPointWidth( GameObject point )
	{
		var controlPoint = point.GetComponent<RiverControlPoint>();
		return controlPoint.IsValid() ? controlPoint.Width : DefaultWidth;
	}

	private void ApplyShaderSettings()
	{
		if ( !_renderer.IsValid() )
		{
			return;
		}

		var attributes = _renderer.Attributes;
		attributes.Set( "RiverFlowSpeed", FlowSpeed );
		attributes.Set( "RiverSurfaceAmplitude", SurfaceAmplitude );
		attributes.Set( "RiverShallowColor", ShallowColor );
		attributes.Set( "RiverDeepColor", DeepColor );
		attributes.Set( "RiverFoamColor", FoamColor );
		attributes.Set( "RiverRoughness", Roughness );
		attributes.Set( "RiverBankFoam", BankFoam );
		attributes.Set( "RiverRippleStrength", RippleStrength );

		ApplyObstacleAttributes( attributes );
		ApplyRippleAttributes( attributes );
	}

	private void ApplyObstacleAttributes( RenderAttributes attributes )
	{
		var count = 0;
		foreach ( var obstacle in Scene.GetAll<RiverObstacle>() )
		{
			if ( !obstacle.Active || count >= MaxInteractions )
			{
				continue;
			}

			if ( !TryGetSurfaceInfo( obstacle.WorldPosition, MaximumDepth, out _, out _ ) )
			{
				continue;
			}

			attributes.Set(
				$"RiverObstacle{count}",
				new Vector4( obstacle.WorldPosition.x, obstacle.WorldPosition.y, obstacle.Radius, obstacle.Strength )
			);
			count++;
		}

		for ( ; count < MaxInteractions; ++count )
		{
			attributes.Set( $"RiverObstacle{count}", Vector4.Zero );
		}
	}

	private void ApplyRippleAttributes( RenderAttributes attributes )
	{
		for ( var i = 0; i < MaxInteractions; ++i )
		{
			var ripple = _ripples[i];
			attributes.Set(
				$"RiverRipple{i}",
				new Vector4( ripple.Position.x, ripple.Position.y, ripple.Radius, ripple.BornTime )
			);
		}
	}

	private static Vector3 EvaluateCatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * (
			2f * p1
			+ (-p0 + p2) * t
			+ (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
			+ (-p0 + 3f * p1 - 3f * p2 + p3) * t3
		);
	}

	private static Vector3 EvaluateCatmullRomTangent( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		return 0.5f * (
			-p0 + p2
			+ 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t
			+ 3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2
		);
	}
}
