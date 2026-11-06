using System;
using System.Collections.Generic;
using System.Windows.Input;
using Noesis;
using SeaPower;
using SeapowerUI;
using SeapowerUI.ViewModels;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The main menu's SURVIVORS button, flush with the right edge of the screen
/// halfway down, where the menu has nothing. It is added to the menu's root
/// panel, the way Quick Start adds its overlays, rather than to the menu's
/// list, which is compiled XAML; Quick Start already takes the list's one
/// spare entry. It shows only while the main menu itself is the menu's window.
/// </summary>
/// <remarks>
/// Each return to the menu builds a new menu, so the button is attached
/// afresh to whichever root the menu has.
/// </remarks>
public class MenuButton : MonoBehaviour
{
    const float CheckSeconds = 0.25f;
    const float DesignHeight = 1080f;

    readonly Model model = new Model();
    FrameworkElement button;
    FrameworkElement attachedTo;
    float nextCheck;
    bool failed;

    void Update()
    {
        if (failed || Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + CheckSeconds;
        try { Tick(); }
        catch (Exception e)
        {
            // One failure is logged, not one every quarter second; the test panel's launcher still works.
            failed = true;
            Entry.Log.LogError($"Main menu button disabled: {e}");
        }
    }

    void Tick()
    {
        if (!Launcher.AtMainMenu)
        {
            // The menu's tree goes with the menu scene.
            button = attachedTo = null;
            return;
        }
        var menu = Singleton<MenuSystemViewModel>.Instance;
        var root = menu.RootControl;
        if (root == null) return;
        if (attachedTo != root || button == null || button.Parent == null) Attach(root);
        if (button == null) return;

        bool show = menu.CurrentWindow is MainMenuView;
        button.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        model.Refresh();
        Scale(menu, root);
    }

    void Attach(FrameworkElement root)
    {
        var host = HostPanel(root);
        if (host == null) return;
        button = Views.Load("MenuButton.xaml");
        button.DataContext = model;
        // Over every row and column, if the host is a grid, so it can sit at the screen's edge.
        Grid.SetRowSpan(button, 99);
        Grid.SetColumnSpan(button, 99);
        host.Children.Add(button);
        attachedTo = root;
        Entry.Log.LogInfo($"Main menu button added to the menu's {host.GetType().Name}");
    }

    /// <summary>The root itself if it is a panel, else the first panel under it.</summary>
    static Panel HostPanel(FrameworkElement root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        for (int visited = 0; queue.Count > 0 && visited < 512; visited++)
        {
            var node = queue.Dequeue();
            if (node is Panel panel) return panel;
            int children = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < children; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    /// <summary>
    /// Laid out at 1080p, scaled as the menu scales its pages: by the screen's
    /// height and the UI scale option, allowing for the view rendering at
    /// other than the screen's size.
    /// </summary>
    void Scale(MenuSystemViewModel menu, FrameworkElement root)
    {
        float view = root.ActualWidth > 0f && Screen.width > 0 ? root.ActualWidth / Screen.width : 1f;
        float scale = menu.ScaleValueY * view;
        if (scale <= 0.2f || scale >= 6f) scale = Screen.height / DesignHeight;
        if (button.LayoutTransform is ScaleTransform current && Math.Abs(current.ScaleX - scale) < 0.005f) return;
        button.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>What the button binds to: the bank, and the command that opens the Survivors screen.</summary>
    public sealed class Model : Observable
    {
        int shown = -1;

        public string Bank { get; private set; }
        public ICommand OpenCommand { get; } = new DelegateCommand(_ => SurvivorsScreen.Show());

        internal void Refresh()
        {
            int bank = Profile.Current.Bank;
            if (bank == shown) return;
            shown = bank;
            Bank = $"{bank:N0} XP to spend";
            Changed(nameof(Bank));
        }
    }
}
