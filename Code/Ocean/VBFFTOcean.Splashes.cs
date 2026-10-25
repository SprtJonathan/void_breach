using System;
using VoidBreach.Water;

namespace VoidBreach.Ocean;

public sealed partial class VBFFTOcean
{
	private GameObject _splashObject;
	private ParticleEffect _spray;
	private Texture _dropTexture;
	private int _splashSeed;
	public int SplashParticleCount => _spray.IsValid() ? _spray.ParticleCount : 0;
	public int ShotImpactCount { get; private set; }
	public int CharacterImpactCount { get; private set; }

	/// <summary>Common presentation entry point. Fields and spray have independent, bounded budgets.</summary>
	public bool EmitSurfaceImpact( Vector3 position, float radius, float strength,
		FluidImpactKind kind = FluidImpactKind.Custom, Vector3 velocity = default )
	{
		if ( !float.IsFinite( velocity.x + velocity.y + velocity.z ) ) return false;
		if ( !ApplySurfaceImpulse( position, radius, strength, kind == FluidImpactKind.Wake ? new Vector2( velocity.x, velocity.y ) : Vector2.Zero ) ) return false;
		if ( kind == FluidImpactKind.Shot ) ShotImpactCount++;
		if ( kind is FluidImpactKind.Entry or FluidImpactKind.Exit ) CharacterImpactCount++;
		if ( kind != FluidImpactKind.Wake ) EmitSpray( position, radius, strength, velocity );
		return true;
	}

	private void EmitSpray( Vector3 position, float radius, float strength, Vector3 velocity )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !float.IsFinite( velocity.x + velocity.y + velocity.z ) ) return;
		if ( Scene.Camera.IsValid() && (Scene.Camera.WorldPosition - position).LengthSquared > 12000 * 12000 ) return;
		if ( !_splashObject.IsValid() )
		{
			_splashObject = new GameObject( GameObject, false, "Water spray (pooled)" );
			_splashObject.WorldTransform = new Transform( Vector3.Zero, Rotation.Identity, Vector3.One );
			_splashObject.Flags = GameObjectFlags.NotSaved; _splashObject.NetworkMode = NetworkMode.Never;
			_spray = _splashObject.AddComponent<ParticleEffect>();
			_spray.MaxParticles = 256; _spray.Lifetime = new ParticleFloat( 0.3f, 0.65f );
			_spray.Force = true; _spray.ForceDirection = Vector3.Down; _spray.ForceScale = 420;
			_spray.Damping = 0.6f; _spray.Collision = false;
			_spray.ApplyAlpha = false; _spray.ApplyColor = false; _spray.ApplyShape = false;
			_spray.OnStep = UpdateSprayAlpha;
			var pixels = new byte[32 * 32 * 4];
			for ( var y = 0; y < 32; y++ )
			for ( var x = 0; x < 32; x++ )
			{
				var dx = (x + 0.5f - 16) / 16; var dy = (y + 0.5f - 16) / 16;
				var i = (y * 32 + x) * 4;
				pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
				pixels[i + 3] = (byte)(MathF.Pow( Math.Clamp( 1 - dx * dx - dy * dy, 0, 1 ), 2 ) * 255);
			}
			_dropTexture = Texture.Create( 32, 32, ImageFormat.RGBA8888 )
				.WithName( $"vb_water_spray_{Guid.NewGuid():N}" ).WithAnonymous( false ).WithData( pixels ).Finish();
			var renderer = _splashObject.AddComponent<ParticleSpriteRenderer>();
			renderer.Sprite = Sprite.FromTexture( _dropTexture ); renderer.Lighting = true;
			renderer.Shadows = false; renderer.DepthFeather = 4; renderer.FaceVelocity = true;
			_splashObject.Enabled = true;
		}
		var count = Math.Min( Math.Clamp( (int)(MathF.Abs( strength ) * 0.15f), 6, 22 ), _spray.MaxParticles - _spray.ParticleCount );
		var random = new Random( ++_splashSeed * 7919 );
		for ( var i = 0; i < count; i++ )
		{
			var angle = random.NextSingle() * 2 * MathF.PI;
			var (sn, cs) = MathF.SinCos( angle ); var direction = new Vector3( cs, sn, 0 );
			var p = _spray.Emit( position + Vector3.Up * 2 + direction * radius * random.NextSingle() * 0.25f, i / (float)Math.Max( count, 1 ) );
			p.Velocity = direction * (20 + random.NextSingle() * radius * 2)
				+ Vector3.Up * (35 + MathF.Abs( strength ) * (0.5f + random.NextSingle())) + velocity.WithZ( 0 ) * 0.12f;
			var size = 1.5f + random.NextSingle() * Math.Clamp( radius * 0.12f, 1, 5 );
			p.Size = new Vector3( size, size * 1.8f, size ); p.Color = new Color( 0.8f, 0.91f, 0.94f ); p.Alpha = 0.8f;
		}
	}
	private static void UpdateSprayAlpha( Particle particle, float delta ) => particle.Alpha = Math.Clamp( particle.LifeTimeRemaining * 5, 0, 0.85f );
	private void ReleaseSplashes()
	{
		if ( _spray.IsValid() ) _spray.OnStep = null;
		if ( _splashObject.IsValid() ) { _splashObject.Enabled = false; _splashObject.Destroy(); }
		if ( _dropTexture is not null ) FluidGpuResources.Retire( new[] { _dropTexture }, Array.Empty<GpuBuffer>(), Array.Empty<ComputeShader>() );
		_splashObject = null; _spray = null; _dropTexture = null;
	}
}
