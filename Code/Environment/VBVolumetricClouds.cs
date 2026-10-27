using System;
using Sandbox.Rendering;

namespace VoidBreach.Environment;

public enum VBCloudQuality { Off, Low, Medium, High, Ultra }

/// <summary>HDR cloud layer, rendered before transparencies and viewmodels, with optional approximate ground shadows.</summary>
[Title( "VB Volumetric Clouds" ), Category( "Void Breach/Environment" ), Icon( "cloud_queue" )]
public sealed class VBVolumetricClouds : BasePostProcess<VBVolumetricClouds>
{
	private static ProfilingSampler MarchProfile = new( "VB Clouds / Ray march" );
	private static ProfilingSampler ShadowProfile = new( "VB Clouds / Ground shadows" );
	private static ProfilingSampler CompositeProfile = new( "VB Clouds / Composite" );

	[Property] public VBCloudWeather Weather { get; set; }
	[Property, Group( "Preview" ), Description( "Show clouds in scene views, including scenes whose player camera is created only in Play." )]
	public bool PreviewInEditor { get; set; } = true;
	[Property, Description( "Client rendering budget; weather density and cloud placement are independent of quality." )]
	public VBCloudQuality Quality { get; set; } = VBCloudQuality.Medium;
	[Property, Range( 10000, 300000 )] public float MaximumDistance { get; set; } = 160000;
	[Property, Group( "Ground Shadows" ), Description( "Approximate screen-space attenuation of opaque surfaces. Does not replace the sun's shadow maps." )]
	public bool GroundShadows { get; set; } = true;
	[Property, Group( "Ground Shadows" ), Range( 0, 1 )] public float ShadowStrength { get; set; } = 0.45f;
	[Property, Group( "Ground Shadows" ), Range( 1000, 100000 )] public float ShadowDistance { get; set; } = 36000;

	public int RenderCount { get; private set; }
	public int LastStepCount { get; private set; }
	public int LastResolutionDivisor { get; private set; }
	private GameObject _editorPreviewObject;
	private CameraComponent _editorPreviewCamera;

	protected override void OnUpdate()
	{
		// S&box's editor viewport copies post-process layers from Scene.Camera.
		// Scenes with a runtime-only player camera otherwise stop building effects
		// after Play. Supply an edit-only source; the viewport keeps its own view.
		if ( !Scene.IsEditor || !PreviewInEditor || Quality == VBCloudQuality.Off )
		{
			ReleaseEditorPreview();
			return;
		}
		if ( Scene.Camera.IsValid() && Scene.Camera != _editorPreviewCamera )
		{
			ReleaseEditorPreview();
			return;
		}
		if ( _editorPreviewCamera.IsValid() ) return;
		_editorPreviewObject = new GameObject( Scene, false, "Cloud editor preview camera (temporary)" );
		_editorPreviewObject.Flags = GameObjectFlags.Hidden | GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_editorPreviewObject.NetworkMode = NetworkMode.Never;
		_editorPreviewCamera = _editorPreviewObject.AddComponent<CameraComponent>();
		// Scene.Camera selects main cameras first, then ascending priority.
		// This fallback must yield to authored cameras, including non-main ones.
		_editorPreviewCamera.IsMainCamera = false;
		_editorPreviewCamera.Priority = int.MaxValue;
		_editorPreviewCamera.EnablePostProcessing = true;
		_editorPreviewCamera.ZFar = 300000;
		_editorPreviewObject.Enabled = true;
	}

	private void ReleaseEditorPreview()
	{
		if ( _editorPreviewObject.IsValid() ) _editorPreviewObject.Destroy();
		_editorPreviewObject = null;
		_editorPreviewCamera = null;
	}

	protected override void OnDisabled() => ReleaseEditorPreview();
	protected override void OnDestroy() => ReleaseEditorPreview();
	public static (int Divisor, int Steps, int LightSteps) Budget( VBCloudQuality quality ) => quality switch
	{
		VBCloudQuality.Low => (4, 24, 3),
		VBCloudQuality.Medium => (2, 48, 8),
		VBCloudQuality.High => (2, 72, 10),
		VBCloudQuality.Ultra => (1, 96, 12),
		_ => (1, 0, 0)
	};

	public override void Render()
	{
		if ( Scene.IsEditor && !PreviewInEditor ) return;
		var budget = Budget( Quality );
		if ( budget.Steps == 0 || Camera.Orthographic ) return;
		var weather = Weather.IsValid() && Weather.Active ? Weather : Scene.GetAllComponents<VBCloudWeather>().FirstOrDefault( x => x.Active );
		if ( !weather.IsValid() || weather.Coverage <= 0 || weather.Density <= 0 ) return;

		// A new list owns each view's attribute snapshot and temporary targets. No
		// persistent render targets, history textures or GPU readback on the game path.
		var commands = new CommandList( "Void Breach Volumetric Clouds" );
		weather.WriteAttributes( commands );
		commands.Attributes.Set( "CloudNoiseAtlas", Texture.Load( "environment/textures/cloud_noise.tga" ) );
		commands.Attributes.Set( "CloudWeatherMap", Texture.Load( "environment/textures/cloud_weather.tga" ) );
		commands.Attributes.Set( "CloudSteps", budget.Steps );
		commands.Attributes.Set( "CloudLightSteps", budget.LightSteps );
		commands.Attributes.Set( "CloudMaximumDistance", VBCloudWeather.Finite( MaximumDistance, 160000, 10000, 300000 ) );
		commands.Attributes.Set( "CloudShadowStrength", GroundShadows ? VBCloudWeather.Finite( ShadowStrength, 0.45f, 0, 1 ) : 0 );
		commands.Attributes.Set( "CloudShadowDistance", VBCloudWeather.Finite( ShadowDistance, 36000, 1000, 100000 ) );
		var layer = commands.GetRenderTarget( "VBCloudLayer", ImageFormat.RGBA16161616F, sizeFactor: budget.Divisor );
		commands.SetRenderTarget( layer );
		using ( commands.ProfileScope( MarchProfile ) )
			commands.Blit( Material.Load( "environment/materials/cloud_march.vmat" ) );
		commands.ClearRenderTarget();
		if ( GroundShadows && ShadowStrength > 0 )
		{
			using ( commands.ProfileScope( ShadowProfile ) )
				commands.Blit( Material.Load( "environment/materials/cloud_shadows.vmat" ) );
		}
		commands.Attributes.Set( "CloudLayerTexture", layer.ColorTexture );
		using ( commands.ProfileScope( CompositeProfile ) )
			commands.Blit( Material.Load( "environment/materials/cloud_composite.vmat" ) );
		commands.ReleaseRenderTarget( layer );
		InsertCommandList( commands, Stage.AfterSkybox, -50, "Void Breach Volumetric Clouds" );
		LastStepCount = budget.Steps;
		LastResolutionDivisor = budget.Divisor;
		RenderCount++;
	}
}
