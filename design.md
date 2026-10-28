# Design

Working draft, agreed 2026-10-28. The numbers are starting points for tuning.
Where this document and the code disagree, the code is current:

- the run script is in [Script.cs](src/SeapowerSurvivors/Script.cs);
- XP is in [Progression.cs](src/SeapowerSurvivors/Progression.cs);
- the upgrade lines are in [Upgrades.cs](src/SeapowerSurvivors/Upgrades.cs).

**The ambition.** A simple start turns into something crazily overpowered: a
Sverdlov that starts the run with ordinary 152 mm guns is firing them at 500%
damage and ten times the rate by 15:00. The enemy matches that with
numbers, not quality.

No submarines on either side, for now.

---

## The run

- **Length.** One continuous 15-minute run, in mission time. Surviving to
  15:00 clears it. Losing the starting ship ends it.
- **Final boss.** The run ends with a final boss group, for example an Iowa
  and a Kirov with escorts. *(Not built yet.)*
- **Who fights.** The fleet's weapons are set to Free and fight on their own.
  You steer, manage emissions and pick upgrades.
- **Survival is missile defence.** The ships are exactly as fragile as stock
  Sea Power. Staying alive comes from having missiles to fire, hitting with
  them, and point defence for what leaks through.
- **Every level-up fully rearms every ship in the fleet** (magazines,
  launchers, canisters and guns), with the game's own `RearmAll`. Level-ups
  are the ammunition supply; there are no supply ships.

### Starting ships

Chosen from a short list. The enemy is the other navy.

| Ship | Unit | Status |
|---|---|---|
| Oliver Hazard Perry | `usn_ffg_oliver_hazard_perry` | Built |
| Kidd | `usn_ddg_kidd` | Built |
| Long Beach | `usn_cgn_long_beach_83` | Built |
| Iowa | `usn_bb_iowa` | Built |
| Sverdlov | `wp_kr_sverdlov` | Needs the NATO enemy roster |

## XP and levels

Missile intercepts are the steady XP stream; ships and aircraft pay in larger
lumps.

| Source | XP |
|---|---|
| Enemy missile shot down by our SAM or CIWS | 1 |
| Zhuk / Stenka / Komar / Osa | 2 / 3 / 3 / 4 |
| Tarantul / Nanuchka | 6 / 8 |
| Kashin / Kresta I / Sovremenny | 15 / 20 / 30 |
| Yak-38 / MiG-27 / Su-24A / Su-24M | 3 / 4 / 5 / 6 |
| Tu-16 / Tu-16K / Tu-22M2 | 8 / 8 / 12 |

- **Who gets credit.** An enemy missile counts only if one of our SAM blasts or
  CIWS mounts struck it.
- **The level curve:**

  | Levels | XP for the next level | Wall |
  |---|---|---|
  | 1–9 | 2 × level | — |
  | 10–19 | 3 × level | +20 at level 10 |
  | 20 and up | 4 × level | +50 at level 20 |

- **The card screen.** The game pauses and offers three cards per level, with
  banked levels shown one after another. You get 3 rerolls a run, and a skip.

## Upgrades

All boosts are **direct and stacking**, and apply **across the whole fleet**.
A ship that joins later gets everything already taken. They are written into
each ship's own copies of its parameters, so the enemy's identical weapons
are never boosted.

### Weapon lines (built)

One line per kind of armament in the fleet, each with a **fixed path of
ranks**. A line is offered only if the fleet carries that kind of weapon.
**Arcade, not simulation: three ranks of a line should already feel
overpowered**, and the rest of the path takes it somewhere absurd. Most ranks
carry two effects at once. The fleet's defence scores weight ranks heavily,
so missile and air waves arrive as soon as the upgrades can meet them.

| Line | Ranks | Rank 1 | By rank 3 | At max rank |
|---|---|---|---|---|
| Guns | 8 | Rate of fire and damage ×3 | Rate of fire and damage ×12 | ×384 each |
| Flak | 6 | Proximity fuzes and AA fire-control tables; fuze radius ×4 | Surface-only guns engage aircraft, prediction error ×0.25, fuze ×16 (5 m → 80 m on a 100 mm) | Fuze 180 m, prediction error ×0.125 |
| Surface-to-air missiles | 8 | Reload ×0.25, kill probability +20 | Reload ×0.06, Pk +50, +2 channels on every director | Reload ×0.016, Pk +90 (capped at 1), +8 channels |
| Anti-ship missiles | 8 | Warhead damage ×3 | Damage ×9, speed ×2, range ×2, RCS ×0.5 | Damage ×36, speed ×3, range ×3, RCS ×0.125 |
| Point defence (CIWS) | 6 | Intercept +30, rate of fire ×2 | Intercept +80 (capped at 99), rate of fire ×8 | Rate of fire ×36 |
| Countermeasures (chaff) | 5 | Spoof +25, reload ×0.5 | Spoof +65, reload ×0.125 | Spoof capped at 0.95, reload ×0.06, the game's "three spoofs then a forced failure" rule lifted |

**Why Flak grows the fuze, not the aim.** Traced in game: with fire-control
error removed, shells still pass 100–200 m from a Yak-38, because it moves
while they fly and the game's lead assumes it flies straight. A bigger burst
radius covers that.

**Fire-control channels are the SAM's rate of fire.** A director with one
channel engages one target however fast its launcher reloads, so the SAM line
adds channels.

### Crew lines (built)

| Line | Ranks | Per rank |
|---|---|---|
| Reaction | 5 | The OODA loop runs four times as fast: contact processing, alignment delay, engage checks, target acquisition |
| Gunnery | 5 | Fire-control error quartered, solutions four times as fast |

**Damage control was dropped.** These ships die to two or three hits, so
repair rarely gets the chance to matter. Countermeasures took its place:
a decoyed missile is a stopped one. Its ranks also count towards the
fleet's anti-missile score.

### Fleet ships (next)

- **Where they come from.** Each mini-boss sunk opens a **chest** offering a
  choice of 2–3 ships to join you.
- **Joining.** The chosen ship spawns nearby and holds station on you; you can
  command it as normal.
- **Upgrades.** Its weapons join the weapon lines, so every ship joins already
  upgraded.

### Air track (later)

Aircraft come on their own track, every 5 levels to begin with, separate from
ship chests:

| Asset | What it does |
|---|---|
| AWACS | Sees past the horizon for the whole fleet |
| CAP | A fighter pair that hunts aircraft. It is also the answer to stand-off raids, which appear only once the fleet has air cover |
| Strike flight | Attack aircraft that hit the nearest enemy group on a cooldown |

---

## Enemies

**Enemies never get individually stronger. They only get more numerous**,
through the script's quotas.

The script has one row per minute. Each row sets four things:

- **a menu** of groups the director may send;
- **a quota** of enemy units it keeps alive, topped up as they die;
- **a minimum interval** between sends;
- **anything scripted** for the top of the minute.

| Minute | Quota | Scripted |
|---|---|---|
| 0–2 | 2 → 4 | Patrol boats, torpedo boats, Komars |
| 3 | 4 | Mini-boss: Kashin |
| 5 | 6 | Osa pack: six boats on one bearing |
| 6 | 6 | Mini-boss: Tu-16 trio |
| 8 | 7 | Yak-38 swarm |
| 9 | 8 | Mini-boss: Kresta I |
| 11 | 9 | Osa pack |
| 12 | 9 | Mini-boss: Sovremenny |
| 13 | 10 | Yak-38 swarm |
| 14 | 12 | Everything |

**Mini-boss chests.** Each mini-boss sunk pays a chest. For now that is one
level-up (three at a 20% chance); next, a choice of ships.

### Spawning

- **Surface groups spawn at the edge of the fight,** not on fixed rings. That
  is just outside the fleet's reach against them, plus about a minute's
  closing. Reach means the fleet's longest anti-surface weapon, capped at the
  radar horizon against that target. Enemies come in at full speed.
  - Fixed rings put a gun cruiser's targets an hour's steaming away: no kills,
    no XP, and no levels.
  - Spawning at the edge means the player opens fire within a minute, and a
    short-ranged enemy is shot at all the way in.
- **Faster kills, not hit XP.** The opening minutes are fragile boats in threes
  (Zhuks, PGMs) at a higher quota, so kills come every 20–30 seconds.
- **Missile groups** wait for the fleet's anti-missile score (see
  [Defence.cs](src/SeapowerSurvivors/Defence.cs)). A surface boss always
  comes, but with its anti-ship launchers out of action until the score is
  reached.
- **Air groups** wait for the fleet's anti-air score, in proportion to their
  size:

  | Group size | Anti-air score needed |
  |---|---|
  | One aircraft | Always allowed |
  | Two | 3 |
  | Three | 6 |
  | Four or more | 10 |

  Aircraft airborne at once are capped at half the anti-air score. An air
  boss the fleet cannot face is replaced by a surface one.
- **Stand-off raids** (Tu-16K, Tu-22M2, B-52G) also wait for air cover.
- **Caps.** At most 16 enemy units are alive at once. Air groups are held
  while more than 14 enemy missiles are in flight.

---

## Not doing

These were considered and dropped:

- **Sensor and hull items:** mast height, radar gain, low-altitude search,
  engines, deep magazines, hull toughness and regeneration, and damage
  control.
- **Supply ships:** rearming comes from levels instead.
- **Swapping weapons for newer real variants** (for example RIM-66B →
  RIM-66E): direct boosts instead.
- **Enemy quality scaling.**

## Open questions

1. **Escorts and XP:** do kills by joined ships and aircraft count fully as
   yours?
2. **Final boss:** what is its composition for each side?
