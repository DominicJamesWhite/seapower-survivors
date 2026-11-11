using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Noesis;
using SeaPower;
using SeapowerUI;
using SeapowerUI.ViewModels;

namespace SeapowerSurvivors;

/// <summary>
/// The Survivors screen, shown in the game's menu in place of the main menu,
/// the way the Dynamic Campaign Mod shows its start screen: set as the menu's
/// current window, which remembers the main menu as the window to go back to.
/// Every choice goes straight into the profile, and the lists are rebuilt
/// after each one.
/// </summary>
public sealed class SurvivorsScreen : Observable, IMenuSystemPage
{
    public object PreviousWindow { get; set; }
    public LanguageResourceHandler LanguageResourceHandler => Singleton<LanguageResourceHandler>.Instance;

    public string Bank { get; private set; }
    public string Earned { get; private set; }
    public List<AreaCard> Areas { get; private set; }
    public string AreaName { get; private set; }
    public string AreaBlurb { get; private set; }
    public string AreaRecord { get; private set; }
    /// <summary>A flag for each navy; the flagships and escorts listed are the chosen one's.</summary>
    public List<NavyTab> Navies { get; private set; }
    public List<ShipRow> Flagships { get; private set; }
    public List<ShipRow> Escorts { get; private set; }
    public string FleetSummary { get; private set; }
    public FormationEditor Formation { get; }
    public List<BonusRow> Bonuses { get; private set; }
    public string RefundText { get; private set; }
    public string Message { get; private set; } = "";
    public string LaunchText => Launcher.Launching ? "LOADING..." : "LAUNCH";
    /// <summary>The three slots on the main page: a flag, or an empty frame.</summary>
    public List<EnsignSlot> EnsignSlots { get; private set; }
    public string EnsignSummary { get; private set; }
    public string EnsignBudget { get; private set; }
    public List<bool> EnsignPips { get; private set; }
    public List<EnsignCard> EnsignCards { get; private set; }
    public Visibility MainPage => choosingEnsigns ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EnsignPage => choosingEnsigns ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>The Formation Editor, a window over the screen as it is in the game's Task Force mode.</summary>
    public Visibility FormationWindow => placingShips ? Visibility.Visible : Visibility.Collapsed;
    public string BackText => choosingEnsigns ? "DONE" : "BACK";

    public ICommand BackCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand RefundCommand { get; }
    public ICommand EnsignsCommand { get; }
    public ICommand FormationCommand { get; }
    public ICommand CloseFormationCommand { get; }

    bool choosingEnsigns;
    bool placingShips;
    /// <summary>The flagship last sailed in each navy while the game has been running, by navy id, so coming back to a navy comes back to her.</summary>
    static readonly Dictionary<string, string> lastFlagship = new Dictionary<string, string>();
    FormationPlot plot;

    static Profile Profile => Profile.Current;

    SurvivorsScreen()
    {
        Formation = new FormationEditor(Refresh);
        EnsignsCommand = new DelegateCommand(_ => ShowEnsigns(true));
        FormationCommand = new DelegateCommand(_ => ShowFormation(true));
        CloseFormationCommand = new DelegateCommand(_ => ShowFormation(false));
        BackCommand = new DelegateCommand(_ =>
        {
            if (choosingEnsigns)
            {
                ShowEnsigns(false);
                return;
            }
            if (PreviousWindow != null) Singleton<MenuSystemViewModel>.Instance.CurrentWindow = PreviousWindow;
        });
        LaunchCommand = new DelegateCommand(_ => !Launcher.Launching, _ => Launch());
        RefundCommand = new DelegateCommand(_ => Profile.Refundable > 0, _ =>
        {
            Profile.Refund();
            Refresh();
        });
        Refresh();
    }

    internal static void Show()
    {
        var view = Views.Load("SurvivorsScreen.xaml");
        var screen = new SurvivorsScreen();
        screen.plot = new FormationPlot((Canvas)view.FindName("StationPlot"), screen.Formation);
        Singleton<MenuSystemViewModel>.Instance.CurrentWindow = new UserControl
        {
            Content = view,
            DataContext = screen,
        };
    }

    void Launch()
    {
        try
        {
            Launcher.Launch(Setup.Loadout(Profile));
            Changed(nameof(LaunchText));
            ((DelegateCommand)LaunchCommand).RaiseCanExecuteChanged();
        }
        catch (Exception e)
        {
            Entry.Log.LogError($"Launch failed: {e}");
            Say($"Launch failed: {e.Message}");
        }
    }

    void ShowEnsigns(bool show)
    {
        choosingEnsigns = show;
        Changed(nameof(MainPage));
        Changed(nameof(EnsignPage));
        Changed(nameof(BackText));
    }

    void ShowFormation(bool show)
    {
        placingShips = show;
        Changed(nameof(FormationWindow));
    }

    /// <summary>Click an ensign: unlock it if it can be, hoist it if there is room, strike it if it is flying.</summary>
    void ClickEnsign(Profile profile, Ensign ensign)
    {
        var hoisted = profile.HoistedEnsigns();
        if (hoisted.Contains(ensign))
            profile.Strike(ensign);
        else if (profile.Owns(Store.EnsignId(ensign)) || profile.TryBuy(Store.EnsignItem(ensign)))
        {
            // The card already says why one that cannot fly is not hoisted.
            if (CanFly(profile, ensign, hoisted)) profile.Hoist(ensign);
        }
        Refresh();
    }

    static bool CanFly(Profile profile, Ensign ensign, List<Ensign> hoisted) =>
        Ensigns.ClashWith(ensign, hoisted) == null && Ensigns.Fits(ensign, Setup.Flagship(profile).Type) &&
        Ensigns.PointsFlown(hoisted) + ensign.Points <= Ensigns.Budget;

    void Say(string message)
    {
        Message = message;
        Changed(nameof(Message));
    }

    void Refresh()
    {
        var profile = Profile;
        var area = Setup.Area(profile);
        var flagship = Setup.Flagship(profile);

        Bank = $"{profile.Bank:N0} XP to spend";
        Earned = $"{profile.Earned:N0} XP earned over {profile.Runs} run{(profile.Runs == 1 ? "" : "s")}";

        Areas = SeapowerSurvivors.Areas.All.Select(a => new AreaCard(a, a == area, profile.BestScore(a.Id), () => Choose(a, flagship))).ToList();
        AreaName = area.Name;
        AreaBlurb = area.Blurb;
        AreaRecord = Record(profile.BestScore(area.Id));

        var navy = SeapowerSurvivors.Navies.Of(flagship);
        lastFlagship[navy.Id] = flagship.Type;
        Navies = SeapowerSurvivors.Navies.All.Select(n => new NavyTab(n, n == navy, () => ChooseNavy(n))).ToList();
        Flagships = navy.Flagships.Select(ship => FlagshipRow(profile, ship, ship == flagship, area)).ToList();
        Escorts = Store.StartingEscorts(flagship).Select(escort => EscortRow(profile, escort)).ToList();
        var hoisted = profile.HoistedEnsigns();
        FleetSummary = Summary(profile, flagship, hoisted);

        Formation.Rebuild();

        Bonuses = Store.PowerUps.Select(powerUp => new BonusRow(profile, powerUp, () =>
        {
            profile.TryBuyRank(powerUp);
            Refresh();
        })).ToList();
        EnsignSlots = hoisted.Select(e => new EnsignSlot(e)).ToList();
        int spent = Ensigns.PointsFlown(hoisted);
        // The wording of the game's own Task Force builder.
        EnsignBudget = $"{Ensigns.Budget - spent} available of {Ensigns.Budget} pts";
        EnsignPips = Enumerable.Range(0, Ensigns.Budget).Select(i => i < spent).ToList();
        EnsignSummary = hoisted.Count == 0 ? "None flying" : string.Join(", ", hoisted.Select(e => e.Name));
        EnsignCards = Ensigns.All.Select(e => new EnsignCard(profile, e, hoisted, () => ClickEnsign(profile, e))).ToList();

        RefundText = profile.Refundable > 0 ? $"Refund all bonuses: {profile.Refundable:N0} XP back" : "Nothing to refund";
        ((DelegateCommand)RefundCommand).RaiseCanExecuteChanged();

        foreach (var name in new[]
                 {
                     nameof(Bank), nameof(Earned), nameof(Areas), nameof(AreaName), nameof(AreaBlurb), nameof(AreaRecord),
                     nameof(Navies), nameof(Flagships), nameof(Escorts), nameof(FleetSummary), nameof(Bonuses), nameof(RefundText),
                     nameof(EnsignSlots), nameof(EnsignSummary), nameof(EnsignBudget), nameof(EnsignPips), nameof(EnsignCards),
                 })
            Changed(name);
    }

    /// <summary>Under the chosen area's briefing: the fleet that set its high score. Nothing if it has none.</summary>
    static string Record(HighScore best)
    {
        if (best == null) return "";
        string ship = Launcher.Flagships.FirstOrDefault(s => s.Type == best.Flagship)?.Name ?? best.Flagship;
        static string Listed(List<string> names) => names.Count == 0 ? "None" : string.Join(", ", names);
        return $"High score fleet\nFlagship: {ship}\nEscorts: {Listed(best.Escorts)}\nEnsigns: {Listed(best.Ensigns)}";
    }

    /// <summary>The line under the escort list: what sails, in the numbers it will sail in.</summary>
    static string Summary(Profile profile, StartingShip flagship, List<Ensign> hoisted)
    {
        var sailing = Setup.Sailing(profile);
        if (sailing.Count > 0 && Setup.OneEscortOnly(profile)) return $"{flagship.Name} sails with {Ensigns.EscortCopies} × {sailing[0].Name}.";
        string Named(Escort e) => e.Name + Ensigns.Times(Ensigns.AircraftFactor(hoisted, e));
        var names = sailing.Select(Named).ToList();
        if (EnsignCombat.FreeStrikeFlight(hoisted, sailing, flagship) is Escort free) names.Add($"{Named(free)} (free)");
        return names.Count == 0 ? $"{flagship.Name} sails alone." : $"{flagship.Name} sails with {string.Join(", ", names)}.";
    }

    void Choose(Area area, StartingShip flagship)
    {
        Profile.Choose(area, flagship);
        Refresh();
    }

    /// <summary>Change navy: to its flagship last sailed, or else the first of its flagships owned. Every navy has a free one.</summary>
    void ChooseNavy(Navy navy)
    {
        var profile = Profile;
        var owned = navy.Flagships.Where(s => profile.Owns(Store.FlagshipId(s))).ToList();
        if (owned.Count == 0) return;
        lastFlagship.TryGetValue(navy.Id, out string last);
        Choose(Setup.Area(profile), owned.FirstOrDefault(s => s.Type == last) ?? owned[0]);
    }

    ShipRow FlagshipRow(Profile profile, StartingShip ship, bool chosen, Area area)
    {
        var item = Store.Flagship(ship);
        string title = $"{ship.Name} ({ship.Year})";
        if (profile.Owns(item.Id))
            return new ShipRow(title, ship.Weapons, chosen ? "Flagship" : "Owned", chosen, false, () => Choose(area, ship), canClick: true);
        if (item.WonBy != null)
            return new ShipRow(title, $"{ship.Weapons}\n{item.WonBy} to unlock", "Locked", false, false, () => { }, canClick: false);
        return new ShipRow(title, ship.Weapons, $"{item.Price:N0} XP", false, true, () =>
            {
                if (profile.TryBuy(item)) profile.Choose(area, ship);
                Refresh();
            }, canClick: profile.CanBuy(item));
    }

    ShipRow EscortRow(Profile profile, Escort escort)
    {
        var item = Store.EscortItem(escort);
        string title = $"{escort.Name} ({escort.Year}){Ensigns.Times(Ensigns.AircraftFactor(profile.HoistedEnsigns(), escort))}";
        if (profile.Owns(item.Id))
        {
            bool sails = Setup.Sailing(profile).Contains(escort);
            string name = sails && Setup.OneEscortOnly(profile) ? $"{title} ×{Ensigns.EscortCopies}" : title;
            return new ShipRow(name, escort.Weapons, sails ? "Active" : "Not selected", sails, false, () =>
            {
                Setup.SetSails(profile, escort, !sails);
                Refresh();
            }, canClick: true);
        }
        return new ShipRow(title, escort.Weapons, $"{item.Price:N0} XP", false, true, () =>
        {
            if (profile.TryBuy(item)) Setup.SetSails(profile, escort, true);
            Refresh();
        }, canClick: profile.CanBuy(item));
    }

}

/// <summary>An area to fight in: Quick Start's painting if it can be found, its name, and whether it is chosen.</summary>
public sealed class AreaCard
{
    static readonly Dictionary<string, ImageSource> ArtCache = new Dictionary<string, ImageSource>();

    public string Name { get; }
    public string Region { get; }
    public ImageSource Art { get; }
    public bool Selected { get; }
    /// <summary>The area's high score, in the wording of the game's own Scenarios menu.</summary>
    public string Score { get; }
    public ICommand Command { get; }

    internal AreaCard(Area area, bool chosen, HighScore best, Action choose)
    {
        Name = area.Name;
        Region = area.Region;
        Art = LoadArt(area);
        Selected = chosen;
        Score = best != null ? $"High Score: {best.Score:N0}" : "High Score: none yet";
        Command = new DelegateCommand(_ => choose());
    }

    static ImageSource LoadArt(Area area)
    {
        if (ArtCache.TryGetValue(area.Id, out var art)) return art;
        try
        {
            if (Singleton<ResourceLoader>.InstanceExists(false))
                art = Singleton<ResourceLoader>.Instance.getNoesisTextureSourceResource(area.Art, suppressError: true);
            art ??= LoadLocalArt(area);
        }
        catch (Exception e)
        {
            Entry.Log.LogWarning($"No art for {area.Name}: {e.Message}");
        }
        return ArtCache[area.Id] = art;
    }

    /// <summary>
    /// The mod's own chart of the area, for when Quick Start's painting is not available.
    /// </summary>
    static ImageSource LoadLocalArt(Area area)
    {
        string path = System.IO.Path.Combine(Area.LocalArtFolder, area.Id + ".png");
        if (!System.IO.File.Exists(path)) return null;
        var texture = new UnityEngine.Texture2D(2, 2) { name = path };
        return UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path), markNonReadable: true)
            ? new TextureSource(texture)
            : null;
    }
}

/// <summary>A navy to sail for: its flag, and whether it is the one chosen.</summary>
public sealed class NavyTab
{
    public ImageSource Flag { get; }
    public string Name { get; }
    public bool Selected { get; }
    public double FlagOpacity { get; }
    public ICommand Command { get; }

    internal NavyTab(Navy navy, bool chosen, Action choose)
    {
        Flag = EnsignFlags.Source(navy);
        Name = navy.Name;
        Selected = chosen;
        FlagOpacity = chosen ? 1.0 : 0.6;
        Command = new DelegateCommand(_ => choose());
    }
}

/// <summary>A flagship or a starting ship or flight: chosen or sailing, owned, or for sale.</summary>
public sealed class ShipRow
{
    public string Name { get; }
    public string Detail { get; }
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public string State { get; }
    /// <summary>The flagship, or a ship that sails: a selected row in the game's lists.</summary>
    public bool Selected { get; }
    /// <summary>The state is a price.</summary>
    public bool Emphasis { get; }
    public ICommand Command { get; }

    internal ShipRow(string name, string detail, string state, bool selected, bool forSale, Action click, bool canClick)
    {
        Name = name;
        Detail = detail;
        State = state;
        Selected = selected;
        Emphasis = forSale;
        Command = new DelegateCommand(_ => canClick, _ => click());
    }
}

/// <summary>One of the main page's ensign slots: a flag, or empty.</summary>
public sealed class EnsignSlot
{
    public ImageSource Flag { get; }
    public string Name { get; }

    internal EnsignSlot(Ensign ensign)
    {
        Flag = ensign != null ? EnsignFlags.Source(ensign) : null;
        Name = ensign?.Name ?? "";
    }
}

/// <summary>An ensign on the choosing page: its flag, what it does and takes from the budget, and whether it is flying, owned or for sale.</summary>
public sealed class EnsignCard
{
    public ImageSource Flag { get; }
    public string Name { get; }
    public string Navy { get; }
    public string Boon { get; }
    public string Points { get; }
    public string State { get; }
    public bool Selected { get; }
    /// <summary>The state is a price.</summary>
    public bool Emphasis { get; }
    public double FlagOpacity { get; }
    public double CardOpacity { get; }
    public ICommand Command { get; }

    internal EnsignCard(Profile profile, Ensign ensign, List<Ensign> hoisted, Action click)
    {
        bool flying = hoisted.Contains(ensign), owned = profile.Owns(Store.EnsignId(ensign));
        Flag = EnsignFlags.Source(ensign);
        Name = ensign.Name;
        Navy = ensign.Navy;
        Boon = ensign.Boon;
        Points = $"{ensign.Points} pts";
        var flagship = Setup.Flagship(profile);
        int available = Ensigns.Budget - Ensigns.PointsFlown(hoisted);
        var clash = Ensigns.ClashWith(ensign, hoisted);
        bool fits = Ensigns.Fits(ensign, flagship.Type), enough = ensign.Points <= available;
        State = flying ? "Flying"
            : !owned ? $"Unlock: {ensign.Price:N0} XP"
            : clash != null ? $"Cannot fly with {clash.Name}"
            : !fits ? $"Not on {flagship.Name}"
            : !enough ? $"Not enough points (needs {ensign.Points} / {available})"
            : "Owned";
        CardOpacity = flying || (clash == null && fits && enough) ? 1.0 : 0.6;
        Selected = flying;
        Emphasis = !flying && !owned;
        FlagOpacity = owned ? 1.0 : 0.45;
        var item = Store.EnsignItem(ensign);
        Command = new DelegateCommand(_ => owned || profile.CanBuy(item), _ => click());
    }
}

/// <summary>A permanent upgrade: its ranks as pips, what a rank does, and the next rank's price.</summary>
public sealed class BonusRow
{
    public string Name { get; }
    public string PerRank { get; }
    public List<bool> Pips { get; }
    public string Price { get; }
    public ICommand Command { get; }

    internal BonusRow(Profile profile, PowerUp powerUp, Action buy)
    {
        int rank = profile.RankOf(powerUp);
        Name = powerUp.Name;
        PerRank = powerUp.PerRank;
        Pips = Enumerable.Range(0, powerUp.MaxRank).Select(i => i < rank).ToList();
        Price = rank >= powerUp.MaxRank ? "MAX" : $"{Store.Price(profile, powerUp):N0} XP";
        Command = new DelegateCommand(_ => profile.CanBuyRank(powerUp), _ => buy());
    }
}
