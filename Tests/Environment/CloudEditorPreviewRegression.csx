// Isolated lifecycle regression for scenes whose main camera only exists in Play.
var scene = Scene.CreateEditorScene();
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string message )
{
	if ( !pass ) throw new Exception( message );
	checks.Add( message );
}
try
{
	using var scope = scene.Push();
	var fixture = GameObject.Clone( PrefabFile.Load( "environment/prefabs/volumetric_clouds.prefab" ) );
	var clouds = fixture.GetComponent<VoidBreach.Environment.VBVolumetricClouds>();
	void Tick()
	{
		for ( int i = 0; i < 3; i++ ) scene.EditorTick( 0, 0.016f );
	}
	CameraComponent Preview() => scene.GetAllComponents<CameraComponent>()
		.FirstOrDefault( x => x.GameObject.Name == "Cloud editor preview camera (temporary)" );
	Tick();
	var preview = Preview();
	Check( preview.IsValid() && scene.Camera == preview, "Camera-less editor scene obtains a preview source" );
	Check( preview.EnablePostProcessing && !preview.IsMainCamera && preview.Priority == int.MaxValue, "Preview enables effects as a fallback camera" );
	Check( preview.GameObject.NetworkMode == NetworkMode.Never && preview.GameObject.Flags.HasFlag( GameObjectFlags.NotNetworked ), "Preview cannot become a network camera" );
	Check( !scene.Serialize().ToJsonString().Contains( "Cloud editor preview camera" ), "Preview is excluded from scene serialization and Play copies" );
	clouds.PreviewInEditor = false;
	Tick();
	Check( !Preview().IsValid(), "Disabling editor preview removes its camera" );
	clouds.PreviewInEditor = true;
	Tick();
	Check( Preview().IsValid(), "Re-enabling editor preview recreates its camera" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Off;
	Tick();
	Check( !Preview().IsValid(), "Quality Off removes the temporary camera" );
	clouds.Quality = VoidBreach.Environment.VBCloudQuality.Medium;
	Tick();
	clouds.Enabled = false;
	Tick();
	Check( !Preview().IsValid(), "Disabling the cloud component removes the temporary camera" );
	clouds.Enabled = true;
	Tick();
	var authored = new GameObject( scene, false, "Authored main camera" );
	var camera = authored.AddComponent<CameraComponent>();
	camera.IsMainCamera = true;
	camera.Priority = 0;
	authored.Enabled = true;
	Tick();
	Check( scene.Camera == camera && !Preview().IsValid(), "An authored main camera takes over without a duplicate preview source" );
	authored.Destroy();
	Tick();
	Check( Preview().IsValid(), "Removing the authored camera restores the editor preview" );
	fixture.Destroy();
	Tick();
	Check( !Preview().IsValid(), "Destroying the cloud prefab cleans up its temporary camera" );
	return new { passed = checks.Count, checks };
}
finally
{
	scene.Destroy();
}
