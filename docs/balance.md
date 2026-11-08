# Weapon balance

A way to compare the weapon cards by numbers. There are
two halves: a calculator that predicts each card's value, and a damage log that
measures what happens in game. **The model has not yet been checked against a log.**

## The number

```
effective DPS = drain per hit × shots a second × hit chance
```

Drain is what a hit takes from the hidden integrity pool (see
[combat-model.md](combat-model.md#the-hidden-integrity-pool-is-the-real-hp-bar)),
the one HP bar every weapon shares. Reach is reported beside it, because a weapon
that kills before the others are in range never shows up in a DPS comparison.

Missiles can be shot down, so their number comes from a simulation instead: a
second-by-second attack on the target's SAMs, CIWS and chaff, repeated many times.
It reports drain over the whole attack, from first launch until the ship sinks or
the magazine runs out, flight time included. SAMs are also scored by the share of a
missile raid they stop, which is what they are really for.

## Calculator

Needs Python 3 and the game installed; set `SEAPOWER_DIR` if it isn't in the
csproj's default location. It reads `Upgrades.cs` and the game's INI files, so
changing a rank's value there changes its output.

```
python tools/balance.py                                every flagship with full lines
python tools/balance.py cards wp_rkr_slava usn_cg_belknap               what each rank adds
python tools/balance.py cards wp_rkr_slava usn_cg_belknap --accuracy 15 the same with full Accuracy
python tools/balance.py air                            SAMs against missile raids and aircraft
python tools/balance.py compare                        newest damage log against the model
```

Global options go before the mode, such as `--bonus 0` for neutral difficulty or
`--gun-nm 5` for the range the DPS columns use.

**Ported from the game:** the warhead damage formula, the shell drain table, pool
size, gun and salvo timing with the ready rack's reload, the aiming-error formula,
and the upgrade factors.

**Left out, so the numbers are not the whole picture:**

- Structure, fire and flooding. Guns do much of their damage this way, so **guns
  are understated**. The log's `route` column shows how often that sinks a ship.
- Moving targets. Hit chance assumes a stationary ship, averaged between side-on
  and bow-on, with aiming error and shell spread. No card narrows the spread along
  the line of fire.
- Time spent held by spotting fire. From Guns rank 1 fleet guns have no hold; at rank 0
  the model overstates their rate of fire at ships.
- Splash on nearby ships, so **mortars are understated** against groups.
- Smart shells and Hunting rounds, whose steering has no simple formula.
- The spotted-target damage multiplier (×2), and whether it applies is unconfirmed.
- Ensigns, shop upgrades, escorts and crew skill other than Trained.
- In the missile sim, assumptions rather than game rules: how far a target's radar
  holds a sea-skimmer (15 nm for a Harpoon's echo, or the horizon), half of a ship's
  CIWS bearing, one chaff roll a missile, Armoured as four times the CIWS survival,
  and every SAM fired at a ship hitting unless shot down. Missiles fly straight in
  at full speed. Hit % for missiles is not compared with logs yet.
- Guns and CIWS as anti-air. The air report covers SAMs alone.

## Damage log

`DamageLog.cs` runs in every run and test run, and only observes. Each run writes
to `SeapowerSurvivors/damage/` in the game's persistent data folder, beside
`profile.txt`:

| File | Contents |
|---|---|
| `<time> <flagship> hits.csv` | Shots, hits, pool drain and structure damage, split by round, target, line rank, Accuracy rank and range band |
| `<time> <flagship> kills.csv` | One row per enemy ship sunk: pool and structure left, the route that sank it, time from first hit, last weapon to hit it, each line's share |
| `<time> <flagship> timeline.csv` | Running totals per line each game minute, for each line's share of a real run |

Test-panel runs have ` test` in the name. `hits.csv` is rewritten once a minute and
at the end of a run. When a run ends, a per-round summary also goes to
`BepInEx/LogOutput.log`; **Log damage** on the test panel's Weapons tab writes one
at any time.

A hit is a blast that reached the ship. One mortar round can hit several ships, so
hit % can pass 100 in a group. A shell or round still in flight when ranks change is
counted against the new ranks.

## Checking the model

On a Test Range mission (Ctrl+F6 for the panel, then **Start run**):

1. Take the ranks to test with **+1 rank** on the Upgrades tab.
2. Spawn one target at a set distance, using the targets the calculator lists. The
   panel has the Belknap and Iowa for Soviet flagships, Sverdlov for US ones.
3. Let it sink, then repeat a few times. Rows split by rank and range on their own.
4. Run `python tools/balance.py compare`.

A useful first set is Slava and Sovremenny at Guns 0 and 15, Mortars 0, 10 and 20,
and Accuracy 0 and 15. Where measured and modelled drain per hit disagree, the
damage rules are wrong; where hit % disagrees, the aiming model is.

## Tuning

The Guns line is set so rank 2 is about as lethal as a stock Harpoon and SM-1 ship: a Perry or Kidd adds up to about 8–16 drain a second against a Kresta or Sverdlov. Compare `guns` at rank 2 with the Anti-ship and SAM rows at rank 0.

Change values in `Upgrades.cs`, rerun the calculator, and compare against targets
chosen up front. Measure real runs through `timeline.csv` as well, since only those
show how the fight's range and target mix affect each line.
