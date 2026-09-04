# Weapons System - Void Breach

> Version: Vertical Slice v0.3 - Design in progress

---

## 0. Modularity First

All weapon definitions are data-driven. Stats, ammo type, carry size, fire modes, secondary fire and presentation references are configured outside the core gameplay logic.

Adding a conventional weapon variant should require a new definition and assets, not a new inventory architecture.

---

## 1. Weapon Slots, Carry Size and Ground Swaps

### 1.1 Two generic weapon slots

The player has **two weapon slots**. The slots are not typed as primary and secondary: either slot may contain any weapon that respects the total carry-size quota.

There is no additional weapon storage during the raid.

- The player may carry at most two weapons.
- Each weapon has a configurable `CarrySize`.
- The sum of both weapons must not exceed the configured maximum.
- A weapon found on the ground can be taken only if it fits the remaining quota or if the player swaps out an equipped weapon.
- The dropped weapon remains in the world and can be taken by another player.

### 1.2 Initial size quota

Initial maximum:

```text
MaximumWeaponCarrySize = 5
```

| Category | Initial carry size |
|---|---:|
| Pistol / revolver | 1 |
| Light melee weapon | 1 |
| Heavy melee weapon | 2 |
| SMG | 3 |
| Assault rifle | 4 |
| Shotgun | 4 |
| Crossbow | 4 |
| RPG | 5 |

Examples:

| Combination | Total | Allowed |
|---|---:|---:|
| Assault rifle + pistol | 5 | Yes |
| SMG + pistol | 4 | Yes |
| Shotgun + pistol | 5 | Yes |
| Pistol + pistol | 2 | Yes |
| SMG + SMG | 6 | No |
| Assault rifle + SMG | 7 | No |
| RPG alone | 5 | Yes |
| RPG + pistol | 6 | No |

All values are data-configurable and must be playtested. A future compact weapon may use a lower size without changing the two-slot architecture.

### 1.3 Separate equipment slots

Weapons are separate from equipment.

Initial inventory layout:

- Weapon Slot A;
- Weapon Slot B;
- Throwable slot;
- Medical slot;
- Utility slot;
- locked PDA slot;
- special objective transport outside ordinary weapon storage.

The PDA is permanent, cannot be discarded and does not consume weapon size.

---

## 2. Weapon Categories and Variants

The lists below express the long-term weapon pool. The vertical slice uses only a small subset.

### 2.1 Pistol

| Variant | Origin | Ammo type | Notes |
|---|---|---|---|
| USP Match | Human | `PistolAmmo` | Standard and precise |
| Glock 17 | Human | `PistolAmmo` | Larger clip and faster fire |
| .357 Magnum | Human | `PistolAmmo` | High damage, low capacity, slow reload |
| Combine Pistol | Combine | `EnergyAmmo` | Energy clip; reload only when empty |

Secondary fire: none for the vertical slice.

### 2.2 SMG

| Variant | Origin | Ammo type | Notes |
|---|---|---|---|
| MP7 | Human | `SMGAmmo` | Standard and balanced |
| MP5 | Human | `SMGAmmo` | Slightly slower, higher damage |
| P90 | Human | `SMGAmmo` | High clip capacity |
| Combine SMG | Combine | `EnergyAmmo` | Energy clip; reload only when empty |

Possible secondary fire for human SMGs:

- single loaded underbarrel grenade;
- one `GrenadeLauncherRound` obtained from a dedicated pickup;
- after firing, the launcher remains empty until another dedicated round is found.

This is outside the minimum weapon implementation but keeps the Half-Life identity without making explosive ammunition common.

### 2.3 Assault Rifle

| Variant | Origin | Ammo type | Notes |
|---|---|---|---|
| AR2 | Combine | `EnergyAmmo` | Possible energy-orb secondary fire |
| M4A1 | Human | `RifleAmmo` | Versatile |
| AKM | Human | `RifleAmmo` | Higher damage, stronger recoil |
| G36 | Human | `RifleAmmo` | Accurate and balanced |

### 2.4 Shotgun

| Variant | Origin | Ammo type | Notes |
|---|---|---|---|
| SPAS-12 | Human | `ShotgunShells` | Pump-action, Half-Life feel |
| Combine Shotgun | Combine | `EnergyAmmo` | Energy clip; reload only when empty |

### 2.5 Crossbow

| Variant | Origin | Ammo type | Notes |
|---|---|---|---|
| Crossbow | Human | `CrossbowBolts` | Projectile weapon with high single-target damage |

### 2.6 Grenades and throwables

Throwables use a separate equipment slot and do not count toward the two-weapon limit.

| Variant | Origin | Notes |
|---|---|---|
| Frag Grenade | Human | Standard explosive |
| Combine Grenade | Combine | Potentially larger blast radius |
| Molotov | Human | Fire area interacting with SoftMaxHealth |

Cooking direction - Half-Life 1 style:

- pressing fire pulls the pin and starts the fuse immediately;
- releasing throws the grenade with its remaining fuse;
- holding too long causes it to explode in the player's hand.

This mechanic is post-core-loop and should not block the first firearm implementation.

### 2.7 RPG

- Rare weapon with `CarrySize = 5` initially.
- Occupies the full weapon quota by itself.
- Rockets are individual pickups.
- Initial hard cap: three reserve rockets.
- One rocket loaded at a time.
- No degradation system.
- Helicopters are not part of the vertical slice; RPG relevance must not depend on them.

---

## 3. Secondary Fire

| Category | Secondary | Charges | Resupply |
|---|---|---:|---|
| Pistol | None initially | - | - |
| Human SMG | Underbarrel grenade | 1 loaded round | Dedicated pickup |
| AR2 | Energy orb | Configurable | Energy or dedicated pickup |
| Human assault rifle | None initially | - | - |
| Shotgun | None initially | - | - |
| Crossbow | None initially | - | - |

Secondary fire is data-defined, but only features actually required by the vertical slice should be implemented.

---

## 4. Ammo and Reload System

### 4.1 Direction retained for the vertical slice

Void Breach uses the conventional S&box-compatible model:

- each weapon has a current clip or chamber value;
- the inventory has a reserve ammunition pool per ammo type;
- reloading transfers ammunition from the reserve pool into the weapon's clip;
- partially filled physical magazines are not tracked as inventory objects;
- there is no persistent magazine rotation system.

This matches the desired Hunt: Showdown-style readability and avoids unnecessary network and interface complexity.

### 4.2 Ammo archetypes

| Type | Used by | Unit | Reload rule |
|---|---|---|---|
| `PistolAmmo` | Human pistols | Reserve rounds + weapon clip | Standard reload |
| `SMGAmmo` | Human SMGs | Reserve rounds + weapon clip | Standard reload |
| `RifleAmmo` | Human assault rifles | Reserve rounds + weapon clip | Standard reload |
| `EnergyAmmo` | Combine firearms and AR2 | Reserve energy + weapon clip | Reload only when clip is empty |
| `ShotgunShells` | Conventional shotguns | Individual shells in reserve | Shell-by-shell reload |
| `CrossbowBolts` | Crossbow | Individual bolts | One at a time |
| `GrenadeLauncherRound` | SMG secondary | Individual special round | Dedicated pickup |
| `Grenade` | Throwables | Per item | No reload |
| `RPGRocket` | RPG | Individual rockets | One loaded, reserve cap |

### 4.3 Standard reload

For conventional firearms:

```text
Needed = ClipCapacity - CurrentClip
Transferred = min(Needed, ReserveAmmo)
CurrentClip += Transferred
ReserveAmmo -= Transferred
```

A partial reload does not waste ammunition. The rounds already in the weapon remain in the clip and it is simply topped up from the reserve pool.

### 4.4 Energy reload rule

Combine energy weapons may retain the special rule:

```text
MustBeEmptyToReload = true
```

- A partially depleted energy clip cannot be reloaded.
- The player must empty the current clip before transferring reserve energy into it.
- Ammo pickups add to the reserve pool; they do not create persistent cell objects.

The rule is configurable per weapon and should be playtested for readability and frustration.

### 4.5 Initial clip and reserve placeholders

| Weapon | Clip size | Starting reserve | Maximum reserve |
|---|---:|---:|---:|
| USP / Glock | 18 | 36 | 72 |
| Magnum | 6 | 12 | 24 |
| Combine Pistol | 20 | 20 | 60 |
| MP7 / MP5 | 30 | 60 | 120 |
| P90 | 50 | 50 | 150 |
| Combine SMG | 30 | 30 | 90 |
| AR2 | 30 | 30 | 90 |
| M4A1 / AKM / G36 | 30 | 60 | 120 |
| SPAS-12 | 8 | 16 shells | 24 shells |
| Combine Shotgun | 10 | 10 | 30 |
| Crossbow | 1 | 4 bolts | 12 bolts |
| RPG | 1 | 0-1 rocket | 3 rockets |

All values are placeholders.

---

## 5. Weapon Definition Structure

```csharp
class WeaponDefinition
{
    // Identity and inventory
    string WeaponName;
    WeaponCategory Category;
    int CarrySize;
    AmmoType PrimaryAmmoType;
    AmmoType SecondaryAmmoType;

    // Primary fire
    float Damage;
    float FireRate;
    float Range;
    float HipFireSpread;
    float ADSSpreadMultiplier;
    float ADSTime;
    bool IsAutomatic;
    float ReloadTime;
    int ClipSize;
    int StartingReserveAmmo;
    int MaxReserveAmmo;
    bool IsHitscan;
    float ProjectileSpeed;
    float ArmorOrShieldPierce;
    float HeadshotMultiplier;

    // Reload rules
    bool MustBeEmptyToReload;
    bool ReloadsOneRoundAtATime;

    // Secondary fire
    bool HasSecondaryFire;
    int SecondaryChargesPerPickup;
    float SecondaryDamage;
    float SecondaryBlastRadius;

    // Feel and presentation
    float RecoilStrength;
    float RecoilRecoveryRate;
    float EquipTime;

    // Universal firearm melee push
    float MeleePushDamage;
    float MeleePushForce;
}
```

Names and exact types are illustrative. The real implementation must extend or compose the official S&box inventory and weapon systems rather than duplicate them without need.

---

## 6. Melee

### 6.0 Bare hands baseline

The player starts with permanent fists in Weapon Slot A. This first melee
implementation deliberately uses the native S&box weapon stack:

- `BaseCombatWeapon` with ammunition disabled;
- the official human first-person arms and their punching animation graph;
- the native short-range melee trace, prediction and host validation;
- `Slot1`, `Slot2`, `SlotNext` and `SlotPrev` for weapon selection;
- `BaseInventoryComponent.Switch` for authoritative equipment changes.

Fists cannot be dropped. Their first-pass values are an 80-unit reach, an
8-unit trace radius, 20 blunt damage, 500 force and a 0.45-second attack delay.
These values remain data-configurable on the prefab.

The current HUD exposes Weapon Slots A and B in a Half-Life-style selector at
the top of the screen. It appears when a direct slot or previous/next weapon
input is triggered, then fades after 1.5 seconds without further selection.
It uses the active input origin so the displayed control follows the player's
keyboard or gamepad binding. Weapon names are localized (`Fists` / `Poings`).

### 6.1 Universal push

While holding a firearm, the player may perform a short melee push.

- Light configurable damage.
- Configurable knockback.
- Primarily useful against NPCs.
- Cooldown instead of a stamina cost.
- Input binding must not conflict with the common world-interaction input.

### 6.2 Dedicated melee weapons

Melee weapons occupy one of the two generic weapon slots and have a carry size.

| Variant | Initial size | Notes |
|---|---:|---|
| Crowbar | 1 | Fast, low damage |
| Wrench | 1 | Medium damage, slower |
| Hammer | 2 | High damage, very slow |
| Combine Baton | 1 | Potential stun effect post-slice |

For the first implementation, melee attacks may use a short-range trace instead of a fully simulated swing arc.

---

## 7. Hitscan vs Projectile

| Weapon type | Detection | Reason |
|---|---|---|
| Pistol, SMG, conventional AR, shotgun | Hitscan | Clean network behavior and Half-Life feel |
| Crossbow, RPG | Projectile | Travel time adds tension and skill |
| AR2 energy orb | Projectile | Fits the reference weapon |
| Grenade-launcher round | Projectile | Arc is part of the mechanic |
| Grenades and Molotovs | Physics object | Bounce, cooking and area effect |
| Combine energy firearms | Hitscan initially | Simplicity for the vertical slice |

The choice is configured per weapon definition.

---

## 8. Weapon Attachments

Attachments are not implemented for the vertical slice.

Weapon variants provide differentiation. The data model may later add attachment slots, but no current system should depend on them.

---

## 9. Vertical Slice Arsenal

The complete weapon catalogue is a long-term vision. The vertical slice initially targets:

- one pistol from the official S&box FPS/weapon sample as the first technical weapon;
- one SMG;
- one assault rifle;
- one shotgun.

The first milestone may contain only the sample pistol while the network, ViewModel, WorldModel, inventory and damage integration are validated.

---

## 10. Resolved Decisions

- [x] Two generic weapon slots.
- [x] Global carry-size quota across both slots.
- [x] Initial quota: 5.
- [x] AR + pistol, SMG + pistol and pistol + pistol are allowed.
- [x] SMG + SMG and AR + SMG are initially disallowed by their combined size.
- [x] No additional weapon storage during the raid.
- [x] Ground weapons can be swapped with an equipped weapon.
- [x] PDA uses a locked equipment slot and does not consume weapon size.
- [x] Throwables, medical and utility equipment use separate slots.
- [x] Ammo uses weapon clip + reserve pool per ammo type.
- [x] Persistent magazine objects are not used.
- [x] ADS is supported through configurable values.
- [x] Universal firearm push and dedicated melee weapons remain part of the direction.
- [x] Grenade cooking direction is Half-Life 1 style.
- [x] No weapon degradation.
- [x] Attachments are outside the vertical slice.
- [x] Hitscan/projectile behavior is hybrid and data-driven.
- [x] Permanent fists provide the native melee baseline and fallback weapon.
- [x] Weapon Slots A/B can be selected through native slot and cycle inputs.

## 11. Open Questions

- [ ] Final carry-size values after playtests.
- [ ] Exact reserve-ammunition caps.
- [ ] Whether the empty-only energy reload rule is enjoyable enough to retain.
- [ ] Final input for universal melee push without conflicting with world interaction.
- [ ] Which secondary fires enter the first complete vertical slice.
- [ ] Whether two identical pistols require any special akimbo restriction; by default they are two separately selected weapons, not dual-wielded simultaneously.
