// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **`beginscene` and `endscene` down the world's list**. The scene's start
/// and end walk the world's thing list from its head and tell each thing's
/// own script; a thing enters at the head, so the newest thing's script
/// hears first. Earlier, Vitomir told the scripts in the order they were made. A test-only class,
/// <see cref="OrderProbe"/>, is hung on three things of the place and writes down what it hears
/// with its thing's GUID.
/// </summary>
[Collection("scripts")]
public class SceneOrderTests(Scripts s)
{
    /// <summary>Three things of the place with no script of their own, in the order they entered it, each given the probe.</summary>
    static WorldThing[] Probed(Place m)
    {
        var bare = m.Where.Things.Where(t => t.Script.Length == 0 && t.InWorld).ToList();
        Assert.True(bare.Count >= 3);
        WorldThing[] three = [bare[0], bare[bare.Count / 2], bare[^1]];
        foreach (var t in three)
        {
            OrderProbe.Give(t);
            m.Scenes.Created(t);
        }
        OrderProbe.Clear();
        return three;
    }

    [Fact]
    public void Beginscene_reaches_the_newest_thing_first()
    {
        using var m = Place.Enter(s, "P1_MON1");
        OrderProbe.Ready(s);
        var t = Probed(m);
        var (first, middle, last) = (t[0], t[1], t[2]);

        m.Scenes.Arrived(m.Christof.Guid, joined: false);         // as a player's arrival sends it

        Assert.Equal([$"beginscene {last.Guid}", $"beginscene {middle.Guid}", $"beginscene {first.Guid}"], OrderProbe.Heard());
    }

    [Fact]
    public void Endscene_reaches_the_newest_thing_first()
    {
        using var m = Place.Enter(s, "P1_MON1");
        OrderProbe.Ready(s);
        var t = Probed(m);
        var (first, middle, last) = (t[0], t[1], t[2]);

        m.Scenes.LeftBy(m.Christof.Guid);                          // the scene's end for a player leaving

        Assert.Equal([$"endscene {last.Guid}", $"endscene {middle.Guid}", $"endscene {first.Guid}"], OrderProbe.Heard());
    }

    /// <summary>
    /// A thing taken out of the world and put back (then entered again) is at the head of the
    /// list again, and its script is the first told — whatever order the scripts were made in.
    /// </summary>
    [Fact]
    public void A_thing_put_back_into_the_world_hears_first()
    {
        using var m = Place.Enter(s, "P1_MON1");
        OrderProbe.Ready(s);
        var t = Probed(m);
        var (first, middle, last) = (t[0], t[1], t[2]);

        Assert.True(m.Where.Withdraw(first));
        m.Where.Enter(first);
        m.Scenes.Arrived(m.Christof.Guid, joined: false);

        Assert.Equal([$"beginscene {first.Guid}", $"beginscene {last.Guid}", $"beginscene {middle.Guid}"], OrderProbe.Heard());
    }
}

/// <summary>
/// The test-only script class the scene-order tests compile into the scripts' build (never
/// shipped, never in the game's tree, its class file deleted again the moment it is loaded): it
/// keeps the GUID `created` gave it and writes each `beginscene` and `endscene` it hears with that
/// GUID into a static log.
/// </summary>
static class OrderProbe
{
    public const string Class = "VitomirOrderProbe";

    const string Source = """
        public class VitomirOrderProbe extends Codex
        {
            public static String heard = "";
            int mine;

            public VitomirOrderProbe() { }

            public void created(int thing) { mine = thing; }
            public void beginscene(int client, int captureID) { heard = heard + "beginscene " + mine + ";"; }
            public void endscene(int client, int captureID, int exitNum) { heard = heard + "endscene " + mine + ";"; }
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
                var dir = Path.Combine(Path.GetTempPath(), $"vitomir-order-probe-{Environment.ProcessId}");
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

    public static void Clear() => cls!.getField("heard").set(null, "");
}
