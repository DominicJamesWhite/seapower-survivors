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

## Test ranges

The mod adds a **Survivors (test)** category to the Scenarios menu:
**Test Range - Spruance**, **Test Range - Kidd** and **Test Range - Perry**.
Each one puts a single ship in open water between Iceland and the Faroes,
with weapons Free and no enemies until you spawn them. The only ending is
losing the ship; leave through the pause menu. The missions are plain INI
files in [mod/missions/Survivors/](mod/missions/Survivors/).

## Test panel

**Ctrl+F6** opens the test panel in any mission with a player ship. The game
binds Ctrl+F3, Ctrl+F5 and Ctrl+F7; QuickStart takes Ctrl+F8;
SeapowerMultiplayer takes Ctrl+F9 to Ctrl+F11. Windows swallows Ctrl+Shift+0.

| Section | Does |
|---|---|
| Spawn enemy | Spawns an Osa I, Sovremenny or Slava at 10, 20 or 40 nm on a random bearing. It is revealed to you, weapons Free, and sent toward you |
| Apply to your ship | Mast +30 m (radar horizon), low-altitude search, radar gain +3 dB, launcher reload halved, or a rearm with the game's own `RearmAll` |
| Detection tab | Each search radar's antenna height, range and altitude floor; then every enemy unit and missile, either SEEN (and by which radars) or HIDDEN, with what stops each radar: role, altitude window, max range, horizon, terrain or power |
| Weapons tab | Launcher reloads and loaded rounds, and magazine counts |
| Events | The last few actions and deaths, with XP |

Everything on the event feed is also written to the log.
