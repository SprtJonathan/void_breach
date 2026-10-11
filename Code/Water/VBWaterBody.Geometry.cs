using System;

namespace VoidBreach.Water;

public sealed partial class VBWaterBody
{
	private readonly List<Vector3> _left = new();
	private readonly List<Vector3> _right = new();
	private readonly List<float> _distance = new();
	private void EnsureGeometry()
	{
		var hash = new HashCode();
		hash.Add( Shape ); hash.Add( Size ); hash.Add( Resolution ); hash.Add( WaveAmplitude );
		hash.Add( IsInfinite ); hash.Add( HorizonDistance );
		hash.Add( WorldScale ); hash.Add( RiverWidth ); hash.Add( RiverSubdivisions );
		if ( Shape == WaterShape.River && RiverPoints is not null )
			foreach ( var p in RiverPoints ) hash.Add( p );
		var value = hash.ToHashCode();
		if ( _geometryReady && _geometryHash == value ) return;
		_geometryReady = true;
		_geometryHash = value;
		_model = null;
		_left.Clear(); _right.Clear(); _distance.Clear();
		if ( Shape != WaterShape.River || RiverPoints is null || RiverPoints.Count < 2 ) return;
		// Cap authoring data to prevent accidentally allocating an unbounded mesh.
		var count = Math.Min( RiverPoints.Count, 128 );
		var steps = Math.Clamp( RiverSubdivisions, 2, 32 );
		var distance = 0f;
		var previous = RiverPoints[0];
		for ( var segment = 0; segment < count - 1; segment++ )
		{
			var p0 = RiverPoints[Math.Max( 0, segment - 1 )];
			var p1 = RiverPoints[segment];
			var p2 = RiverPoints[segment + 1];
			var p3 = RiverPoints[Math.Min( count - 1, segment + 2 )];
			var end = segment == count - 2 ? steps : steps - 1;
			for ( var step = 0; step <= end; step++ )
			{
				var t = step / (float)steps;
				var p = Catmull( p0, p1, p2, p3, t );
				var tangent = 0.5f * ((-p0 + p2) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t
					+ 3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t * t);
				var side = Vector3.Cross( Vector3.Up, tangent ).Normal;
				if ( side.LengthSquared < 0.01f ) side = new Vector3( 0, 1, 0 );
				distance += (p - previous).Length;
				previous = p;
				_left.Add( p - side * RiverWidth * 0.5f );
				_right.Add( p + side * RiverWidth * 0.5f );
				_distance.Add( distance );
			}
		}
	}

	private static Vector3 Catmull( Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t ) =>
		0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t
			+ (-a + 3f * b - 3f * c + d) * t * t * t);

	private bool TryBaseSurface( Vector3 p, out float height, out Vector3 flow, out Vector3 normal )
	{
		height = 0; flow = new Vector3( Direction.x, Direction.y, 0 ); normal = Vector3.Up;
		if ( IsInfinite ) return true;
		if ( Shape != WaterShape.River )
		{
			var x = p.x / MathF.Max( Size.x * 0.5f, 1 );
			var y = p.y / MathF.Max( Size.y * 0.5f, 1 );
			return Shape == WaterShape.Ellipse ? x * x + y * y <= 1 : MathF.Abs( x ) <= 1 && MathF.Abs( y ) <= 1;
		}
		for ( var row = 0; row + 1 < _left.Count; row++ )
		{
			var min = Vector3.Min( Vector3.Min( _left[row], _right[row] ), Vector3.Min( _left[row + 1], _right[row + 1] ) );
			var max = Vector3.Max( Vector3.Max( _left[row], _right[row] ), Vector3.Max( _left[row + 1], _right[row + 1] ) );
			if ( p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y ) continue;
			for ( var column = 0; column < 8; column++ )
			{
				var a = Vector3.Lerp( _left[row], _right[row], column / 8f );
				var b = Vector3.Lerp( _left[row], _right[row], (column + 1) / 8f );
				var c = Vector3.Lerp( _left[row + 1], _right[row + 1], column / 8f );
				var d = Vector3.Lerp( _left[row + 1], _right[row + 1], (column + 1) / 8f );
				if ( !TriangleHeight( p, a, c, d, out height, out normal )
					&& !TriangleHeight( p, a, d, b, out height, out normal ) ) continue;
				flow = ((_left[row + 1] + _right[row + 1]) - (_left[row] + _right[row])).Normal;
				return true;
			}
		}
		return false;
	}

	internal static bool TriangleHeight( Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float height, out Vector3 normal )
	{
		height = 0; normal = Vector3.Up;
		var ab = b - a; var ac = c - a; var ap = p - a;
		var det = ab.x * ac.y - ab.y * ac.x;
		if ( MathF.Abs( det ) < 0.0001f ) return false;
		var u = (ap.x * ac.y - ap.y * ac.x) / det;
		var v = (ab.x * ap.y - ab.y * ap.x) / det;
		if ( u < -0.00001f || v < -0.00001f || u + v > 1.00001f ) return false;
		height = a.z + ab.z * u + ac.z * v;
		normal = Vector3.Cross( ab, ac ).Normal;
		if ( normal.z < 0 ) normal = -normal;
		return true;
	}

	private Model BuildModel( Material material )
	{
		var river = Shape == WaterShape.River;
		if ( river && _left.Count < 2 ) return null;
		var columns = river ? 8 : Math.Clamp( Resolution, 8, 160 );
		var rows = river ? _left.Count - 1 : columns;
		var vertices = new Vertex[(rows + 1) * (columns + 1)];
		var indices = new int[rows * columns * 6];
		var min = new Vector3( float.MaxValue ); var max = new Vector3( float.MinValue );
		for ( var y = 0; y <= rows; y++ )
		for ( var x = 0; x <= columns; x++ )
		{
			var u = x / (float)columns; var v = y / (float)rows;
			if ( river ) v = _distance[y] / MathF.Max( _distance[^1], 1 );
			var p = river ? Vector3.Lerp( _left[y], _right[y], u ) : new Vector3( (u - 0.5f) * Size.x, (v - 0.5f) * Size.y, 0 );
			if ( IsInfinite )
			{
                // Constant world-space lattice; only the outer boundary extends to the horizon.
                float Coordinate(int index) => index==0 ? -HorizonDistance : index==columns ? HorizonDistance : (index-columns/2f)*128;
                // Infinite VS consumes these directly as world offsets, independent of object yaw/scale.
                p=new Vector3(Coordinate(x),Coordinate(y),0);
			}
			var tangent = Vector3.Forward;
			if ( river )
			{
				var next = Math.Min( rows, y + 1 ); var prev = Math.Max( 0, y - 1 );
				tangent = ((_left[next] + _right[next]) - (_left[prev] + _right[prev])).Normal;
			}
			vertices[y * (columns + 1) + x] = new Vertex( p, Vector3.Up, tangent,
				new Vector4( u, v, river ? _distance[y] : p.x, river ? u * RiverWidth : p.y ) );
			min = Vector3.Min( min, p ); max = Vector3.Max( max, p );
		}
		var index = 0;
		for ( var y = 0; y < rows; y++ )
		for ( var x = 0; x < columns; x++ )
		{
			var a = y * (columns + 1) + x; var b = a + 1; var c = a + columns + 1; var d = c + 1;
			// River's longitudinal and lateral coordinates have the opposite orientation to the plane grid.
			indices[index++] = a; indices[index++] = river ? c : b; indices[index++] = d;
			indices[index++] = a; indices[index++] = d; indices[index++] = river ? b : c;
		}
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		var padding = new Vector3( WaveAmplitude / WorldScale.x + 1, WaveAmplitude / WorldScale.y + 1, WaveAmplitude / MathF.Max( WorldScale.z, 0.001f ) + 4 );
		mesh.Bounds = new BBox( min - padding, max + padding );
		// The shader centers the mesh for each view (including editor/reflection cameras).
		// Keep the proxy visible throughout Source's practical world coordinate range.
		if ( IsInfinite ) mesh.Bounds = new BBox( new Vector3( -10000000 ), new Vector3( 10000000 ) );
		return Model.Builder.WithName( "vb_water_surface" ).AddMesh( mesh ).Create();
	}
}
