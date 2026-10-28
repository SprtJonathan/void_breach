using System;
using System.Collections.Generic;

namespace VoidBreach.Environment;

public sealed partial class VBPrecipitation
{
	private readonly List<RainColumn> _nearPaths = new();
	private int _nearCursor;
	public int ExposedNearCells { get; private set; }
	private static int NearSide( VBRainQuality quality ) => quality switch { VBRainQuality.Low => 12, VBRainQuality.Medium => 16, VBRainQuality.High => 20, _ => 0 };

	private void UpdateUniformNearRain( Vector3 camera, float radius, float intensity, int maximum, int frameLimit )
	{
		var side = NearSide( Quality );
		var count = side * side;
		_nearPaths.EnsureCapacity( count );
		while ( _nearPaths.Count < count ) _nearPaths.Add( new RainColumn { CheckedAt = -100 } );
		if ( _nearPaths.Count > count ) _nearPaths.RemoveRange( count, _nearPaths.Count - count );
		var cell = radius * 2 / side;
		var reference = MathF.Floor( camera.z / 128 ) * 128;
		var cloudBase = VBCloudWeather.Finite( Weather.Altitude, 16000, 1000, 80000 );
		var depth = VBCloudWeather.Finite( Depth, 1000, 128, 4000 );
		var refresh = VBCloudWeather.Finite( ShelterRefreshSeconds, 2, 0.25f, 5 );
		var quota = Math.Max( 1, (int)(_traceTokens * 0.67f) );
		for ( var visited = 0; visited < count && quota > 0; visited++ )
		{
			var index = _nearCursor % count; _nearCursor = (_nearCursor + 73) % count;
			var x = WrappedCell( (int)MathF.Floor( camera.x / cell ) - side / 2, index % side, side );
			var y = WrappedCell( (int)MathF.Floor( camera.y / cell ) - side / 2, index / side, side );
			var path = _nearPaths[index];
			if ( path.X == x && path.Y == y && path.CellSize == cell && path.ReferenceHeight == reference && path.CloudBase == cloudBase &&
				!(path.Blocker is not null && !path.Blocker.IsValid()) && _rainClock - path.CheckedAt < refresh ) continue;
			if ( !CanTrace( 1, frameLimit ) ) break;
			quota--; LastTraceCount++;
			var center = new Vector3( (x + 0.5f) * cell, (y + 0.5f) * cell, reference );
			var start = center - RainVelocity * ((cloudBase - 32 - reference) / -RainVelocity.z);
			var end = center + RainVelocity * ((reference - camera.z + depth) / -RainVelocity.z);
			var half = cell * 0.5f;
			var hit = Scene.Trace.Ray( start, end ).Size( new BBox( new Vector3( -half, -half, -0.5f ), new Vector3( half, half, 0.5f ) ) )
				.WithoutTags( IgnoreTags ?? Array.Empty<string>() ).Run();
			path = new RainColumn { X = x, Y = y, Tier = 2, CellSize = cell, ReferenceHeight = reference, CloudBase = cloudBase,
				Start = start, End = hit.EndPosition, Velocity = RainVelocity, CheckedAt = _rainClock,
				Valid = !hit.StartedSolid, Hit = hit.Hit, Blocker = hit.Collider };
			if ( !path.Valid || path.End.z >= camera.z + VBCloudWeather.Finite( Height, 550, 128, 2000 ) ) BlockedSpawns++;
			_nearPaths[index] = path;
		}
		_drops.Clear(); ExposedNearCells = 0;
		var target = Math.Clamp( (int)MathF.Ceiling( maximum * MathF.Min( intensity / 8, 1 ) ), count, maximum );
		for ( var index = 0; index < count; index++ )
		{
			var path = _nearPaths[index];
			if ( !path.Valid || _rainClock - path.CheckedAt > CacheLifetime ) continue;
			var top = MathF.Min( path.Start.z, camera.z + VBCloudWeather.Finite( Height, 550, 128, 2000 ) );
			if ( top - path.End.z < 8 ) continue;
			ExposedNearCells++;
			var start = path.Start + path.Velocity * ((path.Start.z - top) / -path.Velocity.z);
			var duration = (top - path.End.z) / -path.Velocity.z;
			var allocation = (index + 1) * target / count - index * target / count;
			var half = MathF.Max( 0, path.CellSize * 0.5f - 4 );
			var seed = HashUnit( ColumnHash( path.X, path.Y, 2 ) );
			for ( var i = 0; i < allocation; i++ )
			{
				var offset = new Vector3( (Frac( (i + 0.5) / allocation + seed ) * 2 - 1) * half,
					(Frac( i * 0.61803398875 + seed * 7.31 ) * 2 - 1) * half, 0 );
				var phase = Frac( _rainClock / duration + HashUnit( ColumnHash( path.X + i * 197, path.Y - i * 79, 3 ) ) );
				_drops.Add( new Drop { Start = start + offset, End = path.End + offset, Age = phase * duration, Lifetime = duration } );
			}
		}
	}
	private void ReleaseNearRain()
	{
		_nearPaths.Clear(); _nearCursor = 0; ExposedNearCells = 0;
	}
}
