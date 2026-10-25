using Sandbox;

/// <summary>Animated bones are traceable, but must not shove props as infinite-mass bodies.</summary>
[Title( "Animated Body Contacts" ), Category( "Void Breach/Physics" )]
public sealed class VBAnimatedBodyContacts : Component, IScenePhysicsEvents
{
	private readonly Dictionary<PhysicsBody, bool> _defaults = new();
	void IScenePhysicsEvents.PrePhysicsStep()
	{
		bool seated = GetComponent<VBVehiclePlayerDriver>()?.ActiveSeat.IsValid() == true;
		foreach ( var model in GetComponentsInChildren<ModelPhysics>( includeDisabled: true, includeSelf: true ) )
		{
			foreach ( var bone in model.Bodies )
			{
				var body = bone.Component.IsValid() ? bone.Component.PhysicsBody : null;
				if ( !body.IsValid() ) continue;
				if ( !_defaults.ContainsKey( body ) ) _defaults.Add( body, body.EnableSolidCollisions );
				body.EnableSolidCollisions = !seated && model.MotionEnabled && _defaults[body];
			}
		}
	}
	protected override void OnDisabled()
	{
		foreach ( var pair in _defaults )
			if ( pair.Key.IsValid() ) pair.Key.EnableSolidCollisions = pair.Value;
		_defaults.Clear();
	}
}
