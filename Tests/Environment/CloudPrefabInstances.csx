// Validate saved cloud prefab patches, not just fresh clones of the source asset.
var root = Project.Current.GetRootPath();
var prefab = PrefabFile.Load( "environment/prefabs/volumetric_clouds.prefab" );
var rootId = prefab.RootObject["__guid"].ToString();
var checkedInstances = new System.Collections.Generic.List<object>();
var scene = Scene.CreateEditorScene();
try
{
	using var scope = scene.Push();
	void CheckNode( System.Text.Json.Nodes.JsonNode node, string path )
	{
		if ( node is System.Text.Json.Nodes.JsonArray array )
		{
			foreach ( var item in array ) CheckNode( item, path );
			return;
		}
		if ( node is not System.Text.Json.Nodes.JsonObject obj ) return;
		if ( obj["__Prefab"]?["Path"]?.GetValue<string>() == "environment/prefabs/volumetric_clouds.prefab" )
		{
			if ( obj["__PrefabIdToInstanceId"]?[rootId] is null )
				throw new Exception( $"{path}: cloud prefab instance uses an obsolete root identity." );
			var go = new GameObject( scene, false, "Saved cloud prefab verification" );
			try
			{
				go.Deserialize( obj.DeepClone().AsObject() );
				var weather = go.GetComponent<VoidBreach.Environment.VBCloudWeather>();
				var clouds = go.GetComponent<VoidBreach.Environment.VBVolumetricClouds>();
				if ( go.GetComponents<VoidBreach.Environment.VBCloudWeather>().Count() != 1
					|| go.GetComponents<VoidBreach.Environment.VBVolumetricClouds>().Count() != 1
					|| go.GetComponents<PostProcessVolume>().Count() != 1 || clouds.Weather != weather )
					throw new Exception( $"{path}: saved cloud prefab loses or duplicates components on load." );
				checkedInstances.Add( new { path, weather.Altitude, weather.Thickness, weather.Erosion, weather.ShapeScale, clouds.MaximumDistance } );
			}
			finally { go.Destroy(); }
			return;
		}
		foreach ( var pair in obj ) CheckNode( pair.Value, path );
	}
	foreach ( var path in System.IO.Directory.EnumerateFiles( System.IO.Path.Combine( root, "Assets" ), "*.scene", System.IO.SearchOption.AllDirectories ) )
	{
		cancellationToken.ThrowIfCancellationRequested();
		CheckNode( System.Text.Json.Nodes.JsonNode.Parse( System.IO.File.ReadAllText( path ) ), path );
	}
	return new { passed = true, checkedInstances };
}
finally { scene.Destroy(); }
