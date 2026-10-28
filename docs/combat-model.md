# Combat model

How Sea Power's weapons, fire control and damage work. This comes from shipped
INI data and from `Seapower-Scripts.dll` decompiled with ilspycmd, build
**0.8.4 Build 261002**. **Nothing here has been tested in game yet.**

References are `Class.Member` in the decompiled game code. Line numbers change
with every decompile, so they are left out. Data paths are relative to
`Sea Power_Data/StreamingAssets/original/`.

---

## Where the data lives

| What | Where |
|---|---|
| Ship hull, its weapon systems and sensors | `vessels/<type>.ini`, with `<type>_variants.ini` |
| Aircraft and loadouts | `aircraft/<type>.ini`, with `<type>_squadrons.ini` |
| Ammunition (444 files) | `ammunition/<ammo>.ini` |
| Launcher, gun and CIWS performance, keyed by `SystemName` | `systems/weapons.ini` |
| Radars, fire-control channels | `systems/sensors.ini` |
| System hit points by `ModuleType` | `systems/modules.ini` |
| Impact sizes, armour matrix | `ammunition/damage.ini` |

- **Weapon changes come from loadouts, not variants.** Ship variants are
  almost all cosmetic. To change weapons, the vessel file lists
  `AvailableLoadouts=Default,Late`, and a suffixed section
  (`[WeaponSystem1Late]`) replaces the base section, apparently chosen by
  mission year (`LoadoutDate_Late=1983`).
- **Spruance's real alternatives are separate files:** `usn_dd_spruance_abl`
  (armoured box launchers) and `usn_dd_spruance_vls` (Mk41 vertical launch
  system).

## Weapons a target can be engaged with

The ammunition's `TargetType` (AAW / ASuW / ASW) decides what it can be fired
at, and so does the director's `Role` in `sensors.ini`.

- **Surface targets only:** Harpoon and every Soviet anti-ship missile
  (SS-N-2/12/19/22, AS-2/4/6). None of them can engage aircraft or missiles.
- **Air first, ships second:** SM-1, Sea Sparrow and SA-N-3/4/6/7 are AAW with
  `SecondaryTargetType=ASuW`. The director must still allow surface targets:
  - Perry's MK92 and Kidd's SPG-51 do, so their SM-1s can hit ships.
  - Spruance's MK-95 is `Role=Air`, so its Sea Sparrows cannot be fired at
    ships.
- **Anti-submarine only:** ASROC and SS-N-14A. Kresta II therefore has no
  anti-ship missile.

## Fire control is the bottleneck

A semi-active or command-guided missile, or a gun, needs a director channel to
fire. Each director has two limits: `TargetChannels`, the number of targets
engaged at once, and `WeaponChannels`, the number of missiles in flight
(`SensorSystem._targetChannels` / `_weaponChannels`). Active-radar
fire-and-forget missiles (Harpoon, Soviet anti-ship missiles) skip this check.

| Hull | SAM channels | Notes |
|---|---|---|
| Perry | 2 (MK92 FCS + SPG-60), targets ships too | Mk13 single rail, `ReloadTime=4`; magazine 34 SM-1 + 6 Harpoon |
| Spruance | 1 target / 4 missiles (one MK-95), air only | Mk29 8 cells, **`ReloadTime=900`**; 8 Harpoons, no reload |
| Kidd | up to 3 (2× SPG-51 + SPG-60), targets ships too | 2× Mk26 twin-arm, `ReloadTime=7`; 52 SM-1, 8 Harpoons |

Soviet directors for comparison: Top_Dome (SA-N-6) 3 targets / 6 missiles;
Front_Dome (SA-N-7) 1/2 each, ×6 on Sovremenny.

## Radar detection

Search radar detection is decided in `SearchRadar.createRadarEchoes`, once
per scan for every object.

- **The parameters are live.** Each radar on each ship builds its own
  `RadarParameters` at spawn, and every scan reads it.
- **There is no hidden copy.** `SensorDataRadarComponent` is a per-sensor-type
  descriptor, used only for point values, the encyclopedia and ESM.
- **Changes do not survive** a reload or respawn.
- **Distances** are in Unity units: 1 unit = 67.2 m, and 1 nm = 27.56 units.

A target is detected only if it passes **every** gate below. In practice the
binding gate is almost always the horizon or the altitude window.

| Gate | Where | Reads |
|---|---|---|
| Role | `Radar.IsDetectableTarget_Unconditional` | `_rp._role`. A Surface radar never sees air targets; an Air radar never sees ships |
| Altitude window (air targets only) | `Radar.IsDetectableTarget_Conditional` | `_rp._minAltitude` / `_maxAltitude` against `AltitudeOverClutter`. **A hard reject** |
| Max range | same | `_rp._maxRange` |
| Horizon and terrain | `UnitSensorCache.UnitsHaveRadarlLOS` → `RadarCalculator.getRadarHorizon` | A 4/3-earth horizon from antenna height (`SensorPosition.y`, which includes `_sensorOffset`) and the target's `VisibleAltitude` |
| Field of view, look-down | same | View arcs; `_lookDownRange` only when `LookDownMultiplier < 1` (no ship radar sets it) |
| Radar equation | `RadarCalculator.createRadarEcho` | R = [P·1000·G²·λ²·σ / (Pmin·1000·(4π)³)]^¼, from `_peakPower`, `_gainFactor`, `_waveLength` and the target's RCS (lerped frontal to broadside); ×(1 − 0.05·SeaState) for ships |
| Jamming | same | Burn-through from support jammers only. Search radars ignore self-screening jammers |
| Two-sweep confirmation | `SearchRadar` | A contact needs two consecutive sweeps. The sweep period is 60/RPM: 8 s on an SPS-40 at 15 RPM |

**The horizon is the real limit.** Example: a Spruance's SPS-40 against an
Osa.

| Gate | Range |
|---|---|
| Radar equation | about 220 km |
| `_maxRange` | 407 km |
| **Horizon** | **about 18 nm** |

An Osa at 20 nm is invisible whatever the radar's power. Air targets at
altitude are the only case where `_maxRange` or power ever binds.

**Air-search radars cannot see sea-skimmers.** The altitude floor rejects
them outright:

| Radar | Minimum altitude |
|---|---|
| SPS-40 (Spruance) | 10 m |
| SPS-48 (Kidd) | 15 m |
| SPS-49 (Perry) | 3 m |

Low fliers fall to the short-range radars (Nav, SPQ-9, Mk92 search), which
have a 0 m floor and reach 32–37 km.

**Contacts are shared.** Every sensor feeds its unit's plotting table, which
feeds the taskforce table unconditionally. A helicopter's radar contact is
therefore the whole force's. There is no contact cap.

**Power is a poor trade.** Detection grows as P^¼ but enemy ESM range as
P^½, so ×16 power doubles detection and quadruples ESM exposure. Gain is
better: detection grows as G^½, and ESM exposure as G^½ too. Set
`_gainFactor` directly: `_gain` (dB) is only converted in the constructor.

### Radar fits

Ranges in km; minimum altitude in m. Source: `systems/sensors.ini`.

| Hull | Search radars | Fire control |
|---|---|---|
| Spruance | SPS-40 (Air+Surface, UHF, 407 km, 10 m), MK-23 TAS (Air+Surface, 120 km, 0 m), Nav (Surface, 32 km), SPQ-9 (37 km, 0 m) | MK-95 (Air, 1 target / 4 missiles), SPG-60 |
| Kidd | SPS-48 (3D, S band, 400 km, 15 m), Nav, SPQ-9 | 2× SPG-51 (167 km), SPG-60 |
| Perry | SPS-49 (Air only, 463 km, 3 m), SPS-55 (Surface, 65 km), Mk92 search (37 km) | MK92 FCS (86 km), SPG-60 |
| Osa I | Square Tie (Surface, 74 km), Nav | Drum Tilt (22 km) |
| Sovremenny | Top Steer, Top Plate (300 km), 3× Palm Frond (46 km) | 6× Front Dome, Band Stand (Moskit, 129 km) |
| Slava | Top Steer, Top Pair (600 km), 3× Palm Frond | Top Dome (3 targets / 6 missiles), Front Door (Bazalt, 500 km) |

Target RCS in m² (`[RCS]` in `sensors.ini`):

| Target | RCS |
|---|---|
| Harpoon | 0.2 |
| P-15 | 6 |
| Osa | Meager (50) |
| Tu-16 | 160 |
| Perry | SemiLarge (1,000) |
| Spruance, Kidd, Sovremenny | Large (10,000) |
| Slava | VeryLarge (100,000) |

## From contact to launch

`AI.OnFixedUpdate` runs about once a second. `WeaponAllocator` and the
`ReactionRules` system are **dead code**: nothing calls them.

1. **Track.** The contact is in the ship's or taskforce's plot. Air contacts
   then pass an "orient" delay first (`AI.ProcessContacts`, crew
   `_newContactProcessTime`), limited by `ReactionTime.ConcurrentOrientLimit`.
2. **Status.**
   - Units: `Free` engages hostiles; `Tight` engages only threats within
     10 nm.
   - Incoming missiles: ignored for their first 5 s of flight.
3. **Target priority** (`ObjectBase.GetEngagePriority`). Lower number goes
   first, then nearest:

   | Target | Priority |
   |---|---|
   | Threats aimed at us | 1 + number already in flight at it |
   | Missiles | 3 + number already in flight at it |
   | Ships within 8 nm: fast attack craft | 2 |
   | Ships within 8 nm: anti-surface ships | 3 |
   | Ships within 8 nm: other ships | 4–5 |

4. **Overkill check** (`AI.CheckIfAlreadyHandled`). This adds up the
   `_saturationValue` of everything already in flight at the target. For the
   player's ship against air targets, doctrine `AASalvoSize` (One / Two /
   Saturation) decides instead.
5. **Ammo choice** (`AI.SelectLauncherAmmoForAutoEngage`). Against ships it
   keeps the matching ammo with the **longest** range.
6. **Launcher sequence** (`WeaponSystemLauncher.OnUpdate`), in order:
   - random engage delay;
   - acquisition time;
   - free weapon channel;
   - the fire-control radar must actually see the target (this is also the
     line-of-sight check);
   - arc and slew;
   - fire-rate delay;
   - shared launch interval (Harpoon: 5 s).

   Acquisition time ladder (`sensors.ini [TargetAquisitionTime]`):

   | Speed | VerySlow | Slow | Medium | Fast | VeryFast |
   |---|---|---|---|---|---|
   | Seconds | 80 | 50 | 30 | 15 | 7 |

## Missile flight and seekers

- **Flight profile:** loft, then sea-skim from `SeaSkimmingStartDistToTarget`,
  then terminal altitude, with an optional pop-up. Selective pop-up only
  happens against ships over 420 t.
- **Active-radar seekers lock the strongest echo** (RCS ÷ R²) among **all**
  ships in view, ignoring which side they are on
  (`ActiveRadarSeeker.searchForTarget`). After 180 s active, a seeker may
  switch its target. Friendly ships and large neutral ships can therefore be
  hit.
- **Semi-active (SARH) missiles** home only on their commanded target while the
  director keeps guiding them. If guidance is lost, they self-destruct.
- **Chaff** (`SeekerBase.CheckCountermeasureLock`):
  - One roll per seeker every 35 s.
  - Chance = `_decoySpoofChance` × the difficulty modifier, minus the
    missile's `_antiChaffBonus`.
  - **Pity rule:** after 3 spoofs in a row against the player, the next one
    is forced to fail.
- **ECM** (`ECM.jamWeapon`):
  - One roll per missile, inside 3 nm, and only if the jammer's band covers
    the seeker.
  - A jammed missile is a guaranteed miss.

## Interception

- **SAMs: one dice roll per missile, about 1 nm before arrival**
  (`Utils.GetInterceptionChance`). The chance starts from the missile's
  `KillProbability`, then:
  - **Target size:** large aircraft are easier (+0.1, or +0.2 against SARH);
    helicopters −0.05; missiles −0.2.
  - **Speed:** a penalty above 570 kn.
  - **Low altitude:** −0.01 to −0.12.
  - **Outside the SAM's engagement envelope:** −0.5, capped at 0.05.
  - **Target bonus:** minus the incoming missile's `MissileDefenceBonus`
    (P-500 and P-700: 0.15).
  - **Floor:** 0.05.

  A failed roll forces a physical miss.
- **Sea-skimmers below a SAM's `MinAttackAltitude` drop to about 5%.** Example:
  SM-1 has a 50 ft floor, and SS-N-22B terminal altitude is 23 ft.
- **CIWS is a statistical model run each tick**
  (`WeaponSystemCIWS.ResolveFireTick`). It is calibrated so that a reference
  engagement reproduces the `MissileInterceptChance` in `weapons.ini`:

  | CIWS | Intercept chance |
  |---|---|
  | Mk15 | 40 |
  | Mk15 Block 1 | 50 |
  | AK-630 | 20 |
  | AK-230 | 10 |

  A CIWS only engages missiles **aimed at its own ship**.
- **Missile and aircraft hit points** apply to guns and CIWS only. Missiles
  have 1–10 HP from dry mass, aircraft 1–32 HP. A missile's `[General] Armor`
  cuts cannon damage (P-500 and P-270: 0.3). Any blast in range of an aircraft
  kills it instantly.

## Damage to ships

### The hidden integrity pool is the real HP bar

`Compartments`:

- M = 2.5 × displacement^(1/3) × armour factor. `[Physics] IntegrityOverride`
  replaces M outright.
- Structure is 10 compartments of M each.
- The **hidden pool** `_limitIntegrity` starts at 5M. **Damage control never
  refills it.**
- Drain per hit (`Blastzone.CheckForInstakill`):
  - missiles and bombs: 2 × D;
  - torpedoes: 3.5 × D;
  - gun shells: a table, D divided by 1–10 depending on shell and ship size,
    and 0 for small shells against big ships.
- D = warhead `Power` × weapon `DamageMultiplier` × 1.1, then multiplied by:
  - warhead type (fragmentation ×0.5, HEAT ÷3, cluster ÷4);
  - ×2 if the target is "spotted";
  - the difficulty modifiers.
- The ship explodes the tick after the pool goes below 0. In practice, hits to
  kill = floor(2.5M ÷ D) + 1.

### Hits to kill

Neutral difficulty (index 4). The default difficulty (index 2) gives the
player +25% damage dealt and −10% damage taken.

| Target | Hidden pool | Harpoons (Power 45) |
|---|---|---|
| Osa I (209 t) | 74 | 1 |
| Krivak I | 184 | 2 |
| Kresta II | 245 | 3 |
| Slava | 339 | 4 |
| Kirov | 444 | 5 |

| Player hull | Hidden pool | P-15 (66) | P-270 (52) | P-500 / P-700 / Kh-22 |
|---|---|---|---|---|
| Spruance | 301 | 3 | 3 | 2 |
| Perry | 200 | 2 | — | 1 (2 at difficulty 2) |

- **Guns:** 76mm or 130mm need roughly 40–60 hits on a ship, doing most of
  their damage through structure, fire and flooding.
- **Critical hits:** if one blast does at least 30% of current structure (at
  least 10% when below half), there is a 5–10% chance of an instant kill. The
  chance comes from the crew's `_instaDieChance` and the attacker's
  `_instaKillBonus`.
- **Armour:** `[General] ArmorType` only stops gun shells. Every large
  anti-ship missile is `Penetration=Always`.

### Systems, fire and flooding

- **System hits.** A hit damages any system whose `Collider=` is inside the
  blast. System HP comes from `modules.ini`:

  | System | HP |
  |---|---|
  | Sensor | 13 |
  | CIWS | 10 |
  | MediumLauncher | 20 |
  | VLS | 25 |

  One Harpoon-sized hit nearby usually wrecks the system.
- **Damage states.** Light, Medium and Severe each have a chance to knock the
  system out. A damaged radar still works but with worse tracks: range error
  5 / 10 / 20, bearing error 5° / 10° / 15°. Destroyed systems stay dead,
  except engines, shafts, rudder, bridge and combat control centre.
- **Fire.** Start chance on a missile hit is 50% plus half the missile's
  remaining fuel %. A fire burns compartment integrity every second and
  damages systems every 10 s.
- **Flooding.**
  - Below-waterline hits flood at a rate of D ÷ M.
  - Any open flooding caps speed at 10 kn.
  - A ship sinks when average flooding passes 40%, or when pitch passes 8°
    with flooding.
  - A ship capsizes when roll passes 70°.
- **Damage control.**
  - Teams: 1 under 7,000 t, 2 from 7,000 t, 3 over 24,000 t.
  - Each team re-plans every 60 s.
  - Structure is only repaired back to the compartment's worst point + 20%.
  - A system is only repaired to the top of the worst damage band it ever
    reached.

## Difficulty modifiers

`OptionsManager.SetDifficulty`; index 2 is the default. These modifiers apply
**only between the player's side and the AI**: damage dealt and taken, SAM,
CIWS, chaff and ECM chances, fire, damage-control losses and system
explosions. They are therefore usable as ready-made, player-only upgrade
hooks.

## Enemy AI

- **Same code as the player's ships.** Spawned enemies run the same
  `AI.OnFixedUpdate`.
- **Before they will fire**, the player's ship must be in their plot and
  identified as hostile. `Utils.RevealContactToObject` can do this.
- **Alerts spread.** An alert sets a ship to Free and passes to its taskforce
  mates within 10 nm.
- **Launch range is derated:** 0.85× against air targets, 0.95× otherwise.
- **Enemy ships do not close the distance on their own.** They need waypoints.
- **Aircraft do attack on their own.** They take targets within their maximum
  range + 15 nm.
