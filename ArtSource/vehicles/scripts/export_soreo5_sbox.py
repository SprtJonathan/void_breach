# -*- coding: utf-8 -*-
"""Soreo5 -> s&box / ModelDoc (FBX binaire).

Blender 5.x : ouvrir le .blend, passer dans Scripting, ouvrir ce fichier, Run Script.
N'utilise AUCUN bpy.app.driver_namespace / script de construction externe.
Le .blend d'origine n'est pas enregistre ni modifie (copies de travail temporaires).

IMPORTANT : le FBX principal contient le BUS COMPLET, pas uniquement l'interieur.

Usage courant : enregistrer soreo5_770_220_273.blend, puis lancer
Mettre_a_jour_Soreo5_Sbox.cmd. L'export recalcule les normales des solides,
les materiaux et les positions des roues, puis compile dans l'editeur ouvert.
Le rapport et les sauvegardes sont dans exports/soreo5_sbox.
"""
import bpy
import bmesh
import json
import os
import re
import shutil
import struct
import traceback
import hashlib
import importlib
import sys
import zlib
from collections import Counter
from datetime import datetime
from pathlib import Path
from mathutils import Matrix, Vector

# ================== PARAMETRES A MODIFIER SI BESOIN ==================
# Le chemin du FBX existant est calcule depuis l'emplacement de ce script.
FBX_EXISTANT_SBOX = str(Path(__file__).resolve().parents[3] / 'Assets' / 'vehicles' / 'soreo5' / 'soreo5.fbx')

# Par securite, une faute dans le chemin ci-dessus ne cree pas un nouveau FBX.
EXIGER_FBX_EXISTANT = True

# Exports supplementaires (ces fichiers ne remplacent PAS le bus complet).
EXPORTER_LOD1_INTERIEUR = False
EXPORTER_COLLISIONS_INTERIEURES = False
EXPORTER_TEXTURES_PNG = False

# Ne modifier ces axes qu'en fonction de la configuration de ton FBX original.
# D'apres les scripts, l'avant du bus est +X et le haut +Z.
AXE_AVANT = 'X'
AXE_HAUT = 'Z'
AXE_OS_PRIMAIRE = 'Y'
AXE_OS_SECONDAIRE = 'X'

# Uniquement sur les copies d'interieur : reproduit le nettoyage du script agent.
# Les modificateurs de carrosserie restent conserves.
NETTOYER_MODIFICATEURS_INTERIEURS = True

RIG_NAME = 'Soreo5_Rig'
INTERIOR_NAME = 'INTERIEUR HD | Soreo5'
LOD1_NAME = 'EXPORT | Intérieur LOD1'
COLLISION_NAME = 'EXPORT | Collisions intérieures simples'
EXPORT_DIR_NAME = 'soreo5_sbox'
MATERIALS = {}
# ====================================================================


def log(message):
    print('[SOREO5 FBX] ' + str(message), flush=True)


def collection_objects_recursive(collection):
    return {o for o in collection.all_objects}


def export_material(source):
    """Use stable ASCII FBX names; keep the Blender material untouched."""
    if source.name in MATERIALS:
        return MATERIALS[source.name][0]
    name = 'soreo5_mat_' + hashlib.sha1(source.name.encode('utf-8')).hexdigest()[:12]
    copy = source.copy()
    copy.name = name
    shader = next((n for n in source.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if source.use_nodes and source.node_tree else None
    color = list(shader.inputs['Base Color'].default_value[:3]) if shader else list(source.diffuse_color[:3])
    roughness = float(shader.inputs['Roughness'].default_value) if shader else 0.7
    metallic = float(shader.inputs['Metallic'].default_value) if shader else 0.0
    MATERIALS[source.name] = (copy, name, color, roughness, metallic)
    return copy


def linked_image(socket, visited=None):
    """Follow the shader graph without depending on localized node names."""
    visited = set() if visited is None else visited
    for link in socket.links:
        node = link.from_node
        if node in visited:
            continue
        visited.add(node)
        if node.type == 'TEX_IMAGE' and node.image:
            return node.image
        for input_socket in node.inputs:
            image = linked_image(input_socket, visited)
            if image:
                return image
    return None


def rigid_bone(obj):
    """Resolve the rebuilt wheel, steering-wheel and hub objects on export copies."""
    if obj.get('sbox_bone'):
        return obj['sbox_bone']
    if any(c.name == 'INT | 08 Volant Renault' for c in obj.users_collection):
        return 'steering_wheel'
    groups = [g.name for g in getattr(obj, 'vertex_groups', [])
              if g.name in bpy.data.objects[RIG_NAME].data.bones]
    if len(groups) == 1:
        return groups[0]
    side = re.search(r' ([lr])(?:\.\d+)?$', obj.name)
    if side:
        suffix = side.group(1)
        if obj.name.startswith('AV |'):
            if any(s in obj.name for s in ('fusée et portée', 'tambour et flasque', 'écrou intérieur')):
                return 'wheel_front_' + suffix
            if any(s in obj.name for s in ('tôle pare-poussière', 'porte-fusée', 'pivot de direction')):
                return 'steer_front_' + suffix
        if obj.name.startswith('AR |') and any(s in obj.name for s in ('moyeu porteur', 'tambour de frein', 'goujon moyeu')):
            return 'wheel_rear_' + suffix
    return None if len(groups) > 1 else 'chassis'


def orient_closed_surfaces(mesh):
    """Point each solid's faces outward, retaining its corner UVs and weights."""
    bm = bmesh.new()
    try:
        bm.from_mesh(mesh)
        bm.faces.ensure_lookup_table()
        original = {face: face.normal.copy() for face in bm.faces}
        remaining = set(bm.faces)
        solids = []
        while remaining:
            component = {remaining.pop()}
            pending = list(component)
            while pending:
                face = pending.pop()
                for edge in face.edges:
                    for neighbor in edge.link_faces:
                        if neighbor in remaining:
                            remaining.remove(neighbor)
                            component.add(neighbor)
                            pending.append(neighbor)
            if all(edge.is_manifold for face in component for edge in face.edges):
                solids.extend(component)
        bmesh.ops.recalc_face_normals(bm, faces=solids)
        flipped = sum(face.normal.dot(original[face]) < -.5 for face in solids)
        if flipped:
            bm.to_mesh(mesh)
            # Imported custom loop normals must follow the repaired face winding.
            if mesh.has_custom_normals:
                mesh.normals_split_custom_set([(0, 0, 0)] * len(mesh.loops))
            mesh.update()
        return flipped
    finally:
        bm.free()


def write_materials_and_remaps():
    target_dir = Path(FBX_EXISTANT_SBOX).parent
    matdir = target_dir / 'generated_materials'
    matdir.mkdir(exist_ok=True)
    remaps = []
    for source_name, (_, name, color, roughness, metallic) in sorted(MATERIALS.items()):
        if source_name.startswith('tripo_node_'):
            destination = 'vehicles/soreo5/soreo5_body.vmat'
        else:
            # PNG stores display-encoded RGB; Blender's shader colors are linear.
            def srgb(c):
                c = max(0.0, min(1.0, c))
                return int(round(255 * (12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055)))
            def chunk(kind, data):
                return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
            raw = bytes([0] + [srgb(c) for c in color])
            png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')
            (matdir / (name + '.png')).write_bytes(png)
            destination = 'vehicles/soreo5/generated_materials/' + name + '.vmat'
            source = bpy.data.materials[source_name]
            shader = next((n for n in source.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if source.use_nodes else None
            inputs = []
            if shader:
                for socket, channel in [('Base Color', 'Color'), ('Normal', 'Normal')]:
                    image = linked_image(shader.inputs[socket])
                    if image and image.has_data:
                        filename = name + ('_color.png' if channel == 'Color' else '_normal.png')
                        copied = image.copy()
                        try:
                            copied.filepath_raw = str(matdir / filename)
                            copied.file_format = 'PNG'
                            copied.save()
                        finally:
                            bpy.data.images.remove(copied)
                        inputs.append('\tTexture%s "vehicles/soreo5/generated_materials/%s"' % (channel, filename))
            if not any(line.startswith('\tTextureColor ') for line in inputs):
                inputs.append('\tTextureColor "vehicles/soreo5/generated_materials/%s.png"' % name)
            alpha = float(shader.inputs['Alpha'].default_value) if shader else source.diffuse_color[3]
            transmission = float(shader.inputs['Transmission Weight'].default_value) if shader else 0
            glass = alpha < .99 or transmission > .1
            if glass:
                # In s&box this input becomes Material.Opacity directly. A high
                # value paints an opaque grey layer over the scenery.
                opacity = min(alpha, .055) if transmission < .1 else .035
                value = int(round(255 * opacity))
                trans = bytes([0, value, value, value])
                (matdir / (name + '_trans.png')).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(trans)) + chunk(b'IEND', b''))
                inputs.extend(['\tF_GLASS_QUALITY "1"', '\tF_RENDER_BACKFACES "1"', '\tg_flBlurAmount "0"', '\tTextureTranslucency "vehicles/soreo5/generated_materials/%s_trans.png"' % name])
            vmat = 'Layer0\n{\n\tshader "shaders/%s.shader"\n%s\n\tg_flRoughnessScaleFactor "%s"\n\tg_flMetalness "%s"\n}\n' % ('glass' if glass else 'complex', '\n'.join(inputs), roughness, metallic)
            (matdir / (name + '.vmat')).write_text(vmat, encoding='utf-8')
        remaps.append('\t\t\t\t\t\t\t{ from = "%s.vmat" to = "%s" },' % (name, destination))
    vmdl = target_dir / 'soreo5.vmdl'
    content = vmdl.read_text(encoding='utf-8')
    pattern = r'(remaps\s*=\s*\[)(.*?)(\])'
    match = re.search(pattern, content, re.S)
    if not match:
        raise RuntimeError('Remaps introuvables dans le ModelDoc : ' + str(vmdl))
    legacy = '''
                            { from = "soreo5_body.vmat" to = "vehicles/soreo5/soreo5_body.vmat" },
                            { from = "soreo5_cabin.vmat" to = "vehicles/soreo5/soreo5_cabin.vmat" },
                            { from = "soreo5_glass.vmat" to = "vehicles/soreo5/soreo5_glass.vmat" },'''
    updated = content[:match.start(2)] + legacy + '\n' + '\n'.join(remaps) + '\n\t\t\t\t\t\t' + content[match.end(2):]
    updated = re.sub(r'import_scale\s*=\s*[\d.]+', 'import_scale = 39.37007874015748', updated)
    vmdl.write_text(updated, encoding='utf-8')
    return len(remaps)


def collect_complete_meshes():
    """Collections fonctionnelles 00..19 + interieur HD, sans archives/studio."""
    interior = bpy.data.collections.get(INTERIOR_NAME)
    if interior is None:
        raise RuntimeError('Collection introuvable : ' + INTERIOR_NAME)
    interior_objs = collection_objects_recursive(interior)
    numbered = [c for c in bpy.data.collections
                if re.match(r'^(?:0\d|1\d)(?:\.\d+)?\s*\|', c.name)]
    if not numbered:
        raise RuntimeError('Aucune collection 00..19 detectee : export integral impossible.')
    numbered_objs = set()
    for col in numbered:
        numbered_objs |= collection_objects_recursive(col)

    # Certaines pieces du fichier sont conservees dans des collections d'archive.
    # Les objets egalement lies a une collection ACTIVE restent autorises.
    accepted = numbered_objs | interior_objs
    selected = []
    skipped_hidden = []
    skipped_helper = []
    for obj in sorted(accepted, key=lambda o: o.name):
        if obj.type not in {'MESH', 'CURVE', 'FONT'}:
            continue
        name = obj.name.upper()
        if name.startswith(('TEMP ', 'COL_INT_', 'CUTTER ', 'COLLISION_')):
            skipped_helper.append(obj.name)
            continue
        # Ne pas ajouter les anciens objets explicitement masques au rendu.
        if obj.hide_render:
            skipped_hidden.append(obj.name)
            continue
        selected.append(obj)
    if len(selected) < 30:
        raise RuntimeError('Seulement %d meshes ! Refus de produire un FBX incomplet.' % len(selected))
    if not any(o in interior_objs for o in selected):
        raise RuntimeError('Aucun mesh interieur trouve dans le FBX complet.')
    return selected, interior_objs, {
        'collections_numerotees': [c.name for c in sorted(numbered, key=lambda c: c.name)],
        'ignored_hidden_render': skipped_hidden,
        'ignored_helpers': skipped_helper,
        'meshes_sans_uv': [o.name for o in selected if o.type == 'MESH' and not o.data.uv_layers],
    }


def export_fbx_isole(meshes, destination, rigged, interior_objects, report):
    """Copie les donnees, recible le rig, exporte, puis retire les copies.

    Ni l'armature source, ni les maillages sources, ni les collections d'origine
    ne sont modifies. Conserve les transformations monde et les noms des os.
    """
    source_scene = bpy.context.window.scene
    source_rig = bpy.data.objects.get(RIG_NAME)
    if rigged and (source_rig is None or source_rig.type != 'ARMATURE'):
        raise RuntimeError('Armature absente : ' + RIG_NAME)

    # Capturer AVANT le changement de scene les matrices evaluees des originaux.
    original_world = {o: o.matrix_world.copy() for o in meshes}
    if rigged:
        original_world[source_rig] = source_rig.matrix_world.copy()

    staging = bpy.data.scenes.new('TEMP_SOREO5_EXPORT')
    copies = {}
    new_rig = None
    registered = []
    try:
        bpy.context.window.scene = staging
        if rigged:
            new_rig = source_rig.copy()
            new_rig.data = source_rig.data.copy()
            new_rig.animation_data_clear()
            staging.collection.objects.link(new_rig)
            registered.append(new_rig)
            new_rig.hide_viewport = False
            new_rig.hide_render = False
            new_rig.hide_set(False)
            new_rig.data.pose_position = 'REST'
            for bone in new_rig.pose.bones:
                bone.matrix_basis.identity()
            new_rig.matrix_world = original_world[source_rig]

        for src in meshes:
            duplicate = src.copy()
            duplicate.data = src.data.copy()
            if not duplicate.data.materials or any(m is None for m in duplicate.data.materials):
                fallback = bpy.data.materials.get('Soreo5 export fallback') or bpy.data.materials.new('Soreo5 export fallback')
                fallback.diffuse_color = (0.5, 0.5, 0.5, 1.0)
                safe_fallback = export_material(fallback)
                if not duplicate.data.materials:
                    duplicate.data.materials.append(safe_fallback)
                for index, material in enumerate(duplicate.data.materials):
                    if material is None:
                        duplicate.data.materials[index] = safe_fallback
            for index, material in enumerate(duplicate.data.materials):
                if material and not material.name.startswith('soreo5_mat_'):
                    duplicate.data.materials[index] = export_material(material)
            staging.collection.objects.link(duplicate)
            registered.append(duplicate)
            duplicate.hide_viewport = False
            duplicate.hide_render = False
            duplicate.hide_set(False)
            copies[src] = duplicate

        # Convert text and filled curves in the staging scene, including the new
        # horn pad. They must share the deform bones used by the game.
        bpy.context.view_layer.update()
        for src, obj in list(copies.items()):
            if obj.type != 'MESH':
                curve_data = obj.data
                bpy.ops.object.select_all(action='DESELECT')
                obj.select_set(True)
                bpy.context.view_layer.objects.active = obj
                bpy.ops.object.convert(target='MESH')
                copies[src] = bpy.context.object
                if curve_data.users == 0:
                    bpy.data.curves.remove(curve_data)

        bones = set(new_rig.data.bones.keys()) if new_rig else set()
        if rigged and 'chassis' not in bones:
            raise RuntimeError('Os chassis absent. Export annule pour eviter un rig incompatible.')
        added_chassis = []
        missing_weights = []
        special_parent = []
        removed_mods = 0
        repaired_faces = {}
        for src, obj in copies.items():
            parent = src.parent
            new_parent = (new_rig if parent == source_rig else copies.get(parent)) if rigged else None
            if parent is not None and new_parent is None:
                special_parent.append(src.name)
            # Recrer la hierarchie uniquement si le parent est dans la scene.
            obj.parent = new_parent
            if new_parent:
                obj.parent_type = src.parent_type
                obj.parent_bone = src.parent_bone
                obj.matrix_parent_inverse = src.matrix_parent_inverse.copy()
            else:
                obj.parent_type = 'OBJECT'
                obj.matrix_parent_inverse = Matrix.Identity(4)
            obj.matrix_world = original_world[src]

            for modifier in list(obj.modifiers):
                if modifier.type == 'ARMATURE':
                    if rigged:
                        modifier.object = new_rig
                    else:
                        obj.modifiers.remove(modifier)
                elif src in interior_objects and NETTOYER_MODIFICATEURS_INTERIEURS:
                    obj.modifiers.remove(modifier)
                    removed_mods += 1

            flipped = orient_closed_surfaces(obj.data)
            if flipped:
                repaired_faces[src.name] = flipped

            if rigged and src.parent_type != 'BONE':
                bone_name = rigid_bone(src)
                if bone_name:
                    if bone_name not in bones:
                        raise RuntimeError('Os inconnu pour %s : %s' % (src.name, bone_name))
                    obj.vertex_groups.clear()
                    group = obj.vertex_groups.new(name=bone_name)
                    group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
                # Si le maillage possede deja un groupage de bones, le respecter.
                linked_groups = set(vg.name for vg in obj.vertex_groups) & bones
                has_armature = any(m.type == 'ARMATURE' for m in obj.modifiers)
                if not linked_groups:
                    # Seule la copie temporaire est affectee a chassis.
                    vgroup = obj.vertex_groups.get('chassis') or obj.vertex_groups.new(name='chassis')
                    if len(obj.data.vertices):
                        vgroup.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
                    added_chassis.append(src.name)
                if not has_armature:
                    mod = obj.modifiers.new('Soreo5 Export Skin', 'ARMATURE')
                    mod.object = new_rig
                # Detecter les groupes sans sommet pondere (information, pas de correction).
                if linked_groups and len(obj.data.vertices):
                    if not any(v.groups for v in obj.data.vertices):
                        missing_weights.append(src.name)
                        group = obj.vertex_groups.get('chassis') or obj.vertex_groups.new(name='chassis')
                        group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
                        added_chassis.append(src.name)

        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action='DESELECT')
        selected = list(copies.values()) + ([new_rig] if new_rig else [])
        for o in selected:
            o.hide_set(False)
            o.select_set(True)
        bpy.context.view_layer.objects.active = new_rig or selected[0]

        # Les proprietes varient suivant les versions de Blender : filtrage.
        exporter_properties = bpy.ops.export_scene.fbx.get_rna_type().properties
        valid = {p.identifier for p in exporter_properties}
        kwargs = dict(
            filepath=str(destination),
            check_existing=False,
            use_selection=True,
            object_types={'MESH', 'ARMATURE'} if rigged else {'MESH'},
            axis_forward=AXE_AVANT,
            axis_up=AXE_HAUT,
            primary_bone_axis=AXE_OS_PRIMAIRE,
            secondary_bone_axis=AXE_OS_SECONDAIRE,
            add_leaf_bones=False,
            use_armature_deform_only=False,
            bake_anim=False,
            global_scale=1.0,
            apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_UNITS',
            use_space_transform=True,
            bake_space_transform=False,
            use_mesh_modifiers=True,
            use_triangles=True,
            mesh_smooth_type='FACE',
            path_mode='COPY',
            embed_textures=False,
        )
        supported_kwargs = {k: v for k, v in kwargs.items() if k in valid}
        # Seul use_triangles manque parfois selon la version ; dans ce cas,
        # la triangulation est faite sur les COPIES, jamais sur le modele source.
        if 'use_triangles' not in valid:
            for o in copies.values():
                m = o.modifiers.new('Triangulation FBX', 'TRIANGULATE')
                m.keep_custom_normals = True
        log('Export de %d maillages %s -> %s' %
            (len(copies), '(+ rig)' if rigged else '(sans rig)', destination))
        result = bpy.ops.export_scene.fbx(**supported_kwargs)
        if 'FINISHED' not in result:
            raise RuntimeError('L\'exporteur FBX a retourne : ' + str(result))
        report.update({
            'meshes': len(copies),
            'bones': len(bones),
            'bone_names': sorted(bones) if rigged else [],
            'forced_chassis_weights': added_chassis,
            'unweighted_group_warnings': missing_weights,
            'parents_non_exportes': special_parent,
            'modificateurs_interieur_retires': removed_mods,
            'faces_reorientees': repaired_faces,
            'exporter_options': {k: (sorted(v) if isinstance(v, set) else v)
                                 for k, v in supported_kwargs.items() if k != 'filepath'},
        })
    finally:
        # Toujours revenir a la scene originale meme en cas d'erreur.
        bpy.context.window.scene = source_scene
        for obj in registered:
            if obj.name in bpy.data.objects:
                mesh_data = obj.data if obj.type == 'MESH' else None
                arm_data = obj.data if obj.type == 'ARMATURE' else None
                bpy.data.objects.remove(obj, do_unlink=True)
                if mesh_data is not None and mesh_data.users == 0:
                    bpy.data.meshes.remove(mesh_data)
                if arm_data is not None and arm_data.users == 0:
                    bpy.data.armatures.remove(arm_data)
        bpy.data.scenes.remove(staging)


def verify_binary_fbx(filepath):
    p = Path(filepath)
    if not p.is_file() or p.stat().st_size < 1024:
        raise RuntimeError('FBX absent ou trop petit : ' + str(p))
    with p.open('rb') as f:
        header = f.read(27)
    if not header.startswith(b'Kaydara FBX Binary'):
        raise RuntimeError('Le fichier n\'est pas un FBX binaire : ' + str(p))
    version = struct.unpack('<I', header[23:27])[0]
    return {'path': str(p), 'bytes': p.stat().st_size, 'fbx_version': version}


def update_fbx_sbox(exported_path):
    text = FBX_EXISTANT_SBOX.strip()
    if not text:
        log('Aucun FBX S&box indique : fichier produit, AUCUN fichier du projet remplace.')
        return None
    target = Path(text).expanduser()
    if target.suffix.lower() != '.fbx':
        raise RuntimeError('FBX_EXISTANT_SBOX doit etre un chemin complet vers un .fbx')
    if not target.parent.is_dir():
        raise RuntimeError('Dossier cible introuvable : ' + str(target.parent))
    if EXIGER_FBX_EXISTANT and not target.is_file():
        raise RuntimeError('FBX cible introuvable (protection anti-erreur) : ' + str(target))
    if target.resolve() == Path(exported_path).resolve():
        return {'target': str(target), 'backup': None}
    backup = None
    if target.is_file():
        stamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
        # Mettre la sauvegarde HORS des Assets s&box pour ne pas l'importer aussi.
        backup = Path(exported_path).parent / (target.stem + '.backup_' + stamp + target.suffix)
        shutil.copy2(target, backup)
        log('Sauvegarde du FBX precedent : ' + str(backup))
    pending = target.with_name(target.stem + '.__soreo5_pending__.tmp')
    try:
        shutil.copy2(exported_path, pending)
        verify_binary_fbx(pending)
        os.replace(str(pending), str(target))
    finally:
        if pending.exists():
            pending.unlink()
    log('FBX S&box mis a jour : ' + str(target))
    return {'target': str(target), 'backup': str(backup) if backup else None}


def export_textures(meshes, texdir):
    texdir.mkdir(exist_ok=True, parents=True)
    textures = {}
    warnings = []
    for obj in meshes:
        for mat in obj.data.materials:
            if mat is None or not mat.use_nodes:
                continue
            for node in mat.node_tree.nodes:
                if node.type == 'TEX_IMAGE' and node.image:
                    textures[node.image.name] = node.image
    saved = []
    for name, im in sorted(textures.items()):
        if not im.has_data:
            warnings.append('Image sans pixels disponibles : ' + name)
            continue
        safe_name = re.sub(r'[^\w.\-]+', '_', name, flags=re.UNICODE)
        safe_name = re.sub(r'\.(png|jpg|jpeg|tga|bmp)$', '', safe_name, flags=re.I)
        pathname = texdir / (safe_name + '.png')
        copied = None
        try:
            copied = im.copy()
            copied.filepath_raw = str(pathname)
            copied.file_format = 'PNG'
            copied.save()
            saved.append(str(pathname))
        except Exception as exc:
            warnings.append('Image %s : %s' % (name, exc))
        finally:
            if copied is not None:
                bpy.data.images.remove(copied)
    return {'png_files': saved, 'warnings': warnings}


def main():
    if not bpy.data.filepath:
        raise RuntimeError('Enregistre d\'abord ton fichier .blend (File > Save As).')
    if bpy.context.window is None:
        raise RuntimeError('Execute le script dans l\'interface Blender > Scripting.')
    if not hasattr(bpy.ops.export_scene, 'fbx'):
        raise RuntimeError('Exporteur FBX indisponible : active le module io_scene_fbx.')

    root = Path(bpy.data.filepath).parent / 'exports' / EXPORT_DIR_NAME
    root.mkdir(parents=True, exist_ok=True)
    main_meshes, interior_objects, info = collect_complete_meshes()
    report = {
        'blend_source': bpy.data.filepath,
        'blender_version': bpy.app.version_string,
        'date': datetime.now().isoformat(timespec='seconds'),
        'mode': 'BUS COMPLET (00..19 + interieur HD, pas les archives)',
        'inventaire': info,
        'exports': {},
    }
    log('%d meshes du bus complet selectionnes ; %d meshes d\'interieur.' %
        (len(main_meshes), sum(o in interior_objects for o in main_meshes)))
    temp_main = root / 'soreo5_complet.__export_temp__.fbx'
    final_main = root / 'soreo5_complet.fbx'
    try:
        export_fbx_isole(main_meshes, temp_main, True, interior_objects,
                         report['exports'].setdefault('principal', {}))
        report['exports']['principal']['file'] = verify_binary_fbx(temp_main)
        os.replace(str(temp_main), str(final_main))
        report['exports']['principal']['file']['path'] = str(final_main)
        log('OK : ' + str(final_main))

        if EXPORTER_LOD1_INTERIEUR:
            col = bpy.data.collections.get(LOD1_NAME)
            if col:
                lods = sorted((o for o in col.all_objects if o.type == 'MESH'), key=lambda o: o.name)
                if lods:
                    output = root / 'soreo5_interieur_LOD1.fbx'
                    export_fbx_isole(lods, output, True, set(),
                                     report['exports'].setdefault('interieur_lod1', {}))
                    report['exports']['interieur_lod1']['file'] = verify_binary_fbx(output)
                    report['exports']['interieur_lod1']['limite'] = 'LOD interieur uniquement, pas un LOD complet du bus'
            else:
                log('LOD1 non trouve : export LOD1 ignore.')
        if EXPORTER_COLLISIONS_INTERIEURES:
            col = bpy.data.collections.get(COLLISION_NAME)
            if col:
                collision_meshes = sorted((o for o in col.all_objects if o.type == 'MESH'), key=lambda o: o.name)
                if collision_meshes:
                    output = root / 'soreo5_collisions_interieures.fbx'
                    export_fbx_isole(collision_meshes, output, False, set(),
                                     report['exports'].setdefault('collisions', {}))
                    report['exports']['collisions']['file'] = verify_binary_fbx(output)
            else:
                log('Collection collisions absente : export collisions ignore.')
        if EXPORTER_TEXTURES_PNG:
            report['textures'] = export_textures(main_meshes, root / 'textures')
        report['materiaux_sbox'] = write_materials_and_remaps()
        report['remplacement_sbox'] = update_fbx_sbox(final_main)
        script_dir = str(Path(__file__).resolve().parent)
        if script_dir not in sys.path:
            sys.path.insert(0, script_dir)
        import sync_soreo5_sbox
        importlib.reload(sync_soreo5_sbox)
        report['vehicule_sbox'] = sync_soreo5_sbox.synchronize(main_meshes)
    except Exception:
        report['erreur'] = traceback.format_exc()
        log(report['erreur'])
        raise
    finally:
        for copied, *_ in MATERIALS.values():
            if copied.users == 0:
                bpy.data.materials.remove(copied)
        MATERIALS.clear()
        if temp_main.exists():
            try:
                temp_main.unlink()
            except OSError:
                pass
        with (root / 'rapport_export.json').open('w', encoding='utf-8') as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        log('Rapport : ' + str(root / 'rapport_export.json'))
    log('TERMINE. Export, materiaux et prefab mis a jour. Source Blender conservee.')


if __name__ == '__main__':
    main()
