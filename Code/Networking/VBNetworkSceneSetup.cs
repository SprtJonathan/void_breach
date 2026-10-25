using Sandbox;

/// <summary>Rejects missing editor references before native player spawning.</summary>
public sealed class VBNetworkSceneSetup : GameObjectSystem<VBNetworkSceneSetup>, ISceneLoadingEvents
{
	public VBNetworkSceneSetup( Scene scene ) : base( scene ) { }

	void ISceneLoadingEvents.AfterLoad( Scene scene )
	{
		if ( scene.IsEditor || !Networking.IsHost ) return;
		foreach ( var helper in scene.GetAllComponents<NetworkHelper>() )
		{
			int removed = helper.SpawnPoints?.RemoveAll( point => !point.IsValid() ) ?? 0;
			if ( removed > 0 )
				Log.Warning( $"{helper.GameObject.Name}: removed {removed} missing spawn reference(s); native SpawnPoint fallback is available." );
		}
	}
}
