// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The five callbacks only a mod answers, sent as 1.1 sends them**, and **a
/// carried item's timers thrown away**. No shipped class declares `keytag`,
/// `beginswing`, `actorworn`, `actorcanuse` or `actorunworn`, so these tests compile a class of
/// their own — <see cref="ModProbe"/>, a test-only script that writes down every call it hears
/// with its own GUID and arguments — into the scripts' build, and hang it on Christof twice: as his
/// own script (capture 0) and as a script holding him (capture 5). Each test pins who hears what,
/// in which order, with which arguments, and what an answer does — including the two senders whose
/// second loop calls the wrong method (`beginswing`, `actorunworn`), which
/// Vitomir keeps.
/// </summary>
[Collection("scripts")]
public class ModCallbackTests(Scripts s)
{
    const int Hold = 5;

    /// <summary>Christof with the probe as his own script and as a hold on him, and their GUIDs.</summary>
    (Place M, int Own, int Holder) Enter()
    {
        var m = Place.Enter(s, "P1_MON1");
        ModProbe.Ready(s);
        ModProbe.Give(m.Christof);
        m.Scenes.Created(m.Christof);                                   // made as a thing's script is, told `created`
        int own = int.Parse(ModProbe.Heard().Single(h => h.EndsWith($" created {m.Christof.Guid}", StringComparison.Ordinal)).Split(' ')[0]);
        var holder = m.Scenes.Make(ModProbe.Class, m.Christof)!;
        int held = s.Runtime!.GuidOf(holder);
        m.Christof.TakeHold(held, Hold);
        ModProbe.Clear();
        return (m, own, held);
    }

    static WorldThing Carried(Place m, string template)
    {
        var thing = m.Where.Make(template)!;
        m.Christof.Actor!.Inventory.Add(thing.Item!);
        thing.Owner = m.Christof;
        return thing;
    }

    [Fact]
    public void An_equip_asks_the_wearer_actorcanuse_and_tells_it_actorworn_after_worn()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;
        var dagger = Carried(m, "Dagger");
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, dagger.Item!, Slot.None, m.Effects, m.Where.Milliseconds));
        int it = dagger.Guid;
        // The use test: the user's own script, then its holder, each with its id; then, with
        // the dagger on, the wear: the same two told `actorworn`. The actor comes first.
        Assert.Equal([$"{own} actorcanuse {me} {it} 0", $"{holder} actorcanuse {me} {it} {Hold}",
                      $"{own} actorworn {me} {it} 0", $"{holder} actorworn {me} {it} {Hold}"], ModProbe.Heard());
    }

    [Fact]
    public void A_false_actorcanuse_refuses_the_equip_and_asks_nobody_after_it()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;
        var dagger = Carried(m, "Dagger");
        int it = dagger.Guid;

        ModProbe.Refuse(own);
        Assert.Equal(EquipResult.Restricted, Equipment.Equip(m.Christof, dagger.Item!, Slot.None, m.Effects, m.Where.Milliseconds));
        Assert.Equal([$"{own} actorcanuse {me} {it} 0"], ModProbe.Heard());       // the holder is not asked

        ModProbe.Clear();
        ModProbe.Refuse(holder);
        Assert.False(Equipment.CanUse(m.Christof, dagger.Item!));
        Assert.Equal([$"{own} actorcanuse {me} {it} 0", $"{holder} actorcanuse {me} {it} {Hold}"], ModProbe.Heard());
        Assert.Equal(0, dagger.Item!.Location & Slot.Worn);                               // not on
    }

    [Fact]
    public void A_false_actorunworn_keeps_it_on_and_a_holder_hears_actorworn_instead_as_in_1_1()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;
        var dagger = Carried(m, "Dagger");
        int it = dagger.Guid;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(m.Christof, dagger.Item!, Slot.None, m.Effects, m.Where.Milliseconds));

        // The wearer's own script says no: it stays on, and the holder is not asked.
        ModProbe.Clear();
        ModProbe.Refuse(own);
        Assert.False(Equipment.Unequip(m.Christof, dagger.Item!, m.Effects));
        Assert.Equal([$"{own} actorunworn {me} {it} 0"], ModProbe.Heard());
        Assert.NotEqual(0, dagger.Item!.Location & Slot.Worn);

        // The holder would say no — but 1.1's second loop calls its `actorworn`, never
        // its `actorunworn`, and the answer is taken as yes (ours): it comes off.
        ModProbe.Clear();
        ModProbe.Refuse(holder);
        Assert.True(Equipment.Unequip(m.Christof, dagger.Item!, m.Effects));
        Assert.Equal([$"{own} actorunworn {me} {it} 0", $"{holder} actorworn {me} {it} {Hold}"], ModProbe.Heard());
        Assert.Equal(Slot.Inventory, dagger.Item!.Location);
    }

    [Fact]
    public void A_swing_at_a_thing_tells_the_swinger_beginswing_and_again_for_each_holder_as_in_1_1()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;
        var spider = m.Where.Spawn("ghoulSpider", m.Christof)!;
        var weapon = m.Christof.Actor!.Weapon!;
        Actions.Strike(m.Christof, spider, weapon, m.Where.Milliseconds, m.Where.Motions);
        var heard = ModProbe.Heard();
        // The swinger's own script with capture 0; then, for the hold whose class declares
        // it, the **swinger's own script again** with the hold's id — the holder hears nothing.
        Assert.Equal(2, heard.Count);
        var first = heard[0].Split(' ');
        Assert.Equal([$"{own}", "beginswing", $"{me}", $"{spider.Guid}"], first[..4]);
        Assert.Equal("0", first[5]);
        Assert.Equal($"{own} beginswing {me} {spider.Guid} {first[4]} {Hold}", heard[1]);
        // The third is how long the swing's motion runs, in milliseconds.
        Assert.True(int.Parse(first[4]) > 0, heard[0]);
        Assert.DoesNotContain(heard, h => h.StartsWith($"{holder} ", StringComparison.Ordinal));

        // With no motions to play (no model), the swing time is 0; a swing at the ground tells nobody.
        ModProbe.Clear();
        m.Christof.Actor!.BusyUntil = 0;
        Actions.Strike(m.Christof, spider, weapon, m.Where.Milliseconds);
        Assert.Equal($"{own} beginswing {me} {spider.Guid} 0 0", ModProbe.Heard()[0]);
        ModProbe.Clear();
        Actions.StrikeAt(m.Christof, spider.Position, weapon, m.Where.Milliseconds, m.Where.Motions);
        Assert.Empty(ModProbe.Heard());
    }

    [Fact]
    public void An_animation_s_Codex_tags_send_keytag_numbered_from_0_and_the_other_tags_do_not()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;
        Assert.True(CodexTags.Crossed(m.Christof, new NadTag(0f, 14)));                // `Codex2`
        Assert.Equal([$"{own} keytag {me} 1 0", $"{holder} keytag {me} 1 {Hold}"], ModProbe.Heard());
        ModProbe.Clear();
        Assert.True(CodexTags.Crossed(m.Christof, new NadTag(0f, 13)));
        Assert.True(CodexTags.Crossed(m.Christof, new NadTag(0f, 15)));
        Assert.Equal([$"{own} keytag {me} 0 0", $"{holder} keytag {me} 0 {Hold}",
                      $"{own} keytag {me} 2 0", $"{holder} keytag {me} 2 {Hold}"], ModProbe.Heard());
        ModProbe.Clear();
        Assert.False(CodexTags.Crossed(m.Christof, new NadTag(0f, 12)));               // `Idle2`
        Assert.False(CodexTags.Crossed(m.Christof, new NadTag(0f, 16)));               // `Repeat`
        Assert.Empty(ModProbe.Heard());
    }

    [Fact]
    public void A_carried_item_s_timer_is_thrown_away_and_one_due_after_it_is_put_down_fires()
    {
        using var m = Place.Enter(s, "P1_MON1");
        ModProbe.Ready(s);
        var dagger = Carried(m, "Dagger");
        var script = m.Scenes.Make(ModProbe.Class, dagger)!;                         // the item's own script
        int g = s.Runtime!.GuidOf(script);
        ModProbe.Clear();
        void SetTimer(int ms, int id) =>
            Assert.True(Place.Bln(Place.Call(s, "Codex.SetTimer", script, g, ms, id, 0f, 0f, 0f, 0f)));

        // In the pack, out of every world: due, taken off the list, never fired.
        SetTimer(200, 7);
        m.Run(0.5f);
        Assert.Empty(ModProbe.Heard());

        // Set while carried, due once it lies on the floor: it fires — the test is made as it comes due.
        SetTimer(200, 8);
        Assert.Equal(GroundResult.Dropped, Ground.Drop(m.Where, m.Christof, dagger.Item!, m.Where.Milliseconds, scripts: m.Where.ItemScripts));
        Assert.True(dagger.InWorld);
        m.Run(0.5f);
        Assert.Equal([$"{g} timer 8"], ModProbe.Heard());
    }

    [Fact]
    public void A_network_host_fires_a_carried_item_s_timer()
    {
        using var m = Place.Enter(s, "P1_MON1");
        ModProbe.Ready(s);
        var set = ChronicleSet.Load(m.Files);
        var settings = GameSettings.For(set["MP_CurseDarkness"]!, "Anna's game", GameFlags.AutoStoryteller);
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        m.Scenes.Host(server);                                                        // a host fires every timer
        var dagger = Carried(m, "Dagger");
        var script = m.Scenes.Make(ModProbe.Class, dagger)!;
        int g = s.Runtime!.GuidOf(script);
        ModProbe.Clear();
        Assert.True(Place.Bln(Place.Call(s, "Codex.SetTimer", script, g, 200, 7, 0f, 0f, 0f, 0f)));
        m.Run(0.5f);
        Assert.Equal([$"{g} timer 7"], ModProbe.Heard());
    }

    [Fact]
    public void A_script_with_a_character_or_no_thing_keeps_its_timers()
    {
        using var m = Place.Enter(s, "P1_MON1");
        ModProbe.Ready(s);
        var mine = m.Scenes.Make(ModProbe.Class, m.Christof)!;                       // like an effect's
        int g = s.Runtime!.GuidOf(mine);
        ModProbe.Clear();
        Assert.True(Place.Bln(Place.Call(s, "Codex.SetTimer", mine, g, 100, 3, 0f, 0f, 0f, 0f)));
        m.Run(0.3f);
        Assert.Equal([$"{g} timer 3"], ModProbe.Heard());
    }
}

/// <summary>
/// The test-only script class the mod-callback tests compile into the scripts' build (never shipped,
/// never in the game's tree, and its class file deleted again the moment it is loaded): it declares every one of the five, plus `created` and `timer`, writes
/// each call into a static log as "guid callback args…", and answers the two boolean questions no
/// when its GUID is in <c>refusers</c>.
/// </summary>
static class ModProbe
{
    public const string Class = "VitomirModProbe";

    const string Source = """
        public class VitomirModProbe extends Codex
        {
            public static String heard = "";
            public static String refusers = " ";

            public VitomirModProbe() { }

            void hear(String what) { heard = heard + GetGUID() + " " + what + ";"; }
            boolean answer() { return refusers.indexOf(" " + GetGUID() + " ") < 0; }

            public void created(int thing) { hear("created " + thing); }
            public void timer(int id, float a, float b, float c, float d) { hear("timer " + id); }
            public void keytag(int thing, int n, int captureID) { hear("keytag " + thing + " " + n + " " + captureID); }
            public void beginswing(int actor, int target, int ms, int captureID) { hear("beginswing " + actor + " " + target + " " + ms + " " + captureID); }
            public void actorworn(int actor, int item, int captureID) { hear("actorworn " + actor + " " + item + " " + captureID); }
            public boolean actorcanuse(int actor, int item, int captureID) { hear("actorcanuse " + actor + " " + item + " " + captureID); return answer(); }
            public boolean actorunworn(int actor, int item, int captureID) { hear("actorunworn " + actor + " " + item + " " + captureID); return answer(); }
        }
        """;

    static readonly Lock gate = new();
    static java.lang.Class? cls;

    /// <summary>Compiles the class into the build the runtime loads from, once a run.</summary>
    public static void Ready(Scripts s)
    {
        lock (gate)
        {
            if (cls is null)
            {
                var dir = Path.Combine(Path.GetTempPath(), $"vitomir-mod-probe-{Environment.ProcessId}");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, Class + ".java");
                var built = Path.Combine(s.Runtime!.Build.OutputDir, Class + ".class");
                File.WriteAllText(file, Source);
                try
                {
                    s.Compiler!.Recompile(file);
                    cls = ikvm.runtime.Util.getClassFromObject(s.Runtime.Instantiate(Class));
                }
                finally
                {
                    // Loaded now, so out of the shared cache at once: the build is counted class for
                    // source (`The_players_scripts_compile`), and other runs share the folder.
                    File.Delete(built);
                    Directory.Delete(dir, true);
                }
            }
        }
        Clear();
    }

    /// <summary>Makes the probe <paramref name="thing"/>'s own script, as a SCRIPT tag would.</summary>
    public static void Give(WorldThing thing) =>
        typeof(WorldThing).GetProperty(nameof(WorldThing.Script))!.SetValue(thing, Class);

    public static List<string> Heard() =>
        [.. ((string)cls!.getField("heard").get(null)).Split(';', StringSplitOptions.RemoveEmptyEntries)];

    public static void Clear()
    {
        cls!.getField("heard").set(null, "");
        cls.getField("refusers").set(null, " ");
    }

    public static void Refuse(int guid) =>
        cls!.getField("refusers").set(null, (string)cls.getField("refusers").get(null) + guid + " ");
}
