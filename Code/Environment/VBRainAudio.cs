using System;
using System.Threading.Tasks;
using Sandbox.Audio;

namespace VoidBreach.Environment;

/// <summary>Two local ambience voices, driven by shared weather and bounded shelter probes.</summary>
[Title( "VB Rain Audio" ), Category( "Void Breach/Environment" ), Icon( "spatial_audio" )]
public sealed partial class VBRainAudio : Component
{
	[Property] public VBCloudWeather Weather { get; set; }
	[Property] public SoundEvent OutdoorSound { get; set; }
	[Property] public SoundEvent ShelteredSound { get; set; }
	[Property, Description( "Optional listening point; otherwise follows the scene camera, like native audio." )]
	public AudioListener Listener { get; set; }
	[Property] public MixerHandle Mixer { get; set; }
	[Property, Range( 0, 2 )] public float Volume { get; set; } = 1;
	[Property, Range( 0.1f, 3 )] public float TransitionSeconds { get; set; } = 0.75f;
	[Property, Group( "Shelter" ), Range( 128, 4096 )] public float HearingDistance { get; set; } = 1200;
	[Property, Group( "Shelter" ), Range( 4, 128 )] public float MaximumThinRoofThickness { get; set; } = 32;
	[Property, Group( "Shelter" ), Range( 0, 1 )] public float ThinRoofVolume { get; set; } = 0.7f;
	[Property, Group( "Shelter" ), Range( 0.05f, 0.5f )] public float ProbeInterval { get; set; } = 0.125f;
	[Property, Group( "Shelter" ), Description( "Same nonblocking tags as precipitation; roofs and walls require colliders." )]
	public string[] IgnoreTags { get; set; } = new[] { "player", "npc", "debris", "trigger", "rain_passthrough" };
	[Property, Group( "Acoustics" )] public bool IndoorReverb { get; set; } = true;
	[Property, Group( "Acoustics" ), Range( 0, 1 )] public float IndoorSpatialBlend { get; set; } = 0.35f;

	public float OutdoorGain { get; private set; }
	public float ShelteredGain { get; private set; }
	public int LastTraceCount { get; private set; }
	public int ActiveVoices => (_outdoor.IsValid() && !_outdoor.Paused ? 1 : 0) + (_sheltered.IsValid() && !_sheltered.Paused ? 1 : 0);
	public bool ExposedToSky { get; private set; }
	public float ShelterAudibility { get; private set; }
	public Vector3 ShelterSoundPosition { get; private set; }

	private SoundHandle _outdoor, _sheltered;
	private SoundEvent _playingOutdoor, _playingSheltered;
	private float _outdoorBaseVolume, _shelteredBaseVolume;
	private float _probeTimer, _exposure, _audibility, _intensity;
	private bool _hasProbe;

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor || Application.IsDedicatedServer || Application.IsHeadless ) return;
		foreach ( var sound in new[] { OutdoorSound, ShelteredSound } )
			if ( sound?.Sounds is not null )
				foreach ( var file in sound.Sounds )
					if ( file is not null ) await file.LoadAsync();
	}

	protected override void OnUpdate()
	{
		LastTraceCount = 0;
		// No editor preview playback, server voices, or per-player replication.
		if ( Scene.IsEditor || Application.IsDedicatedServer || Application.IsHeadless ) { Release(); return; }
		if ( !Weather.IsValid() ) Weather = GetComponent<VBCloudWeather>();
		var camera = Scene.Camera;
		if ( !Weather.IsValid() || !Weather.Active || !camera.IsValid() || !camera.Active || Volume <= 0 ) { Release(); return; }
		var ear = Listener.IsValid() && Listener.Active ? Listener.WorldTransform : camera.WorldTransform;
		if ( Listener.IsValid() && Listener.Active && Listener.UseCameraDirection ) ear.Rotation = camera.WorldRotation;
		var rain = ear.Position.z < VBCloudWeather.Finite( Weather.Altitude, 16000, 1000, 80000 ) ? Weather.Precipitation : 0;
		if ( rain <= 0.001f && _intensity < 0.001f ) { Release(); return; }
		var dt = Math.Clamp( Time.Delta, 0, 0.1f );
		_probeTimer -= dt;
		if ( rain > 0.001f && (_probeTimer <= 0 || !_hasProbe) )
		{
			ProbeEnvironment( ear );
			_probeTimer = VBCloudWeather.Finite( ProbeInterval, 0.125f, 0.05f, 0.5f );
		}
		var blend = 1 - MathF.Exp( -4 * dt / VBCloudWeather.Finite( TransitionSeconds, 0.75f, 0.1f, 3 ) );
		_exposure = MathX.Lerp( _exposure, ExposedToSky ? 1 : 0, blend );
		_audibility = MathX.Lerp( _audibility, ShelterAudibility, blend );
		_intensity = MathX.Lerp( _intensity, MathF.Sqrt( Math.Clamp( rain / 8, 0, 1 ) ), blend );
		var volume = VBCloudWeather.Finite( Volume, 1, 0, 2 ) * _intensity;
		OutdoorGain = volume * MathF.Sqrt( _exposure );
		ShelteredGain = volume * MathF.Sqrt( 1 - _exposure ) * _audibility;
		UpdateLoop( ref _outdoor, ref _playingOutdoor, ref _outdoorBaseVolume, OutdoorSound, ear.Position, OutdoorGain, false );
		UpdateLoop( ref _sheltered, ref _playingSheltered, ref _shelteredBaseVolume, ShelteredSound, ShelterSoundPosition, ShelteredGain, true );
	}

	private void UpdateLoop( ref SoundHandle handle, ref SoundEvent playing, ref float baseVolume, SoundEvent sound, Vector3 position, float gain, bool sheltered )
	{
		if ( playing != sound ) { handle?.Stop(); handle = null; playing = sound; }
		if ( sound is null ) return;
		if ( gain < 0.001f ) { if ( handle.IsValid() ) handle.Paused = true; return; }
		if ( !handle.IsValid() )
		{
			handle = Sound.Play( sound, position, fadeInTime: 0.1f );
			baseVolume = handle.IsValid() ? handle.Volume : 1;
		}
		if ( !handle.IsValid() ) return;
		handle.Paused = false;
		handle.Position = position;
		handle.Volume = baseVolume * gain;
		handle.TargetMixer = Mixer.Get( sound.DefaultMixer.GetOrDefault() );
		handle.SpacialBlend = sheltered ? VBCloudWeather.Finite( IndoorSpatialBlend, 0.35f, 0, 1 ) : 0;
		// Range falloff is evaluated once by our cached source selection. Native
		// propagation still handles material transmission, diffraction and room reverb.
		handle.DistanceAttenuation = false;
		handle.OcclusionEnabled = sheltered;
		handle.AirAbsorption = sheltered;
		handle.ReverbEnabled = sheltered && IndoorReverb;
	}

	private void Release()
	{
		_outdoor?.Stop(); _sheltered?.Stop(); _outdoor = null; _sheltered = null;
		_playingOutdoor = null; _playingSheltered = null;
		_probeTimer = 0; _exposure = 0; _audibility = 0; _intensity = 0;
		OutdoorGain = 0; ShelteredGain = 0; ShelterAudibility = 0; ExposedToSky = false;
		LastTraceCount = 0;
		_hasProbe = false; _probeIndex = 0;
		if ( _openings is not null ) Array.Clear( _openings );
	}
	protected override void OnDisabled() => Release();
	protected override void OnDestroy() => Release();
}
