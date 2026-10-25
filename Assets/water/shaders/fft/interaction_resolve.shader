HEADER
{
	Description = "Resolve interactive height, spatial derivatives and foam";
}
MODES
{
	Default();
}
CS
{
	#include "system.fxc"
	Texture2D<float4> StateIn < Attribute("StateIn"); >;
	RWTexture2D<float4> SurfaceOut < Attribute("SurfaceOut"); >;
	int GridResolution < Attribute("GridResolution"); >;
	float CellSize < Attribute("CellSize"); >;
	float Height(int2 p) { return StateIn.Load(int3(clamp(p,0,GridResolution-1),0)).x; }
	[numthreads(8,8,1)]
	void MainCs(uint3 id : SV_DispatchThreadID)
	{
		if(any(id.xy>=GridResolution)) return;
		int2 p=id.xy;
		float4 s=StateIn.Load(int3(p,0));
		SurfaceOut[id.xy]=float4(s.x,(Height(p+int2(1,0))-Height(p-int2(1,0)))/(2*CellSize),
			(Height(p+int2(0,1))-Height(p-int2(0,1)))/(2*CellSize),s.z);
	}
}
