// Explicit generator. Run with sbox-cli run_csharp. Only writes rain assets, never the active scene.
var assets = System.IO.Path.Combine( Sandbox.Project.Current.GetRootPath(), "Assets" );
var outputs = new System.Collections.Generic.List<string>();
foreach ( var kind in new[] { "drop", "splash" } )
{
	var graph = new Editor.ShaderGraph.ShaderGraph
	{
		Description = kind == "drop" ? "VB rain: transmitted scene light and local refraction" : "VB rain: irregular wet impacts and ballistic microdroplets",
		BlendMode = Editor.ShaderGraph.BlendMode.Translucent,
		ShadingModel = Editor.ShaderGraph.ShadingModel.Unlit
	};
	void Add( Editor.ShaderGraph.BaseNode node, string id, int x, int y )
	{
		node.Identifier = id; node.Position = new Vector2( x, y ); graph.AddNode( node );
	}
	Editor.ShaderGraph.NodeInput Link( string id, string output = "Result" ) => new() { Identifier = id, Output = output };
	Add( new Editor.ShaderGraph.Nodes.TextureCoord(), "uv", -900, 0 );
	Add( new Editor.ShaderGraph.Nodes.Float2 { Value = new Vector2( 0.5f ) }, "center", -900, 150 );
	Add( new Editor.ShaderGraph.Nodes.Subtract { A = Link( "uv" ), B = Link( "center" ) }, "centered", -650, 0 );
	Add( new Editor.ShaderGraph.Nodes.Length { Input = Link( "centered" ) }, "radius", -450, 0 );
	Add( new Editor.ShaderGraph.Nodes.Multiply { A = Link( "radius" ), DefaultB = 2 }, "diameter", -250, 0 );
	var maskInput = "diameter";
	Add( new Editor.ShaderGraph.Nodes.OneMinus { In = Link( maskInput ) }, "inverse", 550, 0 );
	Add( new Editor.ShaderGraph.Nodes.Saturate { Input = Link( "inverse", "Out" ) }, "mask", 750, 0 );
	if ( kind == "splash" ) Add( new VoidBreach.Editor.VBRainImpactMaskNode(), "impactMask", 750, 350 );
	Add( new Editor.ShaderGraph.Nodes.VertexColor(), "color", 750, 200 );
	Add( new Editor.ShaderGraph.Nodes.Multiply { A = Link( kind == "splash" ? "impactMask" : "mask" ), B = Link( "color", "Alpha" ) }, "alpha", 950, 0 );
	Add( new VoidBreach.Editor.VBRainCloudMaskNode(), "clouds", 950, 200 );
	Add( new Editor.ShaderGraph.Nodes.Multiply { A = Link( "alpha" ), B = Link( "clouds" ) }, "cloudAlpha", 1150, 0 );
	var albedo = Link( "color", "RGB" );
	var opacity = Link( "cloudAlpha" );
	{
		// Use the native Scene Color node, with a depth-safe local refraction UV.
		Add( new VoidBreach.Editor.VBRainOpticsNode { SurfaceImpact = kind == "splash" }, "refraction", 700, 500 );
		Add( new Editor.ShaderGraph.Nodes.SceneColor { Coords = Link( "refraction", "Coordinates" ), AddressU = Editor.ShaderGraph.SamplerAddress.Clamp, AddressV = Editor.ShaderGraph.SamplerAddress.Clamp }, "background", 950, 500 );
		Add( new VoidBreach.Editor.VBRainOpticsNode { Background = Link( "background" ), SurfaceImpact = kind == "splash" }, "optics", 1150, 500 );
		Add( new Editor.ShaderGraph.Nodes.Multiply { A = Link( "cloudAlpha" ), B = Link( "optics", "Opacity" ) }, "opticalAlpha", 1350, 0 );
		albedo = Link( "optics", "Color" ); opacity = Link( "opticalAlpha" );
	}
	Add( new Editor.ShaderGraph.Result { Albedo = albedo, Opacity = opacity }, "material", 1600, 0 );
	var path = $"environment/shaders/vb_rain_{kind}";
	graph.Path = path + ".shdrgrph";
	var compiler = new Editor.ShaderGraph.GraphCompiler( graph, false );
	var shader = compiler.Generate();
	if ( shader is null ) throw new Exception( Json.Serialize( compiler.Errors.Select( x => x.Message ) ) );
	shader = string.Join( "\n", shader.Replace( "\r\n", "\n" ).Split( '\n' ).Select( line => line.TrimEnd() ) ).TrimEnd() + "\n";
	System.IO.Directory.CreateDirectory( System.IO.Path.Combine( assets, "environment/shaders" ) );
	System.IO.File.WriteAllText( System.IO.Path.Combine( assets, path + ".shdrgrph" ), graph.Serialize() );
	System.IO.File.WriteAllText( System.IO.Path.Combine( assets, path + ".shader" ), shader );
	System.IO.File.WriteAllText( System.IO.Path.Combine( assets, $"environment/materials/rain_{kind}.vmat" ), $"Layer0\n{{\n shader \"{path}.shader\"\n F_RENDER_BACKFACES \"1\"\n}}\n" );
	var result = await Editor.EditorUtility.CompileShader( path + ".shader", new Sandbox.Engine.Shaders.ShaderCompileOptions { ForceRecompile = true }, cancellationToken );
	if ( !result.Success ) throw new Exception( Json.Serialize( result.Programs.Select( p => new { p.Name, p.Success, p.Output } ) ) );
	outputs.Add( path );
}
return outputs;
