using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>The best run in an area: its score, and the fleet it started with.</summary>
sealed class HighScore
{
    public int Score;
    /// <summary>The starting flagship's type.</summary>
    public string Flagship = "";
    /// <summary>The starting escorts and flights, as shown: "Gearing FRAM II destroyer ×5".</summary>
    public List<string> Escorts = new List<string>();
    public List<string> Ensigns = new List<string>();
    /// <summary>The difficulty it was set at, by its place in Difficulty.All. Its factor is already in the score.</summary>
    public int Difficulty;

    const char ListSeparator = ';';

    internal string Format(string areaId) =>
        string.Join("|", areaId, Score, Flagship, string.Join(ListSeparator.ToString(), Escorts.Select(Escape)), string.Join(ListSeparator.ToString(), Ensigns.Select(Escape)), Difficulty);

    /// <summary>Reads one saved entry. False if it is not whole, so one bad entry cannot lose the profile.</summary>
    internal static bool Parse(string entry, out string areaId, out HighScore score)
    {
        var parts = entry.Split('|');
        areaId = null;
        score = null;
        // A score saved before there were difficulties has no sixth part: it was set at the first.
        if (parts.Length < 5 || parts.Length > 6 || !int.TryParse(parts[1], out int points)) return false;
        int difficulty = 0;
        if (parts.Length == 6 && !int.TryParse(parts[5], out difficulty)) return false;
        areaId = parts[0].Trim();
        score = new HighScore { Score = points, Flagship = parts[2], Escorts = Names(parts[3]), Ensigns = Names(parts[4]), Difficulty = difficulty };
        return true;
    }

    static List<string> Names(string list) => list.Split(new[] { ListSeparator }, StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();

    /// <summary>
    /// A name with the characters the profile splits on written as %2C and the
    /// like. "Leander frigate, Exocet" once went in as it was: its comma cut the
    /// entry in two on the next load, and the area lost its high score.
    /// </summary>
    static string Escape(string name) =>
        name.Replace("%", "%25").Replace(",", "%2C").Replace("|", "%7C").Replace(";", "%3B").Replace("=", "%3D");
}

/// <summary>
/// What is kept between runs, after Vampire Survivors' gold: every run's XP
/// is banked when it ends, and the bank is spent in the store on what a run
/// can start with.
/// </summary>
/// <remarks>
/// A plain <c>key=value</c> file in the game's persistent data folder, beside
/// the launcher's mission file, so it survives reinstalling the mod. It is
/// written whole on every change, through a temporary file, so a crash
/// mid-write leaves the old profile rather than half of one.
/// </remarks>
sealed class Profile
{
    static Profile current;
    internal static Profile Current => current ??= Load();

    /// <summary>XP banked and not yet spent.</summary>
    internal int Bank { get; private set; }
    /// <summary>All the XP ever banked, spent or not.</summary>
    internal int Earned { get; private set; }
    internal int Runs { get; private set; }
    readonly HashSet<string> owned = new HashSet<string>();
    /// <summary>Flagship types a run has been won in: what unlocks the flagships no one can buy.</summary>
    readonly HashSet<string> wins = new HashSet<string>();
    /// <summary>Permanent-upgrade ranks owned, by id.</summary>
    readonly Dictionary<string, int> ranks = new Dictionary<string, int>();
    /// <summary>XP spent on permanent upgrades, which a refund returns.</summary>
    int spentOnRanks;
    internal int Refundable => spentOnRanks;

    /// <summary>The area and flagship last chosen, by id and type, so the Survivors screen opens as it was left. Empty for the defaults.</summary>
    internal string AreaId { get; private set; } = "";
    internal string FlagshipType { get; private set; } = "";
    /// <summary>The difficulty last chosen, by its place in Difficulty.All.</summary>
    internal int Difficulty { get; private set; }

    internal void SetDifficulty(int index)
    {
        if (Difficulty == index) return;
        Difficulty = index;
        Save();
    }
    /// <summary>Where the player has placed starting ships, by store id: nautical miles to starboard of the flagship and ahead of it.</summary>
    readonly Dictionary<string, (float right, float ahead)> stations = new Dictionary<string, (float, float)>();
    /// <summary>Stations swing with the flagship rather than hold their compass bearing from it.</summary>
    internal bool StationsTurn { get; private set; }
    /// <summary>Starting ships and flights owned but left ashore, by store id. Everything else owned sails.</summary>
    readonly HashSet<string> ashore = new HashSet<string>();
    /// <summary>The ensigns the next run flies, by id, in the order they were hoisted.</summary>
    readonly List<string> hoisted = new List<string>();

    static string FilePath => Path.Combine(Application.persistentDataPath, "SeapowerSurvivors", "profile.txt");

    internal bool Owns(string id) => Store.IsFree(id) || owned.Contains(id) || Store.IsWon(this, id);

    internal bool HasWon(string flagshipType) => wins.Contains(flagshipType);

    /// <summary>
    /// A run won in this flagship. Only an owned one counts, so a
    /// <c>Survivors - </c> mission in a locked ship cannot skip the ladder.
    /// Returns the flagships the win unlocks.
    /// </summary>
    internal List<StartingShip> RecordWin(string flagshipType)
    {
        var flagship = Launcher.Flagships.FirstOrDefault(s => s.Type == flagshipType);
        if (flagship == null || !Owns(Store.FlagshipId(flagship)) || HasWon(flagshipType)) return new List<StartingShip>();
        var before = Launcher.Flagships.Where(s => Owns(Store.FlagshipId(s))).ToList();
        wins.Add(flagshipType);
        Save();
        return Launcher.Flagships.Where(s => Owns(Store.FlagshipId(s))).Except(before).ToList();
    }

    /// <summary>Each area's best run, by area id.</summary>
    readonly Dictionary<string, HighScore> scores = new Dictionary<string, HighScore>();

    internal HighScore BestScore(string areaId) => scores.TryGetValue(areaId, out var best) ? best : null;

    /// <summary>Keep this run if it is the area's best. Returns true if it is.</summary>
    internal bool RecordScore(string areaId, HighScore score)
    {
        if (score.Score <= 0 || (scores.TryGetValue(areaId, out var best) && best.Score >= score.Score)) return false;
        scores[areaId] = score;
        Save();
        return true;
    }

    internal bool IsAshore(string id) => ashore.Contains(id);

    /// <summary>The escort picked to sail under the Greek ensign, apart from who sails otherwise. Empty if none has been picked; <see cref="NoEscort"/> to wait for a chest.</summary>
    internal string LoneEscort { get; private set; } = "";
    internal const string NoEscort = "none";

    internal void SetLoneEscort(string id)
    {
        if (LoneEscort == id) return;
        LoneEscort = id;
        Save();
    }

    internal void Choose(Area area, StartingShip flagship)
    {
        if (AreaId == area.Id && FlagshipType == flagship.Type) return;
        AreaId = area.Id;
        FlagshipType = flagship.Type;
        Save();
    }

    internal bool TryGetStation(string key, out float right, out float ahead)
    {
        bool placed = stations.TryGetValue(key, out var station);
        right = station.right;
        ahead = station.ahead;
        return placed;
    }

    internal void SetStation(string key, float right, float ahead)
    {
        stations[key] = (right, ahead);
        Save();
    }

    internal void ClearStations()
    {
        stations.Clear();
        Save();
    }

    internal void SetStationsTurn(bool turn)
    {
        StationsTurn = turn;
        Save();
    }

    internal void SetAshore(string id, bool leaveAshore)
    {
        if (leaveAshore ? ashore.Add(id) : ashore.Remove(id)) Save();
    }

    /// <summary>The ensigns the next run flies: only ones still owned, that suit the flagship and each other, in the order hoisted while the budget lasts.</summary>
    internal List<Ensign> HoistedEnsigns()
    {
        var flagship = Setup.Flagship(this).Type;
        var flying = new List<Ensign>();
        int points = 0;
        foreach (var ensign in hoisted.Select(Ensigns.ById))
        {
            if (ensign == null || !Owns(Store.EnsignId(ensign)) || !Ensigns.Fits(ensign, flagship) || Ensigns.ClashWith(ensign, flying) != null || points + ensign.Points > Ensigns.Budget) continue;
            flying.Add(ensign);
            points += ensign.Points;
        }
        return flying;
    }

    internal void Hoist(Ensign ensign)
    {
        if (hoisted.Contains(ensign.Id)) return;
        hoisted.Add(ensign.Id);
        Save();
    }

    internal void Strike(Ensign ensign)
    {
        if (hoisted.Remove(ensign.Id)) Save();
    }

    /// <summary>Bank one run's XP.</summary>
    internal void Deposit(int xp)
    {
        Bank += xp;
        Earned += xp;
        Runs++;
        Save();
    }

    internal int RankOf(PowerUp powerUp) => ranks.TryGetValue(powerUp.Id, out int rank) ? rank : 0;

    internal bool CanBuyRank(PowerUp powerUp) => RankOf(powerUp) < powerUp.MaxRank && Bank >= Store.Price(this, powerUp);

    internal bool TryBuyRank(PowerUp powerUp)
    {
        if (!CanBuyRank(powerUp)) return false;
        int price = Store.Price(this, powerUp);
        Bank -= price;
        spentOnRanks += price;
        ranks[powerUp.Id] = RankOf(powerUp) + 1;
        Save();
        return true;
    }

    /// <summary>Every XP spent on permanent upgrades back in the bank, and their ranks cleared. Ships and flights stay.</summary>
    internal int Refund()
    {
        int refund = spentOnRanks;
        Bank += refund;
        spentOnRanks = 0;
        ranks.Clear();
        Save();
        return refund;
    }

    internal bool CanBuy(StoreItem item) => item.WonBy == null && !Owns(item.Id) && Bank >= item.Price;

    internal bool TryBuy(StoreItem item)
    {
        if (!CanBuy(item)) return false;
        Bank -= item.Price;
        owned.Add(item.Id);
        Save();
        return true;
    }

    /// <summary>Test aid: XP from nowhere, without counting a run.</summary>
    internal void Grant(int xp)
    {
        Bank += xp;
        Save();
    }

    /// <summary>Test aid: back to a fresh profile.</summary>
    internal void Wipe()
    {
        Bank = Earned = Runs = spentOnRanks = 0;
        owned.Clear();
        wins.Clear();
        ranks.Clear();
        ashore.Clear();
        hoisted.Clear();
        stations.Clear();
        scores.Clear();
        StationsTurn = false;
        Difficulty = 0;
        AreaId = FlagshipType = "";
        Save();
    }

    static Profile Load()
    {
        var profile = new Profile();
        try
        {
            if (!File.Exists(FilePath)) return profile;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "Bank": int.TryParse(value, out int bank); profile.Bank = bank; break;
                    case "Earned": int.TryParse(value, out int earned); profile.Earned = earned; break;
                    case "Runs": int.TryParse(value, out int runs); profile.Runs = runs; break;
                    case "SpentOnRanks": int.TryParse(value, out int spent); profile.spentOnRanks = spent; break;
                    case "Ranks":
                        foreach (var pair in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = pair.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int rank)) profile.ranks[parts[0].Trim()] = rank;
                        }
                        break;
                    case "Area": profile.AreaId = value; break;
                    case "Flagship": profile.FlagshipType = value; break;
                    case "Difficulty": int.TryParse(value, out int difficulty); profile.Difficulty = SeapowerSurvivors.Difficulty.At(difficulty).Index; break;
                    case "StationsTurn": profile.StationsTurn = value == "True"; break;
                    case "Stations":
                        foreach (var entry in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = entry.Split('|');
                            if (parts.Length == 3 &&
                                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float right) &&
                                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ahead))
                                profile.stations[parts[0].Trim()] = (right, ahead);
                        }
                        break;
                    case "LoneEscort": profile.LoneEscort = value.Trim(); break;
                    case "Ashore":
                        foreach (var id in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            profile.ashore.Add(id.Trim());
                        break;
                    case "Hoisted":
                        foreach (var id in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            profile.hoisted.Add(id.Trim());
                        break;
                    case "Owned":
                        foreach (var id in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            profile.owned.Add(id.Trim());
                        break;
                    case "Wins":
                        foreach (var type in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            profile.wins.Add(type.Trim());
                        break;
                    case "Scores":
                        foreach (var entry in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            if (HighScore.Parse(entry, out string area, out var score)) profile.scores[area] = score;
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Entry.Log.LogError($"Could not read the profile at {FilePath}, starting a fresh one: {e.Message}");
        }
        return profile;
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllLines(temp, new[]
            {
                "; Sea Power Survivors: XP banked between runs and what it has bought.",
                $"Bank={Bank}",
                $"Earned={Earned}",
                $"Runs={Runs}",
                $"Owned={string.Join(",", owned.OrderBy(id => id))}",
                $"Wins={string.Join(",", wins.OrderBy(type => type))}",
                $"Scores={string.Join(",", scores.OrderBy(s => s.Key).Select(s => s.Value.Format(s.Key)))}",
                $"Ranks={string.Join(",", ranks.OrderBy(r => r.Key).Select(r => $"{r.Key}:{r.Value}"))}",
                $"SpentOnRanks={spentOnRanks}",
                $"Area={AreaId}",
                $"Flagship={FlagshipType}",
                $"Difficulty={Difficulty}",
                $"StationsTurn={StationsTurn}",
                $"Stations={string.Join(",", stations.OrderBy(s => s.Key).Select(s => string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.0}|{2:0.0}", s.Key, s.Value.right, s.Value.ahead)))}",
                $"Ashore={string.Join(",", ashore.OrderBy(id => id))}",
                $"LoneEscort={LoneEscort}",
                $"Hoisted={string.Join(",", hoisted)}",
            });
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
        }
        catch (Exception e)
        {
            Entry.Log.LogError($"Could not save the profile to {FilePath}: {e.Message}");
        }
    }
}
