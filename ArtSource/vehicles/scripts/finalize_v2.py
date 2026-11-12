import bpy,os,json,bmesh
from mathutils import Vector
ns=bpy.app.driver_namespace['INT_V2'];rig=bpy.data.objects['Soreo5_Rig']
for name in ['01 Planchers','04 Sellerie','08 Volant Renault']:
    col=bpy.data.collections['INT | '+name];obs=list(col.objects)
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:o.select_set(True)
    bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.convert(target='MESH')
    for o in list(col.objects):
        me=o.data;uv=me.uv_layers.new(name='UVMap') if not me.uv_layers else me.uv_layers.active
        for p in me.polygons:
            # Wheel uses its own local plane for stable, fine-grained polyurethane UV.
            axis=max(range(3),key=lambda i:abs(p.normal[i]));axes=[i for i in range(3) if i!=axis]
            mat=me.materials[p.material_index] if me.materials else None
            scale=2.4 if mat and 'Moquette' in mat.name else (1.8 if mat and 'moucheté' in mat.name else 4)
            for li in p.loop_indices:
                v=me.vertices[me.loops[li].vertex_index].co;uv.data[li].uv=(v[axes[0]]*scale,v[axes[1]]*scale)
    bpy.ops.object.select_all(action='DESELECT')
    for o in col.objects:o.select_set(True)
    bpy.context.view_layer.objects.active=list(col.objects)[0];bpy.ops.object.join();o=bpy.context.object;o.name='Soreo5 INT | '+name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    mats=list(o.data.materials);unique=[];mapping={}
    for i,m in enumerate(mats):
        if m not in unique:unique.append(m)
        mapping[i]=unique.index(m)
    inds=[mapping[p.material_index] for p in o.data.polygons];o.data.materials.clear()
    for m in unique:o.data.materials.append(m)
    for p,i in zip(o.data.polygons,inds):p.material_index=i
    vg=o.vertex_groups.new(name='steering_wheel' if name=='08 Volant Renault' else 'chassis');vg.add(list(range(len(o.data.vertices))),1,'REPLACE')
    o.parent=rig;arm=o.modifiers.new('Soreo5 rig existant','ARMATURE');arm.object=rig;o['interior_revision']='2026-11-12 v2'

# Verify absence of interior faces crossing the open wheel cavities. Centroids and
# edge midpoints are tested in the outboard volume. The intentional inner caps are exempt.
violations=[]
for objname in ['Soreo5 INT | 01 Planchers','Soreo5 INT | 02 Parois','Soreo5 INT | 04 Sellerie']:
    o=bpy.data.objects[objname]
    for p in o.data.polygons:
        pts=[o.matrix_world@o.data.vertices[i].co for i in p.vertices]
        samples=[sum(pts,Vector())/len(pts)]+[(pts[i]+pts[(i+1)%len(pts)])/2 for i in range(len(pts))]
        for v in samples:
            if .510<abs(v.y)<1.10 and .27<v.z<.978:
                if any((v.x-cx)**2+max(v.z-.454,0)**2<.523**2 for cx in [1.85,-1.5]):
                    violations.append((o.name,p.index,tuple(v)));break
print('WHEEL CAVITY VIOLATIONS',len(violations),violations[:5])
if violations:raise RuntimeError('Interior still intersects wheel cavity')

# Refresh all LOD1 meshes after the revised geometry is complete.
lc=bpy.data.collections['EXPORT | Intérieur LOD1'];lc.hide_viewport=False
for o in list(lc.objects):bpy.data.objects.remove(o,do_unlink=True)
for src in bpy.data.collections['INTERIEUR HD | Soreo5'].all_objects:
    if src.type!='MESH':continue
    o=src.copy();o.data=src.data.copy();o.name=src.name.replace(' INT |',' LOD1 |');lc.objects.link(o)
    for m in list(o.modifiers):o.modifiers.remove(m)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    m=o.modifiers.new('LOD1','DECIMATE');m.ratio=.37 if '08 Volant' not in src.name else .55;bpy.ops.object.modifier_apply(modifier=m.name)
    m=o.modifiers.new('Soreo5 rig','ARMATURE');m.object=rig;o['export_lod']=1
lc.hide_viewport=True;lc.hide_render=True
cc=bpy.data.collections['EXPORT | Collisions intérieures simples'];cc.hide_viewport=False
for o in list(cc.objects):
    if 'socle_av' in o.name:
        s=1 if o.location.y>0 else -1;o.location=(1.85,s*.78,.70);o.dimensions=(1.06,.55,.58)
    elif 'socle_ar' in o.name:o.dimensions.z+=.16;o.location.z+=.08
    elif 'dossier_ar' in o.name or 'dossier_traverse' in o.name:o.location.z+=.16
    elif 'dossier_av' in o.name:
        s=1 if o.location.y>0 else -1;o.location=(2.1,s*.77,1.40);o.dimensions=(.09,.46,.66)
cc.hide_viewport=True;cc.hide_render=True
snapshot=bpy.app.driver_namespace['exterior_fingerprint'];sig=bpy.app.driver_namespace['exterior_sig']
changed=[n for n,h in snapshot.items() if n not in bpy.data.objects or sig(bpy.data.objects[n])!=h]
assert not changed,changed
print('Exterior unchanged:',len(snapshot))
bpy.context.scene.camera=bpy.data.objects['01 | Entrée avant vers la salle']
bpy.ops.object.select_all(action='DESELECT')
