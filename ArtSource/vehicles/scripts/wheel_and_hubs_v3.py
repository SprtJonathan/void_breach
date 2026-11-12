import bpy,math,bmesh
import numpy as np
from mathutils import Vector,Matrix
from math import sin,cos,pi
globals().update(bpy.app.driver_namespace['INT_V2'])
for fn in [group,mesh,box,tube,rod,text]:
    state=fn.__globals__;state['root']=bpy.data.collections['INTERIEUR HD | Soreo5'];state['groups']={c.name[6:]:c for c in bpy.data.collections if c.name.startswith('INT | ')}
gw='08 Volant Renault'
for o in list(bpy.data.collections['INT | '+gw].objects):bpy.data.objects.remove(o,do_unlink=True)
C=Vector((2.9856169,.5820701,1.233369));U=Vector((0,-1,0));V=Vector((.855,0,.519)).normalized();N=U.cross(V).normalized()
R=Matrix(((U.x,V.x,N.x),(U.y,V.y,N.y),(U.z,V.z,N.z))).to_4x4()
def wp(u,v,n=0):return tuple(C+u*U+v*V+n*N)
wheelmat=bpy.data.materials['INT | Volant Renault polyuréthane gris']
p=next(n for n in wheelmat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(.065,.071,.076,1);wheelmat.diffuse_color=(.065,.071,.076,1)
verts=[];faces=[]
for i in range(192):
    a=i*2*pi/192
    for j in range(20):
        b=j*2*pi/20;radius=.216+.014*cos(b);depth=.0175*sin(b)
        if sin(b)<-.5:depth+=.00055*cos(a*32)*(-sin(b))
        verts.append(wp(radius*cos(a),radius*sin(a),depth))
for i in range(192):
    for j in range(20):faces.append((i*20+j,((i+1)%192)*20+j,((i+1)%192)*20+(j+1)%20,i*20+(j+1)%20))
o=mesh('Renault | jante à section ovale',verts,faces,wheelmat,gw)
for p in o.data.polygons:p.use_smooth=True
def rounded(points,f=.15,steps=5):
    out=[]
    for i,b in enumerate(points):
        a=Vector(points[i-1]);b=Vector(b);c=Vector(points[(i+1)%len(points)]);start=b+(a-b)*f;end=b+(c-b)*f
        for k in range(steps+1):
            t=k/steps;out.append(tuple((1-t)**2*start+2*(1-t)*t*b+t*t*end))
    return out
def plate(name,loops,depth,bevel,z=0,mat=wheelmat):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='2D';cu.fill_mode='BOTH';cu.resolution_u=1;cu.extrude=depth/2;cu.bevel_depth=bevel;cu.bevel_resolution=4
    for points in loops:
        sp=cu.splines.new('POLY');sp.points.add(len(points)-1)
        for p,(x,y) in zip(sp.points,points):p.co=(x,y,0,1)
        sp.use_cyclic_u=True
    ob=bpy.data.objects.new(name,cu);bpy.data.collections['INT | '+gw].objects.link(ob);cu.materials.append(mat);ob.matrix_world=Matrix.Translation(C+N*z)@R;return ob

# The photograph has NO cross-spokes: its upper opening flows around both sides
# of the horn pad. Only a lower crescent and the two sides of the bottom slot remain.
# Photo proportions: horn pad top is about 40% down the wheel's outer diameter.
upper=[(-.194,-.092),(-.183,-.112),(-.167,-.122),(-.093,-.124),(-.075,-.120),(-.066,-.105),(-.061,.038),(-.056,.044),(.056,.044),(.061,.038),(.066,-.105),(.075,-.120),(.093,-.124),(.167,-.122),(.183,-.112),(.194,-.092)]
arc=[(.216*cos(a),.216*sin(a)) for a in np.linspace(math.atan2(-.092,.194),math.atan2(-.092,-.194),81)]
slot=[(-.043,-.122),(.043,-.122),(.041,-.188),(.035,-.198),(-.035,-.198),(-.041,-.188)]
plate('Renault | branches basses continues, ouverture supérieure libre',[upper+arc,rounded(slot,.13)],.016,.0045,-.002)
pad=[(-.057,.043),(.057,.043),(.062,-.101),(.053,-.113),(-.053,-.113),(-.062,-.101)]
plate('Renault | coussin avertisseur aux proportions photo',[rounded(pad,.08)],.011,.0035,.011)
badge=bpy.data.materials['INT | Renault badge aluminium patiné']
plate('Renault | badge encastré',[rounded([(-.048,-.093),(.048,-.093),(.048,-.109),(-.048,-.109)],.1)],.001,.0004,.021,mat=dark)
for y in [-.093,-.109]:tube('Renault | encadrement badge',[wp(-.048,y,.022),wp(.048,y,.022)],.0005,badge,gw,6)
for x in [-.048,.048]:tube('Renault | encadrement badge',[wp(x,-.093,.022),wp(x,-.109,.022)],.0005,badge,gw,6)
o=text('Renault | lettres moulées RENAULT','RENAULT',wp(0,-.1065,.0227),.0115,badge,gw);o.rotation_euler=R.to_euler();o.data.extrude=.0007;o.data.resolution_u=3;o.data.shear=.10
tube('Renault | joint discret arrière',[wp(.216*cos(a),.216*sin(a),-.0176) for a in np.linspace(0,2*pi,193)],.0006,rubber,gw,6)

# Axles/hubs are additive; no existing body, wheel or rig geometry is edited.
mechanical=bpy.data.collections.new('19 | Moyeux essieux et liaisons');bpy.context.scene.collection.children.link(mechanical)
for fn in [group,mesh,box,tube,rod,text]:fn.__globals__['groups']['12 Mécanique']=mechanical
gm='12 Mécanique'
steel=material('Acier peint des essieux',(.028,.035,.037),.60,.62)
cast=material('Fonte des moyeux',(.095,.105,.11),.65,.55)
bolts=material('Boulonnerie zinguée',(.29,.31,.32),.8,.32)
created_bones={}
def bind(ob,bone):created_bones[ob.name]=bone;return ob
def axle_lathe(name,cx,cz,profile,mat,bone,s=1,segments=64):
    vv=[(cx+r*cos(i*2*pi/segments),s*y,cz+r*sin(i*2*pi/segments)) for y,r in profile for i in range(segments)]
    ff=[]
    for j in range(len(profile)-1):
        for i in range(segments):ff.append((j*segments+i,j*segments+(i+1)%segments,(j+1)*segments+(i+1)%segments,(j+1)*segments+i))
    ff.extend([tuple(reversed(range(segments))),tuple((len(profile)-1)*segments+i for i in range(segments))])
    ob=mesh(name,vv,ff,mat,gm)
    bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    for p in ob.data.polygons:p.use_smooth=len(p.vertices)==4
    return bind(ob,bone)
def beam(name,a,b,width,height,mat,bone):
    a,b=Vector(a),Vector(b);ob=box(name,(a+b)/2,(width,height,(b-a).length),mat,gm,.008);ob.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return bind(ob,bone)
for s,side in [(1,'l'),(-1,'r')]:
    z=.43746 if s>0 else .439526
    bone='wheel_front_'+side;steer='steer_front_'+side
    axle_lathe('AV | fusée et portée de roulement '+side,1.85,z,[(.70,.048),(.78,.048),(.80,.075),(.85,.075),(.87,.105),(.91,.105),(.925,.071),(.95,.071)],cast,bone,s)
    axle_lathe('AV | tambour et flasque de frein '+side,1.85,z,[(.785,.13),(.795,.192),(.81,.205),(.865,.205),(.875,.19),(.89,.13)],cast,bone,s)
    axle_lathe('AV | tôle pare-poussière '+side,1.85,z,[(.778,.20),(.783,.212),(.790,.212)],steel,steer,s)
    bind(box('AV | porte-fusée '+side,(1.85,s*.742,z),(.11,.09,.27),steel,gm,.022),steer)
    bind(rod('AV | pivot de direction '+side,(1.85,s*.742,z-.14),(1.85,s*.742,z+.14),.027,bolts,gm),steer)
    for i in range(8):
        a=i*2*pi/8
        bind(tube('AV | écrou intérieur '+side,[(1.85+.145*cos(a),s*.785,z+.145*sin(a)),(1.85+.145*cos(a),s*.774,z+.145*sin(a))],.009,bolts,gm,6),bone)
    # Portal beam remains BELOW the low central aisle.
    beam('AV | bras de liaison abaissé '+side,(1.85,s*.40,.245),(1.85,s*.64,z),.12,.10,steel,'chassis')
    beam('AV | chape de liaison '+side,(1.85,s*.64,z),(1.85,s*.742,z),.12,.10,steel,'chassis')
    for x in [1.61,2.09]:beam('AV | tirant longitudinal '+side,(x,s*.38,.25),(1.85,s*.68,.38),.038,.038,steel,'chassis')
    bind(rod('AV | amortisseur corps '+side,(2.13,s*.63,.36),(2.13,s*.63,.55),.026,steel,gm),'chassis')
    bind(rod('AV | amortisseur tige '+side,(2.13,s*.63,.55),(2.13,s*.63,.69),.011,bolts,gm),'chassis')
    bind(box('AV | ancrage haut amortisseur '+side,(2.13,s*.63,.70),(.12,.11,.035),steel,gm,.006),'chassis')
    # Rear wheel packs connect directly to their bearing hubs and axle shafts.
    bone='wheel_rear_'+side;z=.437458
    axle_lathe('AR | moyeu porteur jumelage '+side,-1.5,z,[(.47,.075),(.54,.075),(.56,.125),(.61,.125),(.635,.18),(.69,.18),(.71,.115),(.79,.115)],cast,bone,s)
    axle_lathe('AR | tambour de frein '+side,-1.5,z,[(.535,.13),(.55,.21),(.61,.215),(.65,.21),(.67,.13)],cast,bone,s)
    axle_lathe('AR | flasque fixe '+side,-1.5,z,[(.525,.205),(.53,.225),(.537,.225)],steel,'chassis',s)
    for i in range(8):
        a=i*2*pi/8
        bind(tube('AR | goujon moyeu '+side,[(-1.5+.15*cos(a),s*.538,z+.15*sin(a)),(-1.5+.15*cos(a),s*.526,z+.15*sin(a))],.010,bolts,gm,6),bone)
    for layer in range(4):
        span=.58-layer*.07
        pts=[(-1.5+t,s*.42,.30+.11*(t/span)**2-layer*.009) for t in np.linspace(-span,span,17)]
        # rectangular leaf sections, not decorative round tubes
        for a,b in zip(pts,pts[1:]):beam('AR | lame de suspension '+side,a,b,.065,.008,steel,'chassis')
    for x in [-2.08,-.92]:bind(box('AR | jumelle au longeron '+side,(x,s*.42,.335),(.075,.09,.16),steel,gm,.005),'chassis')
    bind(rod('AR | amortisseur '+side,(-1.65,s*.45,.34),(-1.90,s*.45,.61),.023,steel,gm),'chassis')
    for x in [-1.58,-1.42]:bind(tube('AR | bride de pont '+side,[(x,s*.36,.30),(x,s*.36,.50),(x,s*.48,.50),(x,s*.48,.30)],.009,bolts,gm,10),'chassis')
beam('AV | traverse abaissée sous allée',(1.85,-.42,.245),(1.85,.42,.245),.12,.09,steel,'chassis')
bind(rod('AV | biellette de direction',(1.98,-.73,.37),(1.98,.73,.37),.014,steel,gm),'chassis')
bind(rod('AR | carter de pont continu',(-1.5,-.57,.437458),(-1.5,.57,.437458),.065,steel,gm),'chassis')
# Faceted casting with concentric cover and fasteners for the differential.
bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,location=(-1.5,0,.43));ob=bpy.context.object;ob.scale=(.20,.22,.165);place(ob,'AR | carter de différentiel',cast,gm);bind(ob,'chassis')
for p in ob.data.polygons:p.use_smooth=True
bind(rod('AR | arbre de transmission',(-3.15,0,.48),(-1.68,0,.43),.034,steel,gm),'chassis')
for x in [-3.08,-1.76]:bind(box('AR | croisillon de cardan',(x,0,.45),(.075,.115,.11),cast,gm,.012),'chassis')
bpy.context.view_layer.update()
bpy.app.driver_namespace['V3_HUB_BONES']=created_bones
bpy.app.driver_namespace['V3_WHEEL']=dict(C=C,U=U,V=V,N=N,R=R)
print('Reference wheel topology rebuilt; mechanical hub and axle parts',len(created_bones))
