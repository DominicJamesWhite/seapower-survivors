using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Noesis;

namespace SeapowerSurvivors;

/// <summary>
/// The menu's views: XAML embedded in the assembly and parsed at run time,
/// the way the Dynamic Campaign Mod builds its screens, since a mod cannot
/// add to the game's compiled XAML.
/// </summary>
static class Views
{
    internal static FrameworkElement Load(string name)
    {
        using var stream = typeof(Views).Assembly.GetManifestResourceStream("SeapowerSurvivors.Views." + name);
        if (stream == null) throw new FileNotFoundException($"View {name} is not embedded in the mod.");
        using var reader = new StreamReader(stream);
        return (FrameworkElement)GUI.ParseXaml(reader.ReadToEnd());
    }

    internal static readonly Brush Clear = Solid(0x00, 0x00, 0x00, 0x00);
    internal static readonly Brush Amber = Solid(0xFF, 0xF0, 0xA0, 0x30);
    internal static readonly Brush Text = Solid(0xFF, 0xE8, 0xE8, 0xE8);
    internal static readonly Brush Muted = Solid(0xFF, 0xB0, 0xB6, 0xBD);
    /// <summary>A row or pip not chosen or not owned.</summary>
    internal static readonly Brush Off = Solid(0xFF, 0x46, 0x4C, 0x54);
    internal static readonly Brush Row = Solid(0xFF, 0x22, 0x26, 0x2B);
    internal static readonly Brush ChosenRow = Solid(0xFF, 0x30, 0x36, 0x3D);

    static Brush Solid(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
}

/// <summary>
/// A view model the views bind to, telling them when a property changes.
/// Public, as is everything bound: Noesis finds properties by reflection.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected void Changed([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
