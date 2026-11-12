import bpy,bmesh,math
from mathutils import Vector
ns=bpy.app.driver_namespace['INT_V2']
globals().update(ns)
for fn in [group,mesh,box,tube,rod,text]:
    state=fn.__globals__
    state['root']=bpy.data.collections['INTERIEUR HD | Soreo5']
    state['groups']={c.name[6:]:c for c in bpy.data.collections if c.name.startswith('INT | ')}

# Move joined trim islands without changing their UVs or bone weights.
o=bpy.data.objects['Soreo5 INT | 10 Finitions'];me=o.data
adj=[[] for v in me.vertices]
for e in me.edges:
    a,b=e.vertices;adj[a].append(b);adj[b].append(a)
seen=set();moved=[]
for v in me.vertices:
    if v.index in seen:continue
    ids=[];stack=[v.index];seen.add(v.index)
    while stack:
        i=stack.pop();ids.append(i)
        for j in adj[i]:
            if j not in seen:seen.add(j);stack.append(j)
    vs=[o.matrix_world@me.vertices[i].co for i in ids]
    lo=Vector([min(p[i] for p in vs) for i in range(3)]);hi=Vector([max(p[i] for p in vs) for i in range(3)]);c=(lo+hi)/2
    delta=None
    if 1.78<lo.z and hi.z<1.98 and abs(c.y)>.96 and (abs(c.x+2.4)<.05 or abs(c.x-1.2)<.05):
        side=1 if c.y>0 else -1
        old=-2.4 if c.x<0 else 1.2
        target=(-2.01 if old<0 else 2.01) if side>0 else (-2.09 if old<0 else 2.42)
        delta=Vector((target-old,side*.017,0));moved.append(('hammer',old,target,side))
    elif 2.10<lo.x and hi.x<2.28 and .80<lo.y and hi.y<.96 and hi.z<.92:
        delta=Vector((.30,-.63,0));moved.append(('extinguisher',len(ids)))
    elif -.54<lo.x and hi.x<-.16 and .98<lo.y and 1.50<lo.z and hi.z<1.64:
        delta=Vector((0,0,-.34))
    if delta:
        local=o.matrix_world.inverted().to_3x3()@delta
        for i in ids:me.vertices[i].co+=local
me.update()

# Split cabin into disconnected parts so overlapping trim islands cannot confuse
# the exact Boolean. Only the three pieces intersecting the arch are cut.
cab=bpy.data.objects['Soreo5 INT | 07 Cabine'];col=bpy.data.collections['INT | 07 Cabine']
bpy.ops.object.select_all(action='DESELECT');cab.select_set(True);bpy.context.view_layer.objects.active=cab
for m in list(cab.modifiers):cab.modifiers.remove(m)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.separate(type='LOOSE');bpy.ops.object.mode_set(mode='OBJECT')
def penetrates(o):
    for p in o.data.polygons:
        pts=[o.matrix_world@o.data.vertices[i].co for i in p.vertices]
        samples=pts+[sum(pts,Vector())/len(pts)]+[(pts[i]+pts[(i+1)%len(pts)])/2 for i in range(len(pts))]
        for v in samples:
            if .503<abs(v.y)<1.10 and .268<v.z<.98 and (v.x-1.85)**2+max(v.z-.454,0)**2<.525**2:return True
    return False
cutters=[]
for s in [-1,1]:
    profile=[(1.85+.530*math.cos(i*math.pi/64),.454+.530*math.sin(i*math.pi/64)) for i in range(65)]+[(1.32,.20),(2.38,.20)]
    verts=[(x,s*y,z) for y in [.503,1.16] for x,z in profile];n=len(profile)
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    cut=mesh('TEMP cabine wheel cutter',verts,faces,None,'TEMP cutters')
    bm=bmesh.new();bm.from_mesh(cut.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(cut.data);bm.free();cutters.append(cut)
bpy.context.view_layer.update();cut_count=0
for obj in list(col.objects):
    if not penetrates(obj):continue
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    for cut in cutters:
        m=obj.modifiers.new('Découpe exacte arche extérieure','BOOLEAN');m.operation='DIFFERENCE';m.solver='EXACT';m.object=cut;bpy.ops.object.modifier_apply(modifier=m.name)
    cut_count+=1
for cut in cutters:bpy.data.objects.remove(cut,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in col.objects:obj.select_set(True)
bpy.context.view_layer.objects.active=cab;bpy.ops.object.join();cab.name='Soreo5 INT | 07 Cabine';cab.parent=bpy.data.objects['Soreo5_Rig'];m=cab.modifiers.new('Soreo5 rig existant','ARMATURE');m.object=cab.parent
print('Trim moved',moved,'Cabin parts cut',cut_count)

# Complete scene-wide cavity audit, including trim, cab and rail hardware.
violations=[]
for obj in bpy.data.collections['INTERIEUR HD | Soreo5'].all_objects:
    if obj.type!='MESH':continue
    for p in obj.data.polygons:
        pts=[obj.matrix_world@obj.data.vertices[i].co for i in p.vertices]
        samples=pts+[sum(pts,Vector())/len(pts)]+[(pts[i]+pts[(i+1)%len(pts)])/2 for i in range(len(pts))]
        for v in samples:
            if .510<abs(v.y)<1.10 and .27<v.z<.978 and any((v.x-cx)**2+max(v.z-.454,0)**2<.523**2 for cx in [1.85,-1.5]):
                violations.append((obj.name,p.index,tuple(v)));break
print('ALL INTERIOR WHEEL CAVITY VIOLATIONS',len(violations),violations[:10])
bpy.app.driver_namespace['V3_CAVITY_AUDIT']=violations
