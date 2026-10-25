Texture2D FftDisplacement < Attribute("FftDisplacement"); SrgbRead(false); >;
Texture2D FftDerivatives < Attribute("FftDerivatives"); SrgbRead(false); >;
SamplerState FftSampler < Filter(MIN_MAG_MIP_LINEAR); AddressU(WRAP); AddressV(CLAMP); >;
float FftGridSpacing < Attribute("FftGridSpacing"); Default(32); >;
float2 FftAdvection < Attribute("FftAdvection"); Default2(0,0); >;
Texture2D FluidTexture < Attribute("FluidTexture"); SrgbRead(false); >;
Texture2D FluidCoarseTexture < Attribute("FluidCoarseTexture"); SrgbRead(false); >;
SamplerState FluidSampler < Filter(MIN_MAG_MIP_LINEAR); AddressU(CLAMP); AddressV(CLAMP); >;
float2 FluidOrigin < Attribute("FluidOrigin"); Default2(0,0); >;
float FluidCellSize < Attribute("FluidCellSize"); Default(32); >;
float2 FluidCoarseOrigin < Attribute("FluidCoarseOrigin"); Default2(0,0); >;
float FluidCoarseCellSize < Attribute("FluidCoarseCellSize"); Default(64); >;
float FluidEnabled < Attribute("FluidEnabled"); Default(0); >;
float FluidResolution < Attribute("FluidResolution"); Default(64); >;
float4 FluidSample(float2 p)
{
	[branch] if(FluidEnabled<0.5) return 0;
	float2 cell=(p-FluidOrigin)/max(FluidCellSize,1);
	float4 edges=float4(cell,FluidResolution-1-cell);
	float edge=min(min(edges.x,edges.y),min(edges.z,edges.w));
	float fadeCells=6*FluidResolution/64;
	// The interior has weight one: no coarse texture access is needed there.
	[branch] if(edge>=fadeCells) return FluidTexture.SampleLevel(FluidSampler,(cell+0.5)/FluidResolution,0);
	float2 coarseCell=(p-FluidCoarseOrigin)/max(FluidCoarseCellSize,1);
	float4 coarse=0;
	[branch] if(all(coarseCell>=0) && all(coarseCell<=FluidResolution-1))
		coarse=FluidCoarseTexture.SampleLevel(FluidSampler,(coarseCell+0.5)/FluidResolution,0);
	[branch] if(edge<0) return coarse;
	float4 fine=FluidTexture.SampleLevel(FluidSampler,(cell+0.5)/FluidResolution,0);
	float t=saturate(edge/fadeCells), weight=t*t*(3-2*t);
	float2 gradient=0;
	if(t>0 && t<1)
	{
		float slope=6*t*(1-t)/(fadeCells*max(FluidCellSize,1));
		if(edge==edges.x) gradient.x=slope; else if(edge==edges.z) gradient.x=-slope;
		else if(edge==edges.y) gradient.y=slope; else gradient.y=-slope;
	}
	float4 result=lerp(coarse,fine,weight);
	result.yz+=(fine.x-coarse.x)*gradient;
	return result;
}

// Hardware bilinear sampling, with an explicit wrap only at each atlas band's final row.
// There is no half-texel phase shift: FFT sample zero is at world coordinate zero.
float4 FftSample(Texture2D textureMap,float2 p,float patch,int cascade)
{
	float2 texel=frac(p/patch)*128;
	float x=(texel.x+0.5)/128;
	float baseY=cascade*128;
	float y=(min(texel.y,127)+baseY+0.5)/256;
	float4 value=textureMap.SampleLevel(FftSampler,float2(x,y),0);
	if(texel.y>127)
		value=lerp(value,textureMap.SampleLevel(FftSampler,float2(x,(baseY+0.5)/256),0),texel.y-127);
	return value;
}
WaveField Field(float2 p,float filterSize=0)
{
	WaveField field; field.offset=0; field.tx=float3(1,0,0); field.ty=float3(0,1,0);
	[unroll] for(int cascade=0;cascade<2;cascade++)
	{
		float patch=(cascade==0 ? 256.0 : 32.0)*39.3700787;
		// Fade under-resolved cascades before they can shimmer at the horizon.
		float visibility=1-smoothstep(patch/128,patch/32,filterSize);
		[branch] if(visibility<0.001) continue;
		float4 d=FftSample(FftDisplacement,p-FftAdvection,patch,cascade)*visibility;
		float4 b=FftSample(FftDerivatives,p-FftAdvection,patch,cascade)*visibility;
		field.offset+=d.xyz;
		field.tx+=float3(b.z,d.w,b.x);
		field.ty+=float3(d.w,b.w,b.y);
	}
	// Local disturbances are anchored to the displaced world surface, not the FFT lattice.
	float4 interaction=FluidSample(p+field.offset.xy);
	field.offset.z+=interaction.x;
	field.foam=interaction.w;
	field.tx.z+=dot(interaction.yz,field.tx.xy);
	field.ty.z+=dot(interaction.yz,field.ty.xy);
	return field;
}
// Vertex displacement does not need the derivative atlas or its tangents.
float3 FieldOffset(float2 p)
{
	float3 offset=0;
	[unroll] for(int cascade=0;cascade<2;cascade++)
		offset+=FftSample(FftDisplacement,p-FftAdvection,(cascade==0 ? 256.0 : 32.0)*39.3700787,cascade).xyz;
	offset.z+=FluidSample(p+offset.xy).x;
	return offset;
}
float3 Waves(float2 p,float filterSize=0)
{
	float2 q=p;
	[unroll] for(int iteration=0;iteration<3;iteration++) q=p-Field(q,filterSize).offset.xy;
	WaveField field=Field(q,filterSize);
	float3 n=cross(field.tx,field.ty);
	return float3(field.offset.z,-n.xy/max(n.z,0.05));
}
