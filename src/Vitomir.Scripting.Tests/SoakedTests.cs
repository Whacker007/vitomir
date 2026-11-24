// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **`soaked`, sent as 1.1 sends it** (*"Yes"*), and **the Fix `soakedvoid`** (a
/// bug to fix, clearly not intended behaviour). 1.1 offers a character's soak to its
/// scripts but finds a `soaked` only when it returns `float`, and both shipped
/// ones return `void`. So these tests compile two test-only classes of their own (<see cref="SoakProbe"/>):
/// one with the engine's `float` shape, which must be heard with its arguments, in its order, its
/// answers taking effect; and one with the shipped `void` shape, which must go unheard unless the Fix
/// is on. Then the two shipped handlers themselves, whose white flash is what the Fix shows.
/// </summary>
[Collection("scripts")]
public class SoakedTests(Scripts s)
{
    const int Hold = 7;

    /// <summary>Christof with a probe of <paramref name="cls"/> as his own script and one holding him, and their GUIDs.</summary>
    (Place M, int Own, int Holder) Enter(string cls)
    {
        var m = Place.Enter(s, "P1_MON1");
        SoakProbe.Ready(s);
        typeof(WorldThing).GetProperty(nameof(WorldThing.Script))!.SetValue(m.Christof, cls);
        m.Scenes.Created(m.Christof);                                    // made as a thing's script is, told `created`
        int own = int.Parse(SoakProbe.Heard().Single(h => h.EndsWith($" created {m.Christof.Guid}", StringComparison.Ordinal)).Split(' ')[0]);
        var holder = m.Scenes.Make(cls, m.Christof)!;
        int held = s.Runtime!.GuidOf(holder);
        m.Christof.TakeHold(held, Hold);
        SoakProbe.Clear();
        return (m, own, held);
    }

    static float Soaked(Place m, WorldThing? by, float damage, int type, float soak) =>
        m.Where.ItemScripts!.Soaked!(m.Christof, by, damage, type, soak);

    [Fact]
    public void Soaked_goes_to_the_character_s_own_script_then_each_holder_with_its_id_and_each_answer_is_the_soak()
    {
        var (m, own, holder) = Enter(SoakProbe.Float);
        using var _ = m;
        int me = m.Christof.Guid;
        var spider = m.Where.Spawn("ghoulSpider", m.Christof)!;
        SoakProbe.Answer(own, 2.5f);
        SoakProbe.Answer(holder, 4f);
        Assert.Equal(4f, Soaked(m, spider, 6f, 2, 1.5f));
        // (thing, attacker, damage, type, soak, capture): the own script with 0, then the
        // holder with its id, told the soak the own script answered.
        Assert.Equal([$"{own} soaked {me} {spider.Guid} 6.0 2 1.5 0", $"{holder} soaked {me} {spider.Guid} 6.0 2 2.5 {Hold}"],
                     SoakProbe.Heard());
    }

    [Fact]
    public void A_negative_soak_ends_the_walk_at_0_before_a_holder_and_stands_with_none()
    {
        var (m, own, holder) = Enter(SoakProbe.Float);
        using var _ = m;
        int me = m.Christof.Guid;
        SoakProbe.Answer(own, -3f);
        Assert.Equal(0f, Soaked(m, null, 6f, 0, 1.5f));                        // the walk stops: 0
        Assert.Equal([$"{own} soaked {me} 0 6.0 0 1.5 0"], SoakProbe.Heard());  // the holder is not told

        SoakProbe.Clear();
        m.Christof.Captures.RemoveAll(c => c.Script == holder);
        SoakProbe.Answer(own, -3f);
        Assert.Equal(-3f, Soaked(m, null, 6f, 0, 1.5f));                       // no holds: the answer stands
    }

    [Fact]
    public void A_blow_s_soak_is_what_the_scripts_answer_and_a_soak_of_0_tells_nobody()
    {
        var (m, own, holder) = Enter(SoakProbe.Float);
        using var _ = m;
        var spider = m.Where.Spawn("ghoulSpider", m.Christof)!;
        var actor = m.Christof.Actor!;
        actor.Health = actor.MaxHealth = 500f;

        // The holder answers 0: nothing is kept off, and the whole blow lands.
        SoakProbe.Answer(holder, 0f);
        var harm = Damage.Apply(m.Christof, spider, HitKind.Melee, DamageType.Normal, 5f, new Engine.Ai.Rng(3));
        Assert.Equal(5f, harm.Taken);
        var heard = SoakProbe.Heard();
        Assert.Equal(2, heard.Count);
        var told = heard[0].Split(' ');
        Assert.Equal(["soaked", $"{m.Christof.Guid}", $"{spider.Guid}", "5.0", "0"], told[1..6]);
        Assert.True(float.Parse(told[6], System.Globalization.CultureInfo.InvariantCulture) > 0f, heard[0]);

        // The holder answers more than the blow: soaked away whole.
        SoakProbe.Clear();
        SoakProbe.Answer(holder, 50f);
        Assert.Equal(0f, Damage.Apply(m.Christof, spider, HitKind.Melee, DamageType.Normal, 5f, new Engine.Ai.Rng(3)).Taken);

        // Nothing to soak with (poison against no soak) rolls nothing, and nobody is told.
        SoakProbe.Clear();
        Assert.Equal(0f, SoakedMessage.Offer(m.Christof, spider, 5f, DamageType.Poison, 0f));
        Assert.Empty(SoakProbe.Heard());
    }

    [Fact]
    public void A_void_soaked_is_not_heard_as_in_1_1_and_is_under_soakedvoid_with_the_soak_unchanged()
    {
        var (m, own, holder) = Enter(SoakProbe.Void);
        using var _ = m;
        int me = m.Christof.Guid;
        Assert.Equal(1.5f, Soaked(m, null, 6f, 0, 1.5f));
        Assert.Empty(SoakProbe.Heard());                                          // 1.1: (IIFIFI)F never resolves

        m.Where.Fixes = new Fixes { SoakedVoid = true };
        Assert.Equal(1.5f, Soaked(m, null, 6f, 0, 1.5f));
        Assert.Equal([$"{own} voidsoaked {me} 0", $"{holder} voidsoaked {me} {Hold}"], SoakProbe.Heard());
    }

    /// <summary>
    /// **What the Fix shows**: the shipped handlers of Fortitude and Skin of the Adder, each holding its
    /// character (their `CaptureThing` in `effectstarted`), set a white shell — `SetShell("white",
    /// 0x5000, 0, 1, 1, …)`: in over a second, out over the next (0x4000), then gone — when the soak is
    /// above 0 and at least the blow. 1.1 never calls them; with the Fix it does, and a blow the soak
    /// falls short of flashes nothing.
    /// </summary>
    [Theory]
    [InlineData("ef_disc_fortitude")]
    [InlineData("ef_disc_skinoftheadder")]
    public void Under_soakedvoid_a_shipped_discipline_flashes_white_on_a_blow_soaked_away_whole(string effect)
    {
        using var m = Place.Enter(s, "P1_MON1");
        var me = s.Runtime!.NewThingRef("CodexActor", m.Christof.Guid);
        Assert.NotEqual(0, Place.Int(Place.Call(s, "CodexActor.AddActorEffectByLevel", me, effect, 60000, 1, m.Christof.Guid, 0, 0, 0f)));
        Assert.NotEmpty(m.Christof.Captures);                                     // the effect's script holds him
        var fx = m.Christof.Fx!;
        fx.EndShell();                                                            // the start's own shell, put away

        Assert.Equal(3f, Soaked(m, null, 1f, 0, 3f));
        Assert.Null(fx.Shell);                                                    // faithful: never called

        m.Where.Fixes = new Fixes { SoakedVoid = true };
        Assert.Equal(0.5f, Soaked(m, null, 1f, 0, 0.5f));                        // short of the blow: nothing
        Assert.Null(fx.Shell);
        Assert.Equal(3f, Soaked(m, null, 1f, 0, 3f));                            // the whole blow: the flash
        Assert.Equal("white", fx.Shell);
        Assert.True(fx.ShellRamping);
        Assert.Equal(ModelFx.ShellInOut, fx.ShellFlags & ModelFx.ShellInOut);
    }
}

/// <summary>
/// The two test-only classes (never shipped, never in the game's tree, their class files deleted the
/// moment they are loaded): <c>VitomirSoakProbe</c> declares the engine's <c>float soaked</c>, writes each
/// call down as "guid soaked thing cause damage type soak capture" and answers what the test set for
/// its GUID (the soak it was told otherwise); <c>VitomirVoidSoakProbe</c> declares the shipped
/// <c>void</c> shape and writes "guid voidsoaked thing capture". Both write down `created`, which is
/// how a test learns which of them is the character's own.
/// </summary>
static class SoakProbe
{
    public const string Float = "VitomirSoakProbe", Void = "VitomirVoidSoakProbe";

    const string FloatSource = """
        public class VitomirSoakProbe extends Codex
        {
            public static java.util.Hashtable answers = new java.util.Hashtable();

            public static String heard = "";

            public VitomirSoakProbe() { }

            public void created(int thing) { heard = heard + GetGUID() + " created " + thing + ";"; }

            public float soaked(int guid, int cause, float damage, int type, float soak, int captureID)
            {
                heard = heard + GetGUID() + " soaked " + guid + " " + cause + " "
                    + damage + " " + type + " " + soak + " " + captureID + ";";
                Object a = answers.get(new Integer(GetGUID()));
                return a == null ? soak : ((Float)a).floatValue();
            }
        }
        """;

    const string VoidSource = """
        public class VitomirVoidSoakProbe extends Codex
        {
            public static String heard = "";

            public VitomirVoidSoakProbe() { }

            public void created(int thing) { heard = heard + GetGUID() + " created " + thing + ";"; }

            public void soaked(int guid, int cause, float damage, int type, float soak, int captureID)
            {
                heard = heard + GetGUID() + " voidsoaked " + guid + " " + captureID + ";";
            }
        }
        """;

    static readonly Lock gate = new();
    static java.lang.Class? floats, voids;

    /// <summary>Compiles both into the build the runtime loads from, once a run.</summary>
    public static void Ready(Scripts s)
    {
        lock (gate)
        {
            voids ??= Compile(s, Void, VoidSource);
            floats ??= Compile(s, Float, FloatSource);
        }
        Clear();
    }

    static java.lang.Class Compile(Scripts s, string cls, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-soak-probe-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, cls + ".java");
        var built = Path.Combine(s.Runtime!.Build.OutputDir, cls + ".class");
        File.WriteAllText(file, source);
        try
        {
            s.Compiler!.Recompile(file);
            return ikvm.runtime.Util.getClassFromObject(s.Runtime.Instantiate(cls));
        }
        finally
        {
            // Loaded now, so out of the shared cache at once: the build is counted class for source
            // (`The_players_scripts_compile`), and other runs share the folder.
            File.Delete(built);
            Directory.Delete(dir, true);
        }
    }

    /// <summary>What both have heard, the float one's first (a test uses one of them).</summary>
    public static List<string> Heard() =>
        [.. ((string)floats!.getField("heard").get(null) + (string)voids!.getField("heard").get(null))
            .Split(';', StringSplitOptions.RemoveEmptyEntries)];

    public static void Clear()
    {
        floats!.getField("heard").set(null, "");
        voids!.getField("heard").set(null, "");
        ((java.util.Hashtable)floats!.getField("answers").get(null)).clear();
    }

    public static void Answer(int guid, float soak) =>
        ((java.util.Hashtable)floats!.getField("answers").get(null)).put(java.lang.Integer.valueOf(guid), java.lang.Float.valueOf(soak));
}
