using Sandbox;

public enum VBAiSoundKind
{
	Generic,
	Gunshot,
	Explosion,
	Impact,
	Voice
}

/// <summary>
/// Host-local gameplay stimulus. Audio playback and AI hearing intentionally
/// stay separate: a sound can be audible to players without alerting AI and
/// vice versa.
/// </summary>
public readonly struct VBAiSoundStimulus
{
	public Vector3 Position { get; }
	public float Radius { get; }
	public GameObject Source { get; }
	public VBAiSoundKind Kind { get; }

	public VBAiSoundStimulus( Vector3 position, float radius, GameObject source, VBAiSoundKind kind )
	{
		Position = position;
		Radius = radius;
		Source = source;
		Kind = kind;
	}

	public static void Emit( Vector3 position, float radius, GameObject source = null, VBAiSoundKind kind = VBAiSoundKind.Generic )
	{
		if ( !Networking.IsHost || radius <= 0f )
			return;

		var stimulus = new VBAiSoundStimulus( position, radius, source, kind );
		IVBAiSoundListener.Post( listener => listener.OnSoundStimulus( stimulus ) );
	}
}

public interface IVBAiSoundListener : ISceneEvent<IVBAiSoundListener>
{
	void OnSoundStimulus( VBAiSoundStimulus stimulus ) { }
}
