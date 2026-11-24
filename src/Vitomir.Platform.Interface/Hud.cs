// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Interface;

/// <summary>
/// What the HUD shows of one person, read from the world's <b>views</b> and nothing else — no rules: the quick
/// slots as the module shows them (<see cref="PowerSlots"/>), each power's label from the module's catalogue
/// (<see cref="IPowerCatalogue"/>), Health and Blood (<see cref="Vitals"/>), and the masquerade's mode
/// (<see cref="MasqueradeState"/>); and the slots' keys as bound (<see cref="SetKeys"/>, the host's). A world without a
/// module that writes the views reads as eight empty slots and no bars.
/// </summary>
public sealed class HudView
{
    /// <summary>The slots, slot 1 first (<see cref="SlotView.None"/> for none).</summary>
    public SlotViews Slots;
    /// <summary>Each slot's power's label from the catalogue, or null.</summary>
    public readonly string?[] Labels = new string?[QuickSlots.Count];
    /// <summary>Each slot's key as bound (its first), or empty when it has none.</summary>
    public readonly string[] Keys = new string[QuickSlots.Count];
    /// <summary>Whether the person has <see cref="Vitals"/> (the bars), and them.</summary>
    public bool HasVitals;
    public Vitals Vitals;
    /// <summary>Whether the person has a masquerade state, and its mode.</summary>
    public bool HasMode;
    public MasqueradeMode Mode;
    /// <summary>Whether the person is in stealth (shown beside the mode).</summary>
    public bool Stealth;

    public HudView()
    {
        Array.Fill(Keys, "");
        Clear();
    }

    /// <summary>Empty slots, no bars, no mode (the keys are kept).</summary>
    public void Clear()
    {
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            Slots[i] = SlotView.None;
            Labels[i] = null;
        }
        HasVitals = false;
        Vitals = default;
        HasMode = false;
        Mode = MasqueradeMode.Masquerade;
        Stealth = false;
    }

    /// <summary>
    /// The slots' keys from the bindings in force: each slot's first source by its name (<paramref name="keyName"/> names a
    /// key's scancode, SDL's name in the program), empty for a slot with none.
    /// </summary>
    public void SetKeys(BindingTable table, Func<int, string> keyName)
    {
        Array.Fill(Keys, "");
        foreach (var (action, source) in table.Entries)
        {
            int at = -1;
            for (int i = 0; i < QuickSlotButtons.Actions.Count; i++) if (QuickSlotButtons.Actions[i] == action) at = i;
            if (at >= 0 && Keys[at].Length == 0) Keys[at] = BindingNames.Name(source, keyName);
        }
    }

    /// <summary>
    /// <paramref name="into"/> filled from <paramref name="person"/>'s views in <paramref name="world"/> (allocation-free:
    /// the stores and the catalogue are looked up, not made; a label is the catalogue's own string).
    /// </summary>
    public static void Read(World world, EntityId person, HudView into)
    {
        into.Clear();
        if (!world.Alive(person)) return;
        if (world.TryStore<PowerSlots>(out var slots) && slots.Has(person)) into.Slots = slots.Get(person).Slots;
        world.TryService<IPowerCatalogue>(out var catalogue);
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            int p = into.Slots[i].Power;
            into.Labels[i] = catalogue is not null && p >= 0 && p < catalogue.Count ? catalogue.Label(p) : null;
        }
        if (world.TryStore<Vitals>(out var vitals) && vitals.Has(person))
        {
            into.HasVitals = true;
            into.Vitals = vitals.Get(person);
        }
        if (world.TryStore<MasqueradeState>(out var masquerade) && masquerade.Has(person))
        {
            into.HasMode = true;
            into.Mode = masquerade.Get(person).Mode;
        }
        if (world.TryStore<Animation.People.PersonStealth>(out var stealth) && stealth.Has(person)) into.Stealth = stealth.Get(person).On != 0;
    }
}

/// <summary>
/// The <b>stand-in HUD</b> (<c>Screens/hud.json</c>): not the designed one — the interface's design offers
/// two or three arrangements as pictures first, to choose from — but a plain working one, so the player sees
/// what the quick slots' keys do. At the bottom of the screen, the eight slots in a row (the key as bound, the power's
/// label, the state: ready with its Blood, live with the time left as a filling bar and the seconds, the recast, short of
/// Blood, unusable, empty), and over them Health and Blood as bars with their numbers (Blood only for a person who has
/// Blood) and the masquerade's mode. Built in the interface framework (a screen file, its pieces, the theme, the
/// player's interface scale); drawn over play, not over the pause menu (the host's choice).
/// <para>
/// <see cref="Draw"/> pushes <see cref="View"/> into the screen's data only where what is shown changed (the seconds by
/// tenths, the bars by hundredths) and lays out and draws the screen again only then, so a picture in which nothing
/// shown changed allocates nothing and reuses its draw list.
/// </para>
/// </summary>
public sealed class Hud
{
    /// <summary>What one slot shows, to know when it changed.</summary>
    readonly record struct SlotShown(SlotState State, int Power, string Label, string Key, int Tenths, int Share, int Cost);

    /// <summary>What the bars and the mode show.</summary>
    readonly record struct VitalsShown(bool HasVitals, int Health, int HealthMax, bool HasBlood, int Blood, int BloodMax, bool HasMode, MasqueradeMode Mode, bool Stealth,
        HealthBand Band = HealthBand.None);

    readonly DrawList list = new();
    readonly SlotShown[] shown = new SlotShown[QuickSlots.Count];
    readonly ScreenData[] rows = new ScreenData[QuickSlots.Count];
    VitalsShown vitalsShown;
    bool dirty = true, everShown;
    UiMetrics laidFor;

    public Hud(ScreenLoader loader)
    {
        Screen = loader.LoadEmbedded("hud");
        for (int i = 0; i < QuickSlots.Count; i++) rows[i] = Screen.Data.Add("slots");
        Update();
    }

    public Screen Screen { get; }

    /// <summary>What the HUD is to show: the host fills it each picture (<see cref="HudView.Read"/>) and its keys when the bindings change.</summary>
    public HudView View { get; } = new();

    /// <summary>How many times the screen has been laid out and drawn (for the tests: only when what is shown changed).</summary>
    public int Redraws { get; private set; }

    /// <summary>The HUD's draw list for a picture of <paramref name="width"/> by <paramref name="height"/> pixels at the player's <paramref name="scale"/>.</summary>
    public DrawList Draw(int width, int height, float scale = 1f)
    {
        Update();
        var metrics = new UiMetrics(width, height, scale);
        if (dirty || metrics != laidFor)
        {
            Screen.Layout(metrics);
            Screen.Draw(list);
            laidFor = metrics;
            dirty = false;
            Redraws++;
        }
        return list;
    }

    /// <summary>Puts into the screen's data what changed in <see cref="View"/> since last time.</summary>
    public void Update()
    {
        var v = View;
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            var s = v.Slots[i];
            var state = s.Power < 0 && s.State != SlotState.Empty ? SlotState.Empty : s.State;
            int tenths = state switch
            {
                SlotState.Live => (int)MathF.Ceiling(MathF.Max(0f, s.SecondsLeft) * 10f - 0.001f),
                SlotState.Recast => (int)MathF.Ceiling(MathF.Max(0f, s.RecastLeft) * 10f - 0.001f),
                _ => 0,
            };
            int share = state == SlotState.Live && s.SecondsTotal > 0f ? (int)MathF.Round(Math.Clamp(s.SecondsLeft / s.SecondsTotal, 0f, 1f) * 100f) : 0;
            var now = new SlotShown(state, state == SlotState.Empty ? -1 : s.Power, state == SlotState.Empty ? "" : v.Labels[i] ?? "",
                                    v.Keys[i], tenths, share, s.Cost);
            if (everShown && now == shown[i]) continue;
            shown[i] = now;
            dirty = true;
            rows[i].Set("key", now.Key.Length > 0 ? now.Key : "—")
                   .Set("power", now.Label)
                   .Set("status", Status(now))
                   .Set("live", state == SlotState.Live)
                   .Set("left", share / 100f)
                   .Set("slotFill", Fill(state))
                   .Set("slotEdge", Edge(state));
        }

        var vt = v.Vitals;
        var vs = new VitalsShown(v.HasVitals, Tenths(vt.Health), Tenths(vt.HealthMax), v.HasVitals && vt.HasBlood, Tenths(vt.Blood), Tenths(vt.BloodMax),
                                 v.HasMode, v.Mode, v.Stealth, v.HasVitals ? vt.Band : HealthBand.None);
        if (!everShown || vs != vitalsShown)
        {
            vitalsShown = vs;
            dirty = true;
            var d = Screen.Data;
            d.Set("hasVitals", vs.HasVitals).Set("hasBlood", vs.HasBlood).Set("hasMode", vs.HasMode)
             .Set("healthText", vs.HasVitals ? $"{Number(vs.Health)} / {Number(vs.HealthMax)}" : "")
             .Set("healthShare", vs.HealthMax > 0 ? Math.Clamp(vs.Health / (float)vs.HealthMax, 0f, 1f) : 0f)
             .Set("hasBand", vs.Band != HealthBand.None).Set("healthBand", BandLabel(vs.Band))       // labels only
             .Set("bloodText", vs.HasBlood ? $"{Number(vs.Blood)} / {Number(vs.BloodMax)}" : "")
             .Set("bloodShare", vs.BloodMax > 0 ? Math.Clamp(vs.Blood / (float)vs.BloodMax, 0f, 1f) : 0f)
             .Set("mode", (vs.Mode == MasqueradeMode.Unbound ? "Unbound" : "Masquerade") + (vs.Stealth ? ", Stealth" : ""))   // stealth beside the mode
             .Set("modeEdge", vs.Mode == MasqueradeMode.Unbound ? "chosenEdge" : "panelEdge");
        }
        everShown = true;
    }

    static int Tenths(float value) => (int)MathF.Round(value * 10f);

    /// <summary>The Health band's label (the rules package's proposals: below 25 % Injured, below 10 % Crippled, at nothing the stop); empty for none.</summary>
    public static string BandLabel(HealthBand band) => band switch
    {
        HealthBand.Injured => "Injured",
        HealthBand.Crippled => "Crippled",
        HealthBand.Stopped => "Stopped",
        _ => "",
    };

    static string Number(int tenths) => (tenths / 10f).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A slot's line under its power: its Blood when ready, the seconds left when live, the recast, what it is short of.</summary>
    static string Status(in SlotShown s) => s.State switch
    {
        SlotState.Ready => s.Cost > 0 ? string.Create(CultureInfo.InvariantCulture, $"{s.Cost} Blood") : "Ready",
        SlotState.Live => string.Create(CultureInfo.InvariantCulture, $"{s.Tenths / 10f:0.0} s"),
        SlotState.Recast => string.Create(CultureInfo.InvariantCulture, $"Recast {s.Tenths / 10f:0.0} s"),
        SlotState.Short => string.Create(CultureInfo.InvariantCulture, $"Needs {s.Cost} Blood"),
        SlotState.Unusable => "Unusable",
        _ => "",
    };

    /// <summary>A slot's plate by its state (theme colours).</summary>
    public static string Fill(SlotState state) => state switch
    {
        SlotState.Ready => "slotReady",
        SlotState.Live => "slotLive",
        SlotState.Short => "slotShort",
        _ => "slotDim",
    };

    /// <summary>A slot's edge by its state (theme colours): gold ready, red live, amber short, dim otherwise.</summary>
    public static string Edge(SlotState state) => state switch
    {
        SlotState.Ready => "gold",
        SlotState.Live => "chosenEdge",
        SlotState.Short => "amber",
        SlotState.Recast => "muted",
        _ => "panelInner",
    };
}
