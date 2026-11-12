import bpy,math,os,json
import numpy as np
from mathutils import Vector,Matrix
globals().update(bpy.app.driver_namespace['INT_BUILD'])

# Floor cutouts and hollow seat plinths clear the immutable tire meshes.
for o in list(group('01 Planchers').objects):
    if o.name in ['Plancher bas raccordé aux seuils','Estrade arrière'] or o.name.startswith('Passage de roue'):
        bpy.data.objects.remove(o,do_unlink=True)
g='01 Planchers'
for name,a,b,hw,z in [('avant central',-.88,3.50,.50,.335),('arrière central',-3.61,-1.19,.50,.685)]:
    box('Plancher '+name,((a+b)/2,0,z),(b-a,hw*2,.06 if z<.5 else .09),floor,g,.003)
for s in [-1,1]:
    for a,b,z in [(-.88,1.235,.335),(2.405,3.50,.335),(-3.61,-2.11,.685)]:
        box('Plancher latéral hors passage',((a+b)/2,s*.76,z),(b-a,.52,.06 if z<.5 else .09),floor,g,.003)
    # No bottom or outboard faces through the tire envelopes.
    for tag,a,b,zbot in [('AV',.68,2.405,.365),('AR',-3.07,-.94,.73)]:
        top=1.015 if tag=='AV' else 1.035
        box('Socle '+tag+' | dessus',((a+b)/2,s*.775,top-.025),(b-a,.53,.05),ivory,g,.014)
        box('Socle '+tag+' | joue allée',((a+b)/2,s*.51,(zbot+top)/2),(b-a,.025,top-zbot),ivory,g,.012)
        for x in [a+.012,b-.012]:box('Socle '+tag+' | joue extrémité',(x,s*.775,(zbot+top)/2),(.025,.53,top-zbot),ivory,g,.010)
        for x in np.arange(a+.13,b-.05,.55):
            rod('Vis socle',(float(x),s*.492,top-.09),(float(x),s*.486,top-.09),.0045,silver,g)
    for x in [-2.65,-2.2]:
        box('Grille chauffage sous banquette',(x,s*.489,.90),(.32,.011,.15),dark,g,.007)
        for zz in [.846,.87,.894,.918,.942]:box('Lamelle chauffage',(x,s*.480,zz),(.29,.01,.007),silver,g,.002)

# Small, readable cabin details with geometry suited to normal-map baking later.
g='10 Finitions'
for s in [-1,1]:
    for x in [-3.0,-2.25,-1.5,.8,1.6,2.2]:
        rod('Fixation appui de baie',(x,s*.96,1.319),(x,s*.96,1.323),.004,silver,g)
    for x in [-2.4,1.2]:
        box('Support marteau de secours',(x,s*1.006,1.88),(.052,.028,.17),dark,g,.009)
        rod('Marteau de secours | manche',(x,s*.978,1.82),(x,s*.978,1.91),.010,red,g)
        rod('Marteau de secours | tête',(x-.033,s*.978,1.925),(x+.033,s*.978,1.925),.012,red,g)
    # Heat-welded floor seam; entirely outside the walking strip on the front risers.
    rod('Jonction sol',( -.84,s*.31,.367),(2.40,s*.31,.367),.0015,dark,g)
box('Afficheur arrêt demandé',(-1.01,0,2.31),(.065,.48,.115),dark,g,.01)
o=text('ARRET DEMANDE','ARRET DEMANDE',(-.974,0,2.293),.032,red,g)
# Font local X toward +Y; local Y +Z; visible toward +X.
o.rotation_euler=Matrix(((0,0,1),(1,0,0),(0,1,0))).to_euler()
box('Plaque priorité',(-.35,.994,1.57),(.36,.012,.12),blue,g,.005)
o=text('Places prioritaires','PLACES PRIORITAIRES',(-.35,.985,1.567),.018,white,g,rot=(pi/2,0,0))
box('Plaque trappe arrière',(-3.604,0,1.67),(.006,.30,.055),silver,g,.003)
o=text('Accès moteur','ACCES MOTEUR',(-3.598,0,1.655),.014,dark,g);o.rotation_euler=Matrix(((0,0,1),(1,0,0),(0,1,0))).to_euler()
# Extinguisher at the back of the driver partition, facing the saloon.
rod('Extincteur | bouteille',(2.195,.88,.49),(2.195,.88,.83),.058,red,g)
rod('Extincteur | col',(2.195,.88,.83),(2.195,.88,.89),.023,dark,g)
rod('Extincteur | poignée',(2.16,.88,.90),(2.23,.88,.90),.009,dark,g)
tube('Extincteur | flexible',[(2.20,.88,.88),(2.12,.88,.87),(2.12,.88,.62)],.009,rubber,g)
box('Extincteur | étiquette',(2.135,.88,.68),(.004,.062,.13),white,g,.002)
box('Extincteur | bride',(2.21,.88,.60),(.13,.14,.025),dark,g,.008)

# UVs in meters and image normal maps: no procedural dependency for GLTF.
def normal_image(name,scale):
    n=512;rng=np.random.default_rng(73);h=rng.random((n,n))
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*scale;dy=(np.roll(h,-1,0)-np.roll(h,1,0))*scale
    vv=np.stack([-dx,-dy,np.ones_like(dx)],axis=2);vv/=np.linalg.norm(vv,axis=2)[...,None]
    rgba=np.ones((n,n,4),np.float32);rgba[:,:,:3]=vv*.5+.5
    im=bpy.data.images.new(name,width=n,height=n);im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(rgba.ravel());im.pack();return im
grain=normal_image('Soreo5_micrograin_normal_512',.18)
cloth=normal_image('Soreo5_textile_normal_512',.5)
for mat in [blue,floor,dark,rubber,ivory,red,railmat]:
    nt=mat.node_tree;p=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
    for node in list(nt.nodes):
        if node.type=='BUMP':nt.nodes.remove(node)
    tex=nt.nodes.new('ShaderNodeTexImage');tex.image=cloth if mat==blue else grain
    nor=nt.nodes.new('ShaderNodeNormalMap');nor.inputs['Strength'].default_value=.40 if mat==blue else .22
    nt.links.new(tex.outputs['Color'],nor.inputs['Color']);nt.links.new(nor.outputs['Normal'],p.inputs['Normal'])

# Apply finishing modifiers and create local planar UVs on all export geometry.
obs=[o for c in groups.values() for o in c.objects]
bpy.ops.object.select_all(action='DESELECT')
for o in obs:o.select_set(True)
bpy.context.view_layer.objects.active=obs[0]
bpy.ops.object.convert(target='MESH')
bpy.context.view_layer.update()
for o in obs:
    if o.type!='MESH':continue
    me=o.data
    uv=me.uv_layers.new(name='UVMap') if not me.uv_layers else me.uv_layers.active
    for poly in me.polygons:
        dominant=max(range(3),key=lambda i:abs(poly.normal[i]));axes=[i for i in range(3) if i!=dominant]
        mat=me.materials[poly.material_index] if me.materials else None
        tiling=2.4 if mat==blue else (1.8 if mat==floor else 4)
        for li in poly.loop_indices:
            co=me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv=(co[axes[0]]*tiling,co[axes[1]]*tiling)

# Consolidate each functional group; wheel remains separate and fully weighted.
rig=bpy.data.objects['Soreo5_Rig']
for name,col in groups.items():
    meshes=[o for o in col.objects if o.type=='MESH']
    if not meshes:continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join()
    o=bpy.context.object;o.name='Soreo5 INT | '+name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    # Remove duplicate material slots without changing face assignments.
    mats=list(o.data.materials);unique=[];mapping={}
    for i,m in enumerate(mats):
        if m not in unique:unique.append(m)
        mapping[i]=unique.index(m)
    indices=[mapping[p.material_index] for p in o.data.polygons]
    o.data.materials.clear()
    for m in unique:o.data.materials.append(m)
    for p,i in zip(o.data.polygons,indices):p.material_index=i
    vg=o.vertex_groups.new(name='steering_wheel' if name=='08 Volant Renault' else 'chassis');vg.add(list(range(len(o.data.vertices))),1,'REPLACE')
    mod=o.modifiers.new('Soreo5 rig existant','ARMATURE');mod.object=rig;o.parent=rig
    o['interior_revision']='2026-11-12';o['export_lod']=0
print('UVs, PBR images and consolidation complete')
bpy.app.driver_namespace['INT_BUILD']=globals().copy()
