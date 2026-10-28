using System;

namespace VoidBreach.Environment;

public sealed partial class VBRainAudio
{
	private struct Opening { public Vector3 Position; public bool Valid; }
	private Opening[] _openings;
	private int _probeIndex;
	private Vector3 _probePosition;

	private SceneTraceResult Trace( Vector3 from, Vector3 to )
	{
		LastTraceCount++;
		return Scene.Trace.Ray( from, to ).WithoutTags( IgnoreTags ?? Array.Empty<string>() ).Run();
	}
	private bool OpenSky( Vector3 point, float sky )
	{
		var hit = Trace( point, point.WithZ( MathF.Max( sky, point.z + 128 ) ) );
		return !hit.Hit && !hit.StartedSolid;
	}
	private static float DistanceGain( float distance, float range )
	{
		var t = Math.Clamp( 1 - distance / range, 0, 1 );
		return t * t;
	}

	// At most eight rays per probe, one of eight directions at a time. No rays
	// scale with player count, rainfall density, or individual audio samples.
	private void ProbeEnvironment( Transform ear )
	{
		LastTraceCount = 0;
		_openings ??= new Opening[8];
		if ( !_hasProbe || ear.Position.Distance( _probePosition ) > 64 ) Array.Clear( _openings );
		_probePosition = ear.Position;
		var sky = VBCloudWeather.Finite( Weather.Altitude, 16000, 1000, 80000 );
		var ceiling = Trace( ear.Position, ear.Position.WithZ( MathF.Max( sky, ear.Position.z + 128 ) ) );
		var exposed = !ceiling.Hit && !ceiling.StartedSolid;
		if ( exposed != ExposedToSky ) Array.Clear( _openings );
		ExposedToSky = exposed; _hasProbe = true;
		ShelterAudibility = 0; ShelterSoundPosition = ear.Position;
		if ( exposed || ceiling.StartedSolid || ear.Position.z >= sky ) return;
		var range = VBCloudWeather.Finite( HearingDistance, 1200, 128, 4096 );
		var thickness = VBCloudWeather.Finite( MaximumThinRoofThickness, 32, 4, 128 );
		var roof = Trace( ceiling.EndPosition + Vector3.Up * (thickness + 1), ceiling.EndPosition - Vector3.Up );
		if ( roof.Hit && !roof.StartedSolid && roof.Collider == ceiling.Collider && roof.Normal.z > 0.5f )
		{
			var depth = MathF.Max( 0, roof.EndPosition.z - ceiling.EndPosition.z );
			ShelterSoundPosition = roof.EndPosition + Vector3.Up * 4;
			ShelterAudibility = VBCloudWeather.Finite( ThinRoofVolume, 0.7f, 0, 1 ) *
				Math.Clamp( 1 - depth / thickness, 0, 1 ) * DistanceGain( ShelterSoundPosition.Distance( ear.Position ), range );
		}
		var forward = ear.Rotation.Forward.WithZ( 0 ).Normal;
		if ( forward.LengthSquared < 0.1f ) forward = Vector3.Forward;
		var angle = _probeIndex * MathF.PI / 4;
		var direction = forward * MathF.Cos( angle ) + Vector3.Cross( Vector3.Up, forward ) * MathF.Sin( angle );
		// Discover exposed rain even behind a wall or around a corner. The native
		// acoustic model, not a duplicate line-of-sight test, handles propagation.
		var distance = range;
		var far = ear.Position + direction * distance;
		var visible = OpenSky( far, sky );
		if ( !visible )
		{
			distance *= 0.5f; far = ear.Position + direction * distance;
			visible = OpenSky( far, sky );
		}
		if ( visible )
		{
			float near = 0;
			for ( int i = 0; i < 4; i++ )
			{
				var middle = (near + distance) * 0.5f;
				if ( OpenSky( ear.Position + direction * middle, sky ) ) distance = middle;
				else near = middle;
			}
			_openings[_probeIndex] = new Opening { Position = ear.Position + direction * distance + Vector3.Up * 2, Valid = true };
		}
		else _openings[_probeIndex] = default;
		_probeIndex = (_probeIndex + 1) % _openings.Length;
		foreach ( var opening in _openings )
		{
			if ( !opening.Valid ) continue;
			var gain = DistanceGain( ear.Position.Distance( opening.Position ), range );
			if ( gain <= ShelterAudibility ) continue;
			ShelterAudibility = gain; ShelterSoundPosition = opening.Position;
		}
	}
}
