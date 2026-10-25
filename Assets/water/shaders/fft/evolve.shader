HEADER
{
	Description = "Two-cascade Tessendorf spectrum evolution";
}
MODES
{
	Default();
}
CS
{
	#include "system.fxc"
	StructuredBuffer<float4> InitialSpectrum < Attribute("InitialSpectrum"); >;
	StructuredBuffer<float4> SwellSpectrum < Attribute("SwellSpectrum"); >;
	float2 BaseWind < Attribute("BaseWind"); >;
	float2 CurrentWind < Attribute("CurrentWind"); >;
	float WindDiffusion < Attribute("WindDiffusion"); >;
	RWStructuredBuffer<float4> SpectrumA < Attribute("SpectrumA"); >;
	RWStructuredBuffer<float4> SpectrumB < Attribute("SpectrumB"); >;
	float FftTime < Attribute("FftTime"); >;
	float Choppiness < Attribute("Choppiness"); >;
	float2 Multiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y,a.x*b.y+a.y*b.x); }
	float2 TimesI(float2 a) { return float2(-a.y,a.x); }
	float DirectionPower(float alignment)
	{
		return alignment*alignment*(alignment>=0 ? 1 : 0.12)/(1.57079632679*1.12)*(1-WindDiffusion)+WindDiffusion/6.28318530718;
	}
	[numthreads(8,8,1)]
	void MainCs(uint3 id : SV_DispatchThreadID)
	{
		const uint N=128;
		uint index=(id.z*N+id.y)*N+id.x;
		float patch=id.z==0 ? 256.0 : 32.0;
		int2 frequency=int2(id.xy)-int2(id.x>=N/2 ? N : 0,id.y>=N/2 ? N : 0);
		float2 k=float2(frequency)*(6.28318530718/patch);
		float magnitude=length(k);
		if(magnitude<0.00001) { SpectrumA[index]=0; SpectrumB[index]=0; return; }
		float fundamental=6.28318530718/256.0;
		float omega=floor(sqrt(9.81*magnitude)/fundamental)*fundamental;
		float sn,cs; sincos(omega*FftTime,sn,cs);
		float4 h0=InitialSpectrum[index];
		float2 direction=k/magnitude;
		float baseAlignment=dot(direction,BaseWind), currentAlignment=dot(direction,CurrentWind);
		// Reweight amplitudes, preserving each travelling wave's phase and wave vector.
		h0.xy*=sqrt(DirectionPower(currentAlignment)/DirectionPower(baseAlignment));
		h0.zw*=sqrt(DirectionPower(-currentAlignment)/DirectionPower(-baseAlignment));
		h0+=SwellSpectrum[index];
		float2 h=Multiply(h0.xy,float2(cs,-sn))+Multiply(h0.zw,float2(cs,sn));
		float2 ih=TimesI(h);
		float2 dx=ih*(Choppiness*k.x/magnitude);
		float2 dy=ih*(Choppiness*k.y/magnitude);
		float2 jxy=h*(-Choppiness*k.x*k.y/magnitude);
		float2 sx=ih*k.x, sy=ih*k.y;
		float2 jxx=h*(-Choppiness*k.x*k.x/magnitude);
		float2 jyy=h*(-Choppiness*k.y*k.y/magnitude);
		// Pack pairs of real spatial fields into each complex transform.
		SpectrumA[index]=float4(dx+TimesI(dy),h+TimesI(jxy));
		SpectrumB[index]=float4(sx+TimesI(sy),jxx+TimesI(jyy));
	}
}
