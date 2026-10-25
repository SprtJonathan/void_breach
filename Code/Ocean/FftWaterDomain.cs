using System;
using System.Collections.Generic;

namespace VoidBreach.Ocean;

public enum FftWaterShape { Ocean, Lake, River }

/// <summary>The same ribbon triangles define rendering, containment, bottom height and river flow.</summary>
internal sealed class FftWaterDomain
{
	internal readonly record struct Row( Vector3 Left, Vector3 Right, Vector3 Flow, float Distance );
	public readonly List<Row> Rows = new();
	public readonly List<Row> ControlRows = new();
	public FftWaterShape Shape { get; private set; }
	public Vector2 Size { get; private set; }
	public float Width { get; private set; }
	public float Length => Rows.Count > 0 ? Rows[^1].Distance : 1;
	public void Build( FftWaterShape shape, Vector2 size, float width, IReadOnlyList<Vector3> points, float spacing )
	{
		Shape = shape; Size = size; Width = MathF.Max( width, 16 ); Rows.Clear(); ControlRows.Clear();
		if ( shape != FftWaterShape.River || points is null || points.Count < 2 ) return;
		var count = Math.Min( points.Count, 64 ); var length = 0f;
		Vector3 Side( int index )
		{
			var previous = points[index] - points[Math.Max( 0, index - 1 )]; previous.z = 0;
			var next = points[Math.Min( count - 1, index + 1 )] - points[index]; next.z = 0;
			if ( previous.LengthSquared < 1 ) previous = next;
			if ( next.LengthSquared < 1 ) next = previous;
			var sideA = Vector3.Cross( Vector3.Up, previous.Normal ); var sideB = Vector3.Cross( Vector3.Up, next.Normal );
			var miter = (sideA + sideB).Normal;
			if ( miter.LengthSquared < 0.1f ) miter = new Vector3( 0, 1, 0 );
			return miter / MathF.Max( Vector3.Dot( miter, sideB ), 0.5f );
		}
		for ( var segment = 0; segment < count - 1; segment++ )
		{
			var a = points[segment]; var b = points[segment + 1]; var delta = b - a;
			if ( new Vector2( delta.x, delta.y ).Length < 1 ) continue;
			var sideA = Side( segment ) * (Width * 0.5f); var sideB = Side( segment + 1 ) * (Width * 0.5f);
			var steps = Math.Clamp( (int)MathF.Ceiling( delta.Length / MathF.Max( spacing, 16 ) ), 1, 48 );
			for ( var step = 0; step <= steps; step++ )
			{
				if ( step == 0 && Rows.Count > 0 ) continue;
				var t = step / (float)steps;
				var center = Vector3.Lerp( a, b, t ); var side = Vector3.Lerp( sideA, sideB, t );
				var row = new Row( center - side, center + side, delta.Normal, length + delta.Length * t );
				Rows.Add( row );
				if ( step == 0 || step == steps ) ControlRows.Add( row );
			}
			length += delta.Length;
		}
	}
	public bool Sample( Vector3 p, out float height, out Vector3 flow, out Vector3 normal )
	{
		height = 0; flow = Vector3.Forward; normal = Vector3.Up;
		if ( Shape == FftWaterShape.Ocean ) return true;
		if ( Shape == FftWaterShape.Lake )
		{
			var x = p.x / MathF.Max( Size.x * 0.5f, 1 ); var y = p.y / MathF.Max( Size.y * 0.5f, 1 );
			return x * x + y * y <= 1;
		}
		for ( var i = 1; i < Rows.Count; i++ )
		{
			var a = Rows[i - 1]; var b = Rows[i];
			if ( !Triangle( p, a.Left, b.Left, b.Right, out height, out normal )
				&& !Triangle( p, a.Left, b.Right, a.Right, out height, out normal ) ) continue;
			flow = b.Flow; return true;
		}
		return false;
	}
	internal static bool Triangle( Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float height, out Vector3 normal )
	{
		height = 0; normal = Vector3.Up;
		var ab = b - a; var ac = c - a; var ap = p - a; var determinant = ab.x * ac.y - ab.y * ac.x;
		if ( MathF.Abs( determinant ) < 0.001f ) return false;
		var u = (ap.x * ac.y - ap.y * ac.x) / determinant; var v = (ab.x * ap.y - ab.y * ap.x) / determinant;
		if ( u < -0.0001f || v < -0.0001f || u + v > 1.0001f ) return false;
		height = a.z + ab.z * u + ac.z * v; normal = Vector3.Cross( ab, ac ).Normal;
		if ( normal.z < 0 ) normal = -normal;
		return true;
	}
}
