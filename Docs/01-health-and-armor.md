# Health, Down State & Energy Resources - Void Breach

> Version: Vertical Slice v0.3 - Design in progress

---

## 0. Core Design Principle - Modularity First

> **Every system in Void Breach must be modular and data-driven.**

- All tunable values are exposed as configurable properties; no magic numbers are embedded in gameplay logic.
- Systems are designed so that individual parts can be modified independently through definitions and modifiers.
- Design decisions can be revisited after playtests without restructuring the code.
- When adding a mechanic, ask: *"Can this be configured without recompiling?"*

This applies to health values, drain rates, heal rates, shield values, damage types, modifier stacks, interaction durations and item capacities.

---

## 1. Philosophy

Players are **ordinary rebel scavengers**, not super-soldiers. They are fragile, but can be saved by any other player willing to take the risk. The system aims for a balance between Half-Life 2 readability and Hunt: Showdown tension, without becoming a full Tarkov-style medical simulation.

There is **no passive health regeneration**. Healing, stabilization and restoration of lost maximum health are active choices that consume time, positioning or resources.

Main healing sources:

- **Health Vials** found in the zone;
- **Personal Medkit**, a rechargeable equipment item;
- **Health Stations** fixed in the environment;
- **Combine Medkit**, a legendary item capable of restoring permanent maximum health.

Energy batteries are not armor items. They are discrete tactical resources that may later be consumed either for personal protection or to power a world device.

---

## 2. Health - Three Values

```text
HardMaxHealth >= SoftMaxHealth >= CurrentHealth
```

| Value | Description |
|---|---|
| **HardMaxHealth** | Permanent health ceiling for the current raid. It drains while the player is downed. It can only be restored by the Combine Medkit and never above the starting cap. |
| **SoftMaxHealth** | Temporary effective ceiling, always less than or equal to HardMaxHealth. Fire, poison and similar effects may reduce it while the player is alive. It recovers gradually when the effect ends. |
| **CurrentHealth** | Actual hit points. It can never exceed SoftMaxHealth. |

### Invariant

The following relationship must always remain true:

```text
CurrentHealth <= SoftMaxHealth <= HardMaxHealth
```

Whenever either ceiling decreases, lower values are clamped immediately:

```text
SoftMaxHealth = min(SoftMaxHealth, HardMaxHealth)
CurrentHealth = min(CurrentHealth, SoftMaxHealth)
```

Example A - player at full health when fire starts:

```text
HardMax = 100, SoftMax = 100, Current = 100
SoftMax drops to 80 -> Current is clamped to 80
SoftMax drops to 60 -> Current is clamped to 60
```

Example B - player already damaged when fire starts:

```text
HardMax = 100, SoftMax = 100, Current = 60
SoftMax drops to 80 -> Current remains 60
SoftMax drops to 60 -> Current remains 60
SoftMax drops to 40 -> Current is clamped to 40
```

The player can heal only up to the first active ceiling.

### 2.1 Normal state

```text
SoftMaxHealth == HardMaxHealth
CurrentHealth <= SoftMaxHealth
```

The player can heal up to HardMaxHealth. There is no natural regeneration.

### 2.2 Fire, poison and temporary maximum-health damage

While the player is alive, fire, poison or another configured status can reduce SoftMaxHealth over time.

- The effect uses a configurable reduction rate.
- CurrentHealth is clamped if SoftMaxHealth drops below it.
- Healing remains capped by SoftMaxHealth.
- When the effect ends, SoftMaxHealth recovers gradually toward HardMaxHealth.
- Several status effects may coexist if their definitions allow it.

When the player becomes downed, status effects no longer apply an independent SoftMax drain. Instead, they modify the HardMaxHealth drain rate through the modifier stack described below. This avoids two permanent-death clocks running in parallel while preserving the danger of being downed while burning or poisoned.

### 2.3 While downed

When CurrentHealth reaches zero and HardMaxHealth is still above zero, the player enters the **Downed** state.

1. CurrentHealth is locked at zero.
2. HardMaxHealth begins to drain at a configurable rate.
3. SoftMaxHealth remains clamped to HardMaxHealth.
4. The player can be revived by any other player while HardMaxHealth remains above zero.
5. On successful revive, the current HardMaxHealth becomes the player's new permanent ceiling for the rest of the raid.
6. If HardMaxHealth reaches zero, the player dies permanently for that raid.

The longer a rescue takes, the more permanently weakened the player becomes.

```text
Example:
HardMaxHealth = 80
Player is downed and drains at 5 HP/s
Revived after 12 seconds -> HardMaxHealth = 20
The player cannot heal above 20 unless a Combine Medkit restores HardMaxHealth
```

### 2.4 Downed drain modifier system

The HardMaxHealth drain rate supports stackable modifiers:

```text
EffectiveDrainRate = BaseDrainRate x ProductOfActiveModifiers
```

| Source | Origin | Effect | Placeholder |
|---|---|---|---:|
| Base bleed-out | System | Normal drain | 5 HP/s |
| Fire while downed | Status effect | Accelerates drain | x2.0 |
| Poison while downed | Status effect | Configurable acceleration | TBD |
| Self-origin stabilization item | Downed player's inventory or automatic trigger | Slows own drain | x0.25 |
| External stasis area | Another player's equipment | Slows drain inside the area | x0.25 |
| Future effects | Any source | Configurable | TBD |

The health system only consumes a modifier value, source identifier and lifetime. It does not need to know whether the modifier came from the downed player, another team, an environmental volume or a future item.

### 2.5 HUD display

The HUD should remain readable and close to Half-Life conventions:

- numeric value: CurrentHealth;
- main bar: CurrentHealth relative to HardMaxHealth;
- temporary unavailable zone: gap between SoftMaxHealth and HardMaxHealth;
- visible drain animation while downed;
- clear state change when HardMaxHealth is critically low.

Suggested color progression:

- high: orange-yellow;
- medium: orange;
- low: red;
- downed: pulsing red.

---

## 3. Healing Sources

All healing and revival actions use the common configurable hold-interaction system. There are no instant heals.

| Item or source | Restores | Hold action | Main role |
|---|---|---:|---|
| **Health Vial** | CurrentHealth | Yes | Common healing resource and fast revive resource |
| **Personal Medkit** | CurrentHealth from a reservoir | Yes | Rechargeable personal healing equipment |
| **Health Station** | CurrentHealth or Personal Medkit reservoir | Yes | Fixed limited-capacity control point |
| **Combine Medkit** | HardMaxHealth | Yes | Legendary restoration and stabilization item |

### 3.1 Health Vial

- Common loot from the terrain or enemies.
- Single-use item.
- Progressive CurrentHealth restoration while the hold is maintained.
- Capped by SoftMaxHealth.
- Can be used on self.
- Can be consumed to perform a fast revive on a downed player.
- Can be siphoned into the Personal Medkit if that feature is retained after prototyping.

Values to tune:

- heal rate;
- maximum carry count;
- amount transferred to the Personal Medkit.

### 3.2 Personal Medkit

The Personal Medkit is an equippable item in its dedicated equipment slot.

- It contains a configurable CurrentHealth reservoir.
- Holding the action restores health progressively.
- Releasing the action stops healing immediately.
- It can be used on self or on a nearby downed player.
- It can be refilled by Health Vials or Health Stations if those interactions are retained.

Initial placeholders:

| Parameter | Placeholder |
|---|---:|
| Startup delay | 0.5 s |
| Heal rate | 15 HP/s |
| Maximum reservoir | 100 HP |

### 3.3 Health Station

- Fixed in strategic areas of the map.
- Limited total capacity for the raid.
- Does not refill automatically during the match.
- Can heal a player directly or refill the Personal Medkit.
- Creates a natural control point that teams may choose to contest.

### 3.4 Combine Medkit

The Combine Medkit is a legendary objective-area resource.

- Initial target: one kit per raid.
- Initial target: two uses per kit.
- Each use consumes one charge after a successful hold interaction.
- Restores a configurable amount of HardMaxHealth.
- Cannot raise HardMaxHealth above the original starting cap.
- Can be used on a living player or a downed player whose HardMaxHealth is still above zero.
- On a downed player, it restores HardMaxHealth and buys more rescue time, but does not itself complete the revive unless explicitly configured later.
- It **cannot resurrect a player whose HardMaxHealth has reached zero** and who has entered the Dead state.

Key distinction:

- Health Vial -> CurrentHealth;
- Personal Medkit -> CurrentHealth;
- Combine Medkit -> HardMaxHealth.

---

## 4. Down State and Revive

### 4.1 Trigger

```text
CurrentHealth <= 0 and HardMaxHealth > 0 -> Downed
HardMaxHealth <= 0 -> Dead
```

While downed:

- the player loses normal control;
- weapons and ordinary interactions are disabled;
- HardMaxHealth drains;
- objectives carried by the player remain attached until the player dies, disconnects or explicitly drops them according to the objective rules;
- any other player can attempt a revive, regardless of team;
- no alliance is created by the action.

The downed camera follows the player's replicated in-place ragdoll. Normal control is disabled, but the owner can still rotate the camera around the body. The capsule remains at the down location and has no physical interaction while the player is incapacitated. The active inventory item is preserved: its first-person view model is hidden while its normal, collision-free world model remains attached to the physical hand. No ragdoll or weapon visual copy is created. On revive, the capsule is teleported to the ragdoll's final grounded position before animation and control are restored. On permanent death, the death screen can spectate another living player. Spectating prioritizes living teammates when a team provider is available and falls back to any living player otherwise.

### 4.2 Revive methods

| Method | Resource | Initial duration | Interruption |
|---|---|---:|---|
| **Fast revive** | Health Vial or Personal Medkit | 3 s | Cancelled on release or configured interruption |
| **CPR** | No item | 10 s | Cancelled on release or configured interruption |
| **HardMax stabilization** | Combine Medkit | 3-5 s to test | Restores HardMaxHealth but does not resurrect a Dead player |

Rules:

- A downed player cannot revive themselves.
- Any living player may revive any downed player.
- While a valid revive attempt is actively held, HardMaxHealth drain pauses.
- If the hold is released or cancelled, the drain resumes immediately.
- Partial progress is not retained unless a future interaction definition explicitly allows it.
- The consumed resource is deducted only when the action succeeds, unless playtests justify consumption on interruption.

On successful revive:

```text
WakeHealth = max(HardMaxHealth x WakePercent, MinWakeHP)
CurrentHealth = min(WakeHealth, SoftMaxHealth)
```

Initial placeholders:

- WakePercent: 25%;
- MinWakeHP: 10 HP.

---

## 5. Energy Batteries and Optional Shield

### 5.1 Philosophy

Void Breach does not use equipable armor vests, armor tiers or percentage absorption equipment.

An energy battery is instead a **discrete tactical resource**. The player may eventually choose between immediate personal protection and powering a valuable world interaction.

### 5.2 Battery rules

- A battery is a full, indivisible item.
- It has no partial-charge state.
- It does not need a battery charge bar.
- One valid action consumes one complete battery.
- Batteries use an equipment or utility inventory slot according to the final inventory layout.

### 5.3 Personal shield use

A battery may be consumed to grant a configurable amount of temporary shield.

Initial direction:

- shield absorbs damage before CurrentHealth;
- shield does not regenerate passively;
- no armor vest is required;
- shield value is separate from HardMaxHealth, SoftMaxHealth and CurrentHealth;
- the battery item itself has no charge meter;
- a simple shield value or HUD indicator may be added only if the feature enters the playable scope.

### 5.4 World-device use

A battery may instead power:

- a locked bonus room;
- an inactive console;
- a shortcut or security door;
- a rare loot device;
- a future hidden extraction or part of its activation process.

The battery is consumed by the device. This creates a clear decision between immediate survival and a possible collective or economic advantage.

### 5.5 Vertical slice scope

The battery and shield loop is **not required for the minimum vertical slice**. The architecture should allow a consumable item to target either the player or a compatible world device, but implementation may wait until the core raid loop is stable.

---

## 6. Modular Damage Architecture

The detailed implementation will be aligned with the existing health classes and code supplied for the project. The following structure expresses the intended design and may be adapted to the real code rather than recreated blindly.

### 6.1 Damage information

```csharp
struct VBDamageInfo
{
    float Amount;
    DamageType Type;
    float ShieldPierce;             // 0 to 1, portion bypassing an optional shield
    float SoftMaxReductionRate;     // HP/s while the living target is affected
    float SoftMaxRecoveryRate;      // HP/s after the effect ends
    float DrainRateModifier;        // multiplier while the target is downed
    GameObject Attacker;
    GameObject Weapon;
    Vector3 HitPosition;
    Vector3 HitDirection;
    Hitbox Hitbox;
    bool IsHeadshot;
}
```

Exact names and engine types must be adapted to the existing code and verified against the S&box API in use.

### 6.2 Damage types

| Type | Vertical slice | SoftMax effect | Downed drain effect |
|---|---:|---|---|
| Bullet | Yes | None | None |
| Blunt | Yes | None | None |
| Explosion | Yes | None | None |
| Fall | Yes | None | None |
| Fire | Later or simplified | Reduces SoftMax while alive | Accelerates HardMax drain |
| Poison | Later | Reduces SoftMax while alive | Configurable |
| Energy | Later or simple bullet-equivalent | None initially | None |
| Freeze/Stasis | Post-slice | None | Can slow HardMax drain |

### 6.3 Damage pipeline

```text
Damage received
    -> validate source and amount on host/server
    -> apply configured shield bypass
    -> if shield exists, subtract the non-bypassing portion from shield first
    -> apply remaining damage to CurrentHealth
    -> register SoftMax effect if the living target receives fire/poison
    -> register downed-drain modifier when relevant
    -> if CurrentHealth <= 0 and HardMaxHealth > 0, enter Downed
    -> if HardMaxHealth <= 0, enter Dead
    -> emit damage, downed, revived and death events
```

---

## 7. Tunable Variables

| Variable | Initial placeholder | Status |
|---|---:|---|
| Starting HardMaxHealth | 100 HP | To validate |
| Base HardMax drain while downed | 5 HP/s | To validate |
| Fire drain modifier while downed | x2.0 | To validate |
| Stabilization modifier | x0.25 | To validate |
| SoftMax recovery after effect | 2 HP/s | To validate |
| Wake health percentage | 25% | To validate |
| Minimum wake health | 10 HP | To validate |
| Fast revive duration | 3 s | Initial decision, playtest |
| CPR duration | 10 s | Retained |
| Health Vial heal rate | 10 HP/s | To validate |
| Personal Medkit capacity | 100 HP | To validate |
| Personal Medkit heal rate | 15 HP/s | To validate |
| Combine Medkit HardMax restore | +25 HP | To validate |
| Combine Medkit uses | 2 | Retained for prototype |
| Combine Medkit per raid | 1 | Retained for prototype |
| Battery shield granted | TBD | Post-core-loop |
| Battery partial charge | No | Locked |
| HardMaxHealth at zero | Permanent death | Locked |

---

## 8. Resolved Decisions

- [x] Health uses CurrentHealth, SoftMaxHealth and HardMaxHealth.
- [x] All healing is active and uses a hold interaction.
- [x] CurrentHealth is clamped immediately when either ceiling falls below it.
- [x] Fire and poison reduce SoftMaxHealth while alive.
- [x] While downed, status effects modify HardMaxHealth drain instead of running a second SoftMax drain.
- [x] HardMaxHealth drain pauses during an actively maintained valid revive.
- [x] Any player may revive any other player; no alliance is created.
- [x] Fast revive initially lasts 3 seconds with a Health Vial or Personal Medkit.
- [x] CPR lasts 10 seconds without a resource.
- [x] HardMaxHealth equal to zero means permanent death for the raid.
- [x] Combine Medkit restores HardMaxHealth but cannot resurrect a Dead player.
- [x] There are no armor vests, armor tiers or armor degradation.
- [x] Batteries are indivisible resources with no partial-charge bar.
- [x] Battery/shield gameplay is optional for the minimum vertical slice.
- [x] Self-revive is not supported.
- [x] Downed and Dead share one owner-simulated, replicated ragdoll state; the authoritative health transition remains host-controlled.
- [x] The downed player loses movement, weapons and ordinary interactions but retains camera look.
- [x] Permanent death displays the downing reason and offers spectate/menu actions.

## 9. Open Questions

- [ ] Exact HardMaxHealth drain rate and status multipliers.
- [ ] Final carry counts and reservoir sizes for medical items.
- [ ] Whether Health Vials refill the Personal Medkit in the first playable version.
- [ ] Shield amount and HUD presentation if the battery feature is implemented.
- [ ] Which devices can consume a battery in the first post-slice iteration.
- [ ] Final integration strategy after reviewing the existing health code.
