# Sea Power Survivors

A survivors-like mode for Sea Power. You sail a flagship and its escorts through
a 20-minute run against waves of enemy ships and aircraft, earning XP for kills
and choosing upgrades on every level-up. Upgrades apply live, mid-battle, and
what you earn between runs buys new ships and permanent bonuses.

It is a C# code mod loaded by [Anchor Chain](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html)
under BepInEx. Numbers throughout are still being tuned.

## Build and install

Subscribe to [Survivors on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3836191830).
You also need Anchor Chain, including its preloader: follow its
[install guide](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).

To build from source instead, with Sea Power and Anchor Chain installed:

```
dotnet build src/SeapowerSurvivors -c Release
```

This builds against the installed game (last built against 0.8.7 Build 261021)
and copies the DLL and everything in [mod/](mod/) into
`Sea Power_Data/StreamingAssets/SeapowerSurvivors/`.

- If the game is not in `D:\SteamLibrary\steamapps\common\Sea Power`, pass
  `-p:SeaPowerDir=...`.
- To build without copying into the game, pass `-p:Deploy=false`.

Code is loaded once per launch, so restart the game after every build. The mod
logs to `<install>/BepInEx/LogOutput.log` under the source `Survivors`.

## Starting a run

The main menu has a **SURVIVORS** button on the right-hand edge, which opens the
Survivors screen:

- **Location.** Ten sea areas, each with its best score so far. The areas are
  the same as Quick Start's, with thanks to its author.
- **Fleet.** Choose a flagship and which bought escorts and flights sail with it.
  **Formation** opens a drag-to-place editor for their stations.
- **Starting bonuses.** Permanent upgrades, bought rank by rank and refundable:
  Rerolls, Banish, Growth, Luck and Flag transfer.
- **Ensigns.** Twenty flags, each with one effect and a points cost. A run can
  fly any mix up to 20 points.

**Launch** writes the mission to
`<persistent data>/SeapowerSurvivors/Survivors run.ini` and loads it. The profile
is kept beside it, as `profile.txt`.

The location cards show charts drawn from the game's terrain by
[tools/render_charts.py](tools/render_charts.py), kept in [mod/art/](mod/art/).
With Quick Start enabled, they show its paintings of each area instead.

### Flagships

| Flagship | How to get it |
|---|---|
| Sverdlov, Oliver Hazard Perry | Free |
| Kidd, Sovremenny | 400 XP each |
| Long Beach | Win in Perry or Kidd |
| Slava | Win in Sverdlov or Sovremenny |
| Iowa | Win in Sverdlov |
| Bunker Hill | Win in Long Beach |
| Kirov | Win in Slava or Iowa |
| Kiev | Win in both Bunker Hill and Kirov |

Soviet flagships fight NATO, and US flagships fight the Soviets. Starting ships
cost 200 XP and flights 300. Each one bought sails as its counterpart in the
flagship's own navy.

## The run

A run lasts 20:00 of mission time and is meant to be played at x1. Surviving it
wins. Losing the flagship ends the run, unless Flag transfer passes the flag to
an escort.

- **XP.** Kills and enemy missiles shot down pay XP, with a boost that fades from
  x3 to x1 over the first 7 minutes. XP is banked at the end of a run.
- **Level-ups.** Each level pauses the game and offers three cards, then refills
  every ship's magazines. Banked levels are shown one after another.
- **Upgrade lines.** There are nine weapon lines of 20 ranks (Guns, Jammers,
  SAMs, Anti-ship missiles, Torpedoes, Rocket mortars, Point defence,
  Countermeasures and Air wing) and two fleet lines of 15 (Reaction and
  Accuracy). A fleet can hold four weapon lines. Every fifth rank is a new
  ability.
- **Rarity.** A Common card gives one rank, Rare two, Epic three and Legendary
  four.
- **HUD.** A status line shows time, level and XP, hull, kills, intercepts,
  enemies and missiles in flight. Rings beneath it show each upgrade's ranks, and
  hovering one shows what is in effect and what comes next.

### What you face

- **Waves.** Each flagship has its own hand-set table of enemies, costs and
  gates, in [Threats.cs](src/SeapowerSurvivors/Threats.cs). Missile ships and
  aircraft wait until the fleet can answer them. Waves arrive together on spread
  bearings, and ships spawn according to their own weapons' range.
- **Pressure.** [Pressure.cs](src/SeapowerSurvivors/Pressure.cs) scales missile
  salvos to the fleet's defences. Enemy missiles getting within 3 nm ease it
  back, and a hit brings a breather.
- **Mini-bosses.** They come at 4, 8, 12 and 16 minutes, and each drops a chest
  with level-ups and sometimes a new ship or flight.
- **Convoys and surface action groups.** These stay far beyond reach. Go after
  them, send aircraft, or fire long-range missiles in the windows contact reports
  open.
- **Final assault.** Stand-off raids launch at 14:00 and 15:30. From 17:00, waves
  come every 20 seconds with no breathers.

A run's score is Sea Power's own debrief score, doubled for a win. The best for
each area is kept with the flagship, escorts and ensigns that earned it.

## Missions

The mod adds a **Survivors** category to the Scenarios menu, from the INI files
in [mod/missions/Survivors/](mod/missions/Survivors/). Each puts one of the ten
flagships in open water between Iceland and the Faroes, with weapons Tight.

| Mission | What happens |
|---|---|
| **Survivors - *flagship*** | A full run, which starts by itself. A win counts towards unlocks only in a flagship the profile owns |
| **Test Range - *flagship*** | No enemies until you spawn them from the test panel or press *Start run* there |

## Test panel

**Ctrl+F6** opens the test panel in any mission with a player ship. A run started
from it banks no XP.

| Section | Does |
|---|---|
| Run | Start or stop a run, skip a phase, grant a level or chest, send a convoy or SAG, set the threat multiplier (x0.5, x1, x2), and pretend to have air cover |
| Spawn enemy | Spawns ships from the navy you are fighting at 10 to 40 nm, or air groups |
| Apply to your ship | Raise the mast, low-altitude search, radar gain, halve launcher reload, rearm, and write AA, fire control or damage reports to the log |
| Detection tab | What each radar can see, and what stops it seeing each enemy |
| Weapons tab | Launcher reloads, loaded rounds and magazines |
| Upgrades tab | Add ranks to any line |
| Events | The last few actions and deaths, also written to the log |

On the main menu, the same panel holds the shop and launcher, plus +500 XP, +Win
and a profile wipe.

## Balance tools

[tools/balance.py](tools/balance.py) reads the upgrade paths and the game's own
weapon data to compare cards by numbers, and can check its model against a real
run. [DamageLog.cs](src/SeapowerSurvivors/DamageLog.cs) records what each weapon
did, in `<persistent data>/SeapowerSurvivors/damage/`.

## License

This mod is [MIT licensed](LICENSE). That covers the mod's own code, not Sea Power or its
data, the charts drawn from that data, or the area positions taken from Quick
Start.
