"""Check shipped NPC profiles and two-headshot tuning against real weapon assets."""

import json
import math
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[2] / "Assets"


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def component(data, name):
    return next(c for c in data["RootObject"]["Components"] if c["__type"] == name)


def validate():
    guns = [(name, component(load(ASSETS / path), "VBCombatWeapon")) for name, path in (
        ("USP", "weapons/pistol/9mmpistol.prefab"),
        ("P90", "weapons/p90/p90.prefab"),
        ("MP5", "weapons/mp5/mp5.prefab"),
    )]
    for path in ("creatures/cartel_soldier/cartel_soldier.prefab", "creatures/zombie_classic/zombie_classic.prefab"):
        health = component(load(ASSETS / path), "VBHealthComponent")
        assert health["UseLocalizedBulletDamage"] and not health["CanBeDowned"], path
        for name, gun in guns:
            damage = gun["Ballistics"]["Damage"]
            headshots = math.ceil(health["StartingHardMaxHealth"] / (damage * health["HeadDamageMultiplier"]))
            bodyshots = math.ceil(health["StartingHardMaxHealth"] / (damage * health["TorsoDamageMultiplier"]))
            assert headshots <= 2 < bodyshots, (path, name, headshots, bodyshots)
            print(f"PASS {path}/{name}: {headshots} headshots, {bodyshots} torso impacts")
    soldier = load(ASSETS / "creatures/cartel_soldier/cartel_soldier.prefab")
    sight = component(soldier, "VBAiPerceptionComponent")
    controller = component(soldier, "VBSoldierController")
    assert sight["FieldOfView"] <= 120 and sight["SightRange"] <= 1200 and sight["AcquisitionDelay"] >= 0.4
    assert controller["AimReactionTime"] >= 0.6 and controller["BurstShotLimit"] <= 3
    assert not controller["UseWeaponBurstTiming"]
    assert 1 <= controller["BurstRestMin"] <= controller["BurstRestMax"] <= 2
    assert 0 < controller["PatrolSpeed"] < controller["SearchSpeed"] <= controller["MoveSpeed"]
    assert 55 <= controller["PatrolSpeed"] <= 75 and 75 <= controller["SearchSpeed"] <= 95
    assert controller["MoveSpeed"] <= 120 and controller["TurnSpeed"] <= 4
    assert controller["AimMemoryTime"] >= 15 and controller["RepositionInterval"] >= 10
    assert controller["AccuracyScale"] >= 2
    dresser = component(soldier, "Sandbox.Dresser")
    assert dresser["Source"] == "Manual" and len(dresser["Clothing"]) >= 7
    print("PASS: deliberate perception/fire pacing and native military Dresser outfit")


if __name__ == "__main__":
    validate()
