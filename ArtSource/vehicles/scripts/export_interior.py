import bpy,os,json,math
from mathutils import Vector
globals().update(bpy.app.driver_namespace['INT_BUILD'])
base=os.path.dirname(bpy.data.filepath);out=os.path.join(base,'exports','soreo5_interior_hd');os.makedirs(out,exist_ok=True)
texdir=os.path.join(out,'textures');os.makedirs(texdir,exist_ok=True)
for im in bpy.data.images:
    if im.name.startswith('Soreo5_') and im.has_data:
        im.filepath_raw=os.path.join(texdir,im.name+'.png');im.file_format='PNG';im.save();im.pack()
lod0=[o for o in bpy.data.collections['INTERIEUR HD | Soreo5'].all_objects if o.type=='MESH']
lc=bpy.data.collections.new('EXPORT | Intérieur LOD1');bpy.context.scene.collection.children.link(lc)
lod1=[]
for src in lod0:
    o=src.copy();o.data=src.data.copy();o.name=src.name.replace(' INT |',' LOD1 |');lc.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    for mod in list(o.modifiers):o.modifiers.remove(mod)
    m=o.modifiers.new('Réduction LOD1','DECIMATE');m.ratio=.37 if '08 Volant' not in src.name else .55
    bpy.ops.object.modifier_apply(modifier=m.name)
    arm=o.modifiers.new('Soreo5 rig existant','ARMATURE');arm.object=rig
    o['export_lod']=1;lod1.append(o)
def count(obs):return sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs)
def export_meshes(obs,path,withrig=True):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:o.select_set(True)
    if withrig:rig.select_set(True)
    bpy.context.view_layer.objects.active=obs[0]
    formats=[i.identifier for i in bpy.ops.export_scene.gltf.get_rna_type().properties['export_format'].enum_items]
    fmt=next((i for i in formats if i=='GLB'),'GLB')
    bpy.ops.export_scene.gltf(filepath=path,export_format=fmt,use_selection=True,export_animations=False,export_cameras=False,export_lights=False,export_skins=withrig,export_texcoords=True,export_normals=True,export_tangents=True,export_extras=True)
export_meshes(lod0,os.path.join(out,'soreo5_interior_LOD0.glb'))
export_meshes(lod1,os.path.join(out,'soreo5_interior_LOD1.glb'))
lc.hide_render=True;lc.hide_viewport=True

cc=bpy.data.collections.new('EXPORT | Collisions intérieures simples');bpy.context.scene.collection.children.link(cc)
cols=[]
def col(name,loc,size):
    o=box('COL_INT_'+name,loc,size,None,'Collisions temporaires',0)
    for c in list(o.users_collection):c.objects.unlink(o)
    cc.objects.link(o);o.display_type='WIRE';o['collision']='convex_box';cols.append(o)
col('plancher_bas',(1.31,0,.335),(4.38,2.04,.06))
col('marche',(-1.035,0,.45),(.31,2.04,.23))
col('plancher_haut',(-2.4,0,.685),(2.42,2.04,.09))
col('coffre_arriere',(-3.41,0,.92),(.47,2.01,.38))
for s in [-1,1]:
    col('socle_av_%s'%s,(1.54,s*.77,.72),(1.73,.52,.72))
    col('socle_ar_%s'%s,(-2.0,s*.77,.89),(2.14,.52,.32))
    col('dossier_av_%s'%s,(1.43,s*.99,1.37),(1.5,.07,.66))
    col('dossier_ar_%s'%s,(-2.14,s*.99,1.40),(2.0,.07,.66))
col('dossier_traverse',(-3.56,0,1.40),(.07,1.99,.66))
col('cabine',(2.9,.60,.85),(1.25,.83,.95))
col('plafond',(-.24,0,2.48),(6.82,2.05,.06))
export_meshes(cols,os.path.join(out,'soreo5_interior_collision.glb'),False)
cc.hide_render=True;cc.hide_viewport=True

# Verify every exterior mesh against the snapshot taken before the interior was rebuilt.
snapshot=bpy.app.driver_namespace['exterior_fingerprint'];sig=bpy.app.driver_namespace['exterior_sig']
changed=[name for name,h in snapshot.items() if name not in bpy.data.objects or sig(bpy.data.objects[name])!=h]
if changed:raise RuntimeError('Exterior modified: '+str(changed))
stats={'lod0_triangles':count(lod0),'lod1_triangles':count(lod1),'collision_boxes':len(cols),'collision_triangles':count(cols),'exterior_objects_verified_unchanged':len(snapshot),'interior_objects_lod0':len(lod0),'materials':len({m for o in lod0 for m in o.data.materials}),'units':'meters','axis_front':'+X','wheel_bone':'steering_wheel','floor_height':.365,'rear_floor_height':.73,'ceiling_underside':2.45}
with open(os.path.join(out,'validation.json'),'w',encoding='utf-8') as f:json.dump(stats,f,indent=2)
report='''SOREO5 — Intérieur HD, 12 novembre 2026

L'extérieur, les roues, les portes et le squelette existants ont été conservés.
301 objets extérieurs contrôlés par empreinte de géométrie, matrice et affectations de matériaux.
Ancien intérieur : collection ARCHIVE masquée, et sauvegarde .blend séparée.

Plafond horizontal à 2,45 m, luminaires à diffuseur et trappes alignées au pavillon.
Plancher bas à 0,365 m ; deux marches derrière la porte centrale ; estrade arrière à 0,73 m.
Passages avant servant de socles aux sièges, avec repose-pieds.
6 places longitudinales avant, 12 places en U arrière, 2 strapontins et poste conducteur.
Volant reconstruit d'après les photos Renault, conservant l'os steering_wheel.
UV tuilées, textures couleur et normales PNG embarquées. Aucun shader procédural requis.
Les caméras et lumières sont destinées à la présentation Blender uniquement.

Fichiers GLB : intérieur seul LOD0, LOD1, collisions convexes séparées.
Les deux LOD conservent le rig original ; ne pas importer simultanément deux squelettes
comme deux véhicules. Rattacher les meshes au rig du véhicule lors de l'intégration.
Le GLB ne remplace pas la configuration d'import du moteur : assigner LOD et collisions,
vérifier les transparences et les distances de transition dans le moteur cible.
Les collisions représentent les volumes intérieurs et ne remplacent pas la carrosserie.
LOD1 issu d'une réduction géométrique ; le contrôle final à distance reste à faire en jeu.
Dimensions et allure du volant déduites des photographies, sans relevé constructeur.

'''+json.dumps(stats,indent=2,ensure_ascii=False)
with open(os.path.join(out,'LISEZMOI.txt'),'w',encoding='utf-8') as f:f.write(report)
txt=bpy.data.texts.new('Intérieur HD | livraison et vérifications');txt.write(report)
print(json.dumps(stats))
bpy.app.driver_namespace['INT_EXPORT']=stats
