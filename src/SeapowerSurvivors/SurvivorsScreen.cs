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
    public string AreaApproach { get; private set; }
    public List<ShipRow> Flagships { get; private set; }
    public List<ShipRow> Escorts { get; private set; }
    public string FleetSummary { get; private set; }
    public List<BonusRow> Bonuses { get; private set; }
    public string RefundText { get; private set; }
    public string Message { get; private set; } = "";
    public string LaunchText => Launcher.Launching ? "LOADING..." : "LAUNCH";
    /// <summary>The three slots on the main page: a flag, or an empty frame.</summary>
    public List<EnsignSlot> EnsignSlots { get; private set; }
    public string EnsignSummary { get; private set; }
    public List<EnsignCard> EnsignCards { get; private set; }
    public Visibility MainPage => choosingEnsigns ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EnsignPage => choosingEnsigns ? Visibility.Visible : Visibility.Collapsed;
    public string BackText => choosingEnsigns ? "DONE" : "BACK";

    public ICommand BackCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand RefundCommand { get; }
    public ICommand EnsignsCommand { get; }

    bool choosingEnsigns;

    static Profile Profile => Profile.Current;

    SurvivorsScreen()
    {
        EnsignsCommand = new DelegateCommand(_ => ShowEnsigns(true));
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
            Say($"Refunded {Profile.Refund()} XP");
            Refresh();
        });
        Refresh();
    }

    internal static void Show()
    {
        Singleton<MenuSystemViewModel>.Instance.CurrentWindow = new UserControl
        {
            Content = Views.Load("SurvivorsScreen.xaml"),
            DataContext = new SurvivorsScreen(),
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

    /// <summary>Click an ensign: unlock it if it can be, hoist it if there is room, strike it if it is flying.</summary>
    void ClickEnsign(Profile profile, Ensign ensign)
    {
        var hoisted = profile.HoistedEnsigns();
        if (hoisted.Contains(ensign))
        {
            profile.Strike(ensign);
            Say($"{ensign.Name} struck");
        }
        else if (!profile.Owns(Store.EnsignId(ensign)))
        {
            var item = Store.EnsignItem(ensign);
            if (profile.TryBuy(item))
            {
                Say($"{ensign.Name} unlocked for {item.Price:N0} XP");
                if (hoisted.Count < Ensigns.MaxHoisted && Ensigns.ClashWith(ensign, hoisted) == null) profile.Hoist(ensign);
            }
        }
        else if (Ensigns.ClashWith(ensign, hoisted) is Ensign clash)
            Say($"{ensign.Name} cannot fly with {clash.Name}");
        else if (hoisted.Count >= Ensigns.MaxHoisted)
            Say($"Three ensigns at most: strike one to hoist {ensign.Name}");
        else
        {
            profile.Hoist(ensign);
            Say($"{ensign.Name} hoisted");
        }
        Refresh();
    }

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

        Areas = SeapowerSurvivors.Areas.All.Select(a => new AreaCard(a, a == area, () => Choose(a, flagship))).ToList();
        AreaName = area.Name;
        AreaBlurb = area.Blurb;
        AreaApproach = Approach(area);

        Flagships = Launcher.Flagships.Select(ship => FlagshipRow(profile, ship, ship == flagship, area)).ToList();
        Escorts = Store.StartingEscorts(flagship).Select(escort => EscortRow(profile, escort)).ToList();
        var sailing = Setup.Sailing(profile);
        FleetSummary = sailing.Count == 0
            ? $"{flagship.Name} sails alone."
            : profile.HoistedEnsigns().Contains(Ensigns.FiveOfAKind)
                ? $"{flagship.Name} sails with {Ensigns.EscortCopies} × {sailing[0].Name}."
                : $"{flagship.Name} sails with {string.Join(", ", sailing.Select(e => e.Name))}.";

        Bonuses = Store.PowerUps.Select(powerUp => new BonusRow(profile, powerUp, () =>
        {
            int rank = profile.RankOf(powerUp);
            if (profile.TryBuyRank(powerUp)) Say($"{powerUp.Name} rank {rank + 1} bought");
            Refresh();
        })).ToList();
        var hoisted = profile.HoistedEnsigns();
        EnsignSlots = Enumerable.Range(0, Ensigns.MaxHoisted)
            .Select(i => new EnsignSlot(i < hoisted.Count ? hoisted[i] : null)).ToList();
        EnsignSummary = hoisted.Count == 0 ? "None flying" : string.Join(", ", hoisted.Select(e => e.Name));
        EnsignCards = Ensigns.All.Select(e => new EnsignCard(profile, e, hoisted, () => ClickEnsign(profile, e))).ToList();

        RefundText = profile.Refundable > 0 ? $"Refund all bonuses: {profile.Refundable:N0} XP back" : "Nothing to refund";
        ((DelegateCommand)RefundCommand).RaiseCanExecuteChanged();

        foreach (var name in new[]
                 {
                     nameof(Bank), nameof(Earned), nameof(Areas), nameof(AreaName), nameof(AreaBlurb), nameof(AreaApproach),
                     nameof(Flagships), nameof(Escorts), nameof(FleetSummary), nameof(Bonuses), nameof(RefundText),
                     nameof(EnsignSlots), nameof(EnsignSummary), nameof(EnsignCards),
                 })
            Changed(name);
    }

    void Choose(Area area, StartingShip flagship)
    {
        Profile.Choose(area, flagship);
        Refresh();
    }

    ShipRow FlagshipRow(Profile profile, StartingShip ship, bool chosen, Area area)
    {
        var item = Store.Flagship(ship);
        string navy = ship.Soviet ? "Soviet: fights NATO" : "US Navy: fights the Soviets";
        if (profile.Owns(item.Id))
            return new ShipRow(ship.Name, navy, chosen ? "Flagship" : "Owned", chosen, chosen, () => Choose(area, ship), canClick: true);
        if (item.WonBy != null)
            return new ShipRow(ship.Name, $"{navy}. {item.WonBy} to unlock", "Locked", false, false, () => { }, canClick: false);
        return new ShipRow(ship.Name, navy, $"{item.Price:N0} XP", false, false, () =>
            {
                if (profile.TryBuy(item))
                {
                    Say($"{ship.Name} bought for {item.Price:N0} XP");
                    profile.Choose(area, ship);
                }
                Refresh();
            }, canClick: profile.CanBuy(item));
    }

    ShipRow EscortRow(Profile profile, Escort escort)
    {
        var item = Store.EscortItem(escort);
        string detail = (escort.IsAir ? $"Flight of {escort.Count}: " : "Ship: ") + SeapowerSurvivors.Escorts.Describe(escort.Roles);
        if (profile.Owns(item.Id))
        {
            bool sails = !profile.IsAshore(item.Id);
            return new ShipRow(escort.Name, detail, sails ? "Sails" : "Ashore", sails, false, () =>
            {
                profile.SetAshore(item.Id, sails);
                Refresh();
            }, canClick: true);
        }
        return new ShipRow(escort.Name, detail, $"{item.Price:N0} XP", false, false, () =>
        {
            if (profile.TryBuy(item)) Say($"{escort.Name} bought for {item.Price:N0} XP: it sails with the flagship");
            Refresh();
        }, canClick: profile.CanBuy(item));
    }

    /// <summary>Which way the enemy comes from, in compass words, as <see cref="Sea"/> will send it.</summary>
    static string Approach(Area area)
    {
        var start = area.PlayerStart;
        string ships = Compass(start.HeadingToPosition(area.EnemyWaters));
        if (area.EnemyAirfield == null) return $"Ships and aircraft come from the {ships}.";
        string air = Compass(start.HeadingToPosition(area.EnemyAir));
        return air == ships ? $"Ships and aircraft come from the {ships}." : $"Ships come from the {ships}; aircraft mostly from the {air}.";
    }

    static readonly string[] Points = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    static string Compass(float bearing) => Points[(int)Math.Round(((bearing % 360f) + 360f) % 360f / 45f) % 8];
}

/// <summary>An area to fight in: Quick Start's painting if it can be found, its name, and a frame when chosen.</summary>
public sealed class AreaCard
{
    static readonly Dictionary<string, ImageSource> ArtCache = new Dictionary<string, ImageSource>();

    public string Name { get; }
    public string Region { get; }
    public ImageSource Art { get; }
    public Brush Frame { get; }
    public ICommand Command { get; }

    internal AreaCard(Area area, bool chosen, Action choose)
    {
        Name = area.Name;
        Region = area.Region;
        Art = LoadArt(area);
        Frame = chosen ? Views.Amber : Views.Clear;
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
    /// A painting copied by hand into the mod's own install folder, for
    /// playing with Quick Start disabled. Never shipped: the art is Quick
    /// Start's author's.
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

/// <summary>A flagship or a starting ship or flight: chosen or sailing, owned, or for sale.</summary>
public sealed class ShipRow
{
    public string Name { get; }
    public string Detail { get; }
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public string State { get; }
    public Brush StateBrush { get; }
    public Brush Bar { get; }
    public Brush Fill { get; }
    public ICommand Command { get; }

    internal ShipRow(string name, string detail, string state, bool lit, bool chosen, Action click, bool canClick)
    {
        Name = name;
        Detail = detail;
        State = state;
        StateBrush = lit ? Views.Amber : Views.Muted;
        Bar = lit ? Views.Amber : Views.Off;
        Fill = chosen ? Views.ChosenRow : Views.Row;
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

/// <summary>An ensign on the choosing page: its flag, what it gives and costs, and whether it is flying, owned or for sale.</summary>
public sealed class EnsignCard
{
    public ImageSource Flag { get; }
    public string Name { get; }
    public string Navy { get; }
    public string Boon { get; }
    public string Cost { get; }
    public string State { get; }
    public Brush StateBrush { get; }
    public Brush Bar { get; }
    public Brush Fill { get; }
    public double FlagOpacity { get; }
    public ICommand Command { get; }

    internal EnsignCard(Profile profile, Ensign ensign, List<Ensign> hoisted, Action click)
    {
        bool flying = hoisted.Contains(ensign), owned = profile.Owns(Store.EnsignId(ensign));
        Flag = EnsignFlags.Source(ensign);
        Name = ensign.Name;
        Navy = ensign.Navy;
        Boon = ensign.Boon;
        Cost = ensign.Cost;
        State = flying ? "Flying" : owned ? "Owned" : $"{ensign.Price:N0} XP";
        StateBrush = flying ? Views.Amber : Views.Muted;
        Bar = flying ? Views.Amber : Views.Off;
        Fill = flying ? Views.ChosenRow : Views.Row;
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
    public List<Brush> Pips { get; }
    public string Price { get; }
    public ICommand Command { get; }

    internal BonusRow(Profile profile, PowerUp powerUp, Action buy)
    {
        int rank = profile.RankOf(powerUp);
        Name = powerUp.Name;
        PerRank = powerUp.PerRank;
        Pips = Enumerable.Range(0, powerUp.MaxRank).Select(i => i < rank ? Views.Amber : Views.Off).ToList();
        Price = rank >= powerUp.MaxRank ? "MAX" : $"{Store.Price(profile, powerUp):N0} XP";
        Command = new DelegateCommand(_ => profile.CanBuyRank(powerUp), _ => buy());
    }
}
