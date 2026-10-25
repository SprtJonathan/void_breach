using System;

namespace VoidBreach.Water;

/// <summary>A reusable 2D damped wave field. Coordinates belong to a caller-defined surface plane.</summary>
public sealed class FluidSurfaceField
{
	public const int Resolution = 64;
	private const int Count = Resolution * Resolution;
	private float[] _height = new float[Count], _velocity = new float[Count], _foam = new float[Count];
	private float[] _nextHeight = new float[Count], _nextVelocity = new float[Count], _nextFoam = new float[Count];
	private readonly bool[] _blocked = new bool[Count];
	private readonly Vector4[] _pixels = new Vector4[Count];
	private readonly float[] _edgeDamping = new float[7];
	public Vector2 Origin { get; private set; }
	public float CellSize { get; private set; } = 32;
	public float Extent => CellSize * Resolution;
	public float WaveSpeed { get; set; } = 160;
	public float Damping { get; set; } = 1.4f;
	public float MaximumHeight { get; set; } = 24;
	public float Energy { get; private set; }
	public int Version { get; private set; }
	public bool IsSleeping { get; private set; } = true;
	public int SimulationSteps { get; private set; }
	private int _pixelVersion = -1;
	private bool _initialized;
	private int _impulses;
	public void Reset()
	{
		Array.Clear( _height ); Array.Clear( _velocity ); Array.Clear( _foam ); Array.Clear( _blocked );
		Energy = 0; IsSleeping = true; _initialized = false; _impulses = 0; Version++;
	}

	/// <summary>Recenter in whole cells, retaining overlapping samples and clearing newly exposed space.</summary>
	public void MoveTo( Vector2 center, float cellSize )
	{
		if ( !float.IsFinite( center.x + center.y + cellSize ) ) return;
		cellSize = Math.Clamp( cellSize, 4, 256 );
		var origin = new Vector2( MathF.Floor( center.x / cellSize ) - Resolution / 2,
			MathF.Floor( center.y / cellSize ) - Resolution / 2 ) * cellSize;
		if ( _initialized && cellSize == CellSize && origin == Origin ) return;
		if ( _initialized && IsSleeping )
		{
			Array.Clear( _blocked ); Origin = origin; CellSize = cellSize; return;
		}
		var sx = _initialized && cellSize == CellSize ? (int)MathF.Round( (origin.x - Origin.x) / cellSize ) : Resolution;
		var sy = _initialized && cellSize == CellSize ? (int)MathF.Round( (origin.y - Origin.y) / cellSize ) : Resolution;
		Array.Clear( _nextHeight ); Array.Clear( _nextVelocity ); Array.Clear( _nextFoam );
		for ( var y = 0; y < Resolution; y++ )
		for ( var x = 0; x < Resolution; x++ )
		{
			var px = x + sx; var py = y + sy;
			if ( px < 0 || py < 0 || px >= Resolution || py >= Resolution ) continue;
			var a = y * Resolution + x; var b = py * Resolution + px;
			_nextHeight[a] = _height[b]; _nextVelocity[a] = _velocity[b]; _nextFoam[a] = _foam[b];
		}
		Swap(); Array.Clear( _blocked ); Origin = origin; CellSize = cellSize; _initialized = true; Version++;
	}

	public void SetBlocked( int x, int y, bool blocked )
	{
		if ( x < 0 || y < 0 || x >= Resolution || y >= Resolution ) return;
		var i = y * Resolution + x; _blocked[i] = blocked;
		if ( blocked ) { _height[i] = 0; _velocity[i] = 0; _foam[i] = 0; }
	}

	/// <summary>Inject a bounded velocity impulse. Returns false outside the local simulation or over budget.</summary>
	public bool Impulse( Vector2 position, float radius, float strength )
	{
		if ( !_initialized || !float.IsFinite( position.x + position.y + radius + strength ) || _impulses >= 64 ) return false;
		var p = (position - Origin) / CellSize;
		if ( p.x < 0 || p.y < 0 || p.x >= Resolution - 1 || p.y >= Resolution - 1 ) return false;
		_impulses++;
		if ( MathF.Abs( strength ) < 0.001f ) return false;
		IsSleeping = false;
		radius = Math.Clamp( radius, CellSize, CellSize * 8 ); strength = Math.Clamp( strength, -160, 160 );
		var r = radius / CellSize; var reach = (int)MathF.Ceiling( r * 2 );
		for ( var y = Math.Max( 1, (int)p.y - reach ); y <= Math.Min( Resolution - 2, (int)p.y + reach ); y++ )
		for ( var x = Math.Max( 1, (int)p.x - reach ); x <= Math.Min( Resolution - 2, (int)p.x + reach ); x++ )
		{
			var index = y * Resolution + x;
			if ( _blocked[index] ) continue;
			var dx = (x - p.x) / r; var dy = (y - p.y) / r;
			// Positive core and negative annulus avoid adding a permanent mound of water.
			var r2 = (dx * dx + dy * dy) * 2;
			var weight = (1 - r2) * MathF.Exp( -r2 );
			_velocity[index] = Math.Clamp( _velocity[index] + strength * weight, -240, 240 );
			_foam[index] = MathF.Max( _foam[index], MathF.Abs( strength * weight ) / 100 );
		}
		return true;
	}

	public void Step( float delta, Vector2 flow )
	{
		_impulses = 0;
		if ( !_initialized || IsSleeping || !float.IsFinite( delta + flow.x + flow.y + WaveSpeed + Damping + MaximumHeight ) || delta <= 0 ) return;
		var speed = Math.Clamp( WaveSpeed, 1, 600 );
		var maximumHeight = Math.Clamp( MaximumHeight, 0.1f, 128 );
		// CFL bound includes the current; large frame gaps are deliberately time-limited.
		delta = MathF.Min( delta, 0.1f );
		flow = flow.ClampLength( 512 );
		var steps = Math.Clamp( (int)MathF.Ceiling( delta * (speed * 1.5f + flow.Length) / (CellSize * 0.45f) ), 1, 64 );
		var dt = delta / steps; var c2 = speed * speed / (CellSize * CellSize);
		for ( var edge = 0; edge < 7; edge++ ) _edgeDamping[edge] = MathF.Exp( -(MathF.Max( Damping, 0.05f ) + Math.Max( 0, 6 - edge ) * 0.8f) * dt );
		var heightDecay = MathF.Exp( -0.1f * dt ); var foamDecay = MathF.Exp( -0.9f * dt );
		var advect = flow.LengthSquared > 0.001f;
		for ( var step = 0; step < steps; step++ )
		{
			Array.Clear( _nextHeight ); Array.Clear( _nextVelocity ); Array.Clear( _nextFoam );
			for ( var y = 1; y < Resolution - 1; y++ )
			for ( var x = 1; x < Resolution - 1; x++ )
			{
				var i = y * Resolution + x;
				if ( _blocked[i] ) continue;
				var h = _height[i];
				float Neighbor( int n ) => _blocked[n] ? h : _height[n];
				var laplacian = Neighbor( i - 1 ) + Neighbor( i + 1 ) + Neighbor( i - Resolution ) + Neighbor( i + Resolution ) - h * 4;
				var edge = Math.Min( Math.Min( x, y ), Math.Min( Resolution - 1 - x, Resolution - 1 - y ) );
				var damping = _edgeDamping[Math.Min( edge, 6 )];
				var v = (_velocity[i] + laplacian * c2 * dt) * damping;
				var backX = x - flow.x * dt / CellSize; var backY = y - flow.y * dt / CellSize;
				var advected = advect ? Bilinear( _height, backX, backY ) : h;
				var advectedVelocity = advect ? Bilinear( _velocity, backX, backY ) : _velocity[i];
				v += advectedVelocity - _velocity[i];
				_nextVelocity[i] = Math.Clamp( v, -240, 240 );
				_nextHeight[i] = Math.Clamp( (advected + v * dt) * heightDecay, -maximumHeight, maximumHeight );
				_nextFoam[i] = Math.Clamp( (advect ? Bilinear( _foam, backX, backY ) : _foam[i]) * foamDecay + MathF.Abs( laplacian ) * dt * 0.05f, 0, 1 );
			}
			Swap();
		}
		Energy = 0; var maxHeight = 0f; var maxVelocity = 0f; var maxFoam = 0f;
		for ( var i = 0; i < Count; i++ )
		{
			Energy += _height[i] * _height[i] + _velocity[i] * _velocity[i] * 0.01f;
			maxHeight = MathF.Max( maxHeight, MathF.Abs( _height[i] ) );
			maxVelocity = MathF.Max( maxVelocity, MathF.Abs( _velocity[i] ) ); maxFoam = MathF.Max( maxFoam, _foam[i] );
		}
		if ( maxHeight < 0.003f && maxVelocity < 0.01f && maxFoam < 0.002f )
		{
			Array.Clear( _height ); Array.Clear( _velocity ); Array.Clear( _foam ); Energy = 0; IsSleeping = true;
		}
		SimulationSteps += steps;
		Version++;
	}

	private void Swap()
	{
		(_height, _nextHeight) = (_nextHeight, _height);
		(_velocity, _nextVelocity) = (_nextVelocity, _velocity);
		(_foam, _nextFoam) = (_nextFoam, _foam);
	}
	private static float Bilinear( float[] data, float x, float y )
	{
		if ( x < 0 || y < 0 || x >= Resolution - 1 || y >= Resolution - 1 ) return 0;
		var ix = (int)x; var iy = (int)y; var fx = x - ix; var fy = y - iy; var a = iy * Resolution + ix;
		return (data[a] * (1 - fx) + data[a + 1] * fx) * (1 - fy)
			+ (data[a + Resolution] * (1 - fx) + data[a + Resolution + 1] * fx) * fy;
	}
	public Vector4 Sample( Vector2 position )
	{
		if ( !_initialized || !float.IsFinite( position.x + position.y ) ) return Vector4.Zero;
		var p = (position - Origin) / CellSize;
		return new Vector4( Bilinear( _height, p.x, p.y ),
			(Bilinear( _height, p.x + 1, p.y ) - Bilinear( _height, p.x - 1, p.y )) / (2 * CellSize),
			(Bilinear( _height, p.x, p.y + 1 ) - Bilinear( _height, p.x, p.y - 1 )) / (2 * CellSize), Bilinear( _foam, p.x, p.y ) );
	}
	public Vector4[] GetPixels()
	{
		if ( _pixelVersion == Version ) return _pixels;
		for ( var y = 0; y < Resolution; y++ )
		for ( var x = 0; x < Resolution; x++ )
		{
			var i = y * Resolution + x;
			_pixels[i] = new Vector4( _height[i],
				((x < Resolution - 1 ? _height[i + 1] : 0) - (x > 0 ? _height[i - 1] : 0)) / (2 * CellSize),
				((y < Resolution - 1 ? _height[i + Resolution] : 0) - (y > 0 ? _height[i - Resolution] : 0)) / (2 * CellSize), _foam[i] );
		}
		_pixelVersion = Version;
		return _pixels;
	}
}
