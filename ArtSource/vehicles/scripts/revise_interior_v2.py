import bpy,bmesh,math,os
import numpy as np
from mathutils import Vector,Matrix
from math import sin,cos,pi
globals().update(bpy.app.driver_namespace['INT_BUILD'])
# Rebuild the floor and seat supports; the immutable exterior wheel liners are the master.
for name in ['01 Planchers','04 Sellerie','08 Volant Renault']:
    for o in list(group(name).objects):bpy.data.objects.remove(o,do_unlink=True)
g='01 Planchers'
box('Sol bas',(1.31,0,.335),(4.38,2.04,.06),floor,g,.003)
box('Première marche',(-1.035,0,.45),(.31,2.04,.23),floor,g,.004)
box('Plancher arrière relevé',(-2.4,0,.685),(2.42,2.04,.09),floor,g,.003)
box('Contremarche',(-1.205,0,.55),(.03,2.04,.36),ivory,g,.003)
for x,z in [(-.89,.567),(-1.205,.733)]:
    box('Profil de nez de marche',(x,0,z),(.042,2.02,.008),silver,g,.002)
    box('Sécurité de marche',(x+.008,0,z+.005),(.025,2,.004),yellow,g,.001)

# The roof and inner end wall of each wheel arch duplicate the exterior liner,
# offset 6 mm out of the tire cavity. No rectangular plinth spans the wheel aperture.
for tag in ['avant','arrière']:
    for s in [-1,1]:
        src=bpy.data.objects['Doublure étanche de passage | '+tag+' | '+str(s)]
        cx=1.85 if tag=='avant' else -1.5
        verts=[]
        for v in src.data.vertices:
            p=src.matrix_world@v.co
            dx=p.x-cx;dz=p.z-.454
            if p.z>=.453:
                length=math.hypot(dx,dz);p.x=cx+dx*(length+.006)/length;p.z=.454+dz*(length+.006)/length
            if abs(p.y)<.52:p.y=s*.496
            elif abs(p.y)>1.04:p.y=s*1.055
            verts.append(tuple(p))
        o=mesh('Passage '+tag+' | copie intérieure exacte '+str(s),verts,[tuple(p.vertices) for p in src.data.polygons],ivory,g)
        for p in o.data.polygons:p.use_smooth=len(p.vertices)==4 and abs(p.normal.z)>.05
        # Small saddle on the crown; the curved housing remains visible.
        if tag=='avant':
            box('Selle de siège sur passage',(1.85,s*.77,.998),(.35,.49,.028),dark,g,.006)
            rod('Repose-pieds avant',(1.20,s*.54,.605),(1.20,s*.98,.605),.022,dark,g)
            for y in [s*.57,s*.95]:rod('Patte de repose-pieds',(1.20,y,.605),(1.31,y,.57),.009,silver,g)
        else:
            # Raised bench deck, wholly above the exterior arch crown.
            box('Socle U latéral rehaussé',(-2.08,s*.785,1.11),(2.10,.43,.22),ivory,g,.014)
            for x in [-2.85,-2.25]:
                box('Joue de support U',(x,s*.79,.90),(.055,.40,.35),ivory,g,.006)
box('Socle U traverse rehaussé',(-3.38,0,1.04),(.48,2.0,.40),ivory,g,.018)

# Seats parallel to vehicle travel. One opposing pair per side clears the front door.
src=open(r'D:\Docs\s&box projects\void_breach\ArtSource\vehicles\scripts/rebuild_interior.py',encoding='utf-8').read()
exec(src[src.index('def seat('):src.index('for s in [-1,1]:\n    for i,x in enumerate([.92')])
for s in [-1,1]:
    seat('AV sur passage / dos à la marche '+str(s),(1.85,s*.77),pi,width=.46,base=1.02)
    seat('AV bas / sens de marche '+str(s),(.62,s*.77),0,width=.46,base=.77)
    for y in [s*.62,s*.92]:
        for x in [.48,.75]:rod('Piètement siège bas',(x,y,.37),(x,y,.77),.015,silver,'04 Sellerie')
    for i,x in enumerate([-1.35,-1.835,-2.32,-2.805]):seat('U rehaussé %s %02d'%(s,i),(x,s*.76),-s*pi/2,base=1.205)
for i,y in enumerate([-.735,-.245,.245,.735]):seat('U traverse rehaussée %02d'%i,(-3.32,y),0,base=1.205)

# Remove ALL interior intersections with the external wheel cavity, including wall
# panels and their plinths. Cutters follow the exact 48-segment external arc + 3 mm.
cutters=[]
for cx in [1.85,-1.5]:
    for s in [-1,1]:
        prof=[(cx+.527*cos(i*pi/48),.454+.527*sin(i*pi/48)) for i in range(49)]
        prof.extend([(cx-.527,.20),(cx+.527,.20)])
        vv=[(x,s*y,z) for y in [.503,1.16] for x,z in prof];n=len(prof)
        ff=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        cut=mesh('TEMP exact wheel cavity',vv,ff,None,'TEMP cutters')
        bm=bmesh.new();bm.from_mesh(cut.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(cut.data);bm.free();cutters.append(cut)
bpy.context.view_layer.update()
targets=list(group('01 Planchers').objects)+list(group('02 Parois').objects)
for o in targets:
    if o.type!='MESH' or o.name.startswith('Passage '):continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    for m in list(o.modifiers):
        if m.type!='ARMATURE':bpy.ops.object.modifier_apply(modifier=m.name)
    for cut in cutters:
        m=o.modifiers.new('Évidement passage extérieur','BOOLEAN');m.operation='DIFFERENCE';m.solver='EXACT';m.object=cut;bpy.ops.object.modifier_apply(modifier=m.name)
for cut in cutters:bpy.data.objects.remove(cut,do_unlink=True)

# Renault wheel: one continuous molded lower web with three rounded apertures,
# a thin upper rim and a raised horn pad. Dimensions taken proportionally from photo.
gw='08 Volant Renault'
C=Vector((2.9856169,.5820701,1.233369));U=Vector((0,-1,0));V=Vector((.855,0,.519)).normalized();N=U.cross(V).normalized()
R=Matrix(((U.x,V.x,N.x),(U.y,V.y,N.y),(U.z,V.z,N.z))).to_4x4()
def wp(u,v,n=0):return tuple(C+u*U+v*V+n*N)
wheelmat=material('Volant Renault polyuréthane gris',(.115,.123,.13),0,.58)
nt=wheelmat.node_tree;p=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED');tx=nt.nodes.new('ShaderNodeTexImage');tx.image=bpy.data.images['Soreo5_micrograin_normal_512'];normal=nt.nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.32;nt.links.new(tx.outputs['Color'],normal.inputs['Color']);nt.links.new(normal.outputs['Normal'],p.inputs['Normal'])
verts=[];faces=[]
for i in range(192):
    a=i*2*pi/192
    for j in range(20):
        b=j*2*pi/20;rad=.216+.0148*cos(b);dep=.0185*sin(b)
        if sin(b)<-.5:dep+=.0006*cos(a*32)*(-sin(b))
        verts.append(wp(rad*cos(a),rad*sin(a),dep))
for i in range(192):
    for j in range(20):faces.append((i*20+j,((i+1)%192)*20+j,((i+1)%192)*20+(j+1)%20,i*20+(j+1)%20))
o=mesh('Renault | jante profilée continue',verts,faces,wheelmat,gw)
for p in o.data.polygons:p.use_smooth=True

def rounded(points,f=.18,steps=5):
    out=[]
    for i,b in enumerate(points):
        a=Vector(points[i-1]);b=Vector(b);c=Vector(points[(i+1)%len(points)])
        start=b+(a-b)*f;end=b+(c-b)*f
        for k in range(steps+1):
            t=k/steps;out.append(tuple((1-t)**2*start+2*(1-t)*t*b+t*t*end))
    return out
def plate(name,loops,depth,bevel,z=0,mat=wheelmat):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='2D';cu.fill_mode='BOTH';cu.resolution_u=1;cu.extrude=depth/2;cu.bevel_depth=bevel;cu.bevel_resolution=3
    for points in loops:
        sp=cu.splines.new('POLY');sp.points.add(len(points)-1)
        for p,(x,y) in zip(sp.points,points):p.co=(x,y,0,1)
        sp.use_cyclic_u=True
    o=bpy.data.objects.new(name,cu);group(gw).objects.link(o);cu.materials.append(mat);o.matrix_world=Matrix.Translation(C+N*z)@R;return o

# Continuous lower web is broad at the rim and necks around the central slot.
upper=[(-.211,-.006),(-.184,-.011),(-.074,-.035),(-.070,.079),(-.060,.092),(.060,.092),(.070,.079),(.074,-.035),(.184,-.011),(.211,-.006)]
lower=[(.211*cos(a),.211*sin(a)) for a in np.linspace(-.029,-pi+.029,65)]
outer=upper+lower
left=[(-.182,-.038),(-.090,-.055),(-.082,-.139),(-.122,-.143),(-.151,-.126),(-.172,-.097)]
right=[(-x,y) for x,y in reversed(left)]
slot=[(-.034,-.103),(.034,-.103),(.030,-.181),(.024,-.191),(-.024,-.191),(-.030,-.181)]
plate('Renault | voile inférieur moulé et trois ajours',[outer,rounded(left,.21),rounded(right,.21),rounded(slot,.19)],.018,.005,-.002)
pad=[(-.062,.088),(.062,.088),(.065,-.079),(.053,-.090),(-.053,-.090),(-.065,-.079)]
plate('Renault | coussin avertisseur galbé',[rounded(pad,.10)],.026,.006,.012)
badge=material('Renault badge aluminium patiné',(.48,.50,.48),.65,.38)
plate('Renault | cartouche encastré',[rounded([(-.049,-.068),(.049,-.068),(.049,-.083),(-.049,-.083)],.1)],.001,.0007,.034,mat=dark)
for yy in [-.068,-.083]:tube('Renault | filet du cartouche',[wp(-.049,yy,.036),wp(.049,yy,.036)],.0006,badge,gw,6)
for xx in [-.049,.049]:tube('Renault | filet du cartouche',[wp(xx,-.068,.036),wp(xx,-.083,.036)],.0006,badge,gw,6)
o=text('Renault | lettrage en relief','RENAULT',wp(0,-.0805,.0365),.0115,badge,gw);o.rotation_euler=R.to_euler();o.data.extrude=.0007;o.data.resolution_u=3;o.data.shear=.12
# Fine molding line around the grip on its underside.
tube('Renault | ligne de joint arrière',[wp(.216*cos(a),.216*sin(a),-.0187) for a in np.linspace(0,2*pi,193)],.00065,rubber,gw,6)
bpy.context.view_layer.update()
bpy.app.driver_namespace['INT_V2']=globals().copy()
print('V2 geometry complete: mirrored wheel arches, longitudinal seat orientations, raised rear +160 mm, rebuilt molded Renault wheel')
