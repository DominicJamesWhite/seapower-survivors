# Design

Working draft, agreed 2026-10-28. The numbers are starting points for tuning.
Where this document and the code disagree, the code is current:

- the run script is in [Script.cs](src/SeapowerSurvivors/Script.cs);
- XP is in [Progression.cs](src/SeapowerSurvivors/Progression.cs);
- the upgrade lines are in [Upgrades.cs](src/SeapowerSurvivors/Upgrades.cs).

**The ambition.** A simple start turns into something a little beyond the
best ever built: a Sverdlov that starts the run with ordinary 152 mm guns
is firing them at four times the damage and nearly three times the rate by
15:00. The enemy matches that with numbers, not quality.

No submarines on either side, for now.

---

## The run

- **Length.** One continuous 20-minute run, in mission time, meant to be played
  at x1, where the fights are readable. Surviving to 20:00 clears it. Losing
  the starting ship ends it.
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

Ten flagships, five a side, each fighting the other navy. Two are free, two
are bought, and the rest are earned by winning runs (see
[Flagship progression](#flagship-progression)).

| Ship | Unit | Navy |
|---|---|---|
| Sverdlov | `wp_kr_sverdlov` | Soviet |
| Oliver Hazard Perry | `usn_ffg_oliver_hazard_perry` | US |
| Kidd | `usn_ddg_kidd` | US |
| Sovremenny | `wp_em_sovremenny` | Soviet |
| Long Beach | `usn_cgn_long_beach_83` | US |
| Slava | `wp_rkr_slava` | Soviet |
| Iowa | `usn_bb_iowa` | US |
| Bunker Hill | `usn_cg_bunker_hill` | US |
| Kirov | `wp_rkr_kirov` | Soviet |
| Kiev | `wp_takr_kiev` | Soviet |

- **Kiev's air wing is the player's.** Its Yak-38s and helicopters stay in
  its hangar until the player launches them through the game's own flight
  deck controls; the mod launches nothing for it.
  - Once airborne they are fleet: every weapon upgrade reaches their missiles,
    bombs and crews, as it does a chest flight's.
  - They rearm on deck, the game's way. The Air wing line's refits and air
    resupply are for chest and starting flights, which have no deck.

## Between runs: the bank and the shop

After Vampire Survivors' gold and PowerUps.

- **The bank.** Every run's XP is banked when the run ends, however it ends:
  won, lost, quit, or stopped.
  - Only real runs count: ones that start by themselves, from the launcher
    or a `Survivors - ` mission.
  - A run started from the test panel banks nothing.
- **For scale:** a run that reaches level 10 has earned about 25 XP; one
  that reaches level 20, about 80; a full 20-minute run, about 1,000 (an estimate until measured). All
  prices are starting points for tuning.
- **The profile** lives in the game's persistent data folder, as
  `SeapowerSurvivors/profile.txt`.
- **Unlocks come from wins or purchases only.** No feats, no milestones
  part-way through a run.

### Flagship progression

```
            US                                   Soviet
   Perry (free)                         Sverdlov (free)
   Kidd (400 XP)                        Sovremenny (400 XP)
     │ win in Perry or Kidd               │ win in Sverdlov or Sovremenny
     ▼                                    ▼
   Long Beach                           Slava
     │ win in Long Beach                  │ win in Slava or Iowa
     ▼                                    ▼
   Bunker Hill                          Kirov
                                          ▲
   Iowa ◄─ win in Sverdlov (crosses navies)

   Kiev ◄─ wins in both Bunker Hill and Kirov
```

| Flagship | How it is had |
|---|---|
| Sverdlov, Oliver Hazard Perry | Free: every profile starts with them |
| Kidd, Sovremenny | Bought: 400 XP each |
| Long Beach | A win in Perry or Kidd |
| Slava | A win in Sverdlov or Sovremenny |
| Iowa | A win in Sverdlov |
| Bunker Hill | A win in Long Beach |
| Kirov | A win in Slava or Iowa |
| Kiev | Wins in both Bunker Hill and Kirov |

- **A win** is a run survived to the end in that flagship, from the
  launcher or a `Survivors - ` mission. It counts for the ship the run
  started in, even if the flag passed to an escort.
- **Only an owned flagship's win counts**, so a `Survivors - ` mission in a
  locked ship cannot skip the ladder.
- **An earned flagship is free.** The win is its price.
- **Why this order:** each step is a real gain in unescorted strength (see
  the scores below). The US and Soviet lines match at each step, and the
  two links across navies give a gun player a ship to win towards.

| Flagship | Missile defence | Air defence | Notes |
|---|---|---|---|
| Sverdlov | 2.5 | 3.5 | Guns only: below every missile group's threshold |
| Perry | 5.2 | 4.7 | One rail, 2 directors, 34 SM-1 |
| Kidd | 10.5 | 9.3 | 3 directors, 52 SM-1MR, 2 Phalanx, helicopters |
| Sovremenny | 8.5 | 9.5 | 48 SA-N-7, 8 SS-N-22, 130 mm guns |
| Long Beach | 10.5 | 9.3 | 120 SM-2ER to 90 nm, Tomahawks, moderate armour |
| Slava | 12.2 | 13.8 | 64 SA-N-6, 40 SA-N-4, 16 SS-N-12 |
| Iowa | 14.0 | 3.2 | No SAMs; heavy armour, 9 big guns, 48 missiles |
| Bunker Hill | 12.8 | 9.3 | Mk 41 VLS: 94 SM-2, 28 Tomahawks, 8 Harpoons |
| Kirov | 13.5 | 14.5 | 96 SA-N-6, 40 SA-N-4, 20 SS-N-19, 8 AK-630 |
| Kiev | 16.5 | 18.5 | SA-N-3 and SA-N-4, 8 SS-N-12, its own air wing |

The scores are `Defence.Score` and `Defence.AirScore` for the ship alone, in
its 1985 fit. The missile defence column predates the reweighting that made
guns and CIWS count for less: by the same parts, Sverdlov is now about 0.8,
Perry 4, Iowa 5, Sovremenny 7, and Kidd and Long Beach 8. The others are still
to re-measure.

### What a run starts with: bought once, at fixed prices

| Item | Price (XP) |
|---|---|
| Kidd, Sovremenny | 400 |
| Each starting ship: the flagship navy's tier 1 frigates and destroyers | 200 |
| Each starting flight: a fighter pair, a strike pair or the AWACS from the flagship's navy | 300 |

- **Everything bought sails, with every flagship.** Escorts are bought by
  slot: each navy fills a slot with its own counterpart, so a fleet is
  always one navy and nothing bought sits idle when the flagship changes
  sides. Leaving one ashore leaves both counterparts ashore.

  | US | Soviet |
  |---|---|
  | Gearing FRAM II | Petya II |
  | Knox | Krivak I |
  | Oliver Hazard Perry | Kashin |
  | Charles F. Adams | Kanin |
  | F-4J Phantom CAP pair | MiG-23MS CAP pair |
  | A-7E Corsair strike pair | Yak-38 strike pair |
  | E-2C Hawkeye AWACS | Tu-95RT Bear-D scout |
- **Starting ships** are written into the mission, beside the flagship.
- **Starting flights** spawn as the run starts, as chest flights do.
- **Both are fleet** from the first second, like anything from a chest.

### Permanent upgrades: ranks, with Vampire Survivors' pricing

| Upgrade | Ranks | Per rank | Base price (XP) |
|---|---|---|---|
| Rerolls | 3 | +1 reroll a run, on top of the 3 every run has | 150 |
| Banish | 3 | +1 banish a run: removes an upgrade from the cards for the rest of the run | 200 |
| Growth | 5 | +10% XP earned during a run | 300 |
| Luck | 5 | Rarer cards turn up more often (see Rarity) | 250 |
| Flag transfer | 2 | When the flagship sinks, an escort ship still afloat becomes the flagship and the run goes on, once a run per rank | 2,000 |

- **Pricing.** A rank costs its base price × the rank being bought. On top
  of that, it costs +10% of the base price for every permanent-upgrade rank
  already owned, whichever upgrade it was.
- **Refund.** It returns every XP spent on permanent upgrades and clears
  their ranks. Ships, starting escorts and wins are not refunded.

## Ensigns

After Soulstone Survivors' runes: a choice made before a run that changes how
the whole run plays. Each is a real naval ensign with one effect and no penalty.
A run flies any mix up to a budget of **20 points**, and each is unlocked once with
banked XP (400 to 1,200, first guesses; a full run banks about 2,000). Some replace
one thing with another, such as SAMs with CIWS: that is the effect, not a price.

Chosen on the Survivors screen: the Ensigns panel at the top of the right-hand
column opens a page of all twenty, which shows what each does without hovering.
Clicking one unlocks, hoists or strikes it. During a run they are small flags at
the end of the status panel; hovering one shows what it does.

| Ensign | Effect | Points |
| --- | --- | --- |
| White Ensign | Guns, missiles, torpedoes and mortars reach ×3 | 8 |
| Stars and Stripes | Guns +300% damage, firing half as fast (net ×2 damage per second) | 5 |
| Icelandic Coast Guard Ensign | Guns do up to ×3 damage to ships, falling evenly to ×1 at the gun's greatest range | 5 |
| Soviet Naval Ensign | All missiles ×2 damage | 7 |
| Indian Naval Ensign | Anti-ship salvos twice the size: the AI's usual one missile a launch becomes two | 5 |
| Dutch Tricolour | CIWS range ×100; guns aim 4× as well and fire and reload 2× as fast; SAMs and air-to-air missiles disabled | 8 |
| St Andrew's Flag | Mortars are EMPs: radars and fire control go dark, 2 nm and 60 s at rank 0, up to 6 nm and 3 min at rank 20; Sovremenny, Slava, Kirov and Kiev only | 5 |
| Italian Naval Ensign | Torpedoes ×3 speed, turn and range, ×1.5 damage; can target ships | 5 |
| Swedish Naval Ensign | Torpedoes wire-guided for half their range at full speed; can target ships | 3 |
| German Naval Ensign | Ships with tubes carry the EA-6B's ALQ-99 jammer, aimed by the player; torpedo cards become jammer cards | 6 |
| Australian White Ensign | Starting flights double, or a free fighter pair | 4 |
| Estonian Naval Ensign | Own aircraft RCS −95% | 3 |
| Argentine Ensign | Unguided bombs ×10 damage and 10× closer; strike aircraft carry only those, at RCS ×0.25; a free strike pair | 6 |
| Greek Ensign | First ship or flight comes as five, and is the only escort; chests then hold only level-ups | 7 |
| New Zealand White Ensign | Each ship or flight from a chest comes as two | 6 |
| Turkish Naval Ensign | The flagship ignores one missile, bomb or torpedo, and one more every 5 levels; shells do not spend a charge, and a near miss that would harm it does | 7 |
| Dannebrog | Each close call adds 10% to all damage for the rest of the run | 4 |
| Norwegian Ensign | Cards at least Rare | 10 |
| Irish Tricolour | A fourth card at every level-up, +3 rerolls, +3 banishes | 5 |
| Spanish Ensign | Four free cards at the start | 4 |

- These pairs cannot fly together, because one removes what the other acts on:
  German with Italian or Swedish, Greek with Australian, Argentine or New Zealand,
  Dutch with Australian.
- Under the Dutch ensign a launcher that also holds other rounds stays in action
  and only its SAMs are withheld, so a Perry keeps its Harpoons.
- Mortars come under the mod's fire control from the start under the St Andrew's
  flag, at their stock reach until range cards extend it.
- Turkish charges and the Dannebrog's close calls show as a count on the flag.
- Damage factors are applied where a warhead's damage is worked out, not written
  into the rounds, so card previews do not show them.
- The numbers above are first guesses and none has been played.
- Under the St Andrew's flag, mortar cards show the pulse each rank brings and
  drop the damage rank's text, as there is no warhead to improve. Range ranks
  work as usual.
- Under the Argentine ensign, chests offer A-6Es, Hornets and Su-24s on their
  own bomb loadouts and do not offer Tu-22M2s, which have none. Air wing refit
  ranks say that flights keep their bombs. A Kiev's deck aircraft are the player's
  to load, so they can still carry guided weapons.
- The Jammers line replaces Torpedoes under the German ensign, 20 ranks: reach
  30 to 135 nm, arc 30° to 270°, jam chance 0.45 to 1.35. Its abilities are High
  power (200 kW), Second look (a resisting missile is retried every 10 s), Hard
  kill (a jammed missile is destroyed), and Extreme range (reach ×4, 540 nm). IR
  seekers and missiles with no anti-jam value in their data cannot be jammed.

Crests are simplified to a plain shield. Flags with contested histories, such as
the Rising Sun and any Nazi-era German ensign, are left out on purpose; the German
ensign here is the modern Bundesmarine one.

## XP and levels

XP comes as a steady drip. Missile intercepts are the stream; ships and
aircraft pay a little more, but never a lump: a single kill is worth at most
two levels. A boss's real reward is its chest. The opening is boosted hard,
since early kills are few and small, and fewer still for a gun cruiser.

| Source | XP |
|---|---|
| Enemy missile shot down by our SAM or CIWS | 1 |
| Any kill | 1.6 × √(its worth), rounded: worth 2 → 2, 4 → 3, 15 → 6, 30 → 9, 60 → 12 |
| Life raft | Nothing |
| Early in the run | Everything ×3 at the start, falling evenly to ×1 at 7 minutes |
| Any one award | At most two levels' worth of the current level's XP |

The worth of each type is in [Progression.cs](src/SeapowerSurvivors/Progression.cs):
a Zhuk 2, an Osa 4, a Kashin 15, a Sovremenny 30, a Kirov or Kiev 60, a
Yak-38 3, a Tu-22M2 12.

- **Who gets credit.** An enemy missile counts only if one of our SAM blasts or
  CIWS mounts struck it.
- **The level curve** has no walls: **1.5 + 0.1 × level + 0.15 × level²** XP
  for the next level. Early levels are cheap, so each early kill is about a
  level; level 10 costs 18, level 20 64 and level 30 140. Walls make a long gap
  and then a rush, which is the lump in another form. A 20-minute run is meant
  to reach about level 25–30, about 40 cards with chests: enough to fill two or
  three lines rather than all of them, so picks matter to the end.
- **The card screen.** The game pauses and offers three cards per level, with
  banked levels shown one after another. You get 3 rerolls a run, and a skip.
  Shop ranks add rerolls and banishes. A reroll leans away from the cards it
  replaces: each is a third as likely as any other to come up again.
  Each card runs in the order a player asks: what it is and what it costs (rank
  and rarity, or the weapon slot it takes), any new ability, the fleet's own
  weapons now and after, then the next ability on the line. A stat with no real
  numbers to show keeps a plain sentence.

## Upgrades

All boosts are **direct and stacking**, and apply **across the whole fleet**.
A ship that joins later gets everything already taken. They are written into
each ship's own copies of its parameters, so the enemy's identical weapons
are never boosted.

### Rules

- **Small ranks, long paths.** Each line is a fixed path of small ranks. A
  weapon line is **four tiers of five (20 ranks)**, a fleet line three
  (15).
- **Every fifth rank is an ability**, and nothing else. Abilities arrive on
  a fixed beat, are always named on the card that grants them, and are
  never a side effect of a stat.
- **A little beyond realistic:** each line at its last rank is set against
  the best real successor to the 1985 weapon it boosts, then pushed
  somewhat past it (a Mk 75's 80 rounds a minute against the Mk 110 57 mm's
  220). That end state is spread evenly over the line's stat ranks: a
  factor f over k ranks is f^(1/k) a rank. This also keeps shell counts
  sane: a gun fires at most one salvo a frame, and a rate of fire past that
  only fills the air with shells the game must move.
- **Chances stack hyperbolically.** SAM hit chance, CIWS intercept, chaff
  spoof and anti-ship evasion approach a cap and never reach it: n ranks
  give cap × (1 − 1/(1 + rate × n)), so each rank adds a little less (SAM
  hit chance: +16, +10, +7, +5, +4, +3 points, 45 of a 70 cap). Factors
  multiply; targets at once add.
- **Weapon upgrades, one per weapon type.** One is offered only if the fleet
  carries that weapon.
- **Two fleet upgrades,** Reaction and Accuracy, which act on every weapon.
- **A cap of four weapon upgrades a run.** Once four are held, cards offer
  only those four and the fleet upgrades.
- **Cards show real numbers:** each card gives the effect, then before →
  after values for the fleet's own weapons, for example
  `Mk 45 5"/54: 3.0 s → 0.4 s between shots`, then the next ability:
  "Then Long reach in 2".
- **Nothing hidden.** If a rank changes something, its card says so.
- **No Refits** (Vampire Survivors' evolutions) **and no Doctrines** (its
  Arcanas), for now.

### Rings

The HUD shows a ring for each weapon slot under its status row, then one for
each fleet line taken: a notch per rank, clockwise from the top, and a dot
outside the ring at every ability. It is quiet on purpose.

- Taken notches are grey; those to come are faint. The one colour is the
  accent on the **next** notch.
- An earned ability's dot is solid; the next ability's is outlined; later
  ones are faint.
- Beside each ring: the line's name, its rank, and the ranks to its next
  ability ("12/20 · ability in 3").
- **Hover a ring** for its panel: what is in effect, the abilities earned,
  and the next four ranks.
- **On a card**, the ring shows what the card would add: taken notches step
  back and the ones it fills turn white.

The rings and their icons are textures drawn in code
([Rings.cs](src/SeapowerSurvivors/Rings.cs)), so the mod ships no image files.

### Rarity

Every upgrade that could be offered is equally likely to be, but for a reroll's
lean away from the cards it replaces. Each card then
rolls a rarity, which is **how many ranks** of its line it gives:

| Rarity | Chance with no Luck | Ranks |
|---|---|---|
| Common | 70% | 1 |
| Rare | 22% | 2 |
| Epic | 7% | 3 |
| Legendary | 1% | 4 |

- **Rarity never makes a step bigger.** It moves you further along the same
  path, so the ring shows exactly what the card adds, and a rarer card that
  reaches an ability names it.
- Near the end of a line a card gives only the ranks that are left.
- **Luck** shifts the odds towards the rarer cards: each rank raises Rare's
  weight by 25%, Epic's by 50% and Legendary's by 100%.

### Weapon upgrades

Ability ranks in bold; the path repeats its stats within each tier.

| Upgrade | Ranks | Stat ranks | Abilities (ranks 5, 10, 15, 20) | At the last rank | Set against |
|---|---|---|---|---|---|
| Guns | 15 | Damage ×1.265 (6), rate of fire ×1.183 (6) | **Anti-air fire**, **Wide bursts** (flak burst radius ×2), **Heavy shells** (damage ×2 again) | Damage ×4.1 then ×2, rate of fire ×2.75, shells ×1.4 as fast; flak bursts within 30 m on a 100 mm | Mk 75 80 rpm → Mk 110 57 mm 220 rpm; 76 mm → 127 mm HE |
| Surface-to-air missiles | 20 | Reload ×0.71 (6), hit chance (6), +1 target at once (4) | **Sea-skimmer killers**, **Hypersonic**, **Long reach**, **Huge blasts** | Reload ÷8, hit chance +45 points, +4 targets | Mk 13 arm (~8 s a round) → Mk 41 VLS (~1 s); Aegis illuminator time-sharing |
| Anti-ship missiles | 20 | Speed ×1.32 (4), evasion (6), damage ×1.32 (4), range ×1.32 and minimum range ÷1.32 (2) | **Smart seekers**, **Wave-top**, **Armoured**, **Ghost** | Damage ×3, speed ×3, range ×1.75, enemy SAMs hit 45% less often | Harpoon Mach 0.85 → BrahMos 2.8; 221 kg → P-700 750 kg |
| Torpedoes | 20 | Front-loaded: speed ×2.5 then ×1.6, range ×3 then ×2 (ranks 1–4); then damage ×1.41 (4), sonar seekers ×1.26 (4), range ×1.1 (4) | **Hunter**, **Sure shot**, **Keel breaker**, **Supercavitating** (speed ×2) | Speed ×4 (×8 supercavitating), range ×8.8, damage ×4, seekers ×2.5 (×5 with Hunter) | Mk 46 → Shkval-fast in three ranks |
| Rocket mortars | 20 | Reach +10 nm (6), speed ×1.68 (4), reload ×0.5 (3), damage ×1.26 (3) | **Hunting rounds**, **Spotters**, **Deep magazines**, **Bombardment** (reach ×2) | Reach +60 nm (about 125 nm with Bombardment), speed ×8, reload ÷8 (an RBU-6000's 127 s to 16 s), damage ×2, firing at ships on their own | Nothing real: RBU-6000 3 nm, out-ranging a Harpoon on purpose. Soviet hulls only: no US ship in the roster carries one |
| Point defence (CIWS) | 20 | Intercept chance (8), rate of fire ×1.10 (8) | **Heavy rounds**, **Flak rounds**, **Overdrive**, **Flak storm** | Intercept +45 points, rate of fire ×2.2 | Phalanx 3,000 → 4,500 rpm; AK-630 → Duet 10,000 rpm |
| Countermeasures (chaff) | 20 | Spoof chance (8), reload ×0.82 (7), **decoys can succeed every time** (1, rank 14) | **Quick bloom**, **Burn-out**, **Turncoat**, **Overcharge** | Spoof +40 points, reload ÷4, the game's "three spoofs then a forced failure" rule lifted | Chaff → Nulka active decoy |
| Air wing | 20 | Bombs and rockets ×1.19 (8, tiers 1–2), resupply ×1.19 as often (8, tiers 3–4) | **Guided weapons refit**, **Air resupply** every 4 min, **New aircraft refit**, **Heavy loadout refit** | Strike flights three rungs up their ladders, pylons rehung every minute, bombs and rockets ×4. Offered once a flight with stores has joined | Mk 82 227 kg → Mk 84 907 kg |

| Chance | Cap | Rate | Ranks | At the last rank |
|---|---|---|---|---|
| SAM hit chance | +70 points | 0.3 | 6 | +45 |
| Anti-ship evasion | 70% | 0.3 | 6 | 45% |
| CIWS intercept | +70 points | 0.225 | 8 | +45 |
| Chaff spoof | +60 points | 0.25 | 8 | +40 |

**Refits trade strike flights up.** Each Refit rank raises the air wing's
standard by one, and every strike aircraft below it moves to the best rung
of its ladder the standard allows, including flights that join later. A
loadout is built into an aircraft when it is made, so a refit replaces the
aircraft where it flies (without scoring) and waits only while a weapon it
guides is flying (a Walleye or Sparrow); a Harpoon flies on without it.

| Standard | US | US (Hornets) | Soviet | Soviet (Backfires) |
|---|---|---|---|---|
| 0 | A-7E, Mk 82 bombs | | Yak-38, FAB-500 bombs | |
| 1 | A-7E, Walleye TV-guided bombs | | Su-24A, AS-14 TV-guided missiles | |
| 2 | A-6E, two Harpoons | F/A-18A, two Harpoons | Su-24M, AS-14 and KAB-500L laser-guided bombs | Tu-22M2, two AS-4s |
| 3 | A-6E, four Harpoons | F/A-18A, four Harpoons | Tu-22M2, three AS-4s | Tu-22M2, three AS-4s |

### Abilities

| Line | Rank | Ability |
|---|---|---|
| Guns | 5 | **Anti-air fire:** every gun engages aircraft (below) |
| | 10 | **Wide bursts:** flak bursts reach 2× as far |
| | 15 | **Heavy shells:** shells hit 2× as hard, on top of the path |
| SAMs | 5 | **Sea-skimmer killers:** no low-altitude penalty, and they engage missiles below the SAM's usual floor |
| | 10 | **Hypersonic:** SAMs fly 2.5× as fast |
| | 15 | **Long reach:** SAM range ×2.5 (the director must still see the target) |
| | 20 | **Huge blasts:** every intercept destroys all enemy missiles and aircraft within 150 m |
| Anti-ship missiles | 5 | **Smart seekers:** they ignore chaff and jamming |
| | 10 | **Wave-top:** they skim and dive in at 10 ft, under almost every enemy SAM's floor (Soviet naval SAMs: 30–80 ft; a few late variants 15 ft) |
| | 15 | **Armoured:** 4× the CIWS hits to bring one down |
| | 20 | **Ghost:** radar signature ×0.1 |
| Torpedoes | 5 | **Hunter:** sonar range and arc ×2, +20 dB seeker gain, ignores decoys, circles back to reattack (below) |
| | 10 | **Sure shot:** seekers and warheads never fail |
| | 15 | **Keel breaker:** damage ×3, on top of the path's ranks |
| | 20 | **Supercavitating:** they run 3× as fast again |
| Mortars | 5 | **Hunting rounds:** rounds steer onto their target in flight, and if it sinks, onto the nearest enemy ship within 15 nm |
| | 10 | **Spotters:** enemy ships within mortar reach go on the plot, seen or not, refreshed every 10 s |
| | 15 | **Deep magazines:** magazines are topped up before they run dry |
| | 20 | **Bombardment:** reach ×2 |
| CIWS | 5 | **Heavy rounds:** damage ×3, so armoured missiles like the P-500 go down in far fewer hits |
| | 10 | **Flak rounds:** each kill destroys other enemy missiles within 25 m |
| | 15 | **Overdrive:** rate of fire ×2, on top of the path's ranks |
| | 20 | **Flak storm:** bursts reach 75 m and destroy aircraft too |
| Chaff | 5 | **Quick bloom:** missiles test against chaff every 5 s instead of every 35 s |
| | 10 | **Burn-out:** a spoofed missile is destroyed, and pays intercept XP |
| | 15 | **Turncoat** (replaces burn-out): a spoofed missile turns round and homes on the nearest enemy ship. Only a missile with its own radar seeker can be turned; one steered by its launcher's radar burns out instead |
| | 20 | **Overcharge:** reload ×4 again, and every launch fires two clouds |
| Air wing | 5, 10, 15, 20 | **Guided weapons refit**, **Air resupply**, **New aircraft refit**, **Heavy loadout refit** |
| Reaction | 5 | **Snap fire:** incoming missiles are engaged at once, not ignored for their first 5 s |
| | 10 | **Full plot:** no limit on new contacts processed at once |
| | 15 | **Ripple fire:** launchers skip the random engage delay and the gap between launches |
| Accuracy | 5 | **Long shots:** gun range ×1.5 |
| | 10 | **Critical hits:** 1 shell hit in 5 deals ×5 damage |
| | 15 | **Smart shells:** shells steer gently onto their target in the last seconds of flight, ships and aircraft alike |

- **Points past 100% still count.** The game never treats a SAM's hit
  chance as the final odds. It multiplies it down for a fast, small, low or
  SAM-resistant target (`Utils.GetInterceptionChance`), and never caps the
  result, so a high bonus against a hard sea-skimmer lands about where a
  lower one lands against an aircraft. Chaff subtracts the missile's
  anti-chaff bonus in the same way. CIWS is the exception: its hit model
  needs the value it is given below 100.
- **Blasts are punchy, not nuclear.** A real SAM warhead kills within about
  10–30 m and a Phalanx round does not explode; missiles in a salvo fly
  100–300 m apart. 150 m sometimes catches a missile's neighbour.
- **No floor on leakers.** At high ranks almost no missile gets past all
  three defence layers. The enemy answers with more missiles.

**Anti-air fire.** This is one capability, written as one rank, which
gives every gun:
- proximity fuzes, with a burst radius 3× the shell's own, and at least
  15 m;
- anti-air fire-control tables, so guns without them can aim upwards, out
  to 60% of their range;
- aircraft as a target, even for guns built only for surface targets.

**Why the burst radius grows, not just the aim.** Traced in game: with
fire-control error removed, shells still pass 100–200 m from a Yak-38,
because it moves while they fly and the game's lead assumes it flies
straight. A bigger burst radius covers that.

**Anti-ship missiles are about getting through.** Damage was never the
problem; a Harpoon already sinks an Osa in one hit. Evasion is the
missile's `MissileDefenceBonus`, which the game multiplies into every
SAM roll against it as (1 − bonus), down to the 5% floor, so +45 leaves
an enemy SAM 55% of its chance.

**Torpedoes are about reaching anything.** They start so far behind
missiles that the line is front-loaded: four ranks take a Mk 46 from 45 kt
and 5 nm to 180 kt and 30 nm, parity rather than an edge, and the rest of
the path refines. The fleet's torpedo is the
Mk 46 Mod 5 from Mk 32 tubes (Kidd, Perry, Long Beach): an anti-submarine
torpedo with ships as its second target type, 45 kt and 5 nm. Ship tubes
auto-engage surface ships (only air-dropped torpedoes are held back), so
speed and range decide whether it ever fires. The Mk 44 (Gearing), the
early Mk 46 (Adams) and every Soviet ship torpedo (SET-40, SET-65) are
anti-submarine only, and the enemy has no submarines, so the line's first
rank gives every ship torpedo of ours ships as its second target, as the
Mk 46 Mod 5 has; otherwise those tubes never fire and the line passes them
by. Before that rank they stay as the game made them. A torpedo that can take
both is locked to submarines when launched with no target (an SS-N-14 or
ASROC whose target died in flight drops it blind), so ours are set to hunt
ships instead. The rockets and missiles that carry a torpedo take the line's
speed and range as well, or they would launch at stock range however far their
torpedo runs; the SS-N-14B stays on the Anti-ship line, as it is an anti-ship
missile first. Bunker Hill and Sovremenny start on their Late fits, whichever way
they join the fleet, as those are the fits with VLA and SS-N-16; enemy
Sovremennys keep the default fit. An SS-N-16 keeps its stock boost, as a shorter
one could end before it turns for the surface from its underwater run. Launches
reset the seeker arc from the ammunition's narrow and
wide arcs, so the sonar ranks boost those too. Hunter makes the sonar
excellent rather than different: wake homing was the milestone once, but the
game's wake seeker sees only 0.25 nm, chases from astern and ignores every
sonar rank before it. An echo must beat the sea's noise as well as be in
range, so Hunter adds seeker gain; and a torpedo that loses its target
circles back instead of running on until its fuel is gone.

**Mortars reload at their animation's pace.** The rounds are spawned by the
load animation, so the reload ranks speed that up along with the timer.

**Mortars are the long gun nobody expected.** Reach adds rather than
multiplies, so a half-mile RBU-1000 and a 3 nm RBU-6000 both get there, and
the RBU-1000 keeps its warhead of twice the power. The game's AI cannot use a
mortar at range: it holds fire past 10 nm, it only fires one at a ship when
every missile launcher is busy, and a mortar in flight or tasked makes a ship
count as dealt with for the whole taskforce. So from the line's first rank
ours have their own fire control: each idle launcher takes the nearest enemy
ship in the AI's own target list that is in reach and arc, preferring one no
salvo is falling on, through the game's engage tasks. That list is what makes
weapons free, tight and hold apply to mortars as to every other weapon. They no longer hold back other weapons.
Against torpedoes they stay with the AI, at its 1 nm. Rounds climb at 35°, over
any escorts, and fly as shells do: aimed once, then unguided, so a ship that
turns in flight is missed unless the rounds are Hunting rounds. The lead is
corrected for the climb, which the game's is not. Each round takes the game's
surface gunnery error, for a gun that has found the range: 1.5 mils across and
0.45% of the range along, for about 170 m and 500 m at 60 nm, against a 120 m
blast. The Accuracy line divides that by up to 5.4, so a salvo at that range
goes from a few hits to nearly all of them. Flight is one to two minutes at
any rank.
A radar sees ships at about 20 nm, so reach past that is wasted without
another ship's contact, until Spotters at rank 10.

**Targets at once are the SAM's rate of fire.** A director with one
fire-control channel engages one target however fast its launcher reloads,
so the SAM upgrade adds channels. They go to the directors that guide a SAM
launcher, the ones its launcher names, and to no other: a Kirov's AK-630
directors and a Kiev's SS-N-12 guidance radar are not SAM directors.

### Fleet upgrades

| Upgrade | Ranks | Per stat rank |
|---|---|---|
| Reaction | 15 | Crews react 1.15× as fast (×5.4 over the 12 stat ranks, as automated fire control cut reaction from tens of seconds to a few): contacts processed, weapons aligned, engagements checked, targets acquired and fire-control solutions found. Three abilities |
| Accuracy | 15 | Gun and mortar aiming error ÷1.15 (÷5.4 over the path): for guns against ships and against aircraft (the lead error on a moving target as well as fire-control error), and for mortars once the Rocket mortars line is taken. Three abilities |

**Damage control was dropped.** These ships die to two or three hits, so
repair rarely gets the chance to matter. Countermeasures took its place:
a decoyed missile is a stopped one. Its ranks also count towards the
fleet's anti-missile score.

**The defence scores and the cap.** Missile and air waves wait for the
fleet's defence scores, which lean on SAM, Point defence and
Countermeasures ranks. Ranks are half-size steps, so each counts half what
an old rank did. With only four weapon upgrades a run, a missile ship that
spends them elsewhere may meet missile waves late. Watch for this in play.

### Reinforcements: ships and aircraft (built)

- **Where they come from.** Each mini-boss sunk opens a **chest**: a pick of
  3 from your own navy, then the chest's level-ups. Ships and flights share
  one pool, so a chest can be all ships, all aircraft, or a mix. Chests at
  phases 3, 6, 9 and 12 offer tiers 1, 2, 2 and 3.

  | Tier | Ships | Flights |
  |---|---|---|
  | 1 | Frigates and destroyers | A fighter pair (F-4J / MiG-23MS), a strike pair (A-7E / Yak-38) |
  | 2 | Destroyers and cruisers | AWACS (E-2C / Tu-95RT scout), a fighter pair (F-14A / MiG-23MLD), a strike pair (A-6E Harpoon / Su-24A) |
  | 3 | The big cruisers, Iowa, Kirov | Four long-range fighters (F-14A Phoenix / MiG-25PD), four strike aircraft (A-6E, F/A-18A / Su-24M), Tu-22M2 pair |

- **Gap-filling.** At least one offer brings something the fleet lacks (guns,
  SAMs, anti-ship missiles, CIWS, air cover, AWACS or air strike) when the
  tier has one, and the card says what it adds. A fleet with no aircraft is
  therefore usually offered at least one flight.
- **Ships** spawn 1.5 nm abeam of the flagship, at its speed and weapons
  status, as ordinary ships in your task force under your command.
- **Flights** spawn airborne abeam of the flagship, in your task force. There
  is no carrier to land on, so they fly on unlimited fuel and never head home.
  The mod gives them no orders; they go where you send them.
- **What flights change.** A fighter adds 4 to the fleet's anti-air score
  and unlocks stand-off raids (air cover). Aircraft are left out of the
  anti-missile score and of the fleet's reach for spawning ships.
- **Upgrades and rearming.** Everything that joins takes every rank the run
  has, and every level-up rearms it with the rest of the fleet. Aircraft
  weapons go through the same lines: air-to-air missiles on the pylons
  through the SAM line, anti-ship missiles through the anti-ship line,
  cannon through the gun line. The Air wing line covers the rest: air
  resupply between level-ups, and bombs and rockets. Kills by anything in
  the fleet earn XP.
- **Losing.** Only the flagship's loss ends the run; reinforcements are
  firepower, not extra lives. The mod ends the mission itself, because the
  mission's own trigger waits for every player ship.

---

## Enemies

**Enemies never get individually stronger. They get more dangerous as the mix
shifts**, with the clock, while what may carry missiles is scaled to the fleet,
so a Sverdlov or an Iowa is not swamped by salvos it cannot stop.

Who each starting ship faces, and when, is set by hand in
[docs/threat-plan.md](docs/threat-plan.md) and
[Threats.cs](src/SeapowerSurvivors/Threats.cs):

- **The opening.** One tiny boat. Each next step comes when the last is sunk, or
  after a minute.
- **Pulses.** Every 45 seconds, a clock-driven budget buys groups from the ship's
  menu for this stage of the run, each on its own bearing. Dearer groups are picked
  more often, and one it cannot yet afford is saved up for. Danger is per ship: the
  same enemy costs different amounts against different flagships.
- **Kills and losses move the curve** within ×0.6–1.4. Killing three-quarters of a
  pulse's budget by the next pulse holds it steady, and more raises it. A close call
  stops it rising, and a breather lowers it.
- **Peaks and relaxes.** Four pulses build to a peak. The fight then gets up to 45 s
  to thin out, and a relax of 30–45 s follows, after Left 4 Dead's director.
- **Gates.** A gated enemy never comes until the fleet has what that ship's table
  asks for. A vanilla Sverdlov meets no aircraft and no missile ships until an escort
  or Guns 10 and Accuracy 10 open them. An open gate also sets a floor under the salvo
  limit, which pressure can halve.
- **Events** land at the top of a row: swarms, and **rings**, a wall of eight boats
  closing at once, if the plan has room for them.
- **Bosses and set pieces stay on the clock,** in a form the ship can face.
- **Caps.** Each ship has a limit on enemy units out, rising through the run,
  because numbers are what overwhelm a ship. Set pieces have 12 more places of their
  own, to hold 30 fps: below that the game itself runs slow.

### Pressure: scaling missiles to the fleet (built, awaiting play-test)

The threat must be there all run without guaranteeing a loss. Ships are fragile:
most hits kill, so damage comes too late to steer by, and the warning is a close
call instead ([Pressure.cs](src/SeapowerSurvivors/Pressure.cs)).

- **Salvo limit.** `Defence.Score` is roughly the missiles the fleet can stop in
  one salvo. Enemy anti-ship missiles in the air and on the rails are held to a
  share of it: 35–55% at the start, 45–70% at the end, by pressure, and never
  more than 24. Stronger fleets meet larger salvos but keep a margin for leaks.
  A missile group is sent only if it fits; otherwise gun ships are sent instead.
- **Pressure** starts at 0.3 and climbs to 1 in two minutes while nothing comes
  near. An enemy missile within 3 nm of a fleet ship takes off 0.15, and a hit
  a ship survives, or a ship lost, takes off 0.4. Either stops the climb for 30 s.
- **Breather.** The flagship hit or an escort lost means 45 s with no new arrivals
  or missile boats, and pressure at zero, with at least 90 s
  between breathers so it cannot be used to sit out the run.
- **Bosses and set pieces count.** They always come, but armed only if the limit
  has room; otherwise their anti-ship launchers stay out of action and they
  count for nothing. Each ship counts for at most its usual salvo.
- **Pending dangers don't count yet.** A ship counts only within 1.5 nm of its
  firing range, and not at all if it can never fire. Tarantuls, Hauks and Snøggs
  fire only from their seeker's 10 nm, so against a long reach they can be
  ignored for a long time, until they can't. Their missiles are still real when
  they get there. At most a third of the cap may be waiting like this, and they
  do not hold back the rest of the ramp. Aircraft
  carrying bombs count as one missile each.
- **A wounded flagship holds pressure down:** pressure is never above its health
  out of 100.
- **The score falls with the fleet.** Ships lost and SAM launchers run dry stop
  counting, so the limit falls too.
- **Kills and XP do not feed it,** so a strong build still gets its rewards.

**Mini-boss chests.** Each mini-boss sunk pays a chest: a choice of ships or
aircraft, then one level-up (three at a 20% chance).

### Set pieces: convoys and SAGs (built, awaiting play-test)

Targets that do not come to the fleet, far beyond its reach, so going for
them is a choice: by steaming out, by sending aircraft, or by building for
long-range anti-ship missiles. They come at rows 1, 4, 7, 10 and 13,
between the mini-bosses ([SetPieces.cs](src/SeapowerSurvivors/SetPieces.cs)).

| | Convoy | Surface action group |
|---|---|---|
| Where | Closest pass 3–5 nm beyond reach, on a lane | 4–6 nm beyond reach, from the enemy's side |
| Moves | Crosses at 12 kn, gone after 8 min | Comes in at 12 kn, withdraws after 9 min |
| Fights | Escorts fire only once they see the fleet | Shadows the fleet and fires from beyond the horizon |
| Pays | Kill XP per hull; all merchants sunk: a level-up | The capital ship sunk: a chest, as a mini-boss |

- **Lanes.** In an area, a convoy's lane runs across the enemy's side of the
  fleet, between it and the enemy's waters, as coastal shipping does. In open
  ocean it lies anywhere. Only an all-water lane is used.
- **Contact reports.** On sighting, and every 2 minutes after, a report
  puts the set piece on the fleet's plot for 1 minute. Missiles that
  find the target themselves (Harpoon, Granit, Basalt, Exocet) fire on it
  then at their full range: the game caps only semi-active and
  command-guided rounds at the horizon. The fleet's own aircraft hold it for
  as long as they see it.
- **Escorts** left afloat when their charge is sunk turn on the fleet.
- Missile-armed set pieces come with their anti-ship launchers out of action
  until the fleet's defence reaches theirs, as surface bosses do.

### Spawning

- **Surface groups spawn by their own weapons,** not by the fleet's reach:
  outrunning the fleet's first shot was what made long-range ships so safe.
  - **Missile ships** start 30 seconds' steaming outside their firing range, at
    most 35 nm out, so they fire almost at once.
  - **Gun ships** start 3 minutes' steaming outside their longest gun or
    ship-killing torpedo, at least 2 nm out, so they come in under fire.
  - **Boats that wait** (Tarantul, Hauk, Snøgg: see Pressure) still start at the
    edge of the fleet's reach.
  - Without missiles, as a disarmed boss, a ship comes in as a gun ship.
  - Firing range is what the AI really fires at: a heat-seeker no further than
    its seeker, and a director-guided missile no further than its director sees.
- **Reach still matters** for boats that wait, and for set pieces, placed beyond
  it. It means the longest anti-surface weapon the fleet still has rounds for,
  at the range its launcher would actually fire at that target: the missile's
  kinematic range, capped at the horizon of the director that guides it or of
  the search radars.
- **Waves come together.** A wave's groups spawn at once, each on a bearing at
  least 60° from the others where the water allows.
- **Missile ships hold** on a fixed station at 90% of their range, at most 35 nm,
  firing for 3 minutes, then close. The fleet is re-revealed to them every 4
  seconds while they hold, since a revealed track lasts only about ten. A fleet
  that steams at them can close, as the station does not move.
- **Ships come by water, from the enemy's side.** A ship spawns only on a
  bearing that is open water all the way to the fleet, since it is sent
  straight at it ([Sea.cs](src/SeapowerSurvivors/Sea.cs)). In a launched
  area, ships lean towards the enemy's waters. Aircraft come mostly (70%)
  from the enemy's airfield, and the rest come from anywhere. A ring of ships
  spreads over the open bearings, so in a strait it closes from both ends.
  When every bearing is blocked by land short of the spawn distance, the
  longest open run is used and the distance shrinks to fit.
- **Faster kills, not hit XP.** The opening minutes are fragile boats in threes
  (Zhuks, PGMs) at a higher quota, so kills come every 20–30 seconds.
- **Reach means what the fleet will really fire.** A missile whose minimum range
  lies beyond what it can reach there does not count, nor does a SAM's surface
  mode. A Slava's SS-N-12 starts at 30 nm, so its reach is its guns until
  anti-ship range ranks shrink that minimum, and then enemies spawn further out. The anti-ship range ranks
  bring that minimum down to 17 nm, and reach follows it once it is inside the
  horizon.
- **Missile groups** wait for the fleet's anti-missile score (see
  [Defence.cs](src/SeapowerSurvivors/Defence.cs)) and for room in the salvo
  limit. A surface boss always comes, but with its anti-ship launchers out of
  action until the score covers it.
- **Air groups are gated by hand:** see [docs/threat-plan.md](docs/threat-plan.md).
  An air boss the fleet cannot face is replaced by a surface one. Stand-off raids
  (Tu-16K, Tu-22M2, B-52G) also need a fighter flight.

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
   yours? For now they do: every enemy sunk pays XP, whoever sank it.
2. **Final boss:** what is its composition for each side?
