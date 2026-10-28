# seapower-survivors

A survivors-like mode for Sea Power: one surface ship, timed waves of Soviet
ships and aircraft, XP for kills, and a choice of upgrades on every level-up.
These are applied live, mid-battle.

It is a C# code mod loaded by AnchorChain under BepInEx, both already part of
the Sea Power modding scene. The mission-file and JavaScript scripting routes
cannot change a ship's stats at runtime, so a code mod is the only way to do
this.

| Document | What it holds |
|---|---|
| [design.md](design.md) | The game design: hulls, XP, upgrades, waves, pickups |
| [docs/combat-model.md](docs/combat-model.md) | How Sea Power's weapons, fire control and damage actually work |
| [docs/hooks.md](docs/hooks.md) | Where in the game code each feature attaches |

Most of `docs/` comes from decompiled code and shipped data, not
from play; what has been seen working is listed under "Confirmed in game" in
[docs/hooks.md](docs/hooks.md#confirmed-in-game).

Game build examined: **0.8.4 Build 261002** (2 October 2026).

## Build and run

```
dotnet build src/SeapowerSurvivors -c Release
```

This builds against the installed game and copies `SeapowerSurvivors.dll`
and `_info.ini` into `Sea Power_Data/StreamingAssets/SeapowerSurvivors/`.

- If the game lives somewhere other than
  `D:\SteamLibrary\steamapps\common\Sea Power`, pass `-p:SeaPowerDir=...`.
- To build without copying into the game, pass `-p:Deploy=false`.

Code is loaded once per launch, so **restart the game after every build**.
The mod logs to `<install>/BepInEx/LogOutput.log` under the source
`Survivors`.

## Missions

The mod adds a **Survivors** category to the Scenarios menu. The missions are
plain INI files in [mod/missions/Survivors/](mod/missions/Survivors/). Each
puts one ship (Perry, Kidd, Long Beach or Iowa) in open water between Iceland and the
Faroes, with weapons Free.

| Mission | What happens |
|---|---|
| **Survivors - Perry / Kidd / Long Beach / Iowa** | A full run. The director starts it by itself 10 s in, because the file name starts with `Survivors - ` |
| **Test Range - Perry / Kidd / Long Beach / Iowa** | No enemies until you spawn them from the test panel, or press *Start run* there |

## The run

- **The script.** [Director.cs](src/SeapowerSurvivors/Director.cs) runs the
  minute-by-minute table in [Script.cs](src/SeapowerSurvivors/Script.cs).
  Each minute has a quota of enemy units kept alive, topped up as they die;
  scripted spikes; and a mini-boss every three minutes. Surviving 15:00 of
  mission time declares victory.
- **XP.** [CombatHooks.cs](src/SeapowerSurvivors/CombatHooks.cs) pays XP for
  kills and for enemy missiles our SAMs or CIWS shoot down. The values and
  level curve are in [Progression.cs](src/SeapowerSurvivors/Progression.cs).
- **Level-ups.** [LevelUpScreen.cs](src/SeapowerSurvivors/LevelUpScreen.cs)
  pauses the game and offers three upgrades from
  [Upgrades.cs](src/SeapowerSurvivors/Upgrades.cs). Banked levels are shown
  one after another.
- **Status line.** One along the top shows time left, level and XP bar, hull,
  kills, intercepts, live enemies and enemy missiles in flight.

## Test panel

**Ctrl+F6** opens the test panel in any mission with a player ship. The game
binds Ctrl+F3, Ctrl+F5 and Ctrl+F7; QuickStart takes Ctrl+F8;
SeapowerMultiplayer takes Ctrl+F9 to Ctrl+F11. Windows swallows Ctrl+Shift+0.

| Section | Does |
|---|---|
| Run | Start or stop a run, skip a minute, grant a level, set the threat multiplier (x0.5, x1, x2) live, and pretend to have air cover |
| Spawn enemy | Spawns an Osa I, Sovremenny or Slava at 10, 20 or 40 nm on a random bearing, or a pair of Tu-16K bombers at 160–175 nm. Each is told where you are, set to weapons Free and sent toward you |
| Apply to your ship | Mast +30 m (radar horizon), low-altitude search, radar gain +3 dB, launcher reload halved, or a rearm with the game's own `RearmAll` |
| Detection tab | Each search radar's antenna height, range and altitude floor; then every enemy unit and missile, either SEEN (and by which radars) or HIDDEN, with what stops each radar: role, altitude window, max range, horizon, terrain or power |
| Weapons tab | Launcher reloads and loaded rounds, and magazine counts |
| Events | The last few actions and deaths, with XP |

Everything on the event feed is also written to the log.
