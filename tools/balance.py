#!/usr/bin/env python3
"""Turn the Guns, Accuracy and Rocket mortars lines into comparable damage numbers.

Reads the upgrade paths from src/SeapowerSurvivors/Upgrades.cs and every weapon
and hull stat from the game's own INI files, so it follows both as they change.
The damage rules are ported from the decompiled game.

    python tools/balance.py                              every flagship, full lines
    python tools/balance.py cards wp_rkr_slava usn_cg_belknap     what each rank adds
    python tools/balance.py air                          SAMs against missile raids and aircraft
    python tools/balance.py compare                      newest damage log against the model
    python tools/balance.py compare "path/to/... hits.csv"
"""
import argparse
import csv
import glob
import math
import os
import random
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GAME = Path(os.environ.get("SEAPOWER_DIR", r"D:\SteamLibrary\steamapps\common\Sea Power"))
DATA = GAME / "Sea Power_Data" / "StreamingAssets" / "original"

M_PER_NM = 1852.0
YEAR = 1985  # the missions' date, which picks each hull's loadout
ARMOUR = {"Minor": 1.2, "Moderate": 1.25, "Heavy": 1.3}
BEARING_MILS = 1.5  # Globals._gunFCBaseBearingErrorMils
RANGE_PERCENT = 1.5  # Globals._gunFCBaseRangeErrorPercent
RANGE_FLOOR_M = 40.0  # Globals._gunFCBaseRangeErrorFloorUnity, in metres
SETTLED_RANGE = 0.3  # the range error left once a gun's solution is complete
BLAST_M = 120.0  # a VeryLarge impact: every mortar round
PATTERN_M = 50.0  # SalvoFireTargetAreaCircleRadius
CRIT_CHANCE, CRIT_FACTOR = 0.2, 5.0  # Abilities.cs
HEAVY_SHELLS, LONG_SHOTS, BOMBARDMENT = 2.0, 1.5, 2.0  # Boosts.cs
FEET_PER_UNIT = 220.47266
WAVE_TOP_FEET = 10.0
GHOST_RCS, ARMOURED_HITS = 0.1, 4.0  # Boosts.cs
HYPERSONIC, LONG_REACH = 2.5, 2.5  # Boosts.cs
INTERCEPT_FLOOR = 0.05  # Globals._missileInterceptChanceFloor
ACQUISITION = {"VerySlow": 80, "Slow": 50, "Medium": 30, "Fast": 15, "VeryFast": 7}  # sensors.ini
MAST_M = 30.0  # the radar height assumed for the sea-skimmer horizon
REFERENCE_RCS, REFERENCE_DETECT_NM = 0.2, 15.0  # assumed: a Harpoon-sized echo is held at 15 nm
AIRCRAFT = {False: ["usn_a-6e", "usaf_b-52g"], True: ["wp_su-24m", "wp_tu-16"]}
RAIDS = {False: ["usn_rgm-84a", "usn_rgm-84c"], True: ["wp_ss-n-2c", "wp_as-4", "wp_ss-n-22"]}

SOVIET_TARGETS = ["wp_ptg_osa1", "wp_rkr_kresta1", "wp_rkr_slava", "wp_kr_sverdlov"]
NATO_TARGETS = ["knm_ptg_hauk", "usn_ff_knox", "usn_cg_belknap", "usn_bb_iowa"]


# ---------------------------------------------------------------- reading files

def read_ini(path):
    """Sections of an INI file; the first value of a repeated key wins."""
    sections, current = {}, None
    with open(path, encoding="utf-8", errors="replace") as f:
        for raw in f:
            line = raw.strip()
            if line.startswith("["):
                end = line.find("]")
                current = sections.setdefault(line[1:end], {}) if end > 0 else None
            elif current is not None and "=" in line and not line.startswith(("#", ";", "//")):
                key, value = line.split("=", 1)
                current.setdefault(key.strip(), re.split(r"\s*(?://|#)", value)[0].strip())
    return sections


def flat(sections):
    merged = {}
    for values in sections.values():
        for key, value in values.items():
            merged.setdefault(key, value)
    return merged


def num(table, key, default=0.0):
    try:
        return float(table.get(key, default))
    except ValueError:
        return default


AMMO_CACHE = {}


def ammo(name):
    if name not in AMMO_CACHE:
        AMMO_CACHE[name] = flat(read_ini(DATA / "ammunition" / f"{name}.ini"))
    return AMMO_CACHE[name]


WEAPONS = None


def weapon_record(name):
    global WEAPONS
    if WEAPONS is None:
        WEAPONS = read_ini(DATA / "systems" / "weapons.ini")
    return WEAPONS.get(name, {})


SENSORS = None


def sensor_record(name):
    global SENSORS
    if SENSORS is None:
        SENSORS = read_ini(DATA / "systems" / "sensors.ini")
    return SENSORS.get(name, {})


def parse_lines():
    """Each line's path from Upgrades.cs: a list of (stat, value) or ("Ability", name)."""
    text = (REPO / "src" / "SeapowerSurvivors" / "Upgrades.cs").read_text(encoding="utf-8")
    lines = {}
    for name in ("Guns", "Mortars", "Accuracy", "AntiShip", "Sams"):
        body = re.search(rf"static Upgrade Make{name}\(\)(.*?)\n    }}\n", text, re.S).group(1)
        steps = {v: (s, float(x)) for v, s, x in re.findall(r"var (\w+) = new Step\(Stat\.(\w+), ([\d.]+)f", body)}
        steps.update({v: (s, float(r), float(c)) for v, s, r, c in re.findall(r"var (\w+) = Step\.Chance\(Stat\.(\w+), ([\d.]+)f, ([\d.]+)f", body)})
        for v, blend in re.findall(r"var (\w+) = Step\.Blend\((.*?)\);", body, re.S):
            effects = []
            for m in re.finditer(r"new Step\(Stat\.(\w+), ([\d.]+)f|Step\.Chance\(Stat\.(\w+), ([\d.]+)f, ([\d.]+)f|\b([a-z]\w*)\b", re.sub(r'"[^"]*"', "", blend)):
                if m[1]:
                    effects.append((m[1], float(m[2])))
                elif m[3]:
                    effects.append((m[3], float(m[4]), float(m[5])))
                elif m[6] in steps:
                    effects.append(steps[m[6]])
            steps[v] = ("Blend", effects)
        path_text = re.search(r"Path = new\[\]\s*\{(.*)\},\s*\};", body, re.S).group(1)
        token = re.compile(
            r'Step\.Ability\(\w+ = new Unlock\("(?P<ability>[^"]+)",\s*"[^"]*"\)\)'
            r'|new Step\(Stat\.(?P<stat>\w+),\s*(?P<value>[\d.]+)f,\s*"[^"]*",\s*"(?P<named>[^"]*)"\)'
            r'|\b(?P<var>[a-z]\w*)\b')
        path = []
        for m in token.finditer(path_text):
            if m["ability"]:
                path.append(("Ability", m["ability"]))
            elif m["stat"]:
                path.append(("Named", m["named"], m["stat"], float(m["value"])))
            else:
                path.append(steps[m["var"]])
        lines[name] = path
    return lines


def totals(path, ranks):
    """A line's first ranks as combined factors, and the abilities it has by then."""
    out = {"Damage": 1.0, "RateOfFire": 1.0, "Reload": 1.0, "Reach": 0.0, "Range": 1.0, "Accuracy": 1.0, "Speed": 1.0, "GunAim": 1.0, "ShellPower": 0.0,
           "Channels": 0, "abilities": set()}
    chances = {}
    for rank in path[:ranks]:
        for step in rank[1] if rank[0] == "Blend" else [rank]:
            if step[0] == "Ability":
                out["abilities"].add(step[1])
            elif step[0] == "Named":
                out["abilities"].add(step[1])
            elif step[0] in ("Reach", "ShellPower"):
                out[step[0]] += step[1]
            elif step[0] == "Channels":
                out["Channels"] += int(step[1])
            elif len(step) == 3:
                rate, cap, n = step[1], step[2], chances.get(step[0], (0, 0, 0))[2] + 1
                chances[step[0]] = (rate, cap, n)
            elif step[0] in out:
                out[step[0]] *= step[1]
    for stat in ("KillChance", "Evasion"):
        rate, cap, n = chances.get(stat, (0, 0, 0))
        out[stat] = cap * (1 - 1 / (1 + rate * n)) if n else 0.0
    return out


def describe(step):
    if step[0] == "Blend":
        return ", ".join(describe(s) for s in step[1])
    if step[0] == "Ability":
        return step[1]
    if step[0] == "Named":
        return step[1]
    if step[0] == "Reach":
        return f"Reach +{step[1]:g} nm"
    if len(step) == 3:
        return f"{step[0]} rate {step[1]:g}"
    if step[0] == "Channels":
        return f"+{step[1]:g} channel"
    if step[0] == "ShellPower":
        return f"Power +{step[1]:g}"
    if step[0] in ("Reload", "Accuracy", "GunAim"):
        return f"{step[0]} /{1 / step[1]:.2f}"
    return f"{step[0]} x{step[1]:.3g}"


# ---------------------------------------------------------------- hulls

class Hull:
    def __init__(self, name):
        self.name = name
        self.sections = read_ini(DATA / "vessels" / f"{name}.ini")
        merged = flat(self.sections)
        self.length, self.beam = num(merged, "Length", 100), num(merged, "Beam", 12)
        m = 2.5 * num(merged, "Displacement", 1000) ** (1 / 3) * ARMOUR.get(merged.get("ArmorType", ""), 1.0)
        override = num(merged, "IntegrityOverride")
        self.pool = 5 * (override if override > 0 else m)
        self.guns, self.mortars, self.missiles, self.sams, self.ciws = [], [], [], [], []
        self.chaff = 0.0
        self.shared = {}
        acquisition = merged.get("TargetAcquisitionTime", "Medium")
        self.acquisition = ACQUISITION.get(acquisition) or num({"x": acquisition}, "x", 30)
        self.read_weapons(merged)

    def read_weapons(self, merged):
        suffix = ""
        for loadout in merged.get("AvailableLoadouts", "").split(","):
            date = merged.get(f"LoadoutDate_{loadout.strip()}")
            if date and float(date) <= YEAR:
                suffix = loadout.strip()
        count = int(num(self.sections.get("WeaponSystems", {}), "NumberOfWeaponSystems"))
        for i in range(1, count + 1):
            section = self.sections.get(f"WeaponSystem{i}{suffix}") or self.sections.get(f"WeaponSystem{i}")
            if section:
                self.read_system(section)

    def rounds(self, section):
        """(ammunition, count) a weapon system can draw on."""
        out = []
        magazine = self.sections.get(section.get("AssociatedMagazine", ""))
        if magazine:
            for n in range(1, int(num(magazine, "NumberOfAmmunitionTypes", 1)) + 1):
                if magazine.get(f"Ammunition{n}"):
                    out.append((magazine[f"Ammunition{n}"], int(num(magazine, f"Ammunition{n}_Count"))))
        for key, value in section.items():
            if key.startswith("Ammunition") and "_" not in key:
                record = weapon_record(section.get("SystemName", ""))
                out.append((value, int(num(section, "NumberOfContainers", 1) * num(record, "NumberOfAttachments", 1))))
        return out

    def read_system(self, section):
        kind = section.get("Type")
        record = weapon_record(section.get("SystemName", ""))
        rounds = self.rounds(section)
        if kind == "Gun":
            pick = [r for r in rounds if "ASuW" in (ammo(r[0]).get("TargetType"), ammo(r[0]).get("SecondaryTargetType"))]
            if pick:
                barrels = max(1, sum(1 for k in section if re.fullmatch(r"Gun\d+", k)))
                self.guns.append(Gun(section["SystemName"], pick[0][0], record, barrels))
        elif kind == "RBU":
            pick = [r for r in rounds if ammo(r[0]).get("TargetType") == "ASW"]
            if pick:
                self.mortars.append(Mortar(section["SystemName"], pick[0], record))
        elif kind == "Missile":
            interval = num(section, "SharedLaunchInterval")
            if interval:
                self.shared[section["SystemName"]] = interval
            reload = num(record, "ReloadTime")
            containers = max(1.0, num(section, "NumberOfContainers", 1))
            launcher = {"system": section["SystemName"], "reload": reload, "containers": containers, "fire_rate": num(record, "FireRate", 60)}
            for name, count in rounds:
                data = ammo(name)
                if data.get("TargetType") == "ASuW" and not re.search(r"chaff|ss-n-14|ss-n-16|asroc", name):
                    self.missiles.append(dict(launcher, ammo=name, count=count))
                elif data.get("TargetType") == "AAW":
                    self.sams.append(dict(launcher, ammo=name, count=count, directors=self.directors(section)))
                    break
        elif kind == "CIWS":
            chance = num(record, "MissileInterceptChance")
            if chance > 0:
                self.ciws.append(chance / 100)
        elif kind == "Chaff" and rounds:
            data = ammo(rounds[0][0])
            self.chaff = max(self.chaff, num(data, "SpoofChance", 0.3))

    def directors(self, section):
        """(target channels, weapon channels, takes ships) of each fire-control radar a launcher names."""
        out = []
        for name in section.get("AssociatedSensors", "").split(","):
            radar = self.sections.get(name.strip(), {})
            record = sensor_record(radar.get("SystemName", ""))
            channels = num(record, "TargetChannels")
            if channels > 0:
                out.append((name.strip(), channels, num(record, "WeaponChannels", channels), record.get("Role", "Air")))
        return out


class Gun:
    def __init__(self, system, shell, record, barrels):
        self.system, self.shell = system, shell
        self.power = num(ammo(shell), "Power")
        self.range_nm = num(ammo(shell), "MaxRange") / M_PER_NM
        self.cep = num(ammo(shell), "CircularErrorProbable", 50)
        self.multiplier = num(record, "DamageMultiplier", 1.0)
        per_shot = 1
        sequential = record.get("FireSequential", "").lower() == "true"
        muzzles = len(record.get("SpawnPosition", "").split("|"))
        if not sequential:
            per_shot = barrels if barrels > 1 else max(1, muzzles)
        self.per_second = num(record, "FireRate", 60) / 60 * per_shot
        self.loaded = num(record, "RoundsLoaded", 1)
        self.reload = num(record, "ReloadTime")

    def rate(self, rate_of_fire, reload_factor=1.0):
        """Shells a second, the ready rack's reload included. Rate of fire divides both."""
        run = self.loaded / self.per_second
        return self.loaded / ((run + self.reload * reload_factor) / rate_of_fire)


class Mortar:
    def __init__(self, system, rounds, record):
        self.system, self.round = system, rounds[0]
        self.magazine = rounds[1]
        data = ammo(self.round)
        self.power = num(data, "Power")
        self.frag = num(data, "WarheadType") == 6  # INI 0 is HighExplosive in code, whatever its comment says
        self.base_reach = num(data, "MaxLaunchRange")
        self.salvo = int(num(record, "SalvoFireAmount", 1))
        self.salvo_gap = num(record, "SalvoFireTime", 0.5)
        self.reload = num(record, "ReloadTime")
        self.multiplier = num(record, "DamageMultiplier", 1.0)


# ---------------------------------------------------------------- the game's damage rules

def warhead_damage(power, multiplier, bonus, frag=False):
    """Blastzone.GetWeaponDamage against a ship, for a round of ours."""
    return power * multiplier * (0.5 if frag else 1.0) * (1 + bonus) * 1.1


SHELL_TABLE = [
    (5.5, [(300, 10), (240, 8), (165, 6), (115, 5), (70, 4), (20, 2)], 1),
    (2.0, [(300, 0), (240, 10), (165, 8), (115, 6), (70, 4), (20, 2)], 1),
    (1.2, [(300, 0), (240, 0), (165, 10), (115, 8), (70, 6), (20, 4)], 2),
    (0.72, [(300, 0), (240, 0), (165, 0), (115, 10), (70, 8), (20, 6)], 4),
    (-1, [(300, 0), (240, 0), (165, 0), (115, 0), (70, 10), (20, 8)], 5),
]


def shell_drain(damage, pool):
    """Blastzone.CheckForInstakill's table: a shell's cut of the hidden pool."""
    for floor, by_pool, smallest in SHELL_TABLE:
        if damage > floor:
            for over, divisor in by_pool:
                if pool > over:
                    return damage / divisor if divisor else 0.0
            return damage / smallest
    return 0.0


def within(half_width, sigma):
    return math.erf(half_width / (sigma * math.sqrt(2))) if sigma > 0 else 1.0


def aim_errors(range_nm, accuracy):
    range_m = range_nm * M_PER_NM
    across = BEARING_MILS * 0.001 * range_m * accuracy
    along = max(RANGE_FLOOR_M, range_m * RANGE_PERCENT * 0.01) * SETTLED_RANGE * accuracy
    return across, along


def shell_spread(cep, range_nm, reach_nm):
    """GunBallistics.InferDispersionSources: no card narrows it, though reach ranks do across the line of fire."""
    return cep / 1.792 * min(1.0, range_nm / reach_nm), 2 * cep / 1.792


def shell_hit(target, range_nm, accuracy, cep, reach_nm):
    """Chance a shell lands on the hull, stationary, averaged between side-on and bow-on: aiming error and shell spread."""
    across, along = aim_errors(range_nm, accuracy)
    spread_across, spread_along = shell_spread(cep, range_nm, reach_nm)
    across, along = math.hypot(across, spread_across), math.hypot(along, spread_along)
    side = within(target.length / 2, across) * within(target.beam / 2, along)
    bow = within(target.beam / 2, across) * within(target.length / 2, along)
    return (side + bow) / 2


def round_hit(target, range_nm, accuracy):
    """Chance a mortar round's blast reaches the hull: the same error, the salvo's pattern and the blast radius."""
    across, along = aim_errors(range_nm, accuracy)
    spread = PATTERN_M ** 2 / 2
    across, along = math.hypot(across, math.sqrt(spread)), math.hypot(along, math.sqrt(spread))
    side = within(BLAST_M + target.length / 2, across) * within(BLAST_M + target.beam / 2, along)
    bow = within(BLAST_M + target.beam / 2, across) * within(BLAST_M + target.length / 2, along)
    return (side + bow) / 2


# ---------------------------------------------------------------- effective numbers

class Build:
    def __init__(self, lines, guns=0, mortars=0, accuracy=0, anti_ship=0, sams=0):
        self.guns = totals(lines["Guns"], guns)
        self.mortars = totals(lines["Mortars"], mortars)
        self.accuracy = totals(lines["Accuracy"], accuracy)
        self.anti_ship = totals(lines["AntiShip"], anti_ship)
        self.sams = totals(lines["Sams"], sams)


def gun_drain_per_hit(gun, build, target, bonus):
    damage = warhead_damage(gun.power + build.guns["ShellPower"], gun.multiplier, bonus) * build.guns["Damage"]
    if "Heavy shells" in build.guns["abilities"]:
        damage *= HEAVY_SHELLS
    plain = shell_drain(damage, target.pool)
    if "Critical hits" in build.accuracy["abilities"]:
        return (1 - CRIT_CHANCE) * plain + CRIT_CHANCE * shell_drain(damage * CRIT_FACTOR, target.pool)
    return plain


def gun_reach(gun, build):
    return gun.range_nm * build.guns["Range"] * (LONG_SHOTS if "Long shots" in build.accuracy["abilities"] else 1.0)


def gun_dps(gun, build, target, range_nm, bonus):
    if range_nm > gun_reach(gun, build):
        return 0.0
    return (gun.rate(build.guns["RateOfFire"], build.guns["Reload"]) * gun_drain_per_hit(gun, build, target, bonus)
            * shell_hit(target, range_nm, build.accuracy["Accuracy"] * build.guns["GunAim"], gun.cep, gun_reach(gun, build)))


def mortar_drain_per_hit(mortar, build, bonus):
    return 2 * warhead_damage(mortar.power, mortar.multiplier, bonus, mortar.frag) * build.mortars["Damage"]


def mortar_reach(mortar, build, radar_nm):
    reach = mortar.base_reach + build.mortars["Reach"]
    if "Bombardment" in build.mortars["abilities"]:
        reach *= BOMBARDMENT
    reach *= 0.95  # Mortars.ReachMargin
    return reach if "Spotters" in build.mortars["abilities"] else min(reach, radar_nm)


def mortar_dps(mortar, build, target, range_nm, bonus, radar_nm):
    if range_nm > mortar_reach(mortar, build, radar_nm):
        return 0.0
    cycle = mortar.reload * build.mortars["Reload"] + (mortar.salvo - 1) * mortar.salvo_gap
    return (mortar.salvo * mortar_drain_per_hit(mortar, build, bonus) / cycle
            * round_hit(target, range_nm, build.accuracy["Accuracy"]))


# ---------------------------------------------------------------- missiles

def intercept_chance(sam, kill_bonus, threat, difficulty, no_floor=False):
    """Utils.GetInterceptionChance: one SAM's chance at one target."""
    speed_mult = num(sam, "InterceptSpeedPenaltyMultiplier", 1.0)
    size_mult = num(sam, "InterceptSizePenaltyMultiplier", 1.0)
    penalty = num(sam, "InterceptOutOfAltitudePenalty", 0.4)
    max_attack = num(sam, "MaxAttackVelocity", math.inf)
    floor = 0.0 if no_floor else num(sam, "MinAttackAltitude", 0.0) / FEET_PER_UNIT
    ceiling = num(sam, "MaxAttackAltitude", 1e9) / FEET_PER_UNIT
    if no_floor:
        penalty = 0.0

    effective = threat["size"]
    size = max(-0.5, min(0.5, (1 - math.sqrt(effective) / 122) / 5 * size_mult)) if effective > 0 else 0.0

    knots = threat["speed"]
    finite = max_attack < math.inf
    reference, start = (max_attack, max_attack / 2) if finite else (670.0, 670.0)
    speed = 0.0
    if knots > start:
        speed = (min(knots, max_attack) - reference / 2) / (reference * 2)
        if finite and knots > reference:
            speed += (knots - reference) / reference
        speed = min(0.5, 0.5 * speed_mult * speed)

    height = threat["feet"] / FEET_PER_UNIT
    altitude = 0.0
    if height < floor:
        altitude = penalty * min(math.sqrt(min((floor - height) / floor, 1.0)) * 0.8 + 0.2, 1.0)
    elif height > ceiling:
        altitude = penalty * math.sqrt(min((height - ceiling) * 10 / ceiling, 1.0))
    elif floor > 0:
        near = min(floor * 3, floor + 0.907142)
        if height < near:
            altitude = (near - height) / (near - floor) * 0.2 * penalty

    chance = (num(sam, "KillProbability", 0.5) + kill_bonus) * (1 - 2 * speed) * (1 - 2 * size) * (1 - 2 * altitude)
    return max(INTERCEPT_FLOOR, chance * (1 - threat["evasion"]) * difficulty)


def threat_from_ammo(name, totals_=None):
    """A missile as a defender sees it, with the Anti-ship line's ranks if it is ours."""
    data = ammo(name)
    t = totals_ or {"Speed": 1.0, "Evasion": 0.0, "abilities": set()}
    feet = num(data, "SeaSkimmingAlt", 1000.0)
    if "Wave-top" in t["abilities"]:
        feet = min(feet, WAVE_TOP_FEET)
    frontal = num(data, "FrontalArea")
    size = (frontal / 0.0047) ** 1.5 if frontal > 0 else num(data, "Mass", 500.0) / 2
    rcs = num(data, "RCS", REFERENCE_RCS) or REFERENCE_RCS
    if "Ghost" in t["abilities"]:
        rcs *= GHOST_RCS
    metres = feet * 0.3048
    horizon = 2.21 * (math.sqrt(MAST_M) + math.sqrt(metres))
    return {"name": name, "speed": num(data, "MaxVelocity", 540.0) * t["Speed"], "feet": feet, "size": size,
            "evasion": min(1.0, num(data, "MissileDefenceBonus") + t["Evasion"]),
            "detect": min(horizon, REFERENCE_DETECT_NM * (rcs / REFERENCE_RCS) ** 0.25),
            "anti_chaff": num(data, "AntiCountermeasuresBonus", 1.0),
            "smart": "Smart seekers" in t["abilities"], "armoured": "Armoured" in t["abilities"]}


def defenders(hull, sam_totals=None):
    """A hull's SAM launchers and fire control, with the SAM line's ranks if it is ours."""
    t = sam_totals or {"Reload": 1.0, "KillChance": 0.0, "Channels": 0, "abilities": set()}
    reach_factor = LONG_REACH if "Long reach" in t["abilities"] else 1.0
    speed_factor = HYPERSONIC if "Hypersonic" in t["abilities"] else 1.0
    launchers, directors = [], {}
    for sam in hull.sams:
        data = ammo(sam["ammo"])
        for name, targets, _, _ in sam["directors"]:
            directors[name] = targets + t["Channels"]
        launchers.append({"data": data, "left": sam["count"], "ready": 0.0,
                          "interval": max(60 / max(sam["fire_rate"], 1), sam["reload"] * t["Reload"] / sam["containers"]),
                          "reach": num(data, "MaxLaunchRange", 10.0) * reach_factor, "floor": num(data, "MinLaunchRange", 0.5),
                          "speed": num(data, "MaxVelocity", 1500.0) * speed_factor * 0.7})  # 0.7: boost and drag, on average
    return launchers, sum(directors.values()), t["KillChance"], "Sea-skimmer killers" in t["abilities"]


def missile_fight(launchers, shared, make_threat, launch_nm, hull, target_pool, drain, rng, sam_totals=None,
                  difficulty=0.8, use_point_defence=True, limit=3600):
    """
    One attack, second by second: launchers fire down a line at a defended ship,
    its directors engage what they have seen, and what is left meets CIWS and chaff.
    Returns launched, stopped, hits, drained, and seconds from first launch to the end.
    """
    sams, channels, kill_bonus, no_floor = defenders(hull, sam_totals)
    launchers = [dict(l, ready=0.0) for l in launchers]
    shared_ready = {}
    flying, engaged = [], []
    launched = stopped = hits = 0
    drained = 0.0
    t = 0
    while t < limit:
        if drained < target_pool:
            for launcher in launchers:
                if launcher["left"] <= 0 or t < launcher["ready"] or t < shared_ready.get(launcher["system"], 0):
                    continue
                launcher["left"] -= 1
                launcher["ready"] = t + launcher["interval"]
                if launcher["system"] in shared:
                    shared_ready[launcher["system"]] = t + shared[launcher["system"]]
                threat = make_threat(launcher["ammo"])
                flying.append({"threat": threat, "range": launch_nm, "seen": None, "busy": False})
                launched += 1
        if not flying and (drained >= target_pool or all(l["left"] <= 0 for l in launchers)):
            break

        for missile in flying:
            missile["range"] -= missile["threat"]["speed"] / 3600
            if missile["seen"] is None and missile["range"] <= missile["threat"]["detect"]:
                missile["seen"] = t

        for shot in [e for e in engaged if e["at"] <= t]:
            engaged.remove(shot)
            missile = shot["missile"]
            missile["busy"] = False
            if missile in flying and rng.random() < shot["chance"]:
                flying.remove(missile)
                stopped += 1

        for sam in sams:
            if sam["left"] <= 0 or t < sam["ready"] or len(engaged) >= channels:
                continue
            ready = [m for m in flying if not m["busy"] and m["seen"] is not None and t >= m["seen"] + hull.acquisition
                     and sam["floor"] < m["range"] <= sam["reach"]]
            if not ready:
                continue
            missile = min(ready, key=lambda m: m["range"])
            closing = (sam["speed"] + missile["threat"]["speed"]) / 3600
            missile["busy"] = True
            engaged.append({"missile": missile, "at": t + missile["range"] / closing,
                            "chance": intercept_chance(sam["data"], kill_bonus, missile["threat"], difficulty, no_floor)})
            sam["left"] -= 1
            sam["ready"] = t + sam["interval"]

        for missile in [m for m in flying if m["range"] <= 0]:
            flying.remove(missile)
            threat = missile["threat"]
            if use_point_defence:
                mounts = sorted(hull.ciws, reverse=True)[:(len(hull.ciws) + 1) // 2]
                leak = 1.0
                for chance in mounts:
                    leak *= (1 - chance) ** (1 / ARMOURED_HITS) if threat["armoured"] else 1 - chance
                if rng.random() > leak:
                    stopped += 1
                    continue
                spoof = 0.0 if threat["smart"] else max(0.0, hull.chaff * difficulty - threat["anti_chaff"])
                if rng.random() < spoof:
                    stopped += 1
                    continue
            hits += 1
            drained += drain(threat["name"])
        t += 1
    return launched, stopped, hits, drained, max(t, 1)


def missile_launchers(hull, kind, totals_, speed_only=False):
    """Launchers ready for a fight; a launcher's next round is its reload over its cells, or its fire rate."""
    out = []
    for m in getattr(hull, kind):
        reload = m["reload"] * totals_.get("Reload", 1.0)
        out.append({"system": m["system"], "ammo": m["ammo"], "left": m["count"],
                    "interval": max(60 / max(m["fire_rate"], 1), reload / m["containers"])})
    return out


def ship_attack(hull, target, build, args, kind):
    """Effective numbers for missiles fired at a defended ship, over many trials."""
    rng = random.Random(f"{hull.name}{target.name}{kind}")
    if kind == "missiles":
        line = build.anti_ship
        make = lambda name: threat_from_ammo(name, line)
        reach = max(num(ammo(m["ammo"]), "MaxLaunchRange") for m in hull.missiles) * line["Range"]
        launchers = missile_launchers(hull, "missiles", line)
        power = lambda name: num(ammo(name), "Power") * line["Damage"]
    else:
        line = build.sams
        make = lambda name: threat_from_ammo(name)
        reach = max(num(ammo(m["ammo"]), "MaxLaunchRange") for m in hull.sams) * (LONG_REACH if "Long reach" in line["abilities"] else 1.0)
        launchers = [l for l, m in zip(missile_launchers(hull, "sams", line), hull.sams) if takes_ships(m)]
        power = lambda name: num(ammo(name), "Power")
    if not launchers:
        return None
    launch_nm = min(args.missile_nm, reach)
    drain = lambda name: 2 * warhead_damage(power(name), 1.0, args.bonus)
    results = [missile_fight(launchers, hull.shared, make, launch_nm, target, target.pool, drain, rng) for _ in range(args.trials)]
    launched, stopped, hits, drained, seconds = (sum(r[i] for r in results) / args.trials for i in range(5))
    sunk = sum(1 for r in results if r[3] >= target.pool) / args.trials
    return {"weapon": "Anti-ship" if kind == "missiles" else "SAMs at ships", "mounts": sum(m["count"] for m in getattr(hull, kind)),
            "reach": reach, "dps": drained / seconds, "near": drained / seconds, "far": drained / seconds, "fight": None,
            "launched": launched, "stopped": stopped / launched if launched else 0.0, "sunk": sunk}


def takes_ships(sam):
    """A SAM can fire at ships if it has a second target type and one of its directors allows it."""
    data = ammo(sam["ammo"])
    return data.get("SecondaryTargetType") == "ASuW" and any("Surface" in d[3] for d in sam["directors"])


def report_air(lines, args):
    full = len(lines["Sams"])
    print(f"Share of a raid of {args.raid} missiles stopped by SAMs alone, launched {args.raid_gap:g} s apart from {args.raid_nm:g} nm, at rank 0, 10 and {full}.")
    print("Kill chance: one SAM against one aircraft, at the same ranks, no fire-control limits.\n")
    for name in flagships():
        hull = Hull(name)
        if not hull.sams:
            continue
        soviet_ship = name.startswith("wp_")
        rows = []
        for threat_name in RAIDS[not soviet_ship]:
            threat = threat_from_ammo(threat_name)
            cells = []
            for rank in (0, 10, full):
                t = totals(lines["Sams"], rank)
                rng = random.Random(f"{name}{threat_name}{rank}")
                stopped = launched = 0
                for _ in range(args.trials):
                    raid = [{"system": "raid", "ammo": threat_name, "left": args.raid, "interval": args.raid_gap}]
                    result = missile_fight(raid, {}, lambda n: threat_from_ammo(n), args.raid_nm, hull, math.inf, lambda n: 0.0, rng,
                                           sam_totals=t, difficulty=1 + 0.2, use_point_defence=False)
                    launched += result[0]
                    stopped += result[1]
                cells.append(f"{stopped / launched:.0%}")
            rows.append([threat_name, f"{threat['speed']:.0f}", f"{threat['feet']:.0f}"] + cells)
        for plane in AIRCRAFT[not soviet_ship]:
            data = flat(read_ini(DATA / "aircraft" / f"{plane}.ini"))
            frontal = num(data, "FrontalArea")
            size = 15000 * (frontal / 8) ** 1.5 if frontal > 0 else num(data, "EmptyMass", 10000.0)
            target = {"speed": num(data, "MaxSpeedAtSeaLevel", 450.0), "feet": 5000.0, "size": size, "evasion": 0.0}
            cells = []
            for rank in (0, 10, full):
                t = totals(lines["Sams"], rank)
                best = max(hull.sams, key=lambda m: num(ammo(m["ammo"]), "KillProbability"))
                cells.append(f"{min(1.0, intercept_chance(ammo(best['ammo']), t['KillChance'], target, 1.2, 'Sea-skimmer killers' in t['abilities'])):.0%}")
            rows.append([plane, f"{target['speed']:.0f}", "5000"] + cells)
        print(f"**{name}**\n")
        table(["Target", "Knots", "Feet", "Rank 0", "Rank 10", f"Rank {full}"], rows)


def closing_fight(dps_at, reach, pool, closing_kt, limit):
    """Follow one target in from a weapon's reach: the range it sinks at, or None and the share of its pool drained."""
    drained, r, step = 0.0, reach, 0.25
    while r > 0:
        drained += dps_at(r) * step / closing_kt * 3600
        if drained >= pool:
            return r, 1.0
        if drained >= limit:
            return None, limit / pool
        r -= step
    return None, drained / pool


def hull_numbers(hull, target, build, args):
    """Dicts per weapon: mounts, reach, DPS at the reference range and at reach, where a closing target sinks, and what sinks it."""
    rows = []
    if hull.guns:
        reach = max(gun_reach(g, build) for g in hull.guns)
        at = lambda r: sum(gun_dps(g, build, target, r, args.bonus) for g in hull.guns)
        rows.append({"weapon": "Guns", "mounts": len(hull.guns), "reach": reach, "dps": at(min(args.gun_nm, reach)),
                     "near": at(min(args.gun_nm, reach)), "far": at(reach),
                     "fight": closing_fight(at, reach, target.pool, args.closing, math.inf)})
    if hull.mortars:
        deep = "Deep magazines" in build.mortars["abilities"]
        reach = max(mortar_reach(m, build, args.radar) for m in hull.mortars)
        at = lambda r: sum(mortar_dps(m, build, target, r, args.bonus, args.radar) for m in hull.mortars)
        limit = math.inf if deep else sum(m.magazine * mortar_drain_per_hit(m, build, args.bonus) for m in hull.mortars)
        rows.append({"weapon": "Mortars", "mounts": len(hull.mortars), "reach": reach, "dps": at(min(args.mortar_nm, reach)),
                     "near": at(min(args.gun_nm, reach)), "far": at(reach),
                     "fight": closing_fight(at, reach, target.pool, args.closing, limit)})
    if hull.missiles:
        rows.append(ship_attack(hull, target, build, args, "missiles"))
    if any(takes_ships(m) for m in hull.sams):
        rows.append(ship_attack(hull, target, build, args, "sams"))
    return rows


def flagships():
    names = []
    for path in sorted((REPO / "mod" / "missions" / "Survivors").glob("Survivors - *.ini")):
        names.append(read_ini(path)["Taskforce1Vessel1"]["Type"])
    return names


def targets_for(hull):
    return NATO_TARGETS if hull.startswith("wp_") else SOVIET_TARGETS


# ---------------------------------------------------------------- reports

def table(headers, rows):
    print("| " + " | ".join(headers) + " |")
    print("|" + "|".join("---" for _ in headers) + "|")
    for row in rows:
        print("| " + " | ".join(row) + " |")
    print()


def fmt(x):
    if x is None:
        return "-"
    if math.isinf(x):
        return "no limit"
    return f"{x:.2f}" if x < 10 else f"{x:.0f}"


def fight_text(row, pool):
    if row["fight"] is None:
        return f"{row['launched']:.1f} fired, {row['stopped']:.0%} stopped, sunk {row['sunk']:.0%}"
    nm, share = row["fight"]
    return f"at {nm:.0f} nm" if nm is not None else f"survives, {share:.0%} of pool"


def report_summary(lines, args):
    full = {k: len(v) for k, v in lines.items()}
    stock = Build(lines)
    no_acc = Build(lines, full["Guns"], full["Mortars"], 0, full["AntiShip"], full["Sams"])
    both = Build(lines, full["Guns"], full["Mortars"], full["Accuracy"], full["AntiShip"], full["Sams"])
    print(f"Pool drain a second, damage bonus +{args.bonus:.0%}: guns at {args.gun_nm:g} nm, mortars at {args.mortar_nm:g} nm or their reach.")
    print(f"Sinks: full lines and Accuracy, a target closing from reach at {args.closing:g} kt, reloads and flight smoothed out.")
    print(f"Missiles: simulated against the target's SAMs, CIWS and chaff, fired from {args.missile_nm:g} nm or their reach, {args.trials} trials; DPS is drain over the whole attack, until sunk or the magazine is out.\n")
    for name in flagships():
        hull = Hull(name)
        rows = []
        for target_name in targets_for(name):
            target = Hull(target_name)
            for base, mid, row in zip(hull_numbers(hull, target, stock, args), hull_numbers(hull, target, no_acc, args),
                                      hull_numbers(hull, target, both, args)):
                seconds = target.pool / row["dps"] if row["dps"] else None
                rows.append([target_name, f"{target.pool:.0f}", row["weapon"], str(row["mounts"]), f"{row['reach']:.0f}",
                             fmt(base["dps"]), fmt(mid["dps"]), fmt(row["dps"]), fmt(seconds), fight_text(row, target.pool)])
        print(f"**{name}**\n")
        table(["Target", "Pool", "Weapon", "Mounts", "Reach nm", "DPS stock", "DPS full line", "DPS + Accuracy", "Seconds to sink", "Sinks"], rows)


def report_cards(lines, args):
    hull, target = Hull(args.hull), Hull(args.target)
    full = {k: len(v) for k, v in lines.items()}
    print(f"{args.hull} against {args.target} (pool {target.pool:.0f}), Accuracy rank {args.accuracy} unless swept.")
    print(f"Gain is the DPS at {args.gun_nm:g} nm (or reach) over the rank before. Sinks follows a target closing at {args.closing:g} kt.\n")

    def sweep(title, key, make, path):
        rows, previous = [], None
        for rank in range(len(path) + 1):
            found = [r for r in hull_numbers(hull, target, make(rank), args) if r["weapon"] == key]
            if not found:
                return
            row = found[0]
            gain = "" if not previous else f"x{row['near'] / previous:.2f}"
            rows.append([str(rank), describe(path[rank - 1]) if rank else "stock", f"{row['reach']:.0f}", fmt(row["near"]), fmt(row["far"]), gain, fight_text(row, target.pool)])
            previous = row["near"]
        print(f"**{title}**\n")
        table(["Rank", "Card", "Reach nm", f"DPS at {args.gun_nm:g} nm", "DPS at reach", "Gain", "Sinks"], rows)

    sweep("Guns", "Guns", lambda r: Build(lines, guns=r, accuracy=args.accuracy), lines["Guns"])
    sweep("Rocket mortars", "Mortars", lambda r: Build(lines, mortars=r, accuracy=args.accuracy), lines["Mortars"])
    sweep("Anti-ship missiles", "Anti-ship", lambda r: Build(lines, anti_ship=r), lines["AntiShip"])
    sweep("SAMs at ships", "SAMs at ships", lambda r: Build(lines, sams=r), lines["Sams"])
    sweep("Accuracy, with full Guns", "Guns", lambda r: Build(lines, guns=full["Guns"], accuracy=r), lines["Accuracy"])
    sweep("Accuracy, with full Rocket mortars", "Mortars", lambda r: Build(lines, mortars=full["Mortars"], accuracy=r), lines["Accuracy"])


def band_middle(band):
    if band.endswith("+"):
        return float(band[:-1]) * 1.2
    low, high = band.split("-")
    return (float(low) + float(high)) / 2


def newest_log():
    base = Path(os.environ.get("USERPROFILE", "~")).expanduser() / "AppData" / "LocalLow"
    found = glob.glob(str(base / "*" / "*" / "SeapowerSurvivors" / "damage" / "* hits.csv"))
    return max(found, key=os.path.getmtime) if found else None


def report_compare(lines, args):
    path = args.path or newest_log()
    if not path:
        sys.exit("No damage log found. Pass its path: python tools/balance.py compare \"... hits.csv\"")
    print(f"{path}\nHit % and drain per hit: measured against the model. Rows under {args.min_shots} shots are left out of hit %.\n")
    rows = []
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row["line"] not in ("Guns", "Mortars", "Anti-ship"):
                continue
            shots, hits = int(row["shots"]), int(row["hits"])
            if not hits and not shots:
                continue
            try:
                target = Hull(row["target"])
            except OSError:
                continue
            rank, acc = int(row["line_rank"]), int(row["accuracy_rank"])
            nm = band_middle(row["range_nm"])
            data = ammo(row["ammo"])
            if row["line"] == "Guns":
                build = Build(lines, guns=rank, accuracy=acc)
                gun = Gun("", row["ammo"], {}, 1)
                drain, hit = gun_drain_per_hit(gun, build, target, args.bonus), shell_hit(target, nm, build.accuracy["Accuracy"] * build.guns["GunAim"], gun.cep, gun_reach(gun, build))
            elif row["line"] == "Mortars":
                build = Build(lines, mortars=rank, accuracy=acc)
                mortar = Mortar("", (row["ammo"], 0), {})
                drain, hit = mortar_drain_per_hit(mortar, build, args.bonus), round_hit(target, nm, build.accuracy["Accuracy"])
            else:
                build = Build(lines, anti_ship=rank)
                drain, hit = 2 * warhead_damage(num(data, "Power"), 1.0, args.bonus) * build.anti_ship["Damage"], None
            measured_hit = f"{hits / shots:.0%}" if shots >= args.min_shots else "-"
            rows.append([row["line"], row["ammo"], row["target"], str(rank), str(acc), row["range_nm"], str(shots), str(hits),
                         measured_hit, f"{hit:.0%}" if hit is not None else "-", fmt(float(row["pool_drain"]) / max(1, hits)), fmt(drain),
                         fmt(float(row["structure"]) / max(1, hits)), row["kills"]])
    table(["Line", "Round", "Target", "Rank", "Acc", "Range nm", "Shots", "Hits", "Hit %", "Model", "Drain/hit", "Model", "Struct/hit", "Kills"], rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bonus", type=float, default=0.25, help="the difficulty's damage bonus; the game's default is 0.25")
    parser.add_argument("--gun-nm", type=float, default=10.0, help="range the DPS columns take for guns")
    parser.add_argument("--mortar-nm", type=float, default=60.0, help="range the DPS columns take for mortars")
    parser.add_argument("--closing", type=float, default=25.0, help="target's closing speed in knots")
    parser.add_argument("--radar", type=float, default=20.0, help="how far ships are on the plot, until Spotters")
    parser.add_argument("--missile-nm", type=float, default=20.0, help="range missiles are fired at ships from, or their reach")
    parser.add_argument("--trials", type=int, default=60, help="simulated attacks per number")
    parser.add_argument("--raid", type=int, default=8, help="missiles in a raid, for the air report")
    parser.add_argument("--raid-gap", type=float, default=5.0, help="seconds between a raid's missiles")
    parser.add_argument("--raid-nm", type=float, default=60.0, help="range a raid is launched from")
    parser.add_argument("--min-shots", type=int, default=20)
    sub = parser.add_subparsers(dest="mode")
    cards = sub.add_parser("cards", help="what each rank of each line adds")
    cards.add_argument("hull")
    cards.add_argument("target")
    cards.add_argument("--accuracy", type=int, default=0, help="Accuracy rank held while sweeping Guns and mortars")
    sub.add_parser("air", help="SAMs against missile raids and aircraft")
    compare = sub.add_parser("compare", help="a damage log against the model")
    compare.add_argument("path", nargs="?")
    args = parser.parse_args()

    if not DATA.exists():
        sys.exit(f"Game data not found at {DATA}. Set SEAPOWER_DIR to the install folder.")
    lines = parse_lines()
    if args.mode == "cards":
        report_cards(lines, args)
    elif args.mode == "compare":
        report_compare(lines, args)
    elif args.mode == "air":
        report_air(lines, args)
    else:
        report_summary(lines, args)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
