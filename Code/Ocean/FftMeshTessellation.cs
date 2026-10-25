using System;
using System.Collections.Generic;

namespace VoidBreach.Ocean;

/// <summary>Refine the existing lattice near the interactive field, preserving distant geometry.</summary>
internal static class FftMeshTessellation
{
	public static float[] Refine( IReadOnlyList<float> coarse, float center, float halfExtent, float spacing )
	{
		var start = MathF.Max( coarse[0], MathF.Floor( (center - halfExtent) / spacing ) * spacing );
		var end = MathF.Min( coarse[^1], MathF.Ceiling( (center + halfExtent) / spacing ) * spacing );
		if ( end <= start ) return new List<float>( coarse ).ToArray();
		var result = new List<float>( coarse.Count + (int)((end - start) / spacing) + 2 );
		foreach ( var p in coarse ) if ( p < start ) result.Add( p );
		result.Add( start );
		for ( var p = MathF.Ceiling( start / spacing ) * spacing; p < end - 0.01f; p += spacing )
			if ( p > result[^1] + 0.01f ) result.Add( p );
		if ( end > result[^1] + 0.01f ) result.Add( end );
		foreach ( var p in coarse ) if ( p > end + 0.01f ) result.Add( p );
		return result.ToArray();
	}
}
