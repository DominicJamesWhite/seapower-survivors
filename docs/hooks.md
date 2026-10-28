# Hooks

Where each feature attaches to the game. This comes from decompiling
`Seapower-Scripts.dll` and the existing Workshop code mods, build
**0.8.4 Build 261002**. **Nothing here has been tested in game yet.**

## Confirmed in game

**2026-10-28, Test Range - Spruance, using the proof-of-concept test panel.**

- **Loading.** AnchorChain loads the DLL from
  `StreamingAssets/SeapowerSurvivors/`, and the mod's
  `missions/Survivors/` folder appears as a category in the Scenarios menu.
- **Spawning.** `createShip` mid-mission with `CivilianRoute`'s post-setup,
  then waypoints, `RevealContactToObject(enemy, player, "Comms", 3)` and
  weapons Free. The spawned Osas and Sovremenny come at the player and fight.
- **Radar.** `_peakPower` and `_maxRange` change on a live ship, but detection
  did not visibly change: Osas at 20–30 nm stayed hidden. The horizon, about
  18 nm against an Osa, was the binding limit.
- **Mast height.** Raising `_sensorOffset` on a live ship moves the radar
  horizon, and the Detection readout matched what the map showed. Seen on the
  Kidd range:
  - The SPS-48 antenna starts at 26 m.
  - A Slava spawned at 40 nm.
  - The antenna was raised in 30 m steps.
  - `_gainFactor` was also doubled, which did not visibly affect detection.
- **Reload.** `_vwp._magazineReloadTime` changes on a live ship. The Harpoon
  launcher (MK141) reads 0: it never reloads, so this upgrade does nothing for
  Harpoons.
- **Rearm.** `RearmAll()` refills the ship.
- **Kills.** The `setDestroyedFlag` prefix/postfix pair fires once per kill.
  - An Osa logged `score=True`, `_unitScoreValue` **1.0**, and
    `_damageByTaskforce` `[Player:383]`.
  - Side attribution works, but which unit made the kill still needs the
    last-hit tracking.

Everything else in this document is still read from code only.

---

## Loading

- **Load chain.** BepInEx 5.4.23 loads `BepInEx/plugins/AnchorChain.Preloader.dll`.
  That finds `AnchorChain.dll` (Workshop item 3380210757) in any mod folder.
- **What AnchorChain loads.** It loads **every** `*.dll` in every mod folder,
  whether or not the mod is ticked in the in-game Mods menu, and creates each
  type that implements `IAnchorChainMod` and carries `[ACPlugin]`.
  - A mod must switch itself off when unticked. To check: compare
    `typeof(Entry).Assembly.Location` against
    `Singleton<FileManager>.Instance.Directories[i].IsChecked`.
  - Do not use `[ACIncompatibility]`. A bug in AnchorChain skips any mod that
    declares one.
- **Local development layout.**
  `Sea Power_Data/StreamingAssets/SeapowerSurvivors/` holding `_info.ini`
  (`[Language_en] Name=, Description=`) and **only our own DLL**. Any
  reference DLL copied there gets loaded a second time.
- **Code loads once per launch.** After a rebuild, the game must be fully
  restarted.
- **Log:** `BepInEx/LogOutput.log`.
- **Build:**
  - Target `netstandard2.1`, LangVersion 11.
  - References, all with `Private=false`:
    - `Seapower-Scripts.dll`, `UnityEngine.CoreModule.dll` and
      `Noesis.NoesisGUI.dll` from `Sea Power_Data/Managed`;
    - `0Harmony.dll` from `BepInEx/core`;
    - `AnchorChain.dll`.

```csharp
[ACPlugin("com.dominicjameswhite.survivors", "Sea Power Survivors", "0.1.0")]
public class Entry : IAnchorChainMod {
    public void TriggerEntryPoint() {
        new Harmony("com.dominicjameswhite.survivors").PatchAll(typeof(Entry).Assembly);
        var go = new GameObject("Survivors"); Object.DontDestroyOnLoad(go);
        go.AddComponent<Director>();
    }
}
```

## Spawning

Spawns go through `Singleton<ObjectsManager>.Instance`: `createShip`,
`createAircraft` and `createHelicopter`. The game's own `CivilianRoute.SpawnUnit`
and the debug spawner `DM.SpawnUnit` are the templates to copy.

```csharp
Singleton<ObjectsManager>.Instance.createShip(variantRef, loadout, "wp_ptg_osa1",
    Singleton<ObjectsManager>.Instance._listOfAllObjects.Count + 1,
    Singleton<SceneCreator>.Instance.GeoCenterPosition, geo, 3 /*telegraph*/,
    new Vector3(0, headingDeg, 0), Globals._enemyTaskforce);
```

- **Taskforces:** `Globals._enemyTaskforce`, `_playerTaskforce`,
  `_neutralTaskforce`. Pickups and airliners go in the neutral taskforce.
- **Variant and loadout:**
  - Variant: a `VariantN` name, or `"random"`.
  - Loadout: `"default"`, a named loadout, or `"Random"`.
  - Aircraft use `SquadronN`.
- **Threading:** spawns must run on the Unity main thread. The object returns
  before its resources finish loading.
- **After spawning, copy `CivilianRoute`'s post-setup:**
  - set the transform position and call `Physics.SyncTransforms()`;
  - seed `_velocityInKnots`, otherwise the ship starts at 0 kn;
  - for aircraft: `ImmediateStartEngines()`, `SetFlightPhysicsModel(true)`.
- **Positions:** 1 nm = 27.559496 Unity units. **The world is a flat
  latitude/longitude grid: a degree of longitude is 60 nm at every latitude**
  (`GeoPosition.GetDistanceInDeg`, `Utils.longLatToLocalV3`). Use
  `GeoOffsetByDistanceOnHeading` as it is; correcting it for cos(latitude)
  stretches east-west offsets 2.1x at 62 N. Use
  `TerrainUtils.IsThereOnlyWaterForRadius` to check for open water.
- **Orders:** `ObjectBase.setWaypointTask(geo)`. A forced attack is an
  `EngageTask` plus `AddEngageTask`, following `CoreService.AttackVehicle`.
- **Making enemies aware of the player:** `Utils.RevealContactToObject`.
- **Despawning with no kill scored:** `destroyObject` with `score:false`.

## Kills and XP

- **The death event.** Use a Harmony prefix and postfix on
  `ObjectBase.setDestroyedFlag(bool score, ...)`. SeapowerMultiplayer hooks
  the same method. Fire on `!wasDestroyed && __instance.isUnit() && score`,
  and filter with `__instance._taskforce == Globals._enemyTaskforce`.
  `OnDestruction` fires late, at despawn, so don't use it.
- **XP value:** `_obp._unitScoreValue`.
- **Who killed it.** The game only records damage per side, in
  `_damageByTaskforce`, so keep a "last hit by" map:

  | Hit type | Hook | Shooter |
  |---|---|---|
  | Missiles, bombs, torpedoes | Prefix `Blastzone.OnHitUnit` | Private `_launchPlatform` field |
  | Gun hits | `Projectile.ProcessCollision` | `Projectile._launchPlatform` |
  | CIWS kills | Prefix `WeaponSystemCIWS.InterceptAirTarget` | `__instance._baseObject` |

## Time, pause, ending

- **Clock:** `GameTime.missionElapsedTime`.
- **Pausing:** `GameTime.Pause(true)` / `UnPause()` / `StopTimeCompression()`.
  The game's own unpause also clears `InputHandler.IsInPauseMode` and
  `IsInMessagePauseMode`.
- **Hold the pause during a level-up** with a prefix that blocks `UnPause` and
  `SetTimeCompression` while the card dialog is open.
- **End the mission:** set `MissionManager.Instance._missionStatus`, then
  `_endMissionTime = missionElapsedTime + delay`.

## Level-up UI

Use our own `NoesisView` on its own camera, with the XAML held in a string.
SeapowerMultiplayer's `NoesisOverlay.EnsureView` does exactly this.
`MouseControlState.setMouseIsOverUIWindow(true)` stops clicks reaching the
map. IMGUI `OnGUI` works for a first prototype.

## Upgrades

Each ship has its own copy of its parameter objects, so changing them buffs
only that ship. **The exception is `AmmunitionParameters`.**
`GetShared(name)` is a master copy per ammo type, and every ammunition object
holds a `MemberwiseClone` of it.

- **Change the clone, never the master.**
- **Lists inside the clone are still shared.** Replace them; never edit them
  in place.

| Upgrade | Member | Notes |
|---|---|---|
| Mast height | `SensorSystem._sensorOffset` (Unity units, 1 = 67.2 m) on each search radar | The only lever on the radar horizon |
| Low-altitude search | `_rp._minAltitude` on radars whose `_rp._role` is not Surface | Hard altitude floor. Leave Surface radars alone: the game pins them at −10 so they never see air targets |
| Radar gain | `_rp._gainFactor` (linear) | `_gain` (dB) is never reconverted after construction. Prefer it to `_peakPower`, which trades badly against enemy ESM |
| Scan rate | Private `_rpm` and `ScanPeriodSeconds` on `SensorSystemRadar` | Two-sweep confirmation, so sweep period sets pickup delay |
| Reapplying radar buffs | Postfix `SensorSystemRadar.init` / `LoadSensorSystemRadar` | A reload rebuilds `RadarParameters` from ini. `DevModeUtils.ResetRadarParameters` would wipe our changes |
| Fire-control channel | `SensorSystem._targetChannels`, `_weaponChannels` on our directors | |
| SAM kill probability | Our ammo clones: `_ap._killProbability` | |
| CIWS intercept | `WeaponSystemCIWS._missileInterceptChance` | Then reset the private `_sigmaFcsMissileMrad = -1`, or the cached calibration ignores the change |
| Reaction time | `_vwp._targetAcquisitionTime`, `_maxReactiontime`, `_preLaunchDelay`, and crew `_gunTargetAquisitionSpeed` | |
| Salvo doctrine | `TacticalDoctrine.AASalvoSize` | |
| Decoys | Our chaff clone `_ap._decoySpoofChance` | Chaff reload is the private `WeaponSystemChaff._reloadTime` |
| Missile range | `_ap._launchRangesInUnity.y`, `_maxLaunchRangeInMiles`, `_maxFlightTime` | For `ApplyKinematics` missiles also set `_dragCoefficient = cached ÷ k`, which bypasses `KinematicsCache` |
| Missile / SAM reload | `_vwp._magazineReloadTime`, `_fireRatePerMinute` | Vertical-launch cells only benefit on their next firing |
| Gun rate of fire | Private `WeaponSystemGun._shellReloadTime`, `_delayBetweenShotsinBurst` | Computed at spawn |
| Gun damage | `_ap._power` on our shell clones | Confirm the field name |
| Deep magazines | `WeaponMagazineSystem._initialAmmoCount[ammo]` plus `increaseAmmunitionCount` | |
| Ammo refill (pickups) | `WeaponMagazineSystem.RefillAmmo`, `WeaponContainer.RefillAmmo` | Test against preloading launchers |
| Hull integrity / Patch-up | `Compartments._limitIntegrity`, `_maxLimitIntegrity` | |
| Damage reduction | Postfix `Blastzone.GetWeaponDamage(hitObject)` and scale `__result` | Does not cover fire or flooding |
| Damage control | `Crew._structureRepair`, `_systemRepair`, `_floodingRepair`, `_fireSeveretyRepair`; `SetDamageControlTeamsNumbers` | **Never** call `SetCrewSkill` again: it multiplies, so the bonuses compound |
| Engines | Edit `VP._telegraphVelocities[]` in place, and raise `_maxForwardVelocityInKnots` | `Compartments` holds the same array |
| ESM, Jammer | Not found yet | ECM jam chance `_ep._jamChance`; how it is stored per ship is unverified |
