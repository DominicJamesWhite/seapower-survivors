using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The pick-one-of-three screen. It pauses the game for each boss chest's
/// ship, then each banked level, one after another, and holds the pause
/// until the last pick is made.
/// </summary>
public class LevelUpScreen : MonoBehaviour
{
    const int Choices = 3;
    /// <summary>At 1080p; scaled with the screen.</summary>
    const float CardWidth = 320f, MinCardHeight = 300f, Gap = 16f;
    const float BanishHeight = 30f;

    /// <summary>True while the screen owns the pause; the time-compression patches below defer to it.</summary>
    internal static bool Holding;
    static bool resuming;
    static float resumeCompression = 1f;

    /// <summary>The upgrade cards: each upgrade and the rarity its card rolled.</summary>
    List<(Upgrade upgrade, Rarity rarity)> offer = new List<(Upgrade, Rarity)>();
    /// <summary>What is drawn for the current cards, in the order of <see cref="offer"/> or <see cref="ships"/>.</summary>
    List<LevelUpCard> cards = new List<LevelUpCard>();
    List<Escort> ships = new List<Escort>();
    /// <summary>True while the cards are a boss chest's ships rather than upgrades.</summary>
    bool choosingShip;

    void Update()
    {
        var run = Run.Current;
        if (run == null || TestPanel.PlayerShip() == null) return;

        if (run.RearmsPending > 0)
        {
            run.RearmsPending = 0;
            try
            {
                run.RearmFleet();
                TestPanel.Report($"Level {run.Level}: fleet rearmed");
            }
            catch (System.Exception e) { Entry.Log.LogError($"Rearm failed: {e}"); }
        }

        if (Holding)
        {
            // The first card, the way a player in a hurry would.
            if (Autopilot.Active)
            {
                if (choosingShip && ships.Count > 0) Pick(run, ships[0]);
                else if (!choosingShip && offer.Count > 0) Pick(run, offer[0].upgrade, offer[0].rarity);
                else Next(run);
            }
            return;
        }
        // Nothing left to rank up: the levels still rearmed the fleet, which is reward enough.
        if (run.PendingLevels > 0 && Upgrades.Eligible(run).Count == 0) run.PendingLevels = 0;
        if (run.PendingLevels > 0 || run.ShipChests.Count > 0) Open(run);
    }

    void Open(Run run)
    {
        Holding = true;
        // Runs are played compressed; remember the rate to resume at it.
        resumeCompression = GameTime.TimeCompression > 0.05f ? GameTime.TimeCompression : 1f;
        GameTime.Pause(true);
        SetMouseCaptured(true);
        Roll(run);
    }

    void Close()
    {
        Holding = false;
        SetMouseCaptured(false);
        // The game's own resume sequence, from MissionManager's message dialog.
        resuming = true;
        try
        {
            GameTime.StopTimeCompression();
            if (resumeCompression > 1f) GameTime.SetTimeCompression(resumeCompression);
            Singleton<InputHandler>.Instance.IsInPauseMode.Value = false;
            Singleton<InputHandler>.Instance.IsInMessagePauseMode = false;
        }
        finally { resuming = false; }
    }

    /// <summary>How likely a card a reroll replaces is to come up again, against any other card.</summary>
    const float RerollRepeatWeight = 1f / 3f;

    /// <summary>Items in the order to deal them. A reroll passes the cards it replaces, which can come back but usually don't.</summary>
    internal static IEnumerable<T> Shuffled<T>(IEnumerable<T> items, ICollection<T> shown = null) =>
        // Sorting on u^(1/weight) draws without replacement in proportion to weight.
        items.OrderByDescending(item => shown != null && shown.Contains(item)
            ? Mathf.Pow(Random.value, 1f / RerollRepeatWeight)
            : Random.value);

    void Roll(Run run, bool reroll = false)
    {
        choosingShip = run.ShipChests.Count > 0;
        if (choosingShip)
        {
            ships = Escorts.Offer(run, run.ShipChests.Peek(), reroll ? ships : null);
            cards = ships.Select(escort => LevelUpCard.For(run, escort)).ToList();
        }
        else
        {
            // Every eligible upgrade is equally likely, but for a reroll's lean
            // away from the cards it replaces; each card then rolls its rarity,
            // which is how many ranks of the line it gives.
            var shown = reroll ? offer.Select(card => card.upgrade).ToList() : null;
            offer = Shuffled(Upgrades.Eligible(run), shown).Take(Ensigns.CardCount(Choices)).Select(u => (u, Rarities.Roll(run.Luck))).ToList();
            cards = offer.Select(card =>
            {
                int ranks = Upgrades.RanksFor(run, card.upgrade, card.rarity);
                CardPreview preview;
                try { preview = Boosts.Preview(run, card.upgrade, ranks); }
                catch (System.Exception e)
                {
                    Entry.Log.LogError($"Preview for {card.upgrade.Name} failed: {e}");
                    preview = new CardPreview();
                }
                return LevelUpCard.For(run, card.upgrade, card.rarity, ranks, preview);
            }).ToList();
        }
    }

    void Pick(Run run, Upgrade upgrade, Rarity rarity)
    {
        run.Take(upgrade, rarity);
        Next(run);
    }

    /// <summary>Take an upgrade out of the cards for the rest of the run, and deal again.</summary>
    void Banish(Run run, Upgrade upgrade)
    {
        run.Banishes--;
        run.Banished.Add(upgrade);
        TestPanel.Report($"{upgrade.Name} banished for the rest of the run");
        if (Upgrades.Eligible(run).Count > 0) Roll(run);
        else Next(run);
    }

    void Pick(Run run, Escort escort)
    {
        try { Escorts.JoinFromChest(run, escort); }
        catch (System.Exception e) { Entry.Log.LogError($"Joining {escort.Type} failed: {e}"); }
        Next(run);
    }

    void Next(Run run)
    {
        // The Greek ensign's escort empties the queue as it joins.
        if (choosingShip && run.ShipChests.Count > 0) run.ShipChests.Dequeue();
        else run.PendingLevels--;
        if (run.ShipChests.Count > 0 || (run.PendingLevels > 0 && Upgrades.Eligible(run).Count > 0)) Roll(run);
        else
        {
            run.PendingLevels = 0;
            Close();
        }
    }

    /// <summary>
    /// Drawn in the game's own look, as the status line is: its modal window
    /// and veil, its window colour for cards, caps on buttons and field labels
    /// only, and each card's rarity as a crew skill mark. Sized for 1080p and
    /// scaled with the screen, and as tall as the cards dealt need.
    /// </summary>
    void OnGUI()
    {
        var run = Run.Current;
        if (!Holding || run == null) return;

        float s = Screen.height / 1080f;
        if (!Mathf.Approximately(s, styleScale)) BuildStyles(s);

        float cardWidth = Mathf.Round(CardWidth * s), gap = Mathf.Round(Gap * s), pad = 24f * s;
        int count = cards.Count;
        float headHeight = Mathf.Round(cards.Select(c => c.HeadHeight(cardWidth, s)).DefaultIfEmpty(0f).Max());
        float cardHeight = Mathf.Round(Mathf.Max(MinCardHeight * s, headHeight + cards.Select(c => c.ContentHeight(cardWidth, s)).DefaultIfEmpty(0f).Max()));
        int widest = Ensigns.CardCount(Choices);
        float cardsWidth = widest * cardWidth + (widest - 1) * gap;
        float panelWidth = cardsWidth + 2f * pad;
        float banish = !choosingShip && run.Banishes > 0 ? BanishHeight * s : 0f;
        float panelHeight = pad + 56f * s + cardHeight + banish + 20f * s + ButtonHeight * s + pad;
        var panel = new Rect(Mathf.Round((Screen.width - panelWidth) / 2f), Mathf.Round((Screen.height - panelHeight) / 2f), panelWidth, panelHeight);

        // The game's modal: the map behind veiled, so the cards read as the one thing to do.
        Hud.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Hud.Veil);
        Hud.Box(panel, Hud.Modal);

        float x0 = panel.x + pad, y = panel.y + pad;
        // Fewer cards than the panel holds sit in its middle.
        float cardsX = x0 + (cardsWidth - (count * cardWidth + (count - 1) * gap)) / 2f;
        if (choosingShip)
        {
            GUI.Label(new Rect(x0, y, cardsWidth, 16f * s), "BOSS CHEST", caps);
            GUI.Label(new Rect(x0, y + 16f * s, cardsWidth, 32f * s), "Reinforcements join your fleet", heading);
            GUI.Label(new Rect(x0, y + 16f * s, cardsWidth, 32f * s), "Under your command. Ships take every upgrade; flights have unlimited fuel.", capsRight);
        }
        else
        {
            // Free cards can outnumber the levels gained so far.
            int shownLevel = Mathf.Max(1, run.Level - run.PendingLevels + 1);
            GUI.Label(new Rect(x0, y, cardsWidth, 16f * s), "CHOOSE AN UPGRADE", caps);
            GUI.Label(new Rect(x0, y + 16f * s, cardsWidth, 32f * s), $"Level {shownLevel}", heading);
            if (run.PendingLevels > 1)
                GUI.Label(new Rect(x0, y, cardsWidth, 16f * s), $"+{run.PendingLevels - 1} MORE TO PICK", capsRight);
        }
        y += 56f * s;

        for (int i = 0; i < count; i++)
        {
            var rect = new Rect(Mathf.Round(cardsX + i * (cardWidth + gap)), Mathf.Round(y), cardWidth, cardHeight);
            bool picked = cards[i].Draw(rect, headHeight, s);
            if (choosingShip)
            {
                if (picked)
                {
                    Pick(run, ships[i]);
                    return;
                }
            }
            else
            {
                var (upgrade, rarity) = offer[i];
                if (picked)
                {
                    Pick(run, upgrade, rarity);
                    return;
                }
                if (run.Banishes > 0 && FlatButton(new Rect(rect.x, rect.yMax + 6f * s, rect.width, banish - 6f * s), $"BANISH  ({run.Banishes} LEFT)", true))
                {
                    Banish(run, upgrade);
                    return;
                }
            }
        }

        float row = y + cardHeight + banish + 20f * s;
        float buttonWidth = 180f * s, buttonHeight = ButtonHeight * s;
        if (FlatButton(new Rect(x0, row, buttonWidth, buttonHeight), $"REROLL  ({run.Rerolls})", run.Rerolls > 0))
        {
            run.Rerolls--;
            Roll(run, reroll: true);
        }
        if (FlatButton(new Rect(x0 + cardsWidth - buttonWidth, row, buttonWidth, buttonHeight), "SKIP", true)) Next(run);
    }

    const float ButtonHeight = 36f;

    float styleScale;
    GUIStyle caps, capsRight, heading, button;

    void BuildStyles(float s)
    {
        styleScale = s;
        int Size(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
        caps = Hud.Text(Size(12), Hud.Label);
        capsRight = Hud.Text(Size(12), Hud.Label, TextAnchor.MiddleRight);
        heading = Hud.Text(Size(24), Hud.Value);
        button = Hud.Text(Size(12), Hud.Label, TextAnchor.MiddleCenter);
    }

    /// <summary>The game's button: flat grey, red with light text on hover, darker with faint text when it cannot be used.</summary>
    bool FlatButton(Rect rect, string text, bool enabled)
    {
        bool hover = enabled && rect.Contains(Event.current.mousePosition);
        Hud.Fill(rect, !enabled ? Hud.Dark : hover ? Hud.Red : Hud.Control);
        button.normal.textColor = !enabled ? Hud.Edge : hover ? Hud.Value : Hud.Label;
        GUI.Label(rect, text, button);
        return enabled && GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    static void SetMouseCaptured(bool captured)
    {
        if (Singleton<MouseControlState>.InstanceExists(false))
            Singleton<MouseControlState>.Instance.setMouseIsOverUIWindow(captured);
    }

    /// <summary>
    /// While the screen is open, nothing else may restart time: not the pause
    /// key, not the time-compression buttons, not another mod.
    /// </summary>
    [HarmonyPatch]
    static class HoldPause
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            new[] { "UnPause", "StopTimeCompression", "SetTimeCompression", "IncreaseTimeCompression", "DecreaseTimeCompression" }
                .Select(name => AccessTools.Method(typeof(GameTime), name))
                .Where(m => m != null);

        static bool Prefix() => !Holding || resuming;
    }
}
