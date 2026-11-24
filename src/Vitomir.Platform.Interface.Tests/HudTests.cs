// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// The stand-in HUD: built from the views alone — a stand-in system writes a person's <see cref="PowerSlots"/> and
/// <see cref="Vitals"/>, a stand-in catalogue names the powers — it shows each slot's state, Health and Blood with their
/// numbers and the masquerade's mode; with no views, eight empty slots with their keys and no bars. Its file loads with
/// nothing said; it sits at the bottom of the screen above the status line and follows the player's scale; a picture in
/// which nothing shown changed is not drawn again and allocates nothing.
/// </summary>
public sealed class HudTests
{
    sealed class Catalogue : IPowerCatalogue
    {
        static readonly string[] Ids = ["celerity.active", "potence.active", "dominate.active", "obfuscate.active", "celerity.six"];
        static readonly string[] Labels = ["Celerity", "Potence", "Dominate", "Obfuscate", "Celerity 6"];
        public int Count => Ids.Length;
        public string Id(int power) => Ids[power];
        public string Label(int power) => Labels[power];
    }

    /// <summary>A world with the powers' contract, the masquerade's state, a stand-in catalogue, and a system that writes one person's views each tick.</summary>
    static (World World, EntityId Person) WorldWithViews(Action<World, EntityId> write, bool catalogue = true)
    {
        var b = new WorldBuilder(1);
        PowerContract.Register(b);
        b.Component<MasqueradeState>();
        if (catalogue) b.Service<IPowerCatalogue>(new Catalogue());
        EntityId person = EntityId.None;
        b.System(Phase.Post, 0, "stand-in views", w => write(w, person));
        var world = b.Build();
        person = world.Create();
        world.Set(person, new MasqueradeState { Mode = MasqueradeMode.Unbound });
        world.Step();
        return (world, person);
    }

    static void EveryState(World w, EntityId e)
    {
        var slots = new PowerSlots();
        for (int i = 0; i < QuickSlots.Count; i++) slots.Slots[i] = SlotView.None;
        slots.Slots[0] = new SlotView { Power = 0, State = SlotState.Ready, Cost = 30 };
        slots.Slots[1] = new SlotView { Power = 1, State = SlotState.Live, SecondsLeft = 6.4f, SecondsTotal = 10f, Cost = 30 };
        slots.Slots[2] = new SlotView { Power = 2, State = SlotState.Recast, RecastLeft = 2.5f, Cost = 10 };
        slots.Slots[3] = new SlotView { Power = 3, State = SlotState.Short, Cost = 40 };
        slots.Slots[4] = new SlotView { Power = 4, State = SlotState.Unusable, Cost = 30 };
        w.Set(e, slots);
        w.Set(e, new Vitals { Health = 5f, HealthMax = 7f, Blood = 70f, BloodMax = 100f, HasBlood = true });
    }

    static BindingTable DefaultTable() => BindingTable.Build(Fixtures.Defaults().Bindings, Fixtures.Code, Fixtures.Name);

    static Hud HudOf(World world, EntityId person)
    {
        var hud = new Hud(new ScreenLoader());
        hud.View.SetKeys(DefaultTable(), Fixtures.Name);
        HudView.Read(world, person, hud.View);
        return hud;
    }

    static ScreenData Row(Hud hud, int slot) => hud.Screen.Data.Items("slots")[slot];

    [Fact]
    public void The_screen_file_loads_with_nothing_said_and_its_note_says_it_is_a_stand_in()
    {
        var loader = new ScreenLoader();
        var hud = new Hud(loader);
        hud.Draw(2560, 1440);
        Assert.Empty(loader.Problems);
        Assert.Contains("STAND-IN", hud.Screen.Note, StringComparison.Ordinal);
        Assert.Equal("none", hud.Screen.Veil);
    }

    [Fact]
    public void With_no_views_the_HUD_shows_eight_empty_slots_with_their_keys_and_no_bars()
    {
        // A world without the powers' contract or a catalogue (no module): nothing but empty slots.
        var world = new WorldBuilder(1).Build();
        var person = world.Create();
        var hud = HudOf(world, person);
        var texts = hud.Draw(2560, 1440).Texts().ToList();
        for (int n = 1; n <= QuickSlots.Count; n++) Assert.Contains(n.ToString(System.Globalization.CultureInfo.InvariantCulture), texts);
        Assert.DoesNotContain(texts, t => t.Contains("ealth", StringComparison.Ordinal) || t.Contains("lood", StringComparison.Ordinal));
        Assert.False(hud.Screen.Find("healthBox")!.Shown);
        Assert.False(hud.Screen.Find("bloodBox")!.Shown);
        Assert.False(hud.Screen.Find("modeBox")!.Shown);
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            Assert.Equal("", Row(hud, i).Text("power"));
            Assert.Equal("slotDim", Row(hud, i).Text("slotFill"));
        }

        // The contract registered but nothing written for this person: the same.
        var (w2, p2) = WorldWithViews((_, _) => { });
        var hud2 = HudOf(w2, p2);
        hud2.Draw(2560, 1440);
        Assert.Equal("", Row(hud2, 0).Text("power"));
        Assert.False(hud2.Screen.Find("healthBox")!.Shown);
        Assert.True(hud2.Screen.Find("modeBox")!.Shown);       // the masquerade's state is the platform's, not the module's
        Assert.Equal("Unbound", hud2.Screen.Data.Text("mode"));
    }

    [Fact]
    public void A_stand_in_system_writing_the_views_shows_each_state_and_both_bars()
    {
        var (world, person) = WorldWithViews(EveryState);
        var hud = HudOf(world, person);
        var texts = hud.Draw(2560, 1440).Texts().ToList();
        string all = string.Concat(texts);

        Assert.Equal(("1", "Celerity", "30 Blood", "slotReady", "gold"), Slot(hud, 0));
        Assert.Equal(("2", "Potence", "6.4 s", "slotLive", "chosenEdge"), Slot(hud, 1));
        Assert.True(Row(hud, 1).Flag("live"));
        Assert.Equal(0.64f, Row(hud, 1).Number("left"), 0.001f);
        Assert.Equal(("3", "Dominate", "Recast 2.5 s", "slotDim", "muted"), Slot(hud, 2));
        Assert.Equal(("4", "Obfuscate", "Needs 40 Blood", "slotShort", "amber"), Slot(hud, 3));
        Assert.Equal(("5", "Celerity 6", "Unusable", "slotDim", "panelInner"), Slot(hud, 4));
        for (int i = 5; i < QuickSlots.Count; i++) Assert.Equal("", Row(hud, i).Text("power"));
        Assert.False(Row(hud, 0).Flag("live"));

        Assert.Contains("5 / 7", texts);
        Assert.Contains("70 / 100", texts);
        Assert.Equal(5f / 7f, hud.Screen.Data.Number("healthShare"), 0.001f);
        Assert.Equal(0.7f, ((Meter)hud.Screen.Find("blood")!).Share, 0.001f);
        Assert.Contains("nbound", all);    // the mode's word, its capital in the capitals face
        // The live slot's bar is drawn: its fill is 64 % of its track.
        var meter = hud.Screen.Find("slots")!.Children[1].Walk().OfType<Meter>().Single();
        Assert.True(meter.Shown);
        Assert.Equal(0.64f, meter.Share, 0.001f);
    }

    [Fact]
    public void The_Health_bar_is_labelled_with_its_band_and_without_one_shows_no_label()
    {
        // Health on the 1-1000 scale, its band (labels only: below 25 % Injured, below 10 % Crippled) beside the number.
        var band = HealthBand.Injured;
        var (world, person) = WorldWithViews((w, e) => w.Set(e, new Vitals { Health = 20f, HealthMax = 100f, Blood = 70f, BloodMax = 100f, HasBlood = true, Band = band }));
        var hud = new Hud(new ScreenLoader());
        HudView.Read(world, person, hud.View);
        var texts = hud.Draw(1920, 1080).Texts().ToList();
        Assert.Contains("Injured", texts);
        Assert.Contains("20 / 100", texts);
        Assert.True(hud.Screen.Find("healthBand")!.Shown);
        world.Get<Vitals>(person).Band = HealthBand.Crippled;
        HudView.Read(world, person, hud.View);
        Assert.Contains("Crippled", hud.Draw(1920, 1080).Texts());
        world.Get<Vitals>(person).Band = HealthBand.None;
        HudView.Read(world, person, hud.View);
        texts = hud.Draw(1920, 1080).Texts().ToList();
        Assert.DoesNotContain("Injured", texts);
        Assert.DoesNotContain("Crippled", texts);
        Assert.False(hud.Screen.Find("healthBand")!.Shown);
        Assert.Equal(("Injured", "Crippled", "Stopped", ""), (Hud.BandLabel(HealthBand.Injured), Hud.BandLabel(HealthBand.Crippled), Hud.BandLabel(HealthBand.Stopped), Hud.BandLabel(HealthBand.None)));
    }

    static (string Key, string Power, string Status, string Fill, string Edge) Slot(Hud hud, int i)
    {
        var r = Row(hud, i);
        return (r.Text("key"), r.Text("power"), r.Text("status"), r.Text("slotFill"), r.Text("slotEdge"));
    }

    [Fact]
    public void Blood_is_shown_only_for_a_person_with_Blood_and_a_slot_without_a_key_shows_a_dash()
    {
        var (world, person) = WorldWithViews((w, e) => w.Set(e, new Vitals { Health = 3f, HealthMax = 3f, HasBlood = false }));
        var hud = new Hud(new ScreenLoader());
        var bindings = Fixtures.Defaults().Bindings;
        bindings["slot2"] = [];
        bindings["slot3"] = ["F"];
        hud.View.SetKeys(BindingTable.Build(bindings, Fixtures.Code, Fixtures.Name), Fixtures.Name);
        HudView.Read(world, person, hud.View);
        var texts = hud.Draw(1920, 1080).Texts().ToList();
        Assert.True(hud.Screen.Find("healthBox")!.Shown);
        Assert.False(hud.Screen.Find("bloodBox")!.Shown);
        Assert.Contains("3 / 3", texts);
        Assert.Equal("—", Row(hud, 1).Text("key"));
        Assert.Equal("F", Row(hud, 2).Text("key"));
    }

    [Fact]
    public void A_picture_in_which_nothing_shown_changed_is_not_drawn_again_and_allocates_nothing()
    {
        float left = 6.4f;
        var (world, person) = WorldWithViews((w, e) =>
        {
            EveryState(w, e);
            w.Get<PowerSlots>(e).Slots[1].SecondsLeft = left;
        });
        var hud = HudOf(world, person);
        var first = hud.Draw(2560, 1440);
        int redraws = hud.Redraws;
        for (int i = 0; i < 3; i++) { HudView.Read(world, person, hud.View); hud.Draw(2560, 1440); }   // warmed
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            HudView.Read(world, person, hud.View);
            Assert.Same(first, hud.Draw(2560, 1440));
        }
        long spent = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, spent);
        Assert.Equal(redraws, hud.Redraws);

        // A hundredth of a second less changes nothing shown; a tenth does, and the picture is drawn again.
        left = 6.395f;
        world.Step();
        HudView.Read(world, person, hud.View);
        hud.Draw(2560, 1440);
        Assert.Equal(redraws, hud.Redraws);
        left = 6.3f;
        world.Step();
        HudView.Read(world, person, hud.View);
        hud.Draw(2560, 1440);
        Assert.Equal(redraws + 1, hud.Redraws);
        Assert.Equal("6.3 s", Row(hud, 1).Text("status"));
        // A new size or scale lays it out again.
        hud.Draw(1920, 1080);
        Assert.Equal(redraws + 2, hud.Redraws);
    }

    [Theory]
    [InlineData(1920, 1080, 1f)]
    [InlineData(3840, 2160, 1f)]
    [InlineData(2560, 1440, 1.5f)]
    [InlineData(3440, 1440, 1f)]
    public void The_slots_sit_in_a_row_at_the_bottom_middle_above_the_status_line_at_every_size(int w, int h, float scale)
    {
        var (world, person) = WorldWithViews(EveryState);
        var hud = HudOf(world, person);
        hud.Draw(w, h, scale);
        var m = hud.Screen.Metrics;
        var slots = hud.Screen.Find("slots")!;
        var rects = slots.Children.Select(c => m.ToPixels(c.Rect)).ToList();
        Assert.Equal(QuickSlots.Count, rects.Count);
        for (int i = 1; i < rects.Count; i++)
        {
            Assert.Equal(rects[0].Y, rects[i].Y);                              // one row
            Assert.True(rects[i].X > rects[i - 1].Right - 1, $"slot {i + 1} after slot {i}");
        }
        // In the middle across, 72 units (at the player's scale) above the bottom, so the status line under it stays clear.
        int middle = (rects[0].X + rects[^1].Right) / 2;
        Assert.InRange(middle, w / 2 - 2, w / 2 + 2);
        Assert.InRange(h - rects[0].Bottom, (int)(72f * m.Pixel) - 2, (int)(72f * m.Pixel) + 2);
        Assert.True(rects[0].X >= 0 && rects[^1].Right <= w);
        // The bars over the slots.
        var health = m.ToPixels(hud.Screen.Find("healthBox")!.Rect);
        Assert.True(health.Bottom <= rects[0].Y);
        Assert.InRange(rects[0].Width, (int)(156f * m.Pixel) - 1, (int)(156f * m.Pixel) + 1);
    }
}
