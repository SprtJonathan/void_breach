// Filtered, warped cellular caustics. A visual approximation, not photon tracing.
float2 WaterCellHash(float2 p)
{
    float3 h=frac(float3(p.xyx)*float3(.1031,.1030,.0973));
    h+=dot(h,h.yzx+33.33);
    return frac((h.xx+h.yz)*h.zy);
}
float WaterCausticPattern(float2 p,float time)
{
    p+=float2(sin(p.y*1.37+time*.31),sin(p.x*1.19-time*.27))*.7;
    p+=float2(sin(p.y*3.1+time*.23),cos(p.x*2.7-time*.19))*.16;
    float2 cell=floor(p), local=frac(p);
    float nearest=10, second=10;
    for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
    {
        float2 id=float2(x,y);
        float2 seed=WaterCellHash(cell+id);
        float2 point=.5+.42*sin(seed*6.2831853+time*.45);
        float d=length(id+point-local);
        if(d<nearest) { second=nearest; nearest=d; }
        else second=min(second,d);
    }
    float edge=second-nearest;
    float aa=max(fwidth(edge),.015);
    float lines=1-smoothstep(.025-aa,.075+aa,edge);
    float footprint=max(length(ddx(p)),length(ddy(p)));
    return lerp(lines,.16,smoothstep(.15,.8,footprint));
}
