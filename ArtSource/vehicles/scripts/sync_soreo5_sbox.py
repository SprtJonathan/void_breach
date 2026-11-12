"""Measure the saved bus and update its existing s&box prefab after FBX export."""
import json
import math
import os
import shutil
import subprocess
import tempfile
import uuid
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

INCHES = 1 / .0254
PROJECT = Path(__file__).resolve().parents[3]
PREFAB = 'vehicles/soreo5/soreo5.prefab'


def vector(value):
    return ','.join(format(float(v), '.9g') for v in value)


def bounds(obj):
    # Disabled helper collections have stale matrix_world values in Blender.
    def matrix(item):
        local = Matrix.LocRotScale(item.location, item.rotation_euler.to_quaternion(), item.scale)
        return matrix(item.parent) @ local if item.parent else local
    points = [matrix(obj) @ Vector(v) for v in obj.bound_box]
    return [Vector(min(v[i] for v in points) for i in range(3)),
            Vector(max(v[i] for v in points) for i in range(3))]


def cli_path():
    engine = os.environ.get('FACEPUNCH_ENGINE')
    if not engine and os.name == 'nt':
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, 'Environment') as key:
                engine = winreg.QueryValueEx(key, 'FACEPUNCH_ENGINE')[0]
        except OSError:
            pass
    candidate = Path(engine) / 'bin/managed/sbox-cli.exe' if engine else None
    if candidate and candidate.is_file():
        return str(candidate)
    return shutil.which('sbox-cli')


class Editor:
    """Use stdin so large prefab documents never hit Windows' argument limit."""
    def __init__(self, cli):
        self.errors = tempfile.TemporaryFile()
        self.process = subprocess.Popen(
            [cli, '--request-timeout', '120', '--connect', str(PROJECT)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.errors,
            text=True, encoding='utf-8', creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        self.sequence = 0
        try:
            self.request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                       'clientInfo': {'name': 'soreo5-export', 'version': '1'}})
            self.send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
        except Exception:
            self.close()
            raise

    def send(self, message):
        self.process.stdin.write(json.dumps(message, ensure_ascii=False) + '\n')
        self.process.stdin.flush()

    def request(self, method, params):
        self.sequence += 1
        self.send({'jsonrpc': '2.0', 'id': self.sequence, 'method': method, 'params': params})
        while True:
            line = self.process.stdout.readline()
            if not line:
                self.errors.seek(0)
                raise RuntimeError(self.errors.read().decode('utf-8', 'replace'))
            reply = json.loads(line)
            if reply.get('id') != self.sequence:
                continue
            if 'error' in reply:
                raise RuntimeError(str(reply['error']))
            return reply['result']

    def call(self, name, arguments):
        result = self.request('tools/call', {'name': name, 'arguments': arguments})
        output = '\n'.join(c['text'] for c in result.get('content', []) if c['type'] == 'text')
        if result.get('isError'):
            raise RuntimeError(name + ': ' + output)
        if 'structuredContent' in result:
            return result['structuredContent']
        try:
            return json.loads(output)
        except ValueError:
            return output

    def close(self):
        if self.process.stdin and not self.process.stdin.closed:
            self.process.stdin.close()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.terminate()  # Only our attached CLI, never the editor.
        self.errors.close()


def update_prefab(document, meshes):
    root = document['RootObject']
    def objects(obj):
        yield obj
        for child in obj.get('Children', []):
            yield from objects(child)
    by_name = {o['Name']: o for o in objects(root)}
    def component(obj, name):
        return next(c for c in obj['Components'] if c['__type'].split('.')[-1] == name)
    renderer = by_name['Soreo5 mesh']
    offset = Vector(float(v) for v in renderer['Position'].split(','))
    def point(v):
        return Vector(v) * INCHES + offset
    def set_position(obj, v):
        obj['Position'] = vector(point(v))
    rig = bpy.data.objects['Soreo5_Rig']
    bone = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}
    # Preserve the asset's existing +Y FBX orientation and matching renderer yaw.
    renderer['Rotation'] = '0,0,-0.707106781,0.707106781'
    renderer['Scale'] = '1,1,1'
    measurements = {}
    gravity = 850.0
    for name, key in [('FrontLeft', 'wheel_front_l'), ('FrontRight', 'wheel_front_r'),
                      ('RearLeft', 'wheel_rear_l'), ('RearRight', 'wheel_rear_r')]:
        wheel = by_name[name]
        settings = component(wheel, 'VBVehicleWheel')
        visual = next(o for o in meshes if o.type == 'MESH' and o.vertex_groups.get(key))
        low, high = bounds(visual)
        radius = (high.z - low.z) * .5
        frequency = settings.get('SuspensionFrequency', 1.7)
        travel = settings.get('SuspensionTravel', 10)
        # Rest-length preload puts the new mesh at its authored ride height.
        rest_length = max(0, travel - gravity / (2 * math.pi * frequency) ** 2)
        wheel['Position'] = vector(point(bone[key]) + Vector((0, 0, rest_length)))
        settings.update(Radius=round(radius * INCHES, 7), BonePivotScale=1)
        measurements[key] = {'pivot_m': list(bone[key]), 'radius_m': radius}
    wheelbase = (bone['wheel_front_l'].x + bone['wheel_front_r'].x - bone['wheel_rear_l'].x - bone['wheel_rear_r'].x) * .5
    body = component(root, 'Rigidbody')
    body['MassCenterOverride'] = vector(point(((bone['wheel_front_l'].x + bone['wheel_rear_l'].x) * .5, 0, .8)))
    cabin = component(root, 'VBVehicleCabin')
    steering = rig.data.bones['steering_wheel']
    cabin['SteeringAxis'] = vector((rig.matrix_world.to_3x3() @ (steering.tail_local - steering.head_local)).normalized())
    cabin['BoardingArea'] = {'Mins': vector(point((2.4, -.94, .28))), 'Maxs': vector(point((3.48, .31, 1.05)))}
    set_position(by_name['Driver eyes'], (2.64, .582, 1.55))
    set_position(by_name['Driver aisle exit'], (2.94, -.36, .395))
    seat = by_name['Driver controls']
    # The seated player is parented below this object. An inherited trigger tag
    # leaves its recreated physics shapes non-solid after leaving the chair.
    # Only the interaction collider needs to be a trigger.
    seat['Tags'] = ','.join(tag.strip() for tag in seat.get('Tags', '').split(',')
                            if tag.strip() and tag.strip() != 'trigger')
    set_position(seat, (2.94, .582, 1.16))
    by_name['Driver position']['Position'] = vector(Vector((2.62, .582, .55)) * INCHES + offset - Vector(float(v) for v in seat['Position'].split(',')))
    # The walk-up trigger is on the dashboard, the seated pose is on the cushion.
    seat_box = component(seat, 'BoxCollider')
    seat_box['Center'] = '0,0,0'
    seat_box['Scale'] = vector(Vector((.40, .42, .60)) * INCHES)
    component(seat, 'VBVehicleDriverSeat')['ChaseDistanceOverride'] = 360

    def stable_id(name):
        return str(uuid.uuid5(uuid.UUID(root['__guid']), 'soreo5-import/' + name))
    collision_root = by_name['Cabin collision']
    template = next(c for c in collision_root['Components'] if c['__type'] == 'Sandbox.BoxCollider')
    colliders = []
    def add_box(name, low, high):
        low, high = Vector(low), Vector(high)
        collider = dict(template)
        collider['__guid'] = stable_id(name)
        collider['Center'] = vector(point((low + high) * .5))
        collider['Scale'] = vector((high - low) * INCHES)
        colliders.append(collider)
    helpers = bpy.data.collections.get('EXPORT | Collisions intérieures simples')
    if not helpers:
        raise RuntimeError('Collection de collisions interieures absente.')
    for obj in helpers.all_objects:
        if obj.type == 'MESH' and obj.name not in {'COL_INT_cabine', 'COL_INT_plafond'}:
            add_box(obj.name, *bounds(obj))
    # Exterior shell. Right-side spans stop at both doorways.
    add_box('left upper wall', (-3.70, 1.055, 1.03), (3.51, 1.10, 2.58))
    for index, (a, b) in enumerate([(-3.70, -.85), (.449, 2.428), (3.41, 3.59)]):
        add_box('right upper wall ' + str(index), (a, -1.10, 1.03), (b, -1.055, 2.58))
    for side in [-1, 1]:
        intervals = [(-3.70, -2.06), (-.94, 1.32), (2.38, 3.51)] if side == 1 else [(-3.70, -2.06), (-.94, -.85), (.449, 1.32), (2.38, 2.428), (3.41, 3.59)]
        for index, (a, b) in enumerate(intervals):
            ys = sorted((side * 1.055, side * 1.10))
            add_box('lower wall %s %s' % (side, index), (a, ys[0], .305), (b, ys[1], 1.03))
    for name, low, high in [
        ('rear wall', (-3.88, -.97, .305), (-3.74, .97, 2.58)),
        ('front wall', (3.53, -1.02, .305), (3.69, 1.02, 2.40)),
        ('roof', (-3.74, -1.055, 2.60), (3.53, 1.055, 2.67)),
        ('front ceiling', (3.17, -1.025, 2.49), (3.53, 1.025, 2.60)),
        ('ceiling', (-3.65, -1.025, 2.45), (3.17, 1.025, 2.51)),
        ('driver platform', (2.255, .13, .37), (3.545, 1.01, .55)),
        ('dashboard', (3.09, .075, .985), (3.49, .985, 1.145)),
        ('driver side console', (2.405, .89, .655), (3.375, 1.03, 1.16)),
        ('driver partition', (2.2675, .245, .38), (2.3325, 1.015, 2.02)),
    ]:
        add_box(name, low, high)
    collision_root['Components'] = [c for c in collision_root['Components'] if c['__type'] != 'Sandbox.BoxCollider'] + colliders
    collision_root['Children'] = []
    # The player's heavy-vehicle contact guard needs this tag on walkable
    # surfaces to preserve the approach velocity used by native step-up.
    for index, shape in enumerate(colliders):
        walkable = any(shape['__guid'] == stable_id(name) for name in
                       ('COL_INT_plancher_bas', 'COL_INT_marche', 'COL_INT_plancher_haut', 'driver platform'))
        if walkable:
            collision_root['Components'].remove(shape)
            collision_root['Children'].append({
                '__guid': stable_id('walkable ' + str(index)), '__version': 2, 'Flags': 0,
                'Name': 'Walkable floor ' + str(index), 'Position': '0,0,0',
                'Rotation': '0,0,0,1', 'Scale': '1,1,1', 'Tags': 'vehicle_step',
                'Enabled': True, 'NetworkMode': 2, 'NetworkFlags': 0,
                'NetworkOrphaned': 1, 'NetworkTransmit': True, 'OwnerTransfer': 0,
                'Components': [shape], 'Children': []})
    # Bevel the collision threshold so the native walking capsule can step from
    # the road to the 365 mm floor without catching on its vertical front edge.
    for name, a, b in [('front', 2.49, 3.37), ('middle', -.81, .408)]:
        start, end = Vector((0, -1.10, .305)), Vector((0, -.99, .365))
        centre = (start + end) * .5
        centre.x = (a + b) * .5
        rotation = (end - start).to_track_quat('X', 'Z')
        shape = dict(template)
        shape.update(__guid=stable_id(name + ' threshold collider'), Center='0,0,0',
                     Scale=vector(Vector(((end - start).length, b - a, .025)) * INCHES))
        collision_root['Children'].append({
            '__guid': stable_id(name + ' threshold'), '__version': 2, 'Flags': 0,
            'Name': name + ' beveled threshold', 'Position': vector(point(centre)),
            'Rotation': vector((rotation.x, rotation.y, rotation.z, rotation.w)),
            'Scale': '1,1,1', 'Tags': 'vehicle_step', 'Enabled': True, 'NetworkMode': 2,
            'NetworkFlags': 0, 'NetworkOrphaned': 1, 'NetworkTransmit': True,
            'OwnerTransfer': 0, 'Components': [shape], 'Children': []})
    for door_name, leaves in [('Front door', [('door_front', 'Porte avant | vantail simple')]),
                              ('Middle doors', [('door_mid_front', 'Porte du milieu | vantail avant'), ('door_mid_rear', 'Porte du milieu | vantail arrière')])]:
        door = by_name[door_name]
        boxes = [bounds(bpy.data.objects[name]) for _, name in leaves]
        low = Vector(min(b[0][i] for b in boxes) for i in range(3))
        high = Vector(max(b[1][i] for b in boxes) for i in range(3))
        centre = (low + high) * .5
        set_position(door, centre)
        trigger = component(door, 'BoxCollider')
        trigger['Center'] = '0,0,0'
        trigger['Scale'] = vector((high - low + Vector((.015, .04, .015))) * INCHES)
        for (key, _), (lo, hi) in zip(leaves, boxes):
            leaf = by_name[key]
            leaf['Position'] = vector((bone[key] - centre) * INCHES)
            leaf['Rotation'] = '0,0,0,1'
            shape = component(leaf, 'BoxCollider')
            shape['Center'] = vector(((lo + hi) * .5 - bone[key]) * INCHES)
            shape['Scale'] = vector(Vector((hi.x - lo.x, .04, hi.z - lo.z)) * INCHES)
    return {'wheelbase_m': wheelbase, 'wheels': measurements, 'colliders': len(colliders),
            'mass_kg': body['MassOverride'], 'engine_kw': component(root, 'VBVehicleController')['EnginePowerKw']}


def synchronize(meshes):
    target = PROJECT / 'Assets' / PREFAB
    export_dir = Path(bpy.data.filepath).parent / 'exports/soreo5_sbox'
    cli = cli_path()
    editor = None
    if cli:
        # Absence of an editor permits an offline update. Other failures are errors.
        instances = subprocess.run([cli, '--list'], capture_output=True, text=True,
                                   encoding='utf-8', check=True, timeout=15,
                                   creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        running = json.loads(instances.stdout)
        if any(Path(i.get('project', '')).parent.resolve() == PROJECT.resolve() for i in running):
            editor = Editor(cli)
    try:
        if editor:
            document = editor.call('asset_read', {'path': PREFAB})
            if isinstance(document, str):
                document = json.loads(document)
        else:
            document = json.loads(target.read_text(encoding='utf-8'))
        original = json.dumps(document, indent=2, ensure_ascii=False)
        measurements = update_prefab(document, meshes)
        (export_dir / 'soreo5_prefab_before_update.json').write_text(original, encoding='utf-8')
        payload = json.dumps(document, indent=2, ensure_ascii=False)
        if editor:
            result = editor.call('asset_write', {'path': PREFAB, 'json': payload})
            if not result.get('Success'):
                raise RuntimeError(str(result))
            result = editor.call('asset_compile', {'path': 'vehicles/soreo5/soreo5.vmdl', 'full': True})
            if not result.get('Success'):
                raise RuntimeError(str(result))
            measurements['native_compile'] = result
        else:
            pending = target.with_suffix('.pending')
            pending.write_text(payload + '\n', encoding='utf-8')
            os.replace(pending, target)
            measurements['native_compile'] = 'Compilation automatique a la prochaine ouverture de S&box.'
        print('[SOREO5] ' + json.dumps(measurements, ensure_ascii=False), flush=True)
        return measurements
    finally:
        if editor:
            editor.close()
