import bpy,os,json,struct
base=os.path.dirname(bpy.data.filepath);out=os.path.join(base,'exports','soreo5_interior_hd')
source_scene=bpy.context.scene;source_rig=bpy.data.objects['Soreo5_Rig']
def export_clean(collection,filename,skin):
    # Isolated scene keeps archived/out-of-scope armature modifiers out of the exporter.
    stage=bpy.data.scenes.new('TEMP | Export intérieur');bpy.context.window.scene=stage
    rig=None
    if skin:
        rig=source_rig.copy();rig.data=source_rig.data.copy();rig.animation_data_clear();stage.collection.objects.link(rig)
    created=[]
    for src in bpy.data.collections[collection].objects:
        if src.type!='MESH':continue
        o=src.copy();o.data=src.data.copy();o.name=src.name;stage.collection.objects.link(o);created.append(o)
        o.hide_set(False);o.hide_viewport=False;o.hide_render=False
        o.parent=rig
        for m in list(o.modifiers):o.modifiers.remove(m)
        bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
        tri=o.modifiers.new('Triangulation export','TRIANGULATE');tri.keep_custom_normals=True
        bpy.ops.object.modifier_apply(modifier=tri.name)
        if rig:
            m=o.modifiers.new('Skeleton','ARMATURE');m.object=rig
    # The main collection contains child collections; collect them separately.
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=os.path.join(out,filename),export_format='GLB',use_selection=True,export_animations=False,export_cameras=False,export_lights=False,export_skins=skin,export_texcoords=True,export_normals=True,export_tangents=True,export_extras=True)
    bpy.context.window.scene=source_scene
    for o in created:bpy.data.objects.remove(o,do_unlink=True)
    if rig:
        data=rig.data;bpy.data.objects.remove(rig,do_unlink=True);bpy.data.armatures.remove(data)
    bpy.data.scenes.remove(stage)

# Link LOD0 into a temporary flat collection for deterministic export.
flat=bpy.data.collections.new('TEMP | LOD0 flat')
for o in bpy.data.collections['INTERIEUR HD | Soreo5'].all_objects:
    if o.type=='MESH':flat.objects.link(o)
try:
    export_clean(flat.name,'soreo5_interior_LOD0.glb',True)
    export_clean('EXPORT | Intérieur LOD1','soreo5_interior_LOD1.glb',True)
    export_clean('EXPORT | Collisions intérieures simples','soreo5_interior_collision.glb',False)
finally:
    bpy.context.window.scene=source_scene
    bpy.data.collections.remove(flat)
reports={}
for fn in ['soreo5_interior_LOD0.glb','soreo5_interior_LOD1.glb','soreo5_interior_collision.glb']:
    with open(os.path.join(out,fn),'rb') as f:
        magic,version,length=struct.unpack('<4sII',f.read(12));size,kind=struct.unpack('<II',f.read(8));doc=json.loads(f.read(size))
    primitives=[p for m in doc.get('meshes',[]) for p in m['primitives']]
    reports[fn]={'meshes':len(doc.get('meshes',[])),'primitives':len(primitives),'skins':len(doc.get('skins',[])),'embedded_images':len(doc.get('images',[])),'triangles':sum(doc['accessors'][p['indices']]['count']//3 for p in primitives),'all_normals':all('NORMAL' in p['attributes'] for p in primitives),'all_uvs':all('TEXCOORD_0' in p['attributes'] for p in primitives),'all_tangents':all('TANGENT' in p['attributes'] for p in primitives),'bytes':os.path.getsize(os.path.join(out,fn))}
    assert magic==b'glTF' and version==2
    if 'collision' not in fn:
        assert reports[fn]['meshes']==10 and reports[fn]['all_uvs'] and reports[fn]['all_tangents']
with open(os.path.join(out,'glb_validation.json'),'w',encoding='utf-8') as f:json.dump(reports,f,indent=2)
print(json.dumps(reports))
