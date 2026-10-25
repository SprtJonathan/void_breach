using Sandbox;

/// <summary>Optional test-scene setup; normal player prefabs remain vulnerable.</summary>
[Title( "Vehicle Test Environment" ), Category( "Void Breach/Debug" ), Icon( "science" )]
public sealed class VBVehicleTestEnvironment : Component
{
	[Property] public bool PlayerGodMode { get; set; } = true;
	private readonly HashSet<VBHealthComponent> _initialized = new();

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !PlayerGodMode ) return;
		_initialized.RemoveWhere( x => !x.IsValid() );
		foreach ( var player in Scene.GetAll<PlayerController>() )
		{
			var health = player.GetComponent<VBHealthComponent>();
			if ( !health.IsValid() || !_initialized.Add( health ) ) continue;
			health.GodMode = true;
			health.ResetHealth();
		}
	}
}
