var results = new System.Collections.Generic.List<object>();
foreach ( var name in new[] { "march", "composite", "shadows" } )
{
	cancellationToken.ThrowIfCancellationRequested();
	var result = await Editor.EditorUtility.CompileShader( $"environment/shaders/vb_cloud_{name}.shader",
		new Sandbox.Engine.Shaders.ShaderCompileOptions { ForceRecompile = true }, cancellationToken );
	results.Add( new { name, result.Success, programs = result.Programs.Select( p => new { p.Name, p.Success, p.Output } ) } );
	if ( !result.Success ) throw new Exception( Json.Serialize( results ) );
}
return results;
