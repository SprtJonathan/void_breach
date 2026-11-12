import bpy, math, random, os
import numpy as np
from mathutils import Vector, Matrix
from math import sin, cos, pi

root=bpy.data.collections.new('INTERIEUR HD | Soreo5');bpy.context.scene.collection.children.link(root)
groups={}
def group(name):
    if name not in groups:
        c=bpy.data.collections.new('INT | '+name);root.children.link(c);groups[name]=c
    return groups[name]
def place(o,name,mat,g):
    o.name=name
    for c in list(o.users_collection):c.objects.unlink(o)
    group(g).objects.link(o)
    if mat:o.data.materials.append(mat)
    return o
def material(name,color,metal=0,rough=.5):
    m=bpy.data.materials.new('INT | '+name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    return m
ivory=material('Stratifié gris perle',(.52,.54,.53),0,.48)
dark=material('ABS anthracite',(.045,.052,.057),0,.6)
rubber=material('Caoutchouc grainé',(.018,.021,.024),0,.85)
silver=material('Aluminium satiné',(.37,.41,.43),.72,.29)
railmat=material('Main courante gris chaud',(.23,.26,.25),.35,.37)
red=material('Coque rouge brique',(.36,.025,.025),0,.42)
blue=material('Moquette bleue à motifs',(.018,.04,.22),0,.86)
floor=material('Sol caoutchouc moucheté',(.12,.15,.17),0,.83)
yellow=material('Sécurité jaune',(.9,.59,.025),0,.6)
white=material('Sérigraphie ivoire',(.78,.82,.76),0,.5)
lamp=material('Diffuseur opale',(.85,.87,.77),0,.33)
p=next(n for n in lamp.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Emission Color'].default_value=(.93,.96,.83,1);p.inputs['Emission Strength'].default_value=2
def texture(m,name,kind):
    rng=np.random.default_rng(41 if kind=='fabric' else 27);n=512
    yy,xx=np.mgrid[0:n,0:n];noise=rng.random((n,n))
    if kind=='fabric':
        col=np.zeros((n,n,3),np.float32);col[:]=(.035,.065,.255)
        weave=(np.sin(xx*pi)*.03+noise*.14)[...,None];col+=weave
        pattern=(((xx+yy*2)%97<5)&((yy%79)<34))
        col[pattern]=(.32,.23,.43)
        pattern=(((xx-yy)%123<4)&((yy%93)>65));col[pattern]=(.13,.38,.52)
    else:
        col=np.repeat((.19+noise*.11)[...,None],3,2);col*=np.array([.85,.95,1.0]);col[noise>.973]=(.49,.51,.49);col[noise<.04]=(.08,.1,.11)
    rgba=np.ones((n,n,4),np.float32);rgba[:,:,:3]=col
    im=bpy.data.images.new(name,width=n,height=n);im.pixels.foreach_set(rgba.ravel());im.pack()
    nt=m.node_tree;p=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED');tex=nt.nodes.new('ShaderNodeTexImage');tex.image=im
    nt.links.new(tex.outputs['Color'],p.inputs['Base Color'])
    bump=nt.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.14;bump.inputs['Distance'].default_value=.001
    nt.links.new(tex.outputs['Color'],bump.inputs['Height']);nt.links.new(bump.outputs['Normal'],p.inputs['Normal'])
texture(blue,'Soreo5_fabric_blue_512', 'fabric');texture(floor,'Soreo5_rubber_fleck_512','floor')
def mesh(name,verts,faces,mat,g,bevel=0):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(name,me);group(g).objects.link(o)
    if mat:me.materials.append(mat)
    if bevel:
        m=o.modifiers.new('Congés moulés','BEVEL');m.width=bevel;m.segments=3
        o.modifiers.new('Normales de surface','WEIGHTED_NORMAL')
    return o
def box(name,loc,size,mat,g,bevel=.006):
    x,y,z=[s/2 for s in size]
    vs=[(-x,-y,-z),(-x,-y,z),(-x,y,-z),(-x,y,z),(x,-y,-z),(x,-y,z),(x,y,-z),(x,y,z)]
    o=mesh(name,vs,[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)],mat,g,bevel);o.location=loc;return o
def tube(name,points,r,mat,g,sides=12):
    pts=[Vector(p) for p in points];vs=[]
    for i,p in enumerate(pts):
        tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        a=tangent.cross(Vector((0,0,1)))
        if a.length<.01:a=tangent.cross(Vector((1,0,0)))
        a.normalize();b=tangent.cross(a).normalized()
        vs.extend([tuple(p+r*(cos(j*2*pi/sides)*a+sin(j*2*pi/sides)*b)) for j in range(sides)])
    fs=[]
    for k in range(len(pts)-1):
        for j in range(sides):fs.append((k*sides+j,k*sides+(j+1)%sides,(k+1)*sides+(j+1)%sides,(k+1)*sides+j))
    fs.extend([tuple(reversed(range(sides))),tuple((len(pts)-1)*sides+j for j in range(sides))])
    o=mesh(name,vs,fs,mat,g)
    for p in o.data.polygons:p.use_smooth=len(p.vertices)==4
    return o
def rod(name,a,b,r,mat,g):return tube(name,[a,b],r,mat,g)
def text(name,body,loc,size,mat,g,rot=None):
    cu=bpy.data.curves.new(name,'FONT');cu.body=body;cu.size=size;cu.extrude=.0003;cu.align_x='CENTER'
    o=bpy.data.objects.new(name,cu);group(g).objects.link(o);o.location=loc;cu.materials.append(mat)
    if rot:o.rotation_euler=rot
    return o

# Continuous low-floor access and two steps, entirely behind the middle doorway.
box('Plancher bas raccordé aux seuils',(1.31,0,.335),(4.38,2.04,.06),floor,'01 Planchers')
box('Marche intermédiaire',(-1.035,0,.45),(.31,2.04,.23),floor,'01 Planchers')
box('Estrade arrière',(-2.40,0,.685),(2.42,2.04,.09),floor,'01 Planchers')
box('Contremarche arrière',(-1.205,0,.55),(.035,2.04,.36),ivory,'01 Planchers')
for x,z in [(-.89,.567),(-1.205,.733)]:
    box('Nez de marche aluminium',(x,0,z),(.045,2.02,.009),silver,'01 Planchers',.002)
    box('Bande de vigilance',(x+.008,0,z+.005),(.025,2,.004),yellow,'01 Planchers',.001)
for s in [-1,1]:
    box('Passage de roue AV / socle de banquette',(1.81,s*.775,.69),(1.18,.52,.65),ivory,'01 Planchers',.04)
    box('Passage de roue AR sous assises',(-1.53,s*.775,.862),(1.15,.52,.285),ivory,'01 Planchers',.035)
    box('Repose-pieds avant',(1.54,s*.455,.65),(1.68,.16,.055),floor,'01 Planchers',.012)
    rod('Profil aluminium repose-pieds',( .71,s*.373,.675),(2.37,s*.373,.675),.009,silver,'01 Planchers')
    box('Socle banquette arrière',(-2.52,s*.80,.88),(1.1,.42,.31),ivory,'01 Planchers',.012)
box('Coffre moteur intérieur',(-3.41,0,.92),(.47,2.01,.38),ivory,'01 Planchers',.028)

# Wall panels stop precisely at the exterior window sill and avoid both doors.
for s in [-1,1]:
    spans=[(-3.58,-.86),(.46,2.42)] if s==-1 else [(-3.58,2.37)]
    for a,b in spans:
        box('Doublage sous baie',((a+b)/2,s*1.024,.846),(b-a,.046,.958),ivory,'02 Parois',.012)
        box('Appui de baie',((a+b)/2,s*1.008,1.319),(b-a,.083,.032),dark,'02 Parois')
        box('Plinthe basse',((a+b)/2,s*.995,.41),(b-a,.026,.087),dark,'02 Parois')
        for x in np.arange(a+.1,b,.6):
            box('Joint de panneau',(float(x),s*.998,.87),(.003,.003,.83),dark,'02 Parois',0)
    pillars=[-3.34,-2.09,-.86,.46,2.42] if s==-1 else [-3.34,-2.01,-.71,.645,2.01,2.37]
    for x in pillars:
        box('Garniture de montant alignée',(x,s*1.039,1.84),(.038,.04,1.00),ivory,'02 Parois')
    box('Bandeau supérieur intérieur',(-.52,s*1.02,2.33),(5.70,.055,.14),ivory,'02 Parois')
box('Dos de coffre arrière',(-3.64,0,1.42),(.055,1.98,.62),ivory,'02 Parois',.02)
box('Tablette sous lunette',(-3.65,0,1.75),(.18,1.83,.045),dark,'02 Parois')

# Flat ceiling, recessed diffusers and hatch liners aligned to the two roof hatches.
box('Raccord plat avant',(3.344,0,2.471),(.31,2.055,.042),ivory,'03 Plafond',.003)
box('Raccord plat arrière',(-3.735,0,2.471),(.17,1.72,.042),ivory,'03 Plafond',.003)
box('Retombée intérieure girouette',(3.50,0,2.35),(.035,1.98,.20),ivory,'03 Plafond',.004)
for i in range(9):
    x=-3.27+i*.76
    box('Panneau de plafond plat %02d'%i,(x,0,2.471),(.754,2.055,.042),ivory,'03 Plafond',.003)
for s in [-1,1]:
    box('Jonction plafond / paroi',(-.25,s*1.017,2.428),(6.8,.05,.07),ivory,'03 Plafond')
    for x in [-2.98,-1.88,-.78,.32,1.42,2.52]:
        box('Cadre luminaire encastré',(x,s*.73,2.441),(.75,.15,.024),silver,'03 Plafond')
        box('Diffuseur prismatique',(x,s*.73,2.424),(.70,.116,.016),lamp,'03 Plafond',.008)
        for dx in [-.36,.36]:box('Embout luminaire',(x+dx,s*.73,2.423),(.026,.15,.023),ivory,'03 Plafond')
for x in [-1.9,1.542]:
    box('Trappe de secours / joint',(x,0,2.441),(.70,.737,.018),dark,'03 Plafond')
    box('Trappe intérieure affleurante',(x,0,2.431),(.65,.687,.018),ivory,'03 Plafond')
    rod('Poignée de trappe',(x-.12,-.10,2.397),(x+.12,-.10,2.397),.012,red,'03 Plafond')
for s in [-1,1]:
    for x in [-2.7,.8,2.65]:
        box('Grille ventilation plafond',(x,s*.93,2.429),(.30,.08,.015),dark,'03 Plafond')
        for i in range(9):box('Lamelle ventilation',(x-.12+i*.03,s*.93,2.418),(.009,.075,.006),ivory,'03 Plafond',.001)

# Seat module: curved and tapered back, rounded padding, red shell and hand grip.
def seat(name,pos,angle,width=.46,base=1.055):
    before=set(group('04 Sellerie').objects);g='04 Sellerie'
    box(name+' | coque assise',(0,0,.025),(.44,width,.055),red,g,.025)
    box(name+' | mousse assise',(.018,0,.073),(.425,width-.036,.089),blue,g,.039)
    # front faces +X; tapered back leans 8 cm toward the rear at the top
    verts=[]
    for z,x,w in [(.04,-.195,width/2),(.27,-.23,width/2),(.59,-.265,width*.455)]:
        verts.extend([(x-.027,-w,z),(x+.027,-w,z),(x+.027,w,z),(x-.027,w,z)])
    faces=[(3,2,1,0),(8,9,10,11)]
    for k in range(2):
        for j in range(4):faces.append((k*4+j,k*4+(j+1)%4,(k+1)*4+(j+1)%4,(k+1)*4+j))
    mesh(name+' | dossier galbé',verts,faces,red,g,.024)
    o=box(name+' | textile dossier',(-.202,0,.335),(.075,width-.055,.475),blue,g,.032);o.rotation_euler.y=-.135
    tube(name+' | poignée', [(-.264,-width*.34,.58),(-.27,-width*.34,.637),(-.27,-width*.28,.67),(-.27,width*.28,.67),(-.27,width*.34,.637),(-.264,width*.34,.58)],.014,railmat,g)
    for y in [-width*.36,width*.36]:
        rod(name+' | fixation',(-.15,y,-.015),(.13,y,-.015),.014,silver,g)
    R=Matrix.Rotation(angle,4,'Z');T=Matrix.Translation(Vector((pos[0],pos[1],base)))
    bpy.context.view_layer.update()
    for o in set(group(g).objects)-before:o.matrix_world=T@R@o.matrix_world

for s in [-1,1]:
    for i,x in enumerate([.92,1.43,1.94]):seat('AV %s %02d'%(s,i),(x,s*.76),-s*pi/2,base=1.025)
    for i,x in enumerate([-1.35,-1.835,-2.32,-2.805]):seat('U côté %s %02d'%(s,i),(x,s*.76),-s*pi/2,base=1.045)
for i,y in enumerate([-.735,-.245,.245,.735]):seat('U traverse %02d'%i,(-3.32,y),0,base=1.045)

# Vertical rails leave the center aisle and both doorway sweeps unobstructed.
for s in [-1,1]:
    rod('Main courante haute',(-3.1,s*.46,2.16),(2.38,s*.46,2.16),.016,railmat,'05 Barres')
    for x in [-3.05,-1.10,.64,2.37]:
        z=.73 if x<-.9 else .37
        tube('Montant cintré',[(x,s*.40,z),(x,s*.40,2.05),(x,s*.405,2.105),(x,s*.43,2.15),(x,s*.46,2.16)],.017,railmat,'05 Barres')
        rod('Rosace de pied',(x,s*.40,z),(x,s*.40,z+.024),.037,silver,'05 Barres')
        rod('Collier de jonction',(x-.025,s*.46,2.16),(x+.025,s*.46,2.16),.023,dark,'05 Barres')
    for x in [-2.7,-2.15,-1.65,.92,1.47,1.98]:
        rod('Suspente plafond',(x,s*.46,2.16),(x,s*.46,2.45),.011,railmat,'05 Barres')
    for x in [.64,-1.1]:
        box('Boîtier arrêt demandé',(x,s*.377,1.40),(.06,.04,.105),dark,'05 Barres',.015)
        box('Bouton rouge',(x,s*.351,1.415),(.037,.012,.043),red,'05 Barres',.012)
for x in [-.35,.15]:
    rod('Attache de poignée',(x,.46,2.16),(x,.46,2.02),.011,dark,'05 Barres')
    pts=[(x+.07*cos(a),.46,1.947+.075*sin(a)) for a in np.linspace(0,2*pi,33)]
    tube('Poignée suspendue',pts,.012,dark,'05 Barres')

# Accessible bay opposite the center door, folded seats and padded leaning support.
for x in [-.48,.04]:
    box('Strapontin relevé | coque',(x,.955,.88),(.43,.085,.58),red,'06 PMR',.035)
    box('Strapontin relevé | coussin',(x,.899,.87),(.385,.07,.51),blue,'06 PMR',.025)
    rod('Axe de strapontin',(x-.21,.88,.60),(x+.21,.88,.60),.023,silver,'06 PMR')
box('Appui PMR vertical',(-.69,.70,1.13),(.13,.32,.52),blue,'06 PMR',.04)
rod('Barre PMR',(-.75,.65,.40),(-.75,.65,1.44),.017,railmat,'06 PMR')
rod('Prise murale PMR',(-.5,.88,1.47),(.35,.88,1.47),.016,railmat,'06 PMR')
print('Interior envelope, seating, rails completed',sum(len(c.objects) for c in groups.values()))
bpy.app.driver_namespace['INT_BUILD']=globals().copy()
