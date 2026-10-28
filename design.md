# Design

Working draft. The numbers are starting points for tuning, not decisions.

Setting: 1980s, a single NATO surface ship against Soviet ships and aircraft.
**No submarines on either side**, for now. The fight is purely surface and air:
incoming missiles, raids and missile boats.

---

## Run structure

- A **run** is up to 4 waves of 15 minutes each (game time). It ends when you
  sink or when you clear wave 4.
- Your ship's weapons are set to Free, so they fire on their own. You steer,
  hold distance, collect pickups and manage your sensors' emissions (EMCON).
- **Level-up:** the game pauses and offers 3 cards. You get 1 reroll and
  1 skip per wave.
- **Between waves:**
  - a partial ammunition refill (50% of what is missing);
  - a hull repair (50% of lost integrity);
  - one Doctrine pick (choose 1 of 3 rare cards).

### Starting hulls

| Hull | Unit | Strengths | Starting perk |
|---|---|---|---|
| Perry frigate | `usn_ffg_oliver_hazard_perry` | Levels fastest, but fragile: 2 hull channels, 6 Harpoons, dies to 2 P-15 missiles | +25% XP |
| Spruance gun destroyer | `usn_dd_spruance` | Most firepower, thinnest air defence: one Sea Sparrow channel, a 15-minute Sea Sparrow reload | Gun rate of fire, rank 1 |
| Kidd destroyer | `usn_ddg_kidd` | The safe pick: 3 SAM channels and two twin-arm SM-1 launchers | SAM reload −20% |

## XP and levels

- **XP per kill** is the enemy's own `_unitScoreValue`, scaled. Rough guide:

  | Kill | XP |
  |---|---|
  | Patrol boat | 1 |
  | Helicopter | 2 |
  | Aircraft | 3 |
  | Osa missile boat | 4 |
  | Frigate | 15 |
  | Boss | 100 |

- **Incoming missile shot down:** 0.5 XP. Defence pays.
- **Level curve:** XP needed for the next level = 5 + 4 × level.
- **Who gets credit:** the game only records damage per side, so the mod tracks
  the last unit to hit each target (see [hooks](docs/hooks.md#kills-and-xp)).
  Kills by your escorts count as yours.

## Upgrade pool

Up to 5 ranks each unless stated. The method column says how the effect is
applied; details are in [hooks.md](docs/hooks.md#upgrades).

- **Direct:** change a value the game reads live.
- **Patch:** intercept a game calculation with a Harmony hook.
- **Spawn:** create a friendly unit.
- **Unverified:** no hook has been found in the code yet.

### Sensors

| Upgrade | Effect per rank | Method |
|---|---|---|
| Mast height | +20 m search-radar antenna height (about +3 nm against an Osa at rank 1, less each rank after) | Direct, confirmed in game |
| Low-altitude search | Lowers air-search radars' minimum altitude (rank 1: halved, rank 2: 0 m) | Direct |
| Radar gain | +1.5 dB, for burn-through against jammers | Direct |
| Scan rate | +20% antenna RPM, so contacts confirm sooner | Patch (private field) |
| ESM sensitivity | Detect enemy radar emissions from farther away | Unverified |

**What limits detection.** The radar horizon is what limits detection of ships
and sea-skimmers, not power: a Spruance cannot see an Osa beyond about 18 nm,
whatever its radar's output. Mast height is the only stat that moves the
horizon, and it grows only with the square root of height, so the Targeting
helicopter and Early warning escorts remain the real way to see past it.

**Upgrades dropped.** "Radar power" did nothing useful and is gone. Look-down
range is also gone, because no ship radar uses it. See
[combat model](docs/combat-model.md#radar-detection).

### Fire control and defence

| Upgrade | Effect per rank | Method |
|---|---|---|
| **Fire-control channel** | +1 target and +1 missile channel on every guidance radar (rare, max 3) | Direct |
| SAM kill probability | +5% intercept chance | Direct |
| CIWS intercept | +5% intercept chance (CIWS: close-in point-defence guns, e.g. Phalanx) | Direct |
| Reaction time | Faster target acquisition and engagement | Direct |
| Salvo doctrine | Fire 2 SAMs per threat, then saturation (max 2) | Direct |
| Decoys | Chaff more likely to spoof; faster chaff reload | Direct + Patch |
| Jammer | Better chance to jam incoming radar-guided missiles | Unverified |

Fire-control channels are the real limit on how much a ship can engage at once
(see [combat model](docs/combat-model.md#fire-control-is-the-bottleneck)).
This upgrade is the "more projectiles" stat.

### Weapons

| Upgrade | Effect per rank | Method |
|---|---|---|
| Missile range | +15% launch range, with drag corrected so the missile really flies there | Direct |
| Missile reload | −15% launcher reload | Direct |
| SAM reload | −15% SAM launcher reload | Direct |
| Gun rate of fire | +15% | Patch |
| Gun damage | +15% shell power | Direct |
| Deep magazines | +25% capacity across all magazines | Direct |

Gun range was dropped: guns need dozens of hits, so range barely matters.

### Survival

| Upgrade | Effect per rank | Method |
|---|---|---|
| Hull integrity | +15% maximum hidden integrity (effectively max HP) | Direct |
| Patch-up | Restore 10% of hull integrity on each level-up | Direct |
| Damage reduction | −8% incoming weapon damage | Patch |
| Damage control | +20% repair rates, +1 damage-control team | Direct |
| Engines | +6% top speed | Direct |
| Replenishment range | +0.5 nm pickup radius | Mod-side |

"Armour" was dropped as a name. The game's armour classes only affect guns;
every large anti-ship missile ignores them.

### Escorts

| Upgrade | Rank 1 | Later ranks |
|---|---|---|
| Targeting helicopter | An SH-2F that spots ships beyond the radar horizon, so Harpoons can use their full range | Faster respawn |
| Fighter cover | 2 F-14s on combat air patrol every 5 minutes | Shorter interval |
| Early warning | An E-2 Hawkeye overhead | Wider coverage |
| Wingman frigate | A Perry that follows you | More ships, or heal it |

Close-in guns (CIWS) only defend their own ship, so a wingman must survive on
its own defences.

### Doctrine picks (between waves only)

| Pick | Effect |
|---|---|
| Swarm Breaker | Each missile shot down reloads 1 round into a random SAM launcher |
| Silent Running | With radar off, take 30% less damage, and enemies spawn farther out |
| Overmatch | +25% missile damage against targets below half integrity |
| Gunline | Gun kills give +50% XP; gun magazines refill twice as fast |

---

## Waves

A threat budget grows each minute. The director spends it on enemy groups from
the wave's menu.

When each group spawns, the director:

1. reveals the player's ship to that group, so they attack at once;
2. gives it waypoints toward the player, because spawned enemy ships do not
   close the distance on their own;
3. sets it to Free.

**Missile cap:** when more than about 12 missiles are already heading for you,
the director holds back new air raids.

### Spawn rings

| Threat | Distance |
|---|---|
| Missile boats | 25–35 nm, just past radar range at sea level |
| Surface groups | 40–60 nm |
| Air raids | 120–150 nm, high altitude |

### Wave 1

| Time | Arrivals |
|---|---|
| 0–3 | Patrol boats, single helicopters |
| 3–6 | Osa pairs (`wp_ptg_osa1`, P-15 missiles), the first incoming missiles |
| 6–9 | First air raid: 2 Tu-16K (`wp_tu-16k`, Strike loadout, KSR-5 missiles) |
| 9–12 | A second raid from a different bearing, and a destroyer pair |
| 12–15 | Everything, budget ×2 |
| 15:00 | **Boss: Sovremenny** (`wp_em_sovremenny`). Sink it to clear the wave. |

### Later waves

| Wave | Budget | New threats | Boss |
|---|---|---|---|
| 2 | ×1.6 | Sovremenny groups with SS-N-22 (Moskit) missiles | Slava (`wp_rkr_slava`) |
| 3 | ×2.4 | Tu-22M2 (`wp_tu-22m2`) with Kh-22 missiles, launched from long range | Kirov (`wp_rkr_kirov`) |
| 4 | ×3.5 | Everything | Kiev carrier group (`wp_takr_kiev`) |

Kresta II was dropped as a boss: its only missile, the SS-N-14, is
anti-submarine.

---

## Pickups

Neutral ships spawn on timers. Sailing within the pickup radius (2 nm to
start) applies the effect and despawns the ship, with no kill scored. Each
pickup is revealed to the player when it spawns and announced, e.g.
"USNS Kilauea on station bearing 270, 14 nm".

| Pickup | Unit | Gives | Behaviour |
|---|---|---|---|
| Ammunition ship | `usn_ae_kilauea` | Refills 50% of missing missiles, including the Harpoon launchers | About 15 kn, spawns 10–20 nm out, leaves after 4 minutes |
| Fast combat support ship | `usn_aoe_sacramento` | Ammunition refill and 25% hull integrity | About once per wave |
| Oiler | `usn_ao_t2` | +20% top speed for 3 minutes | Common |
| Supply helicopter | `usn_ch-46` | 4 missiles reloaded | Flies to you, announced 3 minutes ahead; you must survive until it arrives |

Enemy active-radar missiles lock onto the strongest radar return, and a supply
ship is huge. A pickup near you may therefore draw fire, or be sunk before you
reach it. A sunk pickup scores nothing either way.

## Hazard: airliners

Civilian airliners (`civ_707`, `civ_dc-10`, `civ_il-62`, `civ_tu-154`) cross
the area on timers. They are neutral, so weapons set to Free leave them alone.
Shooting one down costs heavily. Any "fire on anything" doctrine therefore
becomes a real gamble.

---

## Open questions

1. **Wave count:** 4 waves of 15 minutes is assumed and not yet confirmed.
2. **Airliner penalty:** lose XP, or end the run?
3. **Escorts:** do escorts' kills count fully as yours?
4. **Unit names:** the patrol-boat unit name is still unconfirmed. F-14 is
   `usn_f-14a` and E-2 is `usn_e-2c`, both confirmed by the campaign mod's
   generated battle, which loaded and played.

## First milestone: proof of concept

One quick battle and one hotkey. Pressing it should:

1. spawn an enemy destroyer 20 nm out;
2. double the player's radar range;
3. halve one launcher's reload time;
4. refill a magazine;
5. log XP when something dies.

If all five work in game, the rest is content and balance.
