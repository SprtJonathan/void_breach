using System;

namespace VoidBreach.Ocean;

public sealed partial class VBFFTOcean
{
	private readonly FftWaterDomain _domain = new();
	private int _domainHash;
	private bool _domainReady;
	public int SurfaceVertexCount { get; private set; }
	private Transform DomainTransform => new( WorldPosition,
		Vector3.Dot( WorldRotation.Up, Vector3.Up ) > 0.9999f ? WorldRotation : Rotation.Identity, Vector3.One );
	private void EnsureDomain()
	{
		var hash = new HashCode(); hash.Add( Shape ); hash.Add( Size ); hash.Add( RiverWidth ); hash.Add( GridSpacing );
		if ( RiverPoints is not null ) foreach ( var p in RiverPoints.Take( 64 ) ) hash.Add( p );
		var value = hash.ToHashCode();
		if ( _domainReady && value == _domainHash ) return;
		_domain.Build( Shape, new Vector2( MathF.Max( Size.x, 32 ), MathF.Max( Size.y, 32 ) ), RiverWidth, RiverPoints, GridSpacing );
		_domainHash = value; _domainReady = true;
	}
	private void EnsureGeometry()
	{
		if ( !_visual.IsValid() )
		{
			_visual = new GameObject( GameObject, true, "FFT water surface (generated)" );
			_visual.Flags = GameObjectFlags.NotSaved;
			_visual.NetworkMode = NetworkMode.Never; _visual.Tags.Add( "water_surface" );
			_renderer = _visual.AddComponent<ModelRenderer>();
			_renderer.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned | ComponentFlags.NotNetworked;
		}
		_visual.WorldTransform = Shape == FftWaterShape.Ocean ? new Transform( WorldPosition, Rotation.Identity, Vector3.One ) : DomainTransform;
		var fieldCenter = _field.Origin + Vector2.One * _field.Extent * 0.5f;
		var focus = DomainTransform.PointToLocal( Interactions && _field.CellSize > 0 ? new Vector3( fieldCenter.x, fieldCenter.y, WorldPosition.z ) : WorldPosition );
		// Finite domains rebuild only on tile crossings, with enough fine-grid padding
		// to cover the moving near field between crossings. The ocean lattice follows in VS.
		focus = !Interactions || Shape == FftWaterShape.Ocean ? Vector3.Zero : new Vector3( MathF.Floor( focus.x / 128 ) * 128, MathF.Floor( focus.y / 128 ) * 128, 0 );
		var fineSpacing = Math.Clamp( InteractionMeshSpacing, 4, 16 );
		var fineExtent = Math.Clamp( InteractionCellSize, 8, 32 ) * 32;
		var hash = HashCode.Combine( Resolution, GridSpacing, HorizonDistance, _domainHash, WaveHeight,
			HashCode.Combine( Interactions, fineSpacing, fineExtent, focus, InteractionMaximumHeight ) );
		if ( _model.IsValid() && hash == _geometryHash ) return;
		_material ??= Material.Load( "water/materials/fft_ocean.vmat" );
		var resolution = Math.Clamp( Resolution, 32, 192 ); resolution += resolution % 2;
		var spacing = Math.Clamp( GridSpacing, 16, 128 ); var horizon = Math.Clamp( HorizonDistance, 16384, 500000 );
		var river = Shape == FftWaterShape.River;
		float Coordinate( int index )
		{
			var cell = index - resolution / 2; var core = resolution / 2 - 12; var distance = Math.Abs( cell );
			if ( distance <= core ) return cell * spacing;
			var t = (distance - core) / 12f;
			return Math.Sign( cell ) * (core * spacing + spacing * (MathF.Exp( t * MathF.Log( 1 + (horizon - core * spacing) / spacing ) ) - 1));
		}
		var axisX = new float[resolution + 1]; var axisY = new float[resolution + 1];
		for ( var i = 0; i <= resolution; i++ )
		{
			axisX[i] = Shape == FftWaterShape.Ocean ? Coordinate( i ) : (i / (float)resolution - 0.5f) * _domain.Size.x;
			axisY[i] = Shape == FftWaterShape.Ocean ? Coordinate( i ) : (i / (float)resolution - 0.5f) * _domain.Size.y;
		}
		// Bounded local refinement: preserve the FFT horizon and distant lake grid.
		// A 512-unit fine patch adds at most 256 segments per axis at the default quality.
		var extent = MathF.Min( fineExtent, 384 ) + (Shape == FftWaterShape.Ocean ? 32 : 128);
		if ( Interactions && !river )
		{
			axisX = FftMeshTessellation.Refine( axisX, focus.x, extent, fineSpacing );
			axisY = FftMeshTessellation.Refine( axisY, focus.y, extent, fineSpacing );
		}
		var renderRows = new List<FftWaterDomain.Row>();
		if ( river && _domain.Rows.Count > 0 )
		{
			var extraRowBudget = 512;
			renderRows.Add( _domain.Rows[0] );
			for ( var i = 1; i < _domain.Rows.Count; i++ )
			{
				var a = _domain.Rows[i - 1]; var b = _domain.Rows[i];
				var ac = (a.Left + a.Right) * 0.5f; var bc = (b.Left + b.Right) * 0.5f;
				var near = (Vector3.Lerp( ac, bc, 0.5f ).WithZ( 0 ) - focus).Length < extent + (bc - ac).Length;
				var steps = Interactions && near ? Math.Clamp( (int)MathF.Ceiling( (bc - ac).Length / fineSpacing ), 1, 64 ) : 1;
				steps = Math.Min( steps, extraRowBudget + 1 ); extraRowBudget -= steps - 1;
				for ( var j = 1; j <= steps; j++ )
				{
					var t = j / (float)steps;
					renderRows.Add( new( Vector3.Lerp( a.Left, b.Left, t ), Vector3.Lerp( a.Right, b.Right, t ), b.Flow, MathX.Lerp( a.Distance, b.Distance, t ) ) );
				}
			}
		}
		var columns = river ? Math.Clamp( (int)MathF.Ceiling( RiverWidth / (Interactions ? fineSpacing : spacing) ), 4, 128 ) : axisX.Length - 1;
		var rows = river ? renderRows.Count - 1 : axisY.Length - 1;
		if ( rows < 1 ) { _renderer.Model = null; return; }
		var side = columns + 1; var vertices = new Vertex[side * (rows + 1)]; var indices = new int[columns * rows * 6];
		SurfaceVertexCount = vertices.Length;
		var min = new Vector3( float.MaxValue ); var max = new Vector3( float.MinValue );
		for ( var y = 0; y <= rows; y++ )
		for ( var x = 0; x <= columns; x++ )
		{
			var u = x / (float)columns; var v = y / (float)rows;
			var p = river ? Vector3.Lerp( renderRows[y].Left, renderRows[y].Right, u ) : new Vector3( axisX[x], axisY[y], 0 );
			if ( Shape == FftWaterShape.Lake ) { u = p.x / _domain.Size.x + 0.5f; v = p.y / _domain.Size.y + 0.5f; }
			var tangent = river ? renderRows[y].Flow : Vector3.Forward;
			if ( river ) v = renderRows[y].Distance / MathF.Max( _domain.Length, 1 );
			var normal = river ? Vector3.Cross( tangent, renderRows[y].Right - renderRows[y].Left ).Normal : Vector3.Up;
			vertices[y * side + x] = new Vertex( p, normal, tangent, new Vector4( u, v, 0, 0 ) );
			min = Vector3.Min( min, p ); max = Vector3.Max( max, p );
		}
		var index = 0;
		for ( var y = 0; y < rows; y++ )
		for ( var x = 0; x < columns; x++ )
		{
			var a = y * side + x; var b = a + 1; var c = a + side; var d = c + 1;
			indices[index++] = a; indices[index++] = river ? c : b; indices[index++] = d;
			indices[index++] = a; indices[index++] = d; indices[index++] = river ? b : c;
		}
		var mesh = new Mesh( _material ); mesh.CreateVertexBuffer( vertices.Length, vertices ); mesh.CreateIndexBuffer( indices.Length, indices );
		var padding = new Vector3( MathF.Max( WaveHeight, 0 ) * FftOceanSpectrum.UnitsPerMeter * 2 + Math.Clamp( InteractionMaximumHeight, 1, 64 ) + 8 );
		mesh.Bounds = Shape == FftWaterShape.Ocean ? new BBox( new Vector3( -10000000 ), new Vector3( 10000000 ) ) : new BBox( min - padding, max + padding );
		_model = Model.Builder.WithName( "vb_fft_water" ).AddMesh( mesh ).Create();
		_renderer.Model = _model; _geometryHash = hash;
	}
}
