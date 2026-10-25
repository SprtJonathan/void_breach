// Radix-2 inverse FFT, two packed float4 fields, 128 samples in group shared memory.
// Adapted from Noniv/web-ocean (MIT). See Docs/ThirdParty/web-ocean-LICENSE.txt.
#include "system.fxc"
StructuredBuffer<float4> InputA < Attribute("InputA"); >;
StructuredBuffer<float4> InputB < Attribute("InputB"); >;
#if FFT_COLUMNS
RWTexture2D<float4> DisplacementOut < Attribute("DisplacementOut"); >;
RWTexture2D<float4> DerivativesOut < Attribute("DerivativesOut"); >;
#else
RWStructuredBuffer<float4> OutputA < Attribute("OutputA"); >;
RWStructuredBuffer<float4> OutputB < Attribute("OutputB"); >;
#endif
groupshared float4 SharedA[128];
groupshared float4 SharedB[128];
float2 ComplexMultiply(float2 a,float2 b) { return float2(a.x*b.x-a.y*b.y,a.x*b.y+a.y*b.x); }
uint Reverse(uint x) { return reversebits(x)>>25; }
uint Address(uint x,uint y,uint cascade) { return (cascade*128+y)*128+x; }
void Store(uint line,uint element,uint cascade,float4 a,float4 b)
{
#if FFT_COLUMNS
	// a=(Dx,Dy,height,jxy), b=(slopeX,slopeY,jxx,jyy). Metres -> Source units.
	DisplacementOut[uint2(line,element+cascade*128)]=float4(a.xyz*39.3700787,a.w);
	DerivativesOut[uint2(line,element+cascade*128)]=b;
#else
	uint index=Address(element,line,cascade);
	OutputA[index]=a; OutputB[index]=b;
#endif
}
[numthreads(64,1,1)]
void MainCs(uint3 group : SV_GroupID,uint thread : SV_GroupIndex)
{
	uint line=group.y, cascade=group.z;
	uint e0=thread, e1=thread+64;
#if FFT_COLUMNS
	uint i0=Address(line,e0,cascade),i1=Address(line,e1,cascade);
#else
	uint i0=Address(e0,line,cascade),i1=Address(e1,line,cascade);
#endif
	SharedA[Reverse(e0)]=InputA[i0]; SharedA[Reverse(e1)]=InputA[i1];
	SharedB[Reverse(e0)]=InputB[i0]; SharedB[Reverse(e1)]=InputB[i1];
	GroupMemoryBarrierWithGroupSync();
	[unroll] for(uint stage=0;stage<7;stage++)
	{
		uint halfSize=1u<<stage;
		uint pos=thread&(halfSize-1);
		uint a=((thread>>stage)<<(stage+1))+pos, b=a+halfSize;
		float sn,cs; sincos(3.14159265359*float(pos)/float(halfSize),sn,cs);
		float2 w=float2(cs,sn);
		float4 a0=SharedA[a],a1=SharedA[b],b0=SharedB[a],b1=SharedB[b];
		float4 ta=float4(ComplexMultiply(a1.xy,w),ComplexMultiply(a1.zw,w));
		float4 tb=float4(ComplexMultiply(b1.xy,w),ComplexMultiply(b1.zw,w));
		SharedA[a]=a0+ta; SharedA[b]=a0-ta;
		SharedB[a]=b0+tb; SharedB[b]=b0-tb;
		GroupMemoryBarrierWithGroupSync();
	}
	Store(line,e0,cascade,SharedA[e0],SharedB[e0]);
	Store(line,e1,cascade,SharedA[e1],SharedB[e1]);
}
