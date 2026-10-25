using System;
using System.Collections.Generic;

namespace VoidBreach.Water;

/// <summary>Keeps detached GPU resources alive past native end-of-frame scene deletion.</summary>
internal sealed class FluidGpuResources
{
	private readonly Texture[] _textures;
	private readonly GpuBuffer[] _buffers;
	private readonly ComputeShader[] _shaders;
	private readonly ulong _detachedFrame;
	private static Queue<FluidGpuResources> _pending = new();
	public static int PendingBatches => _pending?.Count ?? 0;
	public static int ReleasedBatches { get; private set; }
	public static int FailedBatches { get; private set; }

	private FluidGpuResources( Texture[] textures, GpuBuffer[] buffers, ComputeShader[] shaders )
	{
		_textures = textures; _buffers = buffers; _shaders = shaders;
		_detachedFrame = Application.FrameCount;
	}

	public static void Retire( Texture[] textures, GpuBuffer[] buffers, ComputeShader[] shaders )
	{
		// No task/continuation belongs to the outgoing Play context. Editor scenes
		// and the next scene can drain the same queue after that context has stopped.
		_pending ??= new();
		_pending.Enqueue( new FluidGpuResources( textures, buffers, shaders ) );
	}

	public static void Collect()
	{
		if ( _pending is null || _pending.Count == 0 || Application.FrameCount - _pending.Peek()._detachedFrame < 3 ) return;
		// SceneObject.Delete is deferred. One fence for all eligible batches, after
		// native frame-end deletion, instead of a fence for every texture/clipmap.
		Graphics.FlushGPU();
		while ( _pending.Count > 0 && Application.FrameCount - _pending.Peek()._detachedFrame >= 3 )
		{
			var batch = _pending.Dequeue();
			try
			{
				foreach ( var shader in batch._shaders ) shader?.Attributes.Clear();
				foreach ( var texture in batch._textures ) texture?.Dispose();
				foreach ( var buffer in batch._buffers ) buffer?.Dispose();
				ReleasedBatches++;
			}
			catch ( Exception error )
			{
				FailedBatches++;
				Log.Error( error, "Water GPU resource retirement failed" );
			}
		}
	}
}

/// <summary>Scene-wide resource service; continues in editor scenes after Play teardown.</summary>
public sealed class FluidGpuResourceSystem : GameObjectSystem<FluidGpuResourceSystem>
{
	public FluidGpuResourceSystem( Scene scene ) : base( scene )
		=> Listen( Stage.StartUpdate, -1000, FluidGpuResources.Collect, "Retire detached water GPU resources" );
}
