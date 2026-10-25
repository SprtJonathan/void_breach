"""Validate ammo identity and player input configuration, including saved overrides."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def objects(nodes):
    for node in nodes:
        yield node
        yield from objects(node.get("Children", []))

def validate():
    for weapon, category in [("mp5/mp5", "medium"), ("p90/p90", "medium"), ("pistol/9mmpistol", "small")]:
        prefab = load(ROOT / "Assets/weapons" / f"{weapon}.prefab")["RootObject"]
        gun = next(c for c in prefab["Components"] if c["__type"] == "VBCombatWeapon")
        ammo = gun["PrimaryAmmoType"]
        assert ammo["Path"] == f"ammo/{category}.ammo", (weapon, ammo)
        resource = ROOT / "Assets" / ammo["Path"]
        assert ammo["Id"] == load(Path(str(resource) + ".meta"))["guid"], weapon
        assert load(resource)["MaxReserve"] > 0
    for category in ["small", "medium", "large"]:
        assert load(ROOT / f"Assets/ammo/{category}.ammo")["MaxReserve"] > 0
    projectile = load(ROOT / "Assets/weapons/grenade/frag_grenade.prefab")["RootObject"]
    grenade = next(c for c in projectile["Components"] if c["__type"] == "VBGrenadeWeapon")
    assert grenade["PrimaryAmmoType"]["Path"] == "ammo/frag_grenade.ammo"
    player = load(ROOT / "Assets/player/player controller.prefab")["RootObject"]
    camera_owner = next(c for c in player["Components"] if c["__type"] == "VBPlayerCamera")
    cameras = [(obj, c) for obj in objects([player]) for c in obj.get("Components", [])
               if c["__type"] == "Sandbox.CameraComponent"]
    assert not cameras, "the owning peer creates its camera; no excluded object reference crosses the wire"
    assert "Camera" not in camera_owner
    assert camera_owner["DefaultFieldOfView"] > 0 and camera_owner["CameraPriority"] < 0
    assert 0 < camera_owner["NearClip"] < camera_owner["FarClip"]
    configs = {c["__guid"]: c for c in player["Components"]}
    inventory = next(c for c in configs.values() if c["__type"] == "VBWeaponInventoryComponent")
    selector = next(c for c in configs.values() if c["__type"] == "VBWeaponSelectionController")
    assert inventory["PickupMode"] == "Touch"
    assert inventory["AutoSwitchOnPickup"] is True
    assert selector["DropWeaponInputAction"] == "Drop"
    assert not any(item.get("prefab") == "weapons/grenade/frag_grenade.prefab" for item in inventory["StartingItems"])
    assert inventory["StartingAmmo"] == []
    inputs = load(ROOT / "ProjectSettings/Input.config")
    assert any(i["Name"] == "Drop" and i["KeyboardCode"].lower() == "g" for i in inputs["Actions"])
    for scene in (ROOT / "Assets/scenes").rglob("*.scene"):
        for obj in objects(load(scene).get("GameObjects", [])):
            if obj.get("__Prefab", {}).get("Path") != "player/player controller.prefab":
                continue
            for override in obj.get("__PrefabInstancePatch", {}).get("PropertyOverrides", []):
                key = override["Target"]["IdValue"]
                if key == inventory["__guid"] and override["Property"] == "PickupMode":
                    assert override["Value"] == "Touch", scene
                if key == inventory["__guid"] and override["Property"] == "StartingItems":
                    assert not any("grenade" in item.get("prefab", "") for item in override["Value"]), scene
                if key == inventory["__guid"] and override["Property"] == "StartingAmmo":
                    assert not override["Value"], scene
                if key == selector["__guid"] and override["Property"] == "DropWeaponInputAction":
                    assert override["Value"] == "Drop", scene
    for kind in ["cartel_soldier", "zombie_classic"]:
        npc = load(ROOT / f"Assets/creatures/{kind}/{kind}.prefab")["RootObject"]
        components = npc["Components"]
        assert sum(c["__type"] == "VBNpcLocomotion" for c in components) == 1
        assert sum(c["__type"] == "Sandbox.CharacterController" for c in components) == 1
        assert not any(c["__type"] == "VBNpcVehicleContacts" for c in components)
        nav = next(c for c in components if c["__type"] == "Sandbox.NavMeshAgent")
        assert nav["UpdatePosition"] is False
        assert nav["AutoTraverseLinks"] is False
        assert npc["NetworkMode"] == 1 and npc["OwnerTransfer"] == 1
    assert load(ROOT / "Assets/navigation/npc_climb.navarea")["CostMultiplier"] == 2
    collision = load(ROOT / "ProjectSettings/Collision.config")
    for living in ["player", "npc", "zombie", "npc_body"]:
        for corpse in ["npc_ragdoll", "player_ragdoll"]:
            assert any({p["a"], p["b"]} == {living, corpse} and p["r"] == "Ignore"
                       for p in collision["Pairs"]), (living, corpse)
    print("PASS: shared SMG ammo, small pistol ammo, large AR pool, special grenade ammo, automatic Touch pickup, voluntary swap and G drop.")

if __name__ == "__main__":
    validate()
