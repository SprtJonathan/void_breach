using Sandbox;

/// <summary>Applies the latched slide stance before native physics sizes the body.</summary>
public sealed class VBSlideStanceSystem : GameObjectSystem<VBSlideStanceSystem>
{
	public VBSlideStanceSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.PhysicsStep, -100, UpdateStances, "Slide stance" );
	}

	private void UpdateStances()
	{
		if ( Scene.IsEditor ) return;
		foreach ( var movement in Scene.GetAllComponents<VBMoveModeWalk>() )
			if ( !movement.IsProxy && movement.Enabled && movement.IsSliding ) movement.UpdateSlideStance();
	}
}
