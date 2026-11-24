// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// A save with the game's own scripts in it: what `save()` wrote, the timers still to fire, what is
/// captured — and a load that puts them back in 1.1's order, `beginscene` last.
/// </summary>
[Collection("scripts")]
public partial class SaveScriptTests(Scripts s)
{
    sealed class Game : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required ChronicleSet Chronicles { get; init; }
        public required Location Place { get; init; }
        public required Session Session { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required Simulation Sim { get; init; }
        public void Step(int frames = 1) { for (int i = 0; i < frames; i++) Sim.Step(1f / 30f); }
        public void Dispose() { Scenes.Leave(); Files.Dispose(); }
    }


    Game Begin(SaveGame? from = null, Action<CodexScenes>? before = null,
               string level = "P1_OLDT", string scene = "OLDT_7_1", string chronicle = "Prague")
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        Location place;
        ScriptRestore? restore = null;
        WorldThing? steered = null;
        if (from is null)
        {
            place = Location.Load(files, level, sky: false, scene: scene, player: "Christof");
            place.Awaken(new AiClasses(files), 1, effects, powers);
        }
        else
        {
            SaveApply.Chronicle(set[from.Header.Chronicle]!, from.Chronicle!);
            var world = from.World[from.Header.Location];
            place = Location.Load(files, world.Level, sky: false, scene: world.Scene.Length > 0 ? world.Scene : null,
                                  player: "Christof", entrance: world.Entrance);
            restore = SaveApply.Place(place, world, effects, powers);
            steered = SaveApply.Party(place, from.Party, restore, effects, powers);
            var minds = place.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);
            if (steered is not null) minds.Steered = steered;
            place.Milliseconds = (int)from.Header.ClockMs;
        }
        var session = new Session { Here = place, Chronicle = set[chronicle] };
        place.Session = session;
        if (from is not null) SaveApply.Session(session, from.Session, place);
        var camera = new WorldCamera();
        camera.Start(place.Player!.Thing);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = talk, Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        if (from is not null)
        {
            scenes.Reset();
            if (from.Chronicle is { } c) { scenes.ArmChronicle(c.Scripts); scenes.WeatherEffect = c.Weather ?? ""; }
            scenes.Arm(place, restore!);
        }
        before?.Invoke(scenes);
        scenes.Enter(place);
        var sim = new Simulation(place) { Scripts = scenes, Camera = camera, Talk = talk };
        if (from is not null) sim.Resume(from.Header.ClockMs / 1000f);
        return new Game { Files = files, Chronicles = set, Place = place, Session = session, Scenes = scenes, Sim = sim };
    }

    static Dictionary<string, string> Texts(SaveGame save)
    {
        save.Header.Saved = "";
        return SaveArchive.Parts(save).ToDictionary(p => p.Name, p => p.Text);
    }

    /// <summary>
    /// The first Szlachta's trigger (`OLDT_7_1` [26], `ActorAIOff_TriggerOn`) is walked into and the
    /// game saved before its timer fires. The script's `save` keeps `bTriggered` and the timer is
    /// kept pending; the load gives both back, so the Szlachta wakes after the load exactly as it
    /// would have without one. With the timer lost it would sleep for ever — `beginscene` no longer
    /// pauses it (the script remembers it was triggered), and nothing else would wake it.
    /// </summary>
    [Fact]
    public void A_trigger_walked_into_before_a_save_still_wakes_its_Szlachta_after_the_load()
    {
        SaveGame saved;
        using (var g = Begin())
        {
            var first = g.Place.Things.First(t => t.Template.Equals("szlachta", StringComparison.OrdinalIgnoreCase));
            Assert.True(first.Actor!.Has(Actor.Flag.AiPaused));
            var christof = g.Place.Player!.Thing;
            g.Place.Move(christof, new Vector3(1404f, 1728f, christof.Position.Z));
            // Triggered, and the timer set — not yet fired.
            Assert.True(first.Actor.Has(Actor.Flag.AiPaused));
            saved = SaveCapture.Take(g.Place, g.Session);
            var world = saved.World[saved.Header.Location];
            string region = world.Things.First(kv => kv.Value.Script?.Class == "ActorAIOff_TriggerOn"
                && kv.Value.Script.Values.Count == 1 && kv.Value.Script.Values[0].Bool == true).Key;
            Assert.Contains(world.Timers, t => t.Script == region);
            g.Place.Move(christof, new Vector3(0, 0, christof.Position.Z));
        }

        string zip = Path.Combine(Path.GetTempPath(), $"vitomir-scripts-{Guid.NewGuid():n}{SaveArchive.Extension}");
        try
        {
            SaveArchive.Write(saved, zip);
            var read = SaveArchive.Read(zip);
            using var back = Begin(read);
            var szlachta = back.Place.Things.First(t => t.Template.Equals("szlachta", StringComparison.OrdinalIgnoreCase));
            Assert.True(szlachta.Actor!.Has(Actor.Flag.AiPaused), "the save kept it paused");
            back.Step(3);
            Assert.False(szlachta.Actor.Has(Actor.Flag.AiPaused), "the restored timer woke it");

            // And saving the loaded game again gives the same file, scripts, timers and all —
            // taken before any time has passed, so it is the same moment.
        }
        finally { File.Delete(zip); }
    }

    /// <summary>
    /// What the scripts keep comes back as it was: every thing's `save()` output, every capture,
    /// every timer, the chronicle's own. **Only the scripts' part** is compared, because a load is
    /// not a pause in 1.1 either: `beginscene` runs again after it, and the shipped scripts do
    /// things in it — a `Pedestrian` is given a random draught of blood every time its scene
    /// begins, and one found dead is revived (`Pedestrian.java`).
    /// </summary>
    [Fact]
    public void What_the_scripts_keep_comes_back_as_it_was()
    {
        SaveGame first;
        using (var g = Begin())
        {
            g.Step(10);
            first = SaveCapture.Take(g.Place, g.Session);
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        var second = SaveCapture.Take(back.Place, back.Session);
        string a = Scripts(first), b = Scripts(second);
        Same("scripts", a, b);
        Assert.Contains("\"class\"", a);

        // Every timer is back — and one no `beginscene` re-arms has exactly the time it had left.
        // (Timers stack, as 1.1's do — a second `SetTimer` with the same id is a second
        // timer — so this also pins that a load leaves each script with the timers it had, no more.)
        var before = first.World.Values.Single().Timers;
        var after = second.World.Values.Single().Timers;
        Assert.Equal(before.Select(t => (t.Script, t.Id)).Order(), after.Select(t => (t.Script, t.Id)).Order());
        var scene = before.Single(t => t.Script == "OLDT_7_1#16");
        Assert.Equal(scene.InMs, after.Single(t => t.Script == "OLDT_7_1#16").InMs);
    }

    /// <summary>
    /// **A party member's running discipline keeps its script's words across a load**. Celerity
    /// on Christof: `EffectDiscCelerity` saves one word, the GUID of the trail it spawned and hung on
    /// him. Earlier, the load built the effect's script again but never gave it back what it saved —
    /// the party's scripts were registered with a restore nobody asked — so the script came back with
    /// its fields as its constructor left them.
    ///
    /// The cast rolls in its script — `Discipline.Fizzled`, `Math.random() &lt; (5 − level) / 100` —
    /// and until then that was the Java runtime's generator seeded from the clock, so one run in twenty
    /// the cast fizzled, no effect went on, and this failed (seen three times, always
    /// passing on a rerun). The fixture now starts the scripts' stream at a fixed seed for every test
    /// (<see cref="Scripts.Ready"/>, <see cref="ScriptRandom"/>), and the cast is pinned to take.
    /// </summary>
    [Fact]
    public void A_party_member_s_running_discipline_keeps_what_its_script_saved()
    {
        SaveGame first;
        int trail;
        using (var g = Begin())
        {
            var christof = g.Place.Player!.Thing;
            var powers = new DisciplineDefinitions(g.Files);
            var celerity = DisciplineBook.SetLevel(christof, powers, "Celerity", 0)!;
            Assert.Equal(OrderResult.Started, Cast.Order(christof, null, null, celerity, InputModifier.Space));
            g.Step(90);
            var running = christof.Effects.All.First(e => e.Script is not null && !e.Spent);
            first = SaveCapture.Take(g.Place, g.Session);
            var kept = first.Party.Members[0].Effects!.Single(e => e.Guid == running.Guid).Script;
            Assert.NotNull(kept);
            trail = kept!.Values[0].Int ?? 0;
            Assert.NotEqual(0, trail);
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        var him = back.Place.Player!.Thing;
        var effect = him.Effects.All.First(e => e.Script is not null && !e.Spent);
        var words = back.Scenes.SaveObject(effect.Script!);
        Assert.Equal(trail, words!.Values[0].Int);
        // The trail itself is SAVE NEVER (`Global.not`: THINGFLAGS 0x21), so no save keeps the thing — in
        // 1.1 either, where the word then names a thing that is not there. Here the load makes
        // it again under that number from the effect's record (the repair `loadedeffects`).
        Assert.Equal("celerityTrail", back.Place.Thing(trail)?.Template);
        Assert.Same(him, back.Place.Thing(trail)!.AttachedTo);
    }

    /// <summary>
    /// **A timer that comes due while the party is away is thrown away** (`CodexScenes.Away.cs`,
    /// a due timer fires only if its script's thing is in the place the party is in). The
    /// scene timer of `OLDT_7_1` is pending; the party leaves Old Town, keeping it loaded, and comes
    /// back after it was due — it never fires, and is no longer pending.
    /// </summary>
    [Fact]
    public void A_timer_that_comes_due_while_the_party_is_away_is_thrown_away()
    {
        using var g = Begin();
        g.Step(10);
        var before = SaveCapture.Take(g.Place, g.Session).World.Values.Single().Timers.Single(t => t.Script == "OLDT_7_1#16");
        g.Scenes.Leave(0, keep: true);
        g.Place.Milliseconds += before.InMs + 1000;                 // the game's clock, where Resume puts it
        g.Scenes.Enter(g.Place);
        var after = SaveCapture.Take(g.Place, g.Session).World.Values.Single().Timers;
        Assert.DoesNotContain(after, t => t.Script == "OLDT_7_1#16");
    }

    /// <summary>The same timer, the party back before it is due: it keeps its own time on the game's clock.</summary>
    [Fact]
    public void A_timer_still_to_come_when_the_party_is_back_keeps_its_time()
    {
        using var g = Begin();
        g.Step(10);
        var before = SaveCapture.Take(g.Place, g.Session).World.Values.Single().Timers.Single(t => t.Script == "OLDT_7_1#16");
        int away = before.InMs / 2;
        g.Scenes.Leave(0, keep: true);
        g.Place.Milliseconds += away;
        g.Scenes.Enter(g.Place);
        var after = SaveCapture.Take(g.Place, g.Session).World.Values.Single().Timers.Single(t => t.Script == "OLDT_7_1#16");
        Assert.InRange(after.InMs, before.InMs - away - 2, before.InMs - away + 2);
    }

    /// <summary>Every script's kept state, capture and timer in a save, as one text in a fixed order.</summary>
    static string Scripts(SaveGame save)
    {
        var o = Jsonc.Options;
        var sb = new System.Text.StringBuilder();
        foreach (var (name, place) in save.World.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var (id, t) in place.Things.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (t.Script is { } script) sb.Append(name).Append(' ').Append(id).Append(' ').AppendLine(System.Text.Json.JsonSerializer.Serialize(script, o));
                if (t.Captures is { } caps) sb.Append(name).Append(' ').Append(id).Append(" captures ").AppendLine(System.Text.Json.JsonSerializer.Serialize(caps, o));
            }
        }
        foreach (var (cls, script) in save.Chronicle?.Scripts ?? [])
            sb.Append("chronicle ").Append(cls).Append(' ').AppendLine(System.Text.Json.JsonSerializer.Serialize(script, o));
        return sb.ToString();
    }

    static void Same(string name, string expected, string actual)
    {
        if (expected == actual) return;
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
        int from = Math.Max(0, i - 25);
        string Around(string[] lines) => string.Join("\n", lines.Skip(from).Take(i - from + 4));
        Assert.Fail($"{name} differs at line {i + 1}:\n--- saved first\n{Around(e)}\n--- saved after the load\n{Around(a)}");
    }

    static string Write(SaveGame save)
    {
        string zip = Path.Combine(Path.GetTempPath(), $"vitomir-scripts-{Guid.NewGuid():n}{SaveArchive.Extension}");
        SaveArchive.Write(save, zip);
        return zip;
    }

    /// <summary>
    /// The autosave's moment: once the place is whole and before any script is told the
    /// scene has begun — so a save taken there loads with `beginscene` still to come — and never
    /// for a place a load brings back.
    /// </summary>
    [Fact]
    public void The_scene_start_hook_comes_before_beginscene_and_not_for_a_load()
    {
        var seen = new List<(bool Restored, int Timers)>();
        int after;
        SaveGame taken;
        using (var g = Begin(before: sc => sc.SceneStarting = (place, restored) =>
                   seen.Add((restored, sc.Save(place, guid => guid.ToString(), o => o.GetHashCode().ToString()).Timers.Count))))
        {
            after = g.Scenes.Save(g.Place, guid => guid.ToString(), o => o.GetHashCode().ToString()).Timers.Count;
            taken = SaveCapture.Take(g.Place, g.Session);
        }
        var first = Assert.Single(seen);
        Assert.False(first.Restored);
        // The scene's own `beginscene` arms timers (StingerSound's); none of them was there yet.
        Assert.True(after > first.Timers, $"{after} timers after the scene began, {first.Timers} at the hook");

        seen.Clear();
        using var back = Begin(taken, before: sc => sc.SceneStarting = (place, restored) => seen.Add((restored, 0)));
        Assert.True(Assert.Single(seen).Restored);
    }

    /// <summary>
    /// **A wolf saved and loaded is given back his own shape when the effect ends** (a play-test
    /// reloaded a save while shifted). `EffectDiscShapeOfTheBeast` keeps the shifter's GUID in a field
    /// and writes it in `save()`; the load makes the shape again and the effect's script,
    /// restored, still names the same character when it calls `ActorEndShapeShift`.
    /// </summary>
    [Fact]
    public void A_wolf_saved_and_loaded_is_given_his_own_shape_back_when_the_beast_ends()
    {
        SaveGame first;
        using (var g = Begin())
        {
            var christof = g.Place.Player!.Thing;
            var me = christof.Actor!;
            me.Type = ActorType.Vampire;
            me.Stat[(int)ActorStat.BloodPool] = 100f;
            me.SetBaseStat((int)ActorStat.Blood, 100f);
            me.Stat[(int)ActorStat.Blood] = 100f;
            me.Flags2 |= Actor.Flag2.ScrollCast;
            var powers = new DisciplineDefinitions(g.Files);
            var beast = DisciplineBook.SetLevel(christof, powers, "ShapeOfTheBeast", 0)!;
            Assert.Equal(OrderResult.Started, Cast.Order(christof, null, null, beast, InputModifier.Space));
            for (int i = 0; i < 150 && !ShapeShift.IsShifted(me); i++) g.Step();
            Assert.True(ShapeShift.IsShifted(me));
            g.Step(30);
            first = SaveCapture.Take(g.Place, g.Session);
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        var wolf = back.Place.Player!.Thing;
        Assert.True(ShapeShift.IsShifted(wolf.Actor!));
        Assert.Equal("Wolf.nod", wolf.Model, ignoreCase: true);
        Assert.NotNull(wolf.Effects.Find("ef_disc_shapeofthebeast"));
        for (int i = 0; i < 30 * 16 && wolf.Effects.Find("ef_disc_shapeofthebeast") is not null; i++) back.Step();
        Assert.Null(wolf.Effects.Find("ef_disc_shapeofthebeast"));
        Assert.False(ShapeShift.IsShifted(wolf.Actor!));
        Assert.Equal("christof.nod", wolf.Model, ignoreCase: true);
        Assert.Null(wolf.Effects.Find("ef_disc_shapeofthebeast3"));
    }

    /// <summary>
    /// **The repair `pendulumhome`** (in play, Stephansdom's swinging weight stopped across the
    /// walkway after its switch was pulled). `STE1_Pendulum` saves only whether the trap is on, and a
    /// load's `beginscene` starts a fresh swing from wherever the save caught the weight, so its switch
    /// then parks it there instead of at the place the scene put it. Saved here at the far end of the
    /// swing, as his was, and loaded: the weight is back at its own place, swings from there, and the
    /// switch parks it there. And a save already taken with the weight parked in the way is mended.
    /// </summary>
    [Fact]
    public void The_cathedral_s_swinging_weight_parks_at_home_after_a_load_taken_mid_swing()
    {
        static WorldThing Weight(Game g) => g.Place.Things.Single(t => t.Script == "STE1_Pendulum");
        static WorldThing Switch(Game g) => g.Place.Things.First(t => t.Template == "tracker_thing" && t.Captures.Count > 0);
        Vector3 home, homeTurn;
        SaveGame swinging;
        using (var g = Begin(level: "V1_STE1", scene: "STE1_21_1", chronicle: "Vienna"))
        {
            var weight = Weight(g);
            (home, homeTurn) = (weight.Position, weight.Orientation);
            g.Step(27);                                                   // 0.9 s into the first one-second swing
            Assert.True(Vector3.Distance(weight.Position, home) > 100f, "the weight is at the far end of its swing");
            swinging = SaveCapture.Take(g.Place, g.Session);
        }

        SaveGame parked;
        using (var back = Begin(swinging, level: "V1_STE1", scene: "STE1_21_1", chronicle: "Vienna"))
        {
            var weight = Weight(back);
            back.Step(1);
            Assert.True(Vector3.Distance(weight.Position, home) < 20f, $"it starts from home again, not from {weight.Position}");
            back.Step(40);
            back.Scenes.Clicked(Switch(back), back.Place.Player!.Thing);  // the switch on the far wall
            back.Step(150);
            Assert.Null(weight.Motion);
            Assert.True(Vector3.Distance(weight.Position, home) < 0.01f, $"parked at {weight.Position}, home is {home}");
            Assert.True(Vector3.Distance(weight.Orientation, homeTurn) < 0.01f);

            // A save already taken with it parked in the way (by an older build): mended by the load.
            weight.Orientation = homeTurn + new Vector3(60f, 0f, 0f);
            back.Place.Move(weight, home + new Vector3(0f, 132f, -9f));
            parked = SaveCapture.Take(back.Place, back.Session);
        }

        using var again = Begin(parked, level: "V1_STE1", scene: "STE1_21_1", chronicle: "Vienna");
        again.Step(30);
        Assert.Null(Weight(again).Motion);
        Assert.True(Vector3.Distance(Weight(again).Position, home) < 0.01f);
    }
}
