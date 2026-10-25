"""Validate effective soldier inventories, including saved scene prefab overrides.

Run from any directory: python Tests/AI/validate_soldier_assets.py
"""

import copy
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PREFAB = "creatures/cartel_soldier/cartel_soldier.prefab"
INVENTORIES = {"Sandbox.BaseInventoryComponent", "VBWeaponInventoryComponent"}


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check_inventory(components, label):
    inventories = [c for c in components if c.get("__type") in INVENTORIES]
    assert len(inventories) == 1, f"{label}: expected one inventory, found {len(inventories)}"
    inventory = inventories[0]
    assert inventory["__type"] == "VBWeaponInventoryComponent", f"{label}: legacy inventory type"
    assert inventory["ManualPumping"] is True, f"{label}: NPC can consume player input"
    assert inventory["Behaviour"] == "Buckets" and inventory["MaxSlots"] >= 7, f"{label}: wrong slots"
    assert inventory["AutoSwitchOnPickup"] is False, f"{label}: pickup can override weapon priority"
    assert inventory["UsesLoadout"] is False, f"{label}: native loadout can replace soldier equipment"


def objects(nodes):
    for node in nodes:
        yield node
        yield from objects(node.get("Children", []))


def validate():
    prefab = load(ROOT / "Assets" / PREFAB)["RootObject"]
    prefab_ids = {obj["__guid"] for obj in objects([prefab])}
    prefab_ids.update(c["__guid"] for obj in objects([prefab]) for c in obj.get("Components", []))
    check_inventory(prefab["Components"], PREFAB)
    count = 0
    for scene in (ROOT / "Assets/scenes").rglob("*.scene"):
        for obj in objects(load(scene).get("GameObjects", [])):
            if obj.get("__Prefab", {}).get("Path") != PREFAB:
                continue
            components = {c["__guid"]: copy.deepcopy(c) for c in prefab["Components"]}
            patch = obj.get("__PrefabInstancePatch", {})
            for addition in patch.get("AddedObjects", []):
                assert addition["Id"]["IdValue"] not in prefab_ids, f"{scene}: scene adds an object already in the current prefab"
                if addition["Parent"]["IdValue"] == prefab["__guid"] and addition["ContainerProperty"] == "Components":
                    components[addition["Id"]["IdValue"]] = copy.deepcopy(addition["Data"])
            for override in patch.get("PropertyOverrides", []):
                component = components.get(override["Target"]["IdValue"])
                if component is not None:
                    component[override["Property"]] = override["Value"]
            for removal in patch.get("RemovedObjects", []):
                assert removal["Id"]["IdValue"] in prefab_ids, f"{scene}: stale removal targets an object absent from the current prefab"
                if removal["Id"]["Type"] == "Component":
                    components.pop(removal["Id"]["IdValue"], None)
            check_inventory(components.values(), f"{scene.name}/{obj['__guid']}")
            count += 1
    print(f"PASS: soldier prefab and {count} scene instances each use one correctly configured weapon inventory.")


if __name__ == "__main__":
    validate()
