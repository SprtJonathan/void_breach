HEADER
{
	Description = "Void Breach natural water: shared waves, depth absorption, refraction and local wakes";
	Version = 1;
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	VrForward();
}
COMMON
{
	#define S_TRANSLUCENT 1
	#define BLEND_MODE_ALREADY_SET 1
	#define DEPTH_STATE_ALREADY_SET 1
	#include "common/shared.hlsl"
	float WaterTime < Attribute("WaterTime"); Default(0); >;
	float2 WaveDirection < Attribute("WaveDirection"); Default2(1,0); >;
	float WaveAmplitude < Attribute("WaveAmplitude"); Default(2); >;
	float WaveLength < Attribute("WaveLength"); Default(360); >;
	float WaveSpeed < Attribute("WaveSpeed"); Default(0.55); >;
	float WaterInfinite < Attribute("WaterInfinite"); Default(0); >;
	float3 WaterOrigin < Attribute("WaterOrigin"); Default3(0,0,0); >;
	float WaterMotion < Attribute("WaterMotion"); Default(1); >;
struct WaveField { float3 offset; float3 tx; float3 ty; };
 void AddWave(inout WaveField f,float2 p,float2 d,float a,float l,float phase,float chop,float rate,float filterSize)
 {
  a*=1-smoothstep(l*.15,l*.65,filterSize);
  float k=6.28318530718/max(l,1);
  float angle=dot(p,d)*k-sqrt(386.09*k)*WaveSpeed*WaterTime*rate+phase;
  float sn=sin(angle), cs=cos(angle);
  float horizontal=min(a*chop,0.075/k);
  f.offset+=float3(horizontal*cs*d,a*sn);
  f.tx+=float3(-horizontal*k*sn*d*d.x,a*k*cs*d.x);
  f.ty+=float3(-horizontal*k*sn*d*d.y,a*k*cs*d.y);
 }
 WaveField Field(float2 p,float filterSize=0)
 {
  WaveField f; f.offset=0; f.tx=float3(1,0,0); f.ty=float3(0,1,0);
  float2 d=WaveDirection;
  if(WaterMotion<2.5)
  {
   AddWave(f,p,d,WaveAmplitude*.6,WaveLength,0,0,.65,filterSize);
   AddWave(f,p,float2(-d.y,d.x),WaveAmplitude*.25,WaveLength*.61,1.8,0,.48,filterSize);
   AddWave(f,p,-d,WaveAmplitude*.15,WaveLength*.39,3.2,0,.8,filterSize);
   return f;
  }
  AddWave(f,p,float2(d.x*1.00000000-d.y*0.00000000,d.x*0.00000000+d.y*1.00000000),WaveAmplitude*0.38,WaveLength*1,0,.8,1,filterSize);
  AddWave(f,p,float2(d.x*0.75180573-d.y*0.65938467,d.x*0.65938467+d.y*0.75180573),WaveAmplitude*0.25,WaveLength*0.731,1.7,.8,1,filterSize);
  AddWave(f,p,float2(d.x*0.61374575-d.y*-0.78950374,d.x*-0.78950374+d.y*0.61374575),WaveAmplitude*0.17,WaveLength*0.479,3.1,.8,1,filterSize);
  AddWave(f,p,float2(d.x*-0.29475935-d.y*0.95557152,d.x*0.95557152+d.y*-0.29475935),WaveAmplitude*0.1,WaveLength*0.317,0.8,.8,1,filterSize);
  AddWave(f,p,float2(d.x*0.14033161-d.y*-0.99010456,d.x*-0.99010456+d.y*0.14033161),WaveAmplitude*0.06,WaveLength*0.223,4.4,.8,1,filterSize);
  AddWave(f,p,float2(d.x*-0.87681789-d.y*0.48082261,d.x*0.48082261+d.y*-0.87681789),WaveAmplitude*0.04,WaveLength*0.157,2.3,.8,1,filterSize);
  return f;
 }
 float3 Waves(float2 p,float filterSize=0)
 {
  float2 q=p;
  if(WaterMotion>2.5) for(int j=0;j<5;j++) q=p-Field(q,filterSize).offset.xy;
  WaveField f=Field(q,filterSize);
  float3 n=cross(f.tx,f.ty);
  return float3(f.offset.z,-n.xy/max(n.z,.001));
 }

}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
    float2 waterParameter : TEXCOORD12;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs(VertexInput i)
	{
		PixelInput o = ProcessVertex(i);
        if(WaterInfinite>0.5) o.vPositionWs.xy=WaterOrigin.xy+i.vPositionOs.xy+floor((g_vCameraPositionWs.xy-WaterOrigin.xy)/128)*128;
        float2 parameter=o.vPositionWs.xy;
        o.waterParameter=parameter;
        WaveField field=Field(parameter);
        // Only the outermost horizon skirt flattens. Interior lattice samples do not change with the view.
        float skirt=WaterInfinite>0.5 ? smoothstep(0,.025,min(min(i.vTexCoord.x,1-i.vTexCoord.x),min(i.vTexCoord.y,1-i.vTexCoord.y))) : 1;
        o.vPositionWs+=field.offset*skirt;
		o.vPositionPs = Position3WsToPs(o.vPositionWs);
		return FinalizeVertex(o);
	}
}
PS
{
	// The supplied scans are 1254px, not a power of two. Resample during import,
	// leaving the source images untouched and allowing filtered mip chains.
	CreateInputTexture2D(TextureColor, Srgb, 8, "ScaleToPowerOfTwo", "_color", "Water", Default3(1,1,1));
	CreateInputTexture2D(TextureNormal, Linear, 8, "ScaleToPowerOfTwo", "_normal", "Water", Default3(0.5,0.5,1));
	CreateInputTexture2D(TextureRoughness, Linear, 8, "ScaleToPowerOfTwo", "_rough", "Water", Default(0.5));
	CreateInputTexture2D(TextureAmbientOcclusion, Linear, 8, "ScaleToPowerOfTwo", "_ao", "Water", Default(1));
	Texture2D g_tColor < Channel(RGB, Box(TextureColor), Srgb); OutputFormat(BC7); SrgbRead(true); >;
	Texture2D g_tNormal < Channel(RGB, Box(TextureNormal), Linear); OutputFormat(BC7); SrgbRead(false); >;
	Texture2D g_tRma < Channel(R, Box(TextureRoughness), Linear); Channel(G, Box(TextureAmbientOcclusion), Linear); Channel(B, Box(TextureAmbientOcclusion), Linear); OutputFormat(BC7); SrgbRead(false); >;
	SamplerState TextureFiltering < Filter(ANISOTROPIC); MaxAniso(16); AddressU(WRAP); AddressV(WRAP); >;
	#include "common/pixel.hlsl"
	#include "common/classes/Depth.hlsl"
    #include "common/classes/EnvMap.hlsl"
	BoolAttribute(bWantsFBCopyTexture, true);
	BoolAttribute(NoZPrepass, true);
	BoolAttribute(DoNotCastShadows, true);
	// Keep the nearest wave in depth; alpha is used only for the authored boundary fade.
	// Refraction samples the engine's previously built opaque depth chain.
	RenderState(DepthWriteEnable, true);
	RenderState(DepthEnable, true);
	RenderState(DepthFunc, GREATER_EQUAL);
	RenderState(BlendEnable, true);
    RenderState(SrcBlend, SRC_ALPHA);
    RenderState(DstBlend, INV_SRC_ALPHA);
	RenderState(CullMode, NONE);
	Texture2D WaterBackground < Attribute("FrameBufferCopyTexture"); SrgbRead(false); >;
	SamplerState WaterClamp < Filter(MIN_MAG_MIP_LINEAR); AddressU(CLAMP); AddressV(CLAMP); >;
	float3 WaterShallow < Attribute("WaterShallow"); Default3(0.12,0.23,0.17); >;
	float3 WaterDeep < Attribute("WaterDeep"); Default3(0.018,0.064,0.05); >;
	float WaterClarity < Attribute("WaterClarity"); Default(140); >;
	float WaterRoughness < Attribute("WaterRoughness"); Default(0.12); >;
	float WaterReflectionStrength < Attribute("WaterReflectionStrength"); Default(1); >;
	float WaterTextureScale < Attribute("WaterTextureScale"); Default(180); >;
	float WaterNormalStrength < Attribute("WaterNormalStrength"); Default(0.32); >;
	float WaterMicroNormalStrength < Attribute("WaterMicroNormalStrength"); Default(0.22); >;
	float WaterColorInfluence < Attribute("WaterColorInfluence"); Default(0.08); >;
	float WaterFoam < Attribute("WaterFoam"); Default(0.4); >;
	float WaterShoreWidth < Attribute("WaterShoreWidth"); Default(18); >;
	float2 WaterEdgeFadeUv < Attribute("WaterEdgeFadeUv"); Default2(0.01,0.01); >;
	float WaterRefraction < Attribute("WaterRefraction"); Default(0.012); >;
	float WaterEllipse < Attribute("WaterEllipse"); Default(0); >;
	float WaterCausticStrength < Attribute("WaterCausticStrength"); Default(0.28); >;
	float WaterCausticScale < Attribute("WaterCausticScale"); Default(92); >;
	float WaterCausticSpeed < Attribute("WaterCausticSpeed"); Default(0.7); >;
	float WaterCausticDepth < Attribute("WaterCausticDepth"); Default(260); >;
	float CurrentSpeed < Attribute("CurrentSpeed"); Default(3); >;
	float WaterDebug < Attribute("WaterDebug"); Default(0); >;
	float PondAgitation < Attribute("PondAgitation"); Default(0.35); >;
	float RiverTurbulence < Attribute("RiverTurbulence"); Default(0.6); >;
	float RiverLength < Attribute("RiverLength"); Default(1); >;
	float RiverWidth < Attribute("RiverWidth"); Default(320); >;
	float RippleStrength < Attribute("RippleStrength"); Default(0.6); >;
	float RippleLifetime < Attribute("RippleLifetime"); Default(3); >;
	float RippleSpeed < Attribute("RippleSpeed"); Default(95); >;
	float4 Ripple0 < Attribute("Ripple0"); Default4(0,0,0,0); >;
	float4 Ripple1 < Attribute("Ripple1"); Default4(0,0,0,0); >;
	float4 Ripple2 < Attribute("Ripple2"); Default4(0,0,0,0); >;
	float4 Ripple3 < Attribute("Ripple3"); Default4(0,0,0,0); >;
	float4 Ripple4 < Attribute("Ripple4"); Default4(0,0,0,0); >;
	float4 Ripple5 < Attribute("Ripple5"); Default4(0,0,0,0); >;
	float4 Ripple6 < Attribute("Ripple6"); Default4(0,0,0,0); >;
	float4 Ripple7 < Attribute("Ripple7"); Default4(0,0,0,0); >;
	float2 RippleNormal(float2 p, float4 ripple)
	{
		float age = WaterTime - ripple.w;
		if (ripple.z <= 0 || age < 0 || age >= RippleLifetime) return 0;
		float2 delta = p-ripple.xy;
		float radius = length(delta);
		float ring = radius-age*RippleSpeed;
		float fade = pow(saturate(1-age/RippleLifetime),2);
		float envelope = exp(-ring*ring/1800.0) * fade;
		return delta/max(radius,1) * cos(ring*0.19) * envelope * ripple.z * RippleStrength * 0.22;
	}
	float2 RotateUv(float2 p, float2 axis)
	{
		return float2(p.x*axis.x-p.y*axis.y, p.x*axis.y+p.y*axis.x);
	}
	float2 NormalXY(float2 uv)
	{
		float3 value = g_tNormal.Sample(TextureFiltering,uv).xyz*2-1;
		return clamp(value.xy/max(value.z,0.65),-0.8,0.8);
	}
    #include "water/shaders/vb_caustics.hlsl"
    float Caustics(float2 worldPosition)
    {
        return WaterCausticPattern(worldPosition/max(WaterCausticScale,1),WaterTime*WaterCausticSpeed);
    }
	float FoamNoise(float2 p)
	{
		// Continuous, domain-warped bands: no cell boundaries, binary threshold,
		// or unstable sin/hash amplification. Fade unresolved bands to their mean.
		p+=float2(sin(p.y*0.73),cos(p.x*0.61))*0.65;
		float footprint=max(length(ddx(p)),length(ddy(p)));
		float a=sin(dot(p,float2(1.31,0.87)));
		float b=sin(dot(p,float2(-0.93,1.73))+1.7);
		float c=sin(dot(p,float2(2.39,-1.21))+3.1);
		return 0.5+(a*0.2+b*0.18+c*0.12)*(1-smoothstep(0.3,2,footprint));
	}
	float EdgeCoverage(float2 uv)
	{
		if (max(WaterEdgeFadeUv.x,WaterEdgeFadeUv.y)<=0) return 1;
		float2 distanceUv = min(uv,1-uv);
		if (WaterEllipse>0.5)
		{
			float radial = max(0,(1-length(uv*2-1))*0.5);
			distanceUv = radial.xx;
		}
		return smoothstep(0,1,min(distanceUv.x/max(WaterEdgeFadeUv.x,0.00001),distanceUv.y/max(WaterEdgeFadeUv.y,0.00001)));
	}
	float4 MainPs(PixelInput i, bool frontFace : SV_IsFrontFace) : SV_Target
	{
		if (WaterEllipse > 0.5) clip(1.0-dot(i.vTextureCoords.xy*2-1,i.vTextureCoords.xy*2-1));
		float3 world = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		// Screen derivatives jump across occluding crests. Bound the wave filter with a continuous footprint.
        float3 cameraDelta=world-g_vCameraPositionWs;
        float pixelFootprint=length(cameraDelta)*g_vInvViewportSize.y/max(abs(normalize(cameraDelta).z),.025);
        WaveField surface=Field(i.waterParameter,pixelFootprint);
        float3 macroNormal=cross(surface.tx,surface.ty);
        float3 wave=float3(surface.offset.z,-macroNormal.xy/max(macroNormal.z,.001));
		float baseHeight = WaterMotion>1.5 && WaterMotion<2.5 ? world.z-wave.x : WaterOrigin.z;
		bool underwaterFace = g_vCameraPositionWs.z < baseHeight+Waves(g_vCameraPositionWs.xy).x;
		// Depth resolves overlapping waves; do not punch holes in back-facing crest triangles.
		// UV2 is not populated by the standard vertex path. World coordinates also
		// anchor the detail when an infinite ocean grid follows the camera.
		float2 uv = world.xy / max(WaterTextureScale,1);
		bool river = WaterMotion>1.5 && WaterMotion<2.5;
		bool pond = WaterMotion<1.5;
		if (river) uv=float2(i.vTextureCoords.y*RiverLength,i.vTextureCoords.x*RiverWidth)/max(WaterTextureScale,1);
		// Four decorrelated bands avoid the single scrolling motif visible on calm, shallow water.
		float flow = river ? CurrentSpeed*WaterTime/max(WaterTextureScale,1) : 0;
		uv.x -= flow;
		float detailTime=WaterTime*(pond ? PondAgitation*1.8 : river ? RiverTurbulence*1.2 : 0.65);
		uv += 0.1*float2(sin(uv.y*0.63+detailTime*0.13),sin(uv.x*0.71-detailTime*0.17));
		float2 uvA = RotateUv(uv,float2(0.9701,0.2425))+float2(sin(detailTime*0.11)*0.18,-detailTime*0.021);
		float2 uvB = RotateUv(uv*1.83,float2(0.7648,-0.6442))+float2(detailTime*0.029,cos(detailTime*0.13)*0.16)+0.37;
		float2 uvC = RotateUv(uv*3.91,float2(0.5145,0.8575))+float2(-detailTime*0.019,detailTime*-0.033)+0.71;
		float2 uvD = RotateUv(uv*6.27,float2(0.9138,-0.4061))+float2(detailTime*0.021,detailTime*0.044)+0.13;
		float2 broad = RotateUv(NormalXY(uvA),float2(0.9701,-0.2425))*0.58+RotateUv(NormalXY(uvB),float2(0.7648,0.6442))*0.42;
		float2 micro = RotateUv(NormalXY(uvC),float2(0.5145,-0.8575))*0.62+RotateUv(NormalXY(uvD),float2(0.9138,0.4061))*0.38;
		float footprint = max(length(ddx(uv)),length(ddy(uv)));
		micro *= 1-smoothstep(0.025,0.18,footprint);
		float2 detail = broad*WaterNormalStrength+micro*WaterMicroNormalStrength;
		if (pond) detail*=0.8;
        if (river) detail*=1.35;
		if (river)
		{
			float2 along=normalize(i.vTangentUWs.xy);
			detail=along*detail.x+float2(-along.y,along.x)*detail.y;
		}
		float2 wakes = RippleNormal(world.xy,Ripple0)+RippleNormal(world.xy,Ripple1)
			+RippleNormal(world.xy,Ripple2)+RippleNormal(world.xy,Ripple3)
			+RippleNormal(world.xy,Ripple4)+RippleNormal(world.xy,Ripple5)
			+RippleNormal(world.xy,Ripple6)+RippleNormal(world.xy,Ripple7);
		float distanceFade = 1.0/(1.0+length(world-g_vCameraPositionWs)/4500.0);
		float3 n = normalize(float3(-wave.yz+wakes+detail*distanceFade,1));
		float3 behind = Depth::GetWorldPosition(i.vPositionSs.xy);
		bool hasBottom = Depth::GetNormalized(i.vPositionSs.xy)>0.000001;
		float surfaceDistance = length(world-g_vCameraPositionWs);
		float thickness = max(0,length(behind-g_vCameraPositionWs)-surfaceDistance);
		if (!hasBottom) thickness=10000;
		float2 screenUv = i.vPositionSs.xy * g_vInvViewportSize;
		float refractionDepth = saturate(thickness/max(WaterShoreWidth*2,8));
		float2 refractUv = clamp(screenUv+n.xy*WaterRefraction*refractionDepth,0.001,0.999);
		// Reject samples on foreground geometry: no shoreline/prop refraction halos.
		float3 refractedWorld = Depth::GetWorldPosition(refractUv/g_vInvViewportSize);
		float refractedDistance = length(refractedWorld-g_vCameraPositionWs);
		if (refractedDistance<surfaceDistance)
		{
			refractUv=screenUv;
			refractedWorld=behind;
		}
		else if (hasBottom) thickness=max(0,refractedDistance-surfaceDistance);
		float3 background = WaterBackground.Sample(WaterClamp,refractUv).rgb;
		float3 colorTexture = g_tColor.Sample(TextureFiltering,uvA).rgb;
		float3 rma = g_tRma.Sample(TextureFiltering,uvA).rgb;
		float depth01 = 1-exp(-min(thickness,10000.0)/max(WaterClarity,1));
		float3 absorption = max(float3(0.12,0.12,0.12),1-WaterDeep*2.2)/max(WaterClarity,1);
		float3 beer = exp(-min(thickness,10000.0)*absorption);
		float transmittance = dot(beer,float3(0.2126,0.7152,0.0722));
		float causticFade = exp(-thickness/max(WaterCausticDepth,1))*saturate(thickness/3);
		float caustic = hasBottom ? Caustics(refractedWorld.xy)*WaterCausticStrength*causticFade : 0;
		background *= 1+caustic*float3(0.95,1,0.78);
		float shoreDepth=max(world.z-behind.z,0);
		float shore = hasBottom ? (1-smoothstep(0,max(WaterShoreWidth,1),shoreDepth))*smoothstep(0,2,shoreDepth) : 0;
		float crest = saturate((wave.x/max(WaveAmplitude,0.01)-0.55)*3) * saturate(WaveAmplitude/25);
		float2 foamUv=river ? uv*WaterTextureScale : world.xy;
		float foamNoise = FoamNoise(foamUv*0.013+float2(detailTime*0.04,-detailTime*0.025));
		float foamCells = FoamNoise(foamUv*0.085+float2(-detailTime*0.02,detailTime*0.03));
		float foamFootprint=max(length(ddx(foamUv)),length(ddy(foamUv)));
		foamCells=lerp(foamCells,0.5,smoothstep(3,18,foamFootprint));
		float crestFoam=pond ? 0 : crest*(river ? RiverTurbulence*saturate(abs(CurrentSpeed)/80) : 1);
		float foam = saturate((shore*1.3+crestFoam)*WaterFoam*(pond ? 0.15 : 1));
		foam *= smoothstep(0.16,0.76,foamNoise)*smoothstep(0.1,0.85,foamCells)*2;
		float3 view = normalize(g_vCameraPositionWs-world);
		// Never face-forward a perturbed normal: at grazing angles this toggles
		// its hemisphere per texel and produces the black sparkling holes.
		bool belowPlane = underwaterFace;
		if (belowPlane) n=-n;
		// Keep shading normals above the viewing horizon without flipping them.
		n=normalize(n+view*max(0,0.04-dot(n,view)));
		float fresnel = 0.0204+0.9796*pow(1-saturate(dot(n,view)),5);
		Material m = Material::Init(i);
		m.Normal = n;
		float3 waterColor = lerp(WaterShallow,WaterDeep,depth01);
		waterColor*=lerp(float3(1,1,1),colorTexture*2,WaterColorInfluence*0.2);
		m.Albedo = float3(0.72,0.78,0.72)*foam;
		float normalVariance = dot(ddx(n),ddx(n))+dot(ddy(n),ddy(n));
		m.Roughness = lerp(clamp(sqrt(WaterRoughness*WaterRoughness+min(normalVariance,0.004)),0.065,0.8),0.65,foam);
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.Opacity = 1;
		// Standard lighting already contains Fresnel: never multiply it by Fresnel again.
		CombinerInput lightingInput = ShadingModelStandard::MaterialToCombinerInput(m);
		lightingInput.vSpecularColor = lerp(float3(0.0204,0.0204,0.0204),float3(0.04,0.04,0.04),foam);
		LightingTerms_t terms = InitLightingTerms();
		ComputeDirectLighting(terms,lightingInput);
		CalculateIndirectLighting(terms,lightingInput);
		// The opaque SSR buffer does not describe this refractive surface. Sample scene probes directly.
		float3 environment = EnvMap::From(world,i.vPositionSs,n,m.Roughness.xx)
			* CalcBRDFReflectionFactor(saturate(dot(n,view)),m.Roughness,lightingInput.vSpecularColor)
			* clamp(WaterReflectionStrength,0,2);
		float3 reflected = terms.vSpecular.rgb+environment;
		float3 foamLight = (terms.vDiffuse.rgb+terms.vIndirectDiffuse.rgb)*m.Albedo;
		float3 volumeColor = background*beer+waterColor*(1-beer);
		float3 color = volumeColor*(1-fresnel)*(1-foam)+reflected+foamLight;
		// Under the plane, emphasize total internal reflection and keep the surface readable.
		float underside = step(g_vCameraPositionWs.z,world.z);
		color = lerp(color,color*float3(0.78,0.91,0.88),underside);
		float edge = EdgeCoverage(i.vTextureCoords.xy);
		clip(edge-0.001); // Blend the actual framebuffer at the authored boundary, not the refraction copy.
		if (WaterDebug>0.5 && WaterDebug<1.5) return float4(n*0.5+0.5,1);
		if (WaterDebug>1.5 && WaterDebug<2.5) return float4(foam.xxx,1);
		if (WaterDebug>2.5 && WaterDebug<3.5) return float4(reflected,1);
		if (WaterDebug>3.5) return float4(saturate(thickness/500).xxx,1);
		return DoAtmospherics(world,i.vPositionSs.xy,float4(max(color,0),edge));
	}
}
