# Threat plan: who each ship faces, and when

**Status: first build in [Threats.cs](../src/SeapowerSurvivors/Threats.cs), not yet
played.** Every number here is a first guess, to be tuned in play. The game data comes from
build 0.8.4; where a figure is my estimate rather than a logged value, it says so.

## The rules this plan assumes

1. A run starts with one tiny boat and builds from there. For the first few
   steps, the next comes on a kill or a timer, whichever is first.
2. After that, a clock-driven curve sets a budget of danger for each pulse (about
   every 45 s). Kills and losses shift the curve within ×0.6–1.4.
3. Danger is **per ship**: the same enemy costs different amounts against
   different flagships, because it is a different problem for each.
4. **Gates decide who may come at all.** A locked enemy never spawns, however
   much budget there is. So a vanilla Sverdlov meets no aircraft and no missile
   ships, whatever the clock says.
5. The salvo limit and unit cap stay as separate ceilings on top.
6. Pulses build to a peak, then relax for 30–45 s. Bosses and set pieces stay on
   the clock, and come in a form the ship can face.

## Gates

Every ship's gates are set by hand, in the tables below, and not calculated from a
score: today's anti-missile score counts launchers, not how good they are, and
rates the Kiev highest of all. The salvo limit still uses that score as a ceiling.

A gate is met by the fleet as it stands now, so an escort or upgrade opens it. If
that escort sinks, the gate closes to new arrivals; enemies already out stay.

### What the gates allow

| Gate | Allows |
|---|---|
| **A1** | Bombers, one pair at a time |
| **A2** | Strike packages and missile-armed aircraft |
| **A3** | Stand-off raids |
| **M1** | Two-missile boats: Komar, Hauk, Snøgg |
| **M2** | Salvos of 4–8: Osa, Tarantul, Tiger, Kashin, Nanuchka, Pegasus, Knox |
| **M3** | Heavy ships: Adams, Lütjens, Belknap, armed Kresta, Sovremenny, Slava |

### What opens them, ship by ship

A gate lists any of the things that open it, and "start" means open from the first
second. In these tables:

- **E1, E2:** one or two escort ships with SAMs that still have rounds.
- **F:** a fighter flight.
- **G10, G15:** Guns and Accuracy both at that rank. Guns 10 and Accuracy 10 are how
  a gun ship introduces missiles and aircraft; both lines have 15 ranks.
- **S5, S10:** SAMs at that rank. SAMs 5 is **Sea-skimmer killers**.
- **P4, P8:** Point defence and Countermeasures both at that rank.

| Ship | Faces | A1 | A2 | A3 | M1 | M2 | M3 |
|---|---|---|---|---|---|---|---|
| Sverdlov | NATO | E1, F, G10 | E2, E1+F, G15 | A2+F | E1, G10, P8 | E2, E1+S5, G15 | E2+S5 |
| Iowa | Soviet | E1, F, G10 | E2, E1+F, G15 | A2+F | E1, G10, P4 | E2, E1+S5, G15, P8 | E2+S5 |
| Perry | Soviet | Start | E1, F, S5 | A2+F | Start | E1, S5 | E2, E1+S5, S10 |
| Kiev | NATO | Start | Start | F | Start | E1, S5 | E2, E1+S5, S10 |
| Sovremenny | NATO | Start | Start | F | Start | Start | E1, S5 |
| Long Beach | Soviet | Start | Start | F | Start | Start | E1, S5 |
| Kidd | Soviet | Start | Start | F | Start | Start | E1, S5 |
| Bunker Hill | Soviet | Start | Start | F | Start | Start | E1, S5 |
| Slava | NATO | Start | Start | F | Start | Start | E1, S5 |
| Kirov | NATO | Start | Start | F | Start | Start | E1, S5 |

## Danger values

One enemy of that type costs this much of the ship's budget. A dash means it does
not appear for that ship unless the ship's section says otherwise. Whether it may
come at all is still up to its gate.

### Soviet enemies, against NATO flagships

| Enemy | Gate | Perry | Kidd | Long Beach | Iowa | Bunker Hill |
|---|---|---|---|---|---|---|
| Zhuk | | 1 | 1 | 1 | 1 | 1 |
| Stenka | | 2 | 1 | 1 | 1 | 1 |
| Petya II | | 3 | 2 | 3 | 1 | 2 |
| Sverdlov | | 12 | 8 | 9 | 3 | 6 |
| Komar | M1 | 3 | 2 | 3 | 6 | 2 |
| Osa | M2 | 6 | 4 | 4 | 10 | 3 |
| Tarantul | M2 | 5 | 4 | 4 | 5 | 3 |
| Nanuchka | M2 | 8 | 5 | 5 | 10 | 4 |
| Yak-38 | A1 | 3 | 2 | 2 | 2 | 2 |
| Tu-16 | A1 | 4 | 3 | 3 | 3 | 3 |
| Su-24A | A2 | 7 | 5 | 4 | 6 | 4 |
| MiG-27 | A2 | 8 | 5 | 4 | 6 | 4 |
| Su-24M | A2 | 12 | 8 | 7 | 10 | 6 |
| Tu-16K | A3 | 15 | 10 | 9 | 14 | 8 |
| Tu-22M2 | A3 | - | 14 | 12 | 18 | 10 |

### NATO enemies, against Soviet flagships

| Enemy | Gate | Sverdlov | Sovremenny | Slava | Kirov | Kiev |
|---|---|---|---|---|---|---|
| PGM | | 1 | 1 | 1 | 1 | 2 |
| Sleipner | | 1 | 1 | 2 | 2 | 2 |
| Z-class | | 2 | 2 | 3 | 3 | 4 |
| C-class | | 2 | 2 | 3 | 3 | 4 |
| Battle | | 3 | 2 | 4 | 4 | 5 |
| Gearing | | 3 | 3 | 4 | 4 | 5 |
| Hauk, Snøgg | M1 | 6 | 2 | 3 | 2 | 3 |
| Tiger | M2 | 10 | 4 | 5 | 4 | 6 |
| Pegasus | M2 | 12 | 5 | 5 | 4 | 6 |
| Knox | M2 | 14 | 6 | 6 | 5 | 8 |
| Adams, Lütjens | M3 | 16 | 7 | 7 | 6 | 9 |
| Belknap | M3 | 18 | 9 | 8 | 7 | 10 |
| A-7E | A1 | 4 | 2 | 2 | 1 | 1 |
| A-4G | A1 | 4 | 2 | 2 | 1 | 1 |
| AV-8A | A1 | 5 | 2 | 2 | 2 | 2 |
| A-6E Harpoon | A2 | 12 | 5 | 4 | 3 | 3 |
| B-52G | A3 | - | 10 | 8 | 7 | 7 |

## Budgets

Each ship has two curves, on the unadjusted clock. Budget is the danger each pulse
may spend, and unspent budget carries over for one pulse. Units alive is the most
enemies out at that minute, not counting set pieces. It rises too, because numbers
are what overwhelm a ship: a Sverdlov can kill anything given time, but not while
facing twelve of them.

| Ship | Budget at 0 / 5 / 10 / 15 / 20 min | Units alive at 0 / 5 / 10 / 15 / 20 min |
|---|---|---|
| Perry | 1 / 4 / 8 / 12 / 16 | 2 / 4 / 7 / 10 / 12 |
| Kiev | 1 / 4 / 8 / 12 / 16 | 2 / 4 / 7 / 10 / 14 |
| Sverdlov | 1 / 4 / 8 / 12 / 18 | 2 / 4 / 7 / 11 / 16 |
| Slava | 1 / 5 / 9 / 14 / 18 | 2 / 5 / 8 / 12 / 16 |
| Sovremenny | 1 / 6 / 11 / 17 / 22 | 3 / 6 / 10 / 14 / 18 |
| Long Beach | 1 / 6 / 12 / 18 / 24 | 3 / 6 / 10 / 14 / 18 |
| Kirov | 1 / 6 / 12 / 18 / 24 | 3 / 6 / 10 / 14 / 18 |
| Iowa | 1 / 5 / 10 / 16 / 22 | 3 / 6 / 11 / 16 / 20 |
| Kidd | 1 / 6 / 12 / 18 / 24 | 3 / 6 / 11 / 16 / 20 |
| Bunker Hill | 1 / 8 / 16 / 26 / 36 | 4 / 10 / 16 / 22 / 25 |

Each ship joining adds to this through faster kills, not a fixed bonus.

**Damage is not softened.** Ships are fragile and most hits kill. If a ship's main
guns are knocked out, it is probably lost, and that is fine: there is no repair at
level-up and no drop in the curve to soften it.

## Ship by ship

Parentheses give the gate an entry waits for. Bosses arrive at 4, 8, 12 and 16
minutes. "Guns only" means its anti-ship launchers are out of action.

### Sverdlov (faces NATO)

**The vanilla run is a pure gun war.** No aircraft and no missile ships until an
escort or Guns 10 and Accuracy 10 open a gate. Twelve 6-inch guns can kill anything
that floats, given time and not too many targets at once.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 PGM, then 2 at most, then a Sleipner | None | None |
| 3–8 | PGM and Sleipner groups, 1 Z-class | Hauks (M1) | A-7E pair (A1) |
| 8–14 | Z and C-class, 1 Battle | Hauks, Snøggs (M1); Tigers (M2) | A-7E, A-4G pairs (A1); A-6E (A2) |
| 14–20 | Battles, Gearings, flotillas | Tigers, Pegasus, Knox (M2) | Above; AV-8A (A1) |

- **Bosses:** Battle pair; Gearing pair (A-6E trio only at A2); Oklahoma City, guns
  only; 16 minutes, an Oklahoma City and a Gearing pair. An Iowa only once Accuracy
  5 (Long shots) puts its 6-inch guns level with her 16-inch ones. Otherwise she
  outranges it by nearly 5 nm.
- **Watch for:** how long its first few kills take at x1. That sets how quickly the
  opening should move on.

### Iowa (faces Soviets)

Huge guns and a magazine of Tomahawks and Harpoons, but no SAMs. The danger is the
missile that gets through, so missile ships cost a lot and gun ships very little:
even an enemy Sverdlov is cheap prey.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 Zhuk, then Zhuks and Stenkas | None | None |
| 3–8 | Petyas, Stenka groups | Komars (M1) | Yak-38 pair (A1) |
| 8–14 | Petya packs, 1 Sverdlov | Komars (M1); Tarantuls, Osas (M2) | Yak-38, Tu-16 (A1) |
| 14–20 | Sverdlovs | Above; Nanuchka (M2) | Above; Su-24A, MiG-27 (A2) |

- **Bosses:** Kashin, guns only; Petya trio (Tu-16 trio at A1); Kresta, guns only;
  Sovremenny, guns only until M3.
- **Watch for:** when Guns 10 and Accuracy 10 arrive, as that is when its missile and
  air war begins for a player who takes no escorts.

### Perry (faces Soviets)

One launcher arm shared by SAMs and Harpoons, and one 76 mm gun. It gets
overwhelmed by numbers before quality, so the unit cap is the lowest.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 Zhuk, then 2 | None | None |
| 3–8 | Zhuks, Stenkas | 1 Komar pair | 1 Yak-38 pair |
| 8–14 | Stenkas, 1 Petya pair | Komars; Osas, Tarantuls (M2) | Yak-38, Tu-16 |
| 14–20 | Petyas | Osas, Tarantuls (M2) | Above; Su-24A, MiG-27 (A2) |

- **Bosses:** Petya trio; Tu-16 single, or a Petya trio; Kashin, guns only; Kresta,
  guns only. The Sovremenny only at M3.
- **Watch for:** this is the main test of "no guaranteed loss".

### Kidd (faces Soviets)

Balanced, and the reference for the other ships' numbers. It has the old baseline
run to compare against.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 Zhuk, then Zhuks and Stenkas | None | None |
| 3–8 | Zhuks, Stenkas, Petyas | Komars, then Osas | Yak-38, Tu-16 |
| 8–14 | Petyas, 1 Sverdlov | Osas, Tarantuls, Nanuchka | Su-24A, MiG-27 |
| 14–20 | Sverdlovs | Above, in larger packs | Su-24M; Tu-16K, Tu-22M2 (A3) |

- **Bosses:** Kashin; Tu-16 trio; Kresta; Sovremenny (M3, otherwise guns only).

### Long Beach (faces Soviets)

Strong long-range SAMs and Tomahawks, but its 5-inch guns reach only 9 nm. Close
gun ships cost more than they do for the Kidd, and aircraft less.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 Zhuk, then Zhuks and Stenkas | None | None |
| 3–8 | Zhuks, Stenkas, 1 Petya pair | Komars, then Osas | Yak-38, Tu-16 |
| 8–14 | Petyas | Osas, Tarantuls, Nanuchka | Su-24A, MiG-27, Su-24M |
| 14–20 | Petyas, 1 Sverdlov | Above, in larger packs | Above; Tu-16K, Tu-22M2 (A3) |

- **Bosses:** Kashin; Tu-16 trio; Kresta; Sovremenny (M3, otherwise guns only).

### Bunker Hill (faces Soviets)

Fast vertical launchers and very deep magazines, and it was far too easy. It gets
the steepest curve, and reaches missile groups within a few minutes.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 Zhuk, then quickly Stenkas and Petyas | Komars by minute 3 | Yak-38 by minute 3 |
| 3–8 | Petyas, 1 Sverdlov | Osas, Tarantuls, Nanuchka | Tu-16, Su-24A, MiG-27 |
| 8–14 | Sverdlovs | Osa packs, Nanuchkas | Su-24M; Tu-16K (A3) |
| 14–20 | Sverdlovs | Above, up to the salvo limit | Su-24M; Tu-22M2 (A3) |

- **Bosses:** Kashin; Tu-16 trio; Kresta; Sovremenny, armed from M3, which SAMs 5
  opens by mid-run.
- **Watch for:** a first close call well before minute 10.

### Sovremenny (faces NATO)

Good 130 mm guns and a short missile minimum range, so close-in boats are a fair
fight. It starts at M2 with two SA-N-7s.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 PGM, then PGMs and Sleipners | None | None |
| 3–8 | Sleipners, Z-class | Hauks, Snøggs | A-7E, A-4G |
| 8–14 | Z and C-class, Battles | Tigers, Pegasus | AV-8A, A-6E |
| 14–20 | Gearings, flotillas | Knox; Adams, Lütjens, Belknap (M3) | Above; B-52G (A3) |

- **Bosses:** Battle pair; A-6E trio; Oklahoma City; Iowa, guns only until M3.

### Slava (faces NATO)

Strong SA-N-6s, but one gun mount until range ranks let the SS-N-12s fire. It needs
gun targets it can actually reach while that happens, so the opening is slow.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 PGM, then 2 | None | None |
| 3–8 | PGMs, Sleipners | Hauks, Snøggs | A-7E, A-4G |
| 8–14 | Sleipners, Z-class | Tigers, Pegasus | AV-8A, A-6E |
| 14–20 | C-class, Battles | Knox; Adams, Lütjens, Belknap (M3) | Above; B-52G (A3) |

- **Bosses:** Battle pair; A-6E trio; Oklahoma City, guns only; Iowa, guns only.

### Kirov (faces NATO)

The strongest air and missile defence of the Soviet ships, but its 100 mm guns reach
11.3 nm and its SS-N-19s start at 30 nm. Aircraft and missile ships are cheap here,
and gun ships are the real problem early on.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 PGM, then 2 | None | 1 A-7E pair by minute 3 |
| 3–8 | PGMs, Sleipners | Hauks, Snøggs | A-7E, A-4G, AV-8A |
| 8–14 | Sleipners, Z-class | Tigers, Pegasus, Knox | A-6E |
| 14–20 | C-class, Battles | Adams, Lütjens, Belknap (M3) | A-6E; B-52G (A3) |

- **Bosses:** Battle pair; A-6E trio; Oklahoma City; Iowa, guns only until M3.

### Kiev (faces NATO)

Weak 76 mm guns reaching 8.6 nm: it fights through its aircraft, which the player
has to launch. It has older SAMs, so it starts at M1 by hand. Its opening is the
gentlest, to give those aircraft time to get airborne.

| Minutes | Surface | Missile ships | Aircraft |
|---|---|---|---|
| 0–3 | 1 PGM, nothing more until the first kill or minute 3 | None | None |
| 3–8 | PGMs, Sleipners | Hauks, Snøggs | A-7E, A-4G |
| 8–14 | Sleipners, Z-class | Hauks, Snøggs; Tigers (M2) | AV-8A, A-6E |
| 14–20 | C-class, Battles | Tigers, Pegasus, Knox (M2) | Above; B-52G (A3) |

- **Bosses:** Battle pair; A-6E trio; Oklahoma City, guns only; 16 minutes, an
  Oklahoma City and a Gearing pair, since an Iowa outranges everything it has
  except its aircraft.

## Set pieces

These stay on the clock, far out, and armed only if the salvo limit has room. A
ship held to guns only may not fire anything at the fleet but its guns: not its
anti-ship missiles, and not its SAMs in their surface mode. Each
ship's gates apply to their escorts as well: a Sverdlov with no M gates meets a
Belknap group guns only. If it later opens M3, the next group may be armed.

## Late-run ramp and the final assault

Built 2026-11-08, not yet played. Every number is a first guess (`Script.cs`, and
`BudgetHarden` / `AliveHarden` in `Threats.cs`).

Runs were trivial because of three limits, none of them the clock:

- A build with no escorts or fighters never opened a gate. An air-wing Sverdlov
  saw no aircraft and no missile from the first minute to the last.
- The game's AI holds fire. `AI.CheckIfAlreadyHandled` counts a ship as dealt with
  once the missiles in the air at it reach its `MissilesToSaturate`, or once a salvo
  fired over 20 s ago is still flying. Every enemy shares one taskforce, so one slow
  Styx could silence the sea for over two minutes.
- Pressure, health and breathers all pulled the end of a run back down.

What changed:

- **Curves.** Budget rises to ×1.6 and the unit limit to ×1.4 by minute 20, starting
  at minute 5.
- **AI saturation** (`CombatHooks.EnemyKeepsFiring`). From minute 8 the enemy may keep
  firing until it has up to ×2.5 a ship's usual saturation in the air by minute 17.
  In the final assault there is no limit. The enemy's guns stop waiting too.
- **Salvo share.** At full pressure the salvo limit now reaches 65–90% of the score.

**The final assault is the last three minutes** (from 17:00), three because a
sea-skimmer takes that long to arrive, so that the fire lands in the last two.

- A wave every 20 s, no relaxing, drawn from every stage at once and each navy's
  swarms. It stops spawning 45 s from the end, when nothing could reach the fleet.
- Gates are one step easier: M1 and A1 open for everyone, and each higher gate opens
  if the one below it does. A fleet with no missile defence meets only the small
  boats and bombers.
- No breathers, and pressure is pinned at 1 whatever hits the fleet. The salvo limit
  is 1.4× the score, up to 48 missiles, so a stronger fleet is sent more but a weak
  one is not sent all of it.
- Units spawn near enough to arrive before the end: missile ships at what their
  missiles can cover by 20:00, between 8 and 30 nm, aircraft by flying time, gun ships
  a minute outside their range. Boats that fire from close in are placed close in.
- **Stand-off raids** launch at 14:00 and 15:30, ahead of the assault, because of the
  missiles' flight time. They need the A3 gate after easing, and cost no budget.
  Soviet: Tu-16K with AS-6, then Tu-22M2 with AS-4, released just outside their
  100 nm minimum range. NATO: B-52G, then A-6E, at 30 nm with Harpoons.

Watch for: whether an upgraded fleet survives (the point is that it should),
"Final wave at" and "Enemy launch" log lines, and frame rate, since the unit limit
can reach 40.

## Open questions

1. **XP without kills.** A small award per pulse survived was the research's answer
   to the no-kills spiral. With the gentler opening it may not be needed, so the
   first build leaves it out, to be added if a run stalls.
2. **Waiting boats.** Tarantuls are listed at M2 like any other missile ship, but
   against a long reach they arrive too late to matter. They could spawn earlier
   for those ships, so they become the slow danger they were meant to be. The first
   build leaves this as it is.
3. **Escorts changing the sums.** A Kidd escort opens gates for a Sverdlov, but its
   danger values stay the Sverdlov's. A Tiger would then still cost 10, which may be
   too few of them for what that fleet can now handle. The first build leaves this
   too, since faster kills raise the budget a little.

## Decided

- Guns 10 and Accuracy 10 introduce missiles and aircraft to a gun ship.
- Every ship's gates are set by hand.
- A Sverdlov can kill small boats, given time and not too many at once.
- No repairs or softening for damaged guns: if they go, the ship is probably lost.
