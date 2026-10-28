using System;
using System.Collections.Generic;

namespace VoidBreach.Environment;

public sealed partial class VBPrecipitation
{
	[Property, Group( "Map Coverage" )] public bool DistantRain { get; set; } = true;
	[Property, Group( "Map Coverage" ), Range( 2048, 100000 ), Description( "Rain visibility across the map, independently of the detailed near-drop radius. Limited by the camera far plane." )]
	public float DistantDistance { get; set; } = 24000;
	[Property, Group( "Map Coverage" ), Range( 1024, 20000 )] public float DistantVerticalRange { get; set; } = 6000;
	[Property, Group( "Map Coverage" ), Range( 0.25f, 5 ), Description( "Seconds between cached shelter checks. Independent of rainfall density." )]
	public float ShelterRefreshSeconds { get; set; } = 2;
	public int CachedDistantPaths => _distantPaths.Count;
	public int LastDistantQuads { get; private set; }
	public float EffectiveDistantDistance { get; private set; }

	public static (int Side, int Quads) DistantBudget( VBRainQuality quality ) => quality switch
	{
		VBRainQuality.Low => (8, 2048),
		VBRainQuality.Medium => (12, 8192),
		VBRainQuality.High => (16, 16384),
		_ => (0, 0)
	};
	private readonly List<RainColumn> _distantPaths = new();
	private int _columnCursor;
	private void UpdateDistantRain( CameraComponent camera, Vector3 cameraPosition, int frameLimit )
	{
		var budget = DistantBudget( Quality );
		var viewLimit = Scene.IsEditor ? 100000 : camera.ZFar * 0.95f;
		EffectiveDistantDistance = DistantRain ? MathF.Min( VBCloudWeather.Finite( DistantDistance, 24000, 2048, 100000 ), viewLimit ) : 0;
		if ( EffectiveDistantDistance <= VBCloudWeather.Finite( Radius, 700, 128, 2000 ) || budget.Side == 0 ) { ReleaseDistantRain(); return; }
		var count = budget.Side * budget.Side * 2;
		_distantPaths.EnsureCapacity( count );
		while ( _distantPaths.Count < count ) _distantPaths.Add( new RainColumn { CheckedAt = -100 } );
		if ( _distantPaths.Count > count ) _distantPaths.RemoveRange( count, _distantPaths.Count - count );
		// Spend at most half the available physics budget. Detailed near rain and
		// its impacts retain tokens while the world field warms up or refreshes.
		var quota = Math.Min( frameLimit / 2, (int)(_traceTokens * 0.5f) );
		var cloudBase = VBCloudWeather.Finite( Weather.Altitude, 16000, 1000, 80000 );
		var reference = MathF.Floor( cameraPosition.z / 512 ) * 512;
		for ( var visited = 0; visited < count && quota > 0; visited++ )
		{
			var index = _columnCursor % count;
			_columnCursor = (_columnCursor + 73) % count;
			var tier = index / (budget.Side * budget.Side);
			var extent = tier == 0 ? MathF.Min( 4096, EffectiveDistantDistance ) : EffectiveDistantDistance;
			var cellSize = extent * 2 / budget.Side;
			var x = WrappedCell( (int)MathF.Floor( cameraPosition.x / cellSize ) - budget.Side / 2, index % budget.Side, budget.Side );
			var y = WrappedCell( (int)MathF.Floor( cameraPosition.y / cellSize ) - budget.Side / 2, index / budget.Side % budget.Side, budget.Side );
			var path = _distantPaths[index];
			var matches = path.X == x && path.Y == y && path.Tier == tier && path.ReferenceHeight == reference && path.CloudBase == cloudBase && path.CellSize == cellSize;
			if ( matches && !(path.Blocker is not null && !path.Blocker.IsValid()) && _rainClock - path.CheckedAt < VBCloudWeather.Finite( ShelterRefreshSeconds, 2, 0.25f, 5 ) ) continue;
			var point = new Vector3( (x + 0.5f) * cellSize, (y + 0.5f) * cellSize, reference );
			var horizontal = new Vector2( point.x - cameraPosition.x, point.y - cameraPosition.y ).Length;
			var minimum = tier == 0 ? VBCloudWeather.Finite( Radius, 700, 128, 2000 ) * 0.7f : MathF.Min( 4096, EffectiveDistantDistance ) * 0.8f;
			path = new RainColumn { X = x, Y = y, Tier = tier, ReferenceHeight = reference, CloudBase = cloudBase,
				CellSize = cellSize, CheckedAt = _rainClock, Velocity = RainVelocity };
			// Retain cells whose footprint overlaps a band, even when their center
			// lies inside the near radius. Otherwise diagonal gaps remain at seams.
			var halfDiagonal = cellSize * 0.707107f;
			if ( horizontal + halfDiagonal < minimum || horizontal - halfDiagonal > extent * 1.1f || tier == 1 && EffectiveDistantDistance <= 4096 )
			{
				_distantPaths[index] = path; continue;
			}
			if ( !CanTrace( 1, frameLimit ) ) break;
			quota--; LastTraceCount++;
			var start = point - RainVelocity * ((cloudBase - 32 - point.z) / -RainVelocity.z);
			var end = point + RainVelocity * (VBCloudWeather.Finite( DistantVerticalRange, 6000, 1024, 20000 ) / -RainVelocity.z);
			// Check the whole cell footprint, then scatter independent droplets
			// across it. A column is an area of steady rain, not a vertical queue.
			var half = cellSize * 0.5f;
			var hit = Scene.Trace.Ray( start, end ).Size( new BBox( new Vector3( -half, -half, -0.5f ), new Vector3( half, half, 0.5f ) ) )
				.WithoutTags( IgnoreTags ?? Array.Empty<string>() ).Run();
			path.Start = start; path.End = hit.EndPosition;
			path.Blocker = hit.Collider;
			path.Valid = !hit.StartedSolid && start.z - hit.EndPosition.z > 64;
			_distantPaths[index] = path;
		}
	}
	private void AppendDistantRain( float length, float width )
	{
		LastDistantQuads = 0;
		if ( !DistantRain || EffectiveDistantDistance <= 0 || _distantPaths.Count == 0 ) return;
		var maximum = DistantBudget( Quality ).Quads;
		var target = Math.Clamp( (int)MathF.Ceiling( maximum * MathF.Min( _rainIntensity / 8, 1 ) ), _distantPaths.Count, maximum );
		var vertical = VBCloudWeather.Finite( DistantVerticalRange, 6000, 1024, 20000 );
		var near = VBCloudWeather.Finite( Radius, 700, 128, 2000 );
		var refresh = CacheLifetime;
		var opacity = Tint.a * 0.75f * MathF.Min( _rainIntensity, 1 );
		for ( var index = 0; index < _distantPaths.Count; index++ )
		{
			var path = _distantPaths[index];
			if ( !path.Valid || _rainClock - path.CheckedAt > refresh ) continue;
			var bottom = MathF.Max( path.End.z, _lastCamera.z - vertical );
			var top = MathF.Min( path.Start.z, _lastCamera.z + vertical );
			if ( top - bottom < 64 ) continue;
			var duration = (top - bottom) / MathF.Max( -path.Velocity.z, 1 );
			var perPath = (index + 1) * target / _distantPaths.Count - index * target / _distantPaths.Count;
			var seed = HashUnit( ColumnHash( path.X, path.Y, path.Tier ) );
			var half = MathF.Max( 0, path.CellSize * 0.5f - 24 );
			var direction = path.Velocity.Normal;
			for ( var i = 0; i < perPath; i++ )
			{
				var phase = Frac( _rainClock / duration + HashUnit( ColumnHash( path.X + i * 197, path.Y - i * 79, path.Tier + 7 ) ) );
				var z = top - phase * (top - bottom);
				var offset = new Vector3( (Frac( (i + 0.5) / perPath + seed ) * 2 - 1) * half,
					(Frac( i * 0.61803398875 + seed * 7.31 ) * 2 - 1) * half, 0 );
				var head = path.Start + path.Velocity * ((path.Start.z - z) / -path.Velocity.z) + offset;
				var distance = head.Distance( Graphics.CameraPosition );
				var horizontal = new Vector2( head.x - _lastCamera.x, head.y - _lastCamera.y ).Length;
				var fade = Math.Clamp( (horizontal - near * 0.7f) / MathF.Max( near * 0.5f, 1 ), 0, 1 ) *
					Math.Clamp( (EffectiveDistantDistance - horizontal) / (EffectiveDistantDistance * 0.15f), 0, 1 );
				if ( fade <= 0 ) continue;
				var streak = MathF.Min( MathF.Max( length * 1.5f, distance * 0.006f ), head.Distance( path.Start ) );
				var tail = head - direction * streak;
				var side = Vector3.Cross( direction, Graphics.CameraPosition - head ).Normal * MathF.Min( 20, MathF.Max( width, distance * 0.00065f ) );
				Quad( tail - side, tail + side, head + side, head - side, Tint.WithAlpha( opacity * fade ), Vector3.Up );
				LastDistantQuads++;
			}
		}
	}
	private void ExpandDistantBounds( Vector3 camera )
	{
		if ( EffectiveDistantDistance <= 0 ) return;
		var vertical = VBCloudWeather.Finite( DistantVerticalRange, 6000, 1024, 20000 );
		var padding = new Vector3( EffectiveDistantDistance + 256, EffectiveDistantDistance + 256, vertical + 256 );
		_renderObject.Bounds = new BBox( Vector3.Min( _renderObject.Bounds.Mins, camera - padding ), Vector3.Max( _renderObject.Bounds.Maxs, camera + padding ) );
	}
	private void ReleaseDistantRain()
	{
		_distantPaths.Clear(); _columnCursor = 0; LastDistantQuads = 0; EffectiveDistantDistance = 0;
	}
}
