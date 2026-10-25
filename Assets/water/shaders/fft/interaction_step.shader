HEADER
{
	Description = "Local damped wave heightfield: advection, swept directional sources and shoreline mask";
}
MODES
{
	Default();
}
CS
{
	#include "system.fxc"
	Texture2D<float4> StateIn < Attribute("StateIn"); >;
	Texture2D<float4> DomainMap < Attribute("DomainMap"); >;
	RWTexture2D<float4> StateOut < Attribute("StateOut"); >;
	StructuredBuffer<float4> Sources < Attribute("Sources"); >;
	SamplerState LinearClamp < Filter(MIN_MAG_MIP_LINEAR); AddressU(CLAMP); AddressV(CLAMP); >;
	int GridResolution < Attribute("GridResolution"); >;
	int SourceCount < Attribute("SourceCount"); >;
	int ClearState < Attribute("ClearState"); >;
	float CellSize < Attribute("CellSize"); >;
	float2 GridOrigin < Attribute("GridOrigin"); >;
	float2 SourceShift < Attribute("SourceShift"); >;
	float Delta < Attribute("Delta"); >;
	float WaveSpeed < Attribute("WaveSpeed"); >;
	float Damping < Attribute("Damping"); >;
	float Viscosity < Attribute("Viscosity"); >;
	float MaximumHeight < Attribute("MaximumHeight"); >;
	float3 Read(float2 p)
	{
		if(ClearState!=0 || any(p<0) || any(p>GridResolution-1)) return 0;
		return StateIn.SampleLevel(LinearClamp,(p+0.5)/GridResolution,0).xyz;
	}
	float3 Domain(float2 p) { return DomainMap.SampleLevel(LinearClamp,p/GridResolution+0.5/64,0).xyz; }
	float Neighbor(float2 p,float h)
	{
		return Domain(p).z<0.5 ? h : Read(p+SourceShift).x;
	}
	[numthreads(8,8,1)]
	void MainCs(uint3 id : SV_DispatchThreadID)
	{
		if(any(id.xy>=GridResolution)) return;
		float2 p=id.xy;
		float3 domain=Domain(p);
		float edge=min(min(p.x,p.y),min(GridResolution-1-p.x,GridResolution-1-p.y));
		if(edge<1 || domain.z<0.5) { StateOut[id.xy]=0; return; }
		float3 state=Read(p+SourceShift);
		float lap=Neighbor(p+float2(-1,0),state.x)+Neighbor(p+float2(1,0),state.x)
			+Neighbor(p+float2(0,-1),state.x)+Neighbor(p+float2(0,1),state.x)-4*state.x;
		float2 back=p-clamp(domain.xy,-512,512)*Delta/CellSize;
		// Never advect water through a dry shoreline cell.
		float3 advected=Domain(back).z>=0.5 ? Read(back+SourceShift) : state;
		float velocity=advected.y+lap*WaveSpeed*WaveSpeed/(CellSize*CellSize)*Delta;
		float foam=advected.z*exp(-2.5*Delta);
		float2 world=GridOrigin+p*CellSize;
		for(int i=0;i<SourceCount;i++)
		{
			float4 source=Sources[i*2]; float2 direction=Sources[i*2+1].xy;
			float2 q=(world-source.xy)/max(source.z,CellSize);
			float speed=length(direction);
			float impulse;
			if(speed>0.1)
			{
				float2 axis=direction/speed;
				// Bow uplift and stern drawdown: an oriented pressure dipole, rather
				// than a sequence of stretched circular stamps. Its integral is zero.
				float along=dot(q,axis), side=dot(q,float2(-axis.y,axis.x));
				float bow=exp(-((along-0.55)*(along-0.55)*2+side*side*2));
				float stern=exp(-((along+0.55)*(along+0.55)*2+side*side*2));
				impulse=source.w*(bow-stern);
			}
			else
			{
				float r2=dot(q,q)*2;
				impulse=source.w*(1-r2)*exp(-r2);
			}
			velocity+=impulse;
		}
		velocity*=exp(-(Damping+Viscosity+max(0,6-edge*64/GridResolution)*0.8)*Delta);
		velocity=clamp(velocity,-240,240);
		float height=clamp((advected.x+velocity*Delta)*exp(-0.1*Delta),-MaximumHeight,MaximumHeight);
		// Foam follows steep, energetic water motion. A source must not paint a
		// persistent white disk before its height wave has even formed.
		float2 slope=float2(Neighbor(p+float2(1,0),state.x)-Neighbor(p-float2(1,0),state.x),
			Neighbor(p+float2(0,1),state.x)-Neighbor(p-float2(0,1),state.x))/(2*CellSize);
		foam+=smoothstep(0.35,0.8,length(slope))*smoothstep(25,90,abs(velocity))*2*Delta;
		StateOut[id.xy]=float4(height,velocity,saturate(foam),0);
	}
}
