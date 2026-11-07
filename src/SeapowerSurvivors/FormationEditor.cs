using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Noesis;
using SeaPower;
using SeapowerUI;

namespace SeapowerSurvivors;

/// <summary>
/// The Survivors screen's formation window: the flagship and its starting ships
/// on a bullseye, course up, as the game's Task Force formation editor draws
/// them. Stations go straight into the profile.
/// </summary>
public sealed class FormationEditor : Observable
{
    /// <summary>Half the plot's width in the view.</summary>
    internal const float PlotHalf = 250f;
    const float MinScale = 2f;

    /// <summary>Land is sampled in cells of this many pixels on the plot.</summary>
    const int LandCell = 10;

    readonly Action changed;
    float scale;
    string landDrawnFor;

    public List<StationDot> Dots { get; private set; } = new List<StationDot>();
    /// <summary>Water too shallow for a ship, and land, around the area's start, course up.</summary>
    public Geometry Land { get; private set; }
    public string Summary { get; private set; }
    public string TurnNote { get; private set; }
    public bool Selected { get; private set; }
    public ICommand TurnCommand { get; }
    public ICommand DefaultCommand { get; }

    public float MaxScale => Formations.MaxStationNm;
    /// <summary>The nautical miles from the plot's centre to its edge.</summary>
    public float Scale
    {
        get => scale;
        set
        {
            scale = Math.Max(MinScale, Math.Min(MaxScale, value));
            foreach (var dot in Dots) dot.Place(scale);
            DrawLand();
            Changed();
            Changed(nameof(ScaleText));
        }
    }
    public string ScaleText => $"{scale:0} nm";

    static Profile Profile => Profile.Current;

    internal FormationEditor(Action changed)
    {
        this.changed = changed;
        TurnCommand = new DelegateCommand(_ =>
        {
            Profile.SetStationsTurn(!Profile.StationsTurn);
            changed();
        });
        DefaultCommand = new DelegateCommand(_ =>
        {
            Profile.ClearStations();
            scale = 0f;
            changed();
        });
    }

    /// <summary>Read the fleet and its stations again, after anything that may have changed them.</summary>
    internal void Rebuild()
    {
        var profile = Profile;
        var stations = Setup.Stations(profile);
        // Opens wide enough to show the farthest ship, then stays as the player sets it.
        if (scale < MinScale)
            scale = Math.Max(MinScale, Math.Min(MaxScale, (float)Math.Ceiling(stations.Select(s => Distance(s.Right, s.Ahead)).DefaultIfEmpty(0f).Max() * 1.25f)));

        var dots = new List<StationDot> { new StationDot(null, Setup.Flagship(profile).Name, 0f, 0f, scale) };
        dots.AddRange(stations.Select(s => new StationDot(s.Key, s.Name, s.Right, s.Ahead, scale, s.MovedOffLand)));
        Dots = dots;
        DrawLand();
        Summary = stations.Count == 0
            ? $"{dots[0].Name} sails alone. Ships from chests join on clear water."
            : $"{stations.Count} ship{(stations.Count == 1 ? "" : "s")} on station. Ships from chests join on clear water.";
        Selected = profile.StationsTurn;
        TurnNote = Selected
            ? "Stations swing with the flagship when it turns, and ships sail round to stay on them."
            : "Ships hold their compass bearing from the flagship when it turns.";
        foreach (var name in new[] { nameof(Dots), nameof(Summary), nameof(Selected), nameof(TurnNote), nameof(Scale), nameof(ScaleText) })
            Changed(name);
    }

    /// <summary>Shade the plot's land for the area chosen and this scale, as rows of cells.</summary>
    void DrawLand()
    {
        var area = Setup.Area(Profile);
        string drawing = $"{area.Id}/{scale}";
        if (drawing == landDrawnFor) return;
        landDrawnFor = drawing;
        var start = area.PlayerStart;
        float heading = start.HeadingToPosition(area.EnemyWaters);
        int size = (int)(2f * PlotHalf);
        var path = new System.Text.StringBuilder();
        for (int top = 0; top < size; top += LandCell)
        {
            int runStart = -1;
            for (int left = 0; left <= size; left += LandCell)
            {
                bool land = left < size && !Chart.IsWater(Formations.StationAt(start, heading,
                    (left + LandCell / 2f - PlotHalf) * scale / PlotHalf,
                    (PlotHalf - top - LandCell / 2f) * scale / PlotHalf));
                if (land && runStart < 0) runStart = left;
                if (land || runStart < 0) continue;
                path.Append($"M{runStart} {top}H{left}V{top + LandCell}H{runStart}Z");
                runStart = -1;
            }
        }
        Land = path.Length == 0 ? null : Geometry.Parse(path.ToString());
        Changed(nameof(Land));
    }

    /// <summary>A ship dropped: keep it clear of the others, and save it.</summary>
    internal void Drop(StationDot dot)
    {
        var (right, ahead) = Separated(dot);
        Profile.SetStation(dot.Key, right, ahead);
        changed();
    }

    /// <summary>Pushed directly away from any ship it was dropped on, so the editor cannot make a collision.</summary>
    (float right, float ahead) Separated(StationDot dot)
    {
        float right = dot.Right, ahead = dot.Ahead;
        for (int pass = 0; pass < 12; pass++)
        {
            var near = Dots.FirstOrDefault(o => o != dot && Distance(o.Right - right, o.Ahead - ahead) < Formations.MinStationNm);
            if (near == null) break;
            float dx = right - near.Right, dy = ahead - near.Ahead, length = Distance(dx, dy);
            if (length < 0.01f)
            {
                dx = 1f;
                dy = 0f;
                length = 1f;
            }
            float push = (Formations.MinStationNm + 0.05f) / length;
            right = Clamp(near.Right + dx * push);
            ahead = Clamp(near.Ahead + dy * push);
        }
        return (Round(right), Round(ahead));
    }

    internal static float Distance(float x, float y) => (float)Math.Sqrt(x * x + y * y);
    internal static float Clamp(float nm) => Math.Max(-Formations.MaxStationNm, Math.Min(Formations.MaxStationNm, nm));
    internal static float Round(float nm) => (float)Math.Round(nm, 1);
}

/// <summary>A ship on the bullseye. The flagship has no key, and stays at the centre.</summary>
public sealed class StationDot : Observable
{
    internal readonly string Key;
    readonly bool movedOffLand;
    float scale;

    public string Name { get; }
    public bool IsLeader => Key == null;
    public float Right { get; private set; }
    public float Ahead { get; private set; }
    public float Left => FormationEditor.PlotHalf + Right * FormationEditor.PlotHalf / scale;
    public float Top => FormationEditor.PlotHalf - Ahead * FormationEditor.PlotHalf / scale;
    public Visibility Shown => Math.Abs(Right) <= scale && Math.Abs(Ahead) <= scale ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The game's way of reading a station: range and relative bearing.</summary>
    public string Detail
    {
        get
        {
            if (IsLeader) return "Flagship";
            double bearing = (Math.Atan2(Right, Ahead) * 180.0 / Math.PI + 360.0) % 360.0;
            string station = $"{FormationEditor.Distance(Right, Ahead):0.0} nm, {bearing:000}° relative";
            return movedOffLand ? $"{station}, moved off land" : station;
        }
    }

    internal StationDot(string key, string name, float right, float ahead, float scale, bool movedOffLand = false)
    {
        Key = key;
        this.movedOffLand = movedOffLand;
        Name = name;
        Right = right;
        Ahead = ahead;
        this.scale = scale;
    }

    internal void Place(float newScale)
    {
        scale = newScale;
        Notify();
    }

    /// <summary>Follow the mouse, at <paramref name="x"/> and <paramref name="y"/> on the plot.</summary>
    internal void Drag(float x, float y)
    {
        float half = FormationEditor.PlotHalf;
        Right = FormationEditor.Round(FormationEditor.Clamp(Math.Max(-half, Math.Min(half, x - half)) * scale / half));
        Ahead = FormationEditor.Round(FormationEditor.Clamp(Math.Max(-half, Math.Min(half, half - y)) * scale / half));
        Notify();
    }

    void Notify()
    {
        foreach (var name in new[] { nameof(Left), nameof(Top), nameof(Shown), nameof(Detail) }) Changed(name);
    }
}

/// <summary>
/// Dragging on the plot. A view parsed at run time cannot name its handlers, so
/// these are hooked up in code, the way Dynamic Campaign Mod's formation plot is.
/// </summary>
sealed class FormationPlot
{
    const float GrabPixels = 14f;

    readonly Canvas canvas;
    readonly FormationEditor editor;
    StationDot dragging;

    internal FormationPlot(Canvas canvas, FormationEditor editor)
    {
        this.canvas = canvas;
        this.editor = editor;
        canvas.MouseLeftButtonDown += OnDown;
        canvas.MouseMove += OnMove;
        canvas.MouseLeftButtonUp += OnUp;
        // Lost mid-drag, the alt-tab way: nothing was saved, so back to what was.
        canvas.LostMouseCapture += (_, _) =>
        {
            if (dragging == null) return;
            dragging = null;
            editor.Rebuild();
        };
    }

    void OnDown(object sender, MouseButtonEventArgs args)
    {
        var at = args.GetPosition(canvas);
        float nearest = GrabPixels;
        dragging = null;
        foreach (var dot in editor.Dots.Where(d => !d.IsLeader && d.Shown == Visibility.Visible))
        {
            float distance = FormationEditor.Distance(dot.Left - at.X, dot.Top - at.Y);
            if (distance >= nearest) continue;
            nearest = distance;
            dragging = dot;
        }
        if (dragging == null) return;
        if (canvas.CaptureMouse()) args.Handled = true;
        else dragging = null;
    }

    void OnMove(object sender, MouseEventArgs args)
    {
        if (dragging == null) return;
        var at = args.GetPosition(canvas);
        dragging.Drag(at.X, at.Y);
        args.Handled = true;
    }

    void OnUp(object sender, MouseButtonEventArgs args)
    {
        if (dragging == null) return;
        var dot = dragging;
        dragging = null;
        canvas.ReleaseMouseCapture();
        args.Handled = true;
        editor.Drop(dot);
    }
}
