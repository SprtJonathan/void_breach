namespace VoidBreach.Ocean;

/// <summary>
/// Builds a tessellated ocean plane and drives the Gerstner ocean shader.
/// Put this component on an empty GameObject positioned at the desired sea level.
/// </summary>
[Title( "Gerstner Ocean" )]
[Category( "Void Breach/Ocean" )]
[Icon( "water" )]
public sealed class GerstnerOcean : Component, Component.ExecuteInEditor
{
	private const string ShaderPath = "ocean/shaders/gerstner_ocean.shader";

	[Property, Group( "Geometry" ), Range( 1024f, 65536f )]
	public float Size { get; set; } = 12000f;

	[Property, Group( "Geometry" ), Range( 16, 256 )]
	public int Resolution { get; set; } = 192;

	[Property, Group( "Waves" )]
	public Vector2 WindDirection { get; set; } = new( 1f, 0.25f );

	[Property, Group( "Waves" ), Range( 0f, 256f )]
	public float WaveAmplitude { get; set; } = 52f;

	[Property, Group( "Waves" ), Range( 64f, 4096f )]
	public float WaveLength { get; set; } = 1100f;

	[Property, Group( "Waves" ), Range( 0f, 4f )]
	public float WaveSpeed { get; set; } = 1f;

	[Property, Group( "Waves" ), Range( 0f, 1f )]
	public float Choppiness { get; set; } = 0.72f;

	[Property, Group( "Surface" )]
	public Color DeepColor { get; set; } = new( 0.006f, 0.045f, 0.075f );

	[Property, Group( "Surface" )]
	public Color ShallowColor { get; set; } = new( 0.025f, 0.22f, 0.26f );

	[Property, Group( "Surface" )]
	public Color HorizonColor { get; set; } = new( 0.18f, 0.38f, 0.48f );

	[Property, Group( "Surface" ), Range( 0.01f, 0.6f )]
	public float Roughness { get; set; } = 0.075f;

	[Property, Group( "Foam" ), Range( 0f, 1f )]
	public float FoamThreshold { get; set; } = 0.69f;

	[Property, Group( "Foam" ), Range( 0f, 4f )]
	public float FoamIntensity { get; set; } = 1.15f;

	private ModelRenderer _renderer;
	private Model _generatedModel;
	private float _builtSize;
	private int _builtResolution;

	protected override void OnEnabled()
	{
		EnsureOceanMesh();
		ApplyShaderSettings();
	}

	protected override void OnUpdate()
	{
		EnsureOceanMesh();
		ApplyShaderSettings();
	}

	protected override void OnValidate()
	{
		Size = Size.Clamp( 1024f, 65536f );
		Resolution = Resolution.Clamp( 16, 256 );
		WaveLength = WaveLength.Clamp( 64f, 4096f );
		WindDirection = WindDirection.LengthSquared < 0.0001f ? Vector2.Right : WindDirection.Normal;
	}

	protected override void OnDestroy()
	{
		if ( _renderer.IsValid() && _renderer.Model == _generatedModel )
		{
			_renderer.Model = null;
		}

		_generatedModel = null;
	}

	private void EnsureOceanMesh()
	{
		_renderer ??= GetOrAddComponent<ModelRenderer>();

		if ( _generatedModel.IsValid() && _builtSize == Size && _builtResolution == Resolution )
		{
			return;
		}

		_generatedModel = BuildOceanModel( Size, Resolution );
		_builtSize = Size;
		_builtResolution = Resolution;

		_renderer.Model = _generatedModel;
		_renderer.MaterialOverride = null;
	}

	private Model BuildOceanModel( float size, int resolution )
	{
		var material = Material.FromShader( ShaderPath );
		var mesh = new Mesh( material );
		var verticesPerSide = resolution + 1;
		var vertices = new Vertex[verticesPerSide * verticesPerSide];
		var indices = new int[resolution * resolution * 6];
		var halfSize = size * 0.5f;

		for ( var y = 0; y < verticesPerSide; ++y )
		{
			var v = y / (float)resolution;

			for ( var x = 0; x < verticesPerSide; ++x )
			{
				var u = x / (float)resolution;
				var position = new Vector3( u * size - halfSize, v * size - halfSize, 0f );
				vertices[y * verticesPerSide + x] = new Vertex(
					position,
					Vector3.Up,
					Vector3.Forward,
					new Vector4( u, v, 0f, 0f )
				);
			}
		}

		var index = 0;
		for ( var y = 0; y < resolution; ++y )
		{
			for ( var x = 0; x < resolution; ++x )
			{
				var bottomLeft = y * verticesPerSide + x;
				var bottomRight = bottomLeft + 1;
				var topLeft = bottomLeft + verticesPerSide;
				var topRight = topLeft + 1;

				indices[index++] = bottomLeft;
				indices[index++] = bottomRight;
				indices[index++] = topRight;
				indices[index++] = topRight;
				indices[index++] = topLeft;
				indices[index++] = bottomLeft;
			}
		}

		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );

		var verticalExtent = WaveAmplitude * 2.5f + 64f;
		mesh.Bounds = BBox.FromPositionAndSize(
			Vector3.Zero,
			new Vector3( size, size, verticalExtent * 2f )
		);

		return Model.Builder
			.WithName( "void_breach_gerstner_ocean" )
			.AddMesh( mesh )
			.Create();
	}

	private void ApplyShaderSettings()
	{
		if ( !_renderer.IsValid() )
		{
			return;
		}

		var direction = WindDirection.LengthSquared < 0.0001f ? Vector2.Right : WindDirection.Normal;
		var attributes = _renderer.Attributes;

		attributes.Set( "OceanWindDirection", direction );
		attributes.Set( "OceanWaveAmplitude", WaveAmplitude );
		attributes.Set( "OceanWaveLength", WaveLength );
		attributes.Set( "OceanWaveSpeed", WaveSpeed );
		attributes.Set( "OceanChoppiness", Choppiness );
		attributes.Set( "OceanDeepColor", DeepColor );
		attributes.Set( "OceanShallowColor", ShallowColor );
		attributes.Set( "OceanHorizonColor", HorizonColor );
		attributes.Set( "OceanRoughness", Roughness );
		attributes.Set( "OceanFoamThreshold", FoamThreshold );
		attributes.Set( "OceanFoamIntensity", FoamIntensity );
	}
}
