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

### What sinking ships leave behind

- **Frame rate sets game speed.** Measured from the log, game time per frame
  is capped at about compression ÷ 30 s, and physics at 10 fixed steps a frame
  (`GameTime._physicsTimeScaleCap`). Below 30 fps the game itself runs slow.
- **Life rafts** are full `Vessel`s, but their `Deco` role keeps them out of
  `_listOfAllVesselObjects`. `Compartments.SpawnLifeRafts` is their only source;
  during a run a prefix skips it.
- **Wake bubbles** are `ObjectBase` objects in `_listOfAllObjects`, also outside
  the type lists. `WakeBubble.init` reads `Globals.WakeBubbleParameters._lifetime`
  (1160 s in `systems/sensors.ini`); during a run a prefix makes that 200 s.
  `WakeSeeker` only asks that a bubble has life left.
- **The tail bubble leaks.** Each moving ship has one with an infinite lifetime,
  and nothing removes it when the ship dies. `Clutter.SweepWakes` removes
  bubbles whose ship has sunk or gone.

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

## Launching a mission from code

This is how [Launcher.cs](../src/SeapowerSurvivors/Launcher.cs) starts a run
without a scenario:

- **Launch:** set `Globals.currentMissionFilePath` to any full path, then
  call `MissionManager.DoLoad()`. This is what the Scenarios menu's Play
  button does (`ScenarioMenuViewModel.PlayCommand`).
  - The game's own Instant Action generator writes
    `user/missions/_InstantAction.ini` and loads it the same way.
  - The Encyclopedia button passes `LoadAction` coroutines to `DoLoad`;
    they run during the load.
- **Main menu only.** `DoLoad` loads the mission scene additively on top of
  the menu's background scene.
  - At the main menu, `MenuSystemViewModel` exists and `MissionManager`
    does not. QuickStart uses the same test.
- **The ini cache.** `Session` and `SceneCreator` read the mission file
  uncached. The briefing, the camera and the terrain read it through the
  cache, so a rewritten file needs `IniHandler.invalidateCache(path)`.
- **Ship variants.** A vessel's `VariantReference` defaults to `Default`.
  Every ship offered so far has a `Variant1` in its `_variants.ini`.
- **The main-menu slot.** QuickStart takes the hidden Instant Action button:
  - it patches `MainMenuViewModel.InstantActionEnabled` to return true;
  - it relabels the button through `LanguageResourceHandler`;
  - it catches the click with a prefix on the `MenuSystemViewModel.CurrentWindow`
    setter.

  A Survivors button needs a slot of its own.

## Ensigns

Read from code, build 0.8.4 Build 261002. **Not yet tested in game.** Numbers go
through `Boosts.Apply`; what acts during the fight is in `EnsignCombat`.

- **Damage.** `Blastzone.GetWeaponDamage` postfix covers warheads on ships and
  aircraft. Shells on aircraft go through `CiwsBallistics.VariedHitDamage`, flagged
  by a `Projectile.ProcessCollision` prefix, as Critical hits does.
- **Ignored hits.** An `OnHitUnit` prefix returning false records no impact, so
  `CheckForInstakill` has nothing to drain from the hidden pool either.
- **Bomb accuracy.** A bomb's aim error comes from the aircraft's `_bombingAccuracy`
  and `_bombingMovingTargetPenalty` (`Bomb.GetAimErrorSigma`), and its scatter from
  the round's `_circularErrorRadius` (`Bomb.GetBallisticScatterSigma`).
- **Salvos.** `AI.AutoAttackSalvoSize` is 1 for an anti-ship missile, so a postfix
  doubling it makes two a launch.
- **EMP.** A `CreateExplosion` prefix replaces the mortar round's blast. Radars get
  `Inoperable`, and the ship is held on weapons hold; `Spawner.Alert` skips it.
- **CIWS reach.** Round range, then `_maxRangeSq` and `_sigmaFcsMissileMrad` set to
  −1 so it recalculates and refits its hit model. Tracer life is capped at 8 s.
- **Wire.** `_midCourseCorrection`, `_wireBreakSpeedInKnots`, `_maxWireCount`;
  `BreakWire()` once `_remainingRangePercent` reaches 50.
- **Jamming** (`EcmFit`). `new SensorSystemECM(ship, "AN/ALQ-99", mount)` on a
  dummy mount, as `WeaponBase` makes missile jammers. It is added to `_systems`,
  `_sensorSystems` and `_systemsDictionary`, then `init()`, which fills
  `_ecmSystems`. Aiming is the stock `JamTask`, read back from `_associatedTarget`
  or `_associatedTargetPosition`. A prefix replaces `SensorSystemECM.OnFixedUpdate`
  with a once-a-second pass that skips the horizon check, fills
  `_ECMSystemsTryingToJamMe`, and calls `ECM.jamWeapon` for missiles in the cone.
  Stock, that roll only runs within 3 nm of the missile's target
  (`Missile.CalculateDeviation`), and that path still works, aimed or not.
- **Flags** are one `Texture2D` used by IMGUI directly and by Noesis through
  `TextureSource`.

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
| Missile / torpedo speed | `_maxVelocityInKnots`, `_terminalVelocityInKnots`, `_maxLoftVelocityInKnots`, `_searchVelocityInKnots`, thrust; to keep the path the same shape, turn rates (incl. `_terminalVerticalTurnRate`) and `_ladderTurnDegreesPerSecond` ×speed, `_maxTurnG` ×speed², and `_snakeSearchTurnCycleTime`, `_turnDelay` (wake homing) ÷speed | Anything timed in seconds draws a wider path at speed. `WeaponBase.CheckCollision` ignores hits for 1 s after `_launchTime`, a hard-coded value: prefix it (`Boosts.ArmAtSpeed`) |
| Mortars | `_launchRangesInUnity.y`, `_maxLaunchRangeInMiles`, `_maxVelocityInKnots`, `_acceleration`, `_initialFlightPhaseDuration` ÷speed, `_acceleration` ×speed², on our clones | The AI will not fire them far: see `Mortars`, which queues `InsertEngageTask` itself, drops them from `AI.AmmoForTarget` and transpiles `CheckIfAlreadyHandled`. `_magazineReloadTime` alone does not reload them faster: prefix `ObjectCodeAnimation.update` and move private `_animationStartTime` back. Postfix `RBU.Launch.takeAction` for the lead and aiming error, once a round as `_flightStage` turns to `TerminalApproach`. Postfix `RBU.TerminalApproach.takeAction` to steer. Both set `_estimatedInterceptTargetPosition` and turn `transform.forward` |
| Missile / SAM reload | `_vwp._magazineReloadTime`, `_fireRatePerMinute` | Vertical-launch cells only benefit on their next firing |
| Gun rate of fire | Private `WeaponSystemGun._shellReloadTime`, `_delayBetweenShotsinBurst`, `_burstDelay` (a gun with `_burstCount` > 1 reads only the last two: AK-726, AK-130, Mk 28, ZIF-75); ready-rack reload `_vwp._magazineReloadTime`; shell speed `_muzzleVelocityInUnity` with `_gravity` ×speed² | Computed at spawn. The fire table is cached per ammo name, so boosted shells get their own |
| Gun damage | `_ap._power` on our shell clones | Confirm the field name |
| Deep magazines | `WeaponMagazineSystem._initialAmmoCount[ammo]` plus `increaseAmmunitionCount` | |
| Ammo refill (pickups) | `WeaponMagazineSystem.RefillAmmo`, `WeaponContainer.RefillAmmo` | Test against preloading launchers |
| Hull integrity / Patch-up | `Compartments._limitIntegrity`, `_maxLimitIntegrity` | |
| Damage reduction | Postfix `Blastzone.GetWeaponDamage(hitObject)` and scale `__result` | Does not cover fire or flooding |
| Damage control | `Crew._structureRepair`, `_systemRepair`, `_floodingRepair`, `_fireSeveretyRepair`; `SetDamageControlTeamsNumbers` | **Never** call `SetCrewSkill` again: it multiplies, so the bonuses compound |
| Engines | Edit `VP._telegraphVelocities[]` in place, and raise `_maxForwardVelocityInKnots` | `Compartments` holds the same array |
| ESM, Jammer | Not found yet | ECM jam chance `_ep._jamChance`; how it is stored per ship is unverified |

## UI theme

The game's XAML can be read from its asset files: `sharedassets*.assets` and
`globalgamemanagers.assets` hold it as length-prefixed plain text.
`NoesisTheme.Brushes.DarkRed`, `NoesisTheme.Fonts`, `NoesisTheme.Styles`,
`SeaPower.Theme.Custom` and `SeaPower.Resources` are the ones that matter.

- **Noesis views** use `{DynamicResource Brush.*}` and the game's implicit
  `Button`, as Dynamic Campaign Mod's runtime-parsed XAML does.
- **IMGUI** uses the same colours, named in `Hud`. Roboto Medium and Bold exist
  as Unity fonts in `sharedassets2`; Regular is a Noesis font only, so
  `mod/fonts` ships it, from googlefonts/roboto v2.138 (Apache 2.0).
- **Rarity** uses the crew skill marks and colours from `CrewSkillGlyphTemplate`.
