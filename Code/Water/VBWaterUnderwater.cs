using System;
using Sandbox.Rendering;

namespace VoidBreach.Water;

/// <summary>Camera post-process driven by the sampled water surface at the camera position.</summary>
[Title( "VB Water Underwater" ), Category( "Void Breach/Water" ), Icon( "scuba_diving" )]
public sealed class VBWaterUnderwater : BasePostProcess<VBWaterUnderwater>
{
	[Property, Group( "Color" )] public Color Tint { get; set; } = new( 0.035f, 0.16f, 0.13f, 1 );
	[Property, Group( "Color" ), Range( 32, 4096 )] public float Visibility { get; set; } = 900;
	[Property, Group( "Color" ), Range( 0.5f, 1.5f )] public float Contrast { get; set; } = 0.9f;
	[Property, Group( "Distortion" ), Range( 0, 0.02f )] public float Distortion { get; set; } = 0.0022f;
	[Property, Group( "Distortion" ), Range( 0.2f, 4 )] public float DistortionSpeed { get; set; } = 0.75f;
	[Property, Group( "Caustics" ), Range( 0, 2 )] public float CausticStrength { get; set; } = 0.18f;
	[Property, Group( "Transition" ), Range( 1, 64 )] public float TransitionDepth { get; set; } = 12;

	private float _amount;
	public float SubmergedAmount => _amount;
	public Vector3 LastCameraPosition { get; private set; }
	public int RenderCount { get; private set; }
	private float _surfaceHeight;
	private float _bottomHeight;
	private float _waterClarity = 140;
	private float _causticScale = 92;
	private float _causticSpeed = 0.7f;
	private Color _waterColor = new( 0.018f, 0.064f, 0.05f, 1 );

	private void SampleCamera( Vector3 position )
	{
		var target = 0f;
		if ( WaterSurface.Find( Scene, position, out var sample )
			&& position.z < sample.Height && position.z > sample.Bottom )
		{
			var depth = sample.Height - position.z;
			target = Math.Clamp( depth / MathF.Max( TransitionDepth, 1 ), 0, 1 );
			_surfaceHeight = sample.Height;
			_bottomHeight = sample.Bottom;
			if ( sample.Surface is VBWaterBody body )
			{
				_waterClarity = body.Clarity;
				_waterColor = body.DeepColor;
				_causticScale = body.CausticScale;
				_causticSpeed = body.CausticSpeed;
			}
		}
		// Spatial smoothing is independent of framerate and of the previous rendered
		// camera. A global volume can render the editor and game cameras in one frame.
		_amount = target * target * (3 - 2 * target);
	}

	public override void Render()
	{
		RenderCount++;
		LastCameraPosition = Camera.WorldPosition;
		SampleCamera( Camera.WorldPosition );
		var amount = _amount;
		if ( amount <= 0.001f ) return;
		Attributes.Set( "UnderwaterAmount", amount );
		Attributes.Set( "UnderwaterTint", Tint );
		Attributes.Set( "UnderwaterWaterColor", _waterColor );
		Attributes.Set( "UnderwaterVisibility", MathF.Max( Visibility, _waterClarity ) );
		Attributes.Set( "UnderwaterContrast", Contrast );
		Attributes.Set( "UnderwaterDistortion", Distortion );
		Attributes.Set( "UnderwaterDistortionSpeed", DistortionSpeed );
		Attributes.Set( "UnderwaterSurfaceHeight", _surfaceHeight );
		Attributes.Set( "UnderwaterBottomHeight", _bottomHeight );
		Attributes.Set( "UnderwaterCausticStrength", CausticStrength );
		Attributes.Set( "UnderwaterCausticScale", _causticScale );
		Attributes.Set( "UnderwaterCausticSpeed", _causticSpeed );
		Attributes.Set( "UnderwaterTime", Time.Now );
		var material = Material.FromShader( "water/shaders/vb_underwater.shader" );
		Blit( BlitMode.WithBackbuffer( material, Stage.AfterPostProcess, 180, false ), "Void Breach Underwater" );
	}
}
