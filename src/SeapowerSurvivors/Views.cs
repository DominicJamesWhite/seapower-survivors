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
