using System;
using System.Collections.Generic;

namespace VoidBreach.Environment;

public sealed partial class VBPrecipitation
{
	private struct GroundContact
	{
		public Vector3 Position, Normal;
		public int X, Y;
		public double CheckedAt;
		public float SourceEndHeight;
		public bool Valid;
	}
	private readonly List<GroundContact> _groundContacts = new();
	private int _contactCursor;
	public int GroundContactCount { get; private set; }
	private void UpdateGroundImpacts( Vector3 camera, float intensity, int maximum, int frameLimit )
	{
		_splashes.Clear(); GroundContactCount = 0;
		if ( !Splashes || SplashDensity <= 0 || SplashChance <= 0 || _nearPaths.Count == 0 ) return;
		var count = _nearPaths.Count;
		_groundContacts.EnsureCapacity( count );
		while ( _groundContacts.Count < count ) _groundContacts.Add( new GroundContact { CheckedAt = -100 } );
		if ( _groundContacts.Count > count ) _groundContacts.RemoveRange( count, _groundContacts.Count - count );
		var quota = Math.Min( frameLimit / 4, (int)_traceTokens );
		var refresh = VBCloudWeather.Finite( ShelterRefreshSeconds, 2, 0.25f, 5 );
		for ( var visited = 0; visited < count && quota > 0; visited++ )
		{
			var index = _contactCursor % count; _contactCursor = (_contactCursor + 73) % count;
			var path = _nearPaths[index];
			var contact = _groundContacts[index];
			if ( contact.X == path.X && contact.Y == path.Y && MathF.Abs( contact.SourceEndHeight - path.End.z ) < 1 && _rainClock - contact.CheckedAt < refresh ) continue;
			contact = new GroundContact { X = path.X, Y = path.Y, CheckedAt = _rainClock, SourceEndHeight = path.End.z };
			if ( !path.Valid || !path.Hit || _rainClock - path.CheckedAt > CacheLifetime ) { _groundContacts[index] = contact; continue; }
			if ( !CanTrace( 1, frameLimit ) ) break;
			quota--; LastTraceCount++;
			var seed = HashUnit( ColumnHash( path.X, path.Y, 91 ) );
			var half = MathF.Max( path.CellSize * 0.5f - 4, 0 );
			var offset = new Vector3( (seed * 2 - 1) * half, (Frac( seed * 7.31 ) * 2 - 1) * half, 0 );
			// Trace the full exposed trajectory once per contact, including overhead
			// blockers. Reuse the actual surface point for many brief ripples/spray.
			var hit = Scene.Trace.Ray( path.Start + offset, path.End + offset - Vector3.Up * (path.CellSize * 2 + 8) )
				.WithoutTags( IgnoreTags ?? Array.Empty<string>() ).Run();
			if ( hit.Hit && !hit.StartedSolid && hit.Normal.z > 0.35f && MathF.Abs( hit.EndPosition.z - camera.z ) < VBCloudWeather.Finite( Depth, 1000, 128, 4000 ) )
			{
				contact.Position = hit.EndPosition + hit.Normal * 0.4f; contact.Normal = hit.Normal; contact.Valid = true;
			}
			_groundContacts[index] = contact;
		}
		var target = Math.Clamp( (int)MathF.Ceiling( maximum * MathF.Min( intensity / 8, 1 ) *
			VBCloudWeather.Finite( SplashDensity, 1, 0, 4 ) * VBCloudWeather.Finite( SplashChance, 0.35f, 0, 1 ) / 0.35f ), 0, maximum );
		var lifetime = VBCloudWeather.Finite( SplashLifetime, 0.45f, 0.2f, 1 );
		var radius = VBCloudWeather.Finite( Radius, 700, 128, 2000 );
		for ( var index = 0; index < count; index++ )
		{
			var contact = _groundContacts[index];
			var source = _nearPaths[index];
			if ( !contact.Valid || !source.Valid || contact.X != source.X || contact.Y != source.Y || MathF.Abs( contact.SourceEndHeight - source.End.z ) >= 1 || _rainClock - contact.CheckedAt > CacheLifetime ||
				new Vector2( contact.Position.x - camera.x, contact.Position.y - camera.y ).Length > radius ) continue;
			GroundContactCount++;
			var allocation = (index + 1) * target / count - index * target / count;
			for ( var i = 0; i < allocation; i++ )
			{
				var clock = _rainClock / lifetime + HashUnit( ColumnHash( contact.X + i * 97, contact.Y - i * 59, 81 ) );
				var phase = Frac( clock );
				var cycle = (int)(Math.Floor( clock ) % 65536);
				var seed = HashUnit( ColumnHash( contact.X + cycle * 197, contact.Y - i * 59, 83 ) );
				// Sub-centimetre variation avoids a visibly repeating stamp while
				// staying close to the validated contact and its surface normal.
				var u = Vector3.Cross( contact.Normal, Vector3.Forward ).Normal;
				var v = Vector3.Cross( contact.Normal, u ).Normal;
				var position = contact.Position + u * ((seed * 2 - 1) * 0.25f) + v * ((Frac( seed * 7.31 ) * 2 - 1) * 0.25f);
				_splashes.Add( new Splash { Position = position, Normal = contact.Normal, Age = phase * lifetime, Seed = seed } );
			}
		}
	}
	private void ReleaseGroundImpacts()
	{
		_groundContacts.Clear(); _contactCursor = 0; GroundContactCount = 0;
	}
}
