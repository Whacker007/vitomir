// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **A character's `damaged`, sent as 1.1 sends it**. 1.1's damage offers every blow
/// a character does not refuse to its scripts before the soak, and the answer is
/// the blow: at or below 0 nothing happens. Earlier, Vitomir had the seam as an unfed
/// parameter. No shipped class declares `damaged`, so these tests compile a test-only class of their
/// own (<see cref="DamageProbe"/>) and check that a real blow reaches it with its arguments, in its
/// order (the own script, then each holder with its id), and that its answers change what lands.
/// </summary>
[Collection("scripts")]
public class DamagedTests(Scripts s)
{
    const int Hold = 9;

    /// <summary>Christof with the probe as his own script and one holding him, and their GUIDs.</summary>
    (Place M, int Own, int Holder) Enter()
    {
        var m = Place.Enter(s, "P1_MON1");
        DamageProbe.Ready(s);
        typeof(WorldThing).GetProperty(nameof(WorldThing.Script))!.SetValue(m.Christof, DamageProbe.Class);
        m.Scenes.Created(m.Christof);                                    // made as a thing's script is, told `created`
        int own = int.Parse(DamageProbe.Heard().Single(h => h.EndsWith($" created {m.Christof.Guid}", StringComparison.Ordinal)).Split(' ')[0]);
        var holder = m.Scenes.Make(DamageProbe.Class, m.Christof)!;
        int held = s.Runtime!.GuidOf(holder);
        m.Christof.TakeHold(held, Hold);
        DamageProbe.Clear();
        var actor = m.Christof.Actor!;
        actor.Health = actor.MaxHealth = 500f;
        return (m, own, held);
    }

    [Fact]
    public void A_blow_goes_to_the_character_s_own_script_then_each_holder_and_the_last_answer_is_what_lands()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;
        var spider = m.Where.Spawn("ghoulSpider", m.Christof)!;
        DamageProbe.Answer(own, 7f);
        DamageProbe.Answer(holder, 3f);
        // Poison: nothing soaks it (the character's soak against it is 0), so what the scripts answer is what is lost.
        var harm = Damage.Apply(m.Christof, spider, HitKind.Effect, DamageType.Poison, 5f, new Engine.Ai.Rng(3));
        Assert.Equal(3f, harm.Taken);
        Assert.Equal(497f, m.Christof.Actor!.Health);
        // (thing, attacker, damage, type, capture): the own script with 0 and the blow as struck, then
        // the holder with its id and what the own script answered.
        Assert.Equal([$"{own} damaged {me} {spider.Guid} 5.0 {(int)DamageType.Poison} 0",
                      $"{holder} damaged {me} {spider.Guid} 7.0 {(int)DamageType.Poison} {Hold}"],
                     DamageProbe.Heard());
    }

    [Fact]
    public void An_answer_of_0_or_less_stops_the_blow_and_a_negative_one_ends_the_walk()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        int me = m.Christof.Guid;

        // The holder answers 0: the blow does nothing at all, no soak asked for.
        DamageProbe.Answer(holder, 0f);
        var harm = Damage.Apply(m.Christof, null, HitKind.Melee, DamageType.Normal, 5f, new Engine.Ai.Rng(3));
        Assert.Equal(new Harm(0f, false, false), harm);
        Assert.Equal(500f, m.Christof.Actor!.Health);

        // The own script answers below 0: the holder is never told, and nothing lands.
        DamageProbe.Clear();
        DamageProbe.Answer(own, -2f);
        Assert.Equal(0f, Damage.Apply(m.Christof, null, HitKind.Melee, DamageType.Normal, 5f, new Engine.Ai.Rng(3)).Taken);
        Assert.Equal([$"{own} damaged {me} 0 5.0 0 0"], DamageProbe.Heard());
        Assert.Equal(500f, m.Christof.Actor!.Health);
    }

    [Fact]
    public void A_refused_blow_tells_nobody_and_no_scripts_leave_the_blow_as_struck()
    {
        var (m, own, holder) = Enter();
        using var _ = m;
        m.Christof.Actor!.Flags |= Actor.Flag.Invulnerable;              // refused before the message
        Assert.True(Damage.Apply(m.Christof, null, HitKind.Melee, DamageType.Normal, 5f, new Engine.Ai.Rng(3)).Refused);
        Assert.Empty(DamageProbe.Heard());
        m.Christof.Actor!.Flags &= ~Actor.Flag.Invulnerable;

        m.Where.ItemScripts = null;                                       // no scripts: the blow stands
        Assert.Equal(5f, DamagedMessage.Offer(m.Christof, null, 5f, DamageType.Poison));
        Assert.Empty(DamageProbe.Heard());
    }

    /// <summary>
    /// No shipped class declares a `damaged` of any shape (every class of the build, its own methods
    /// and its parents'), so 1.1's game, and Vitomir's, is unchanged by sending it.
    /// </summary>
    [Fact]
    public void No_shipped_class_declares_damaged()
    {
        s.Ready();
        var rt = s.Runtime!;
        int classes = 0;
        var declaring = new List<string>();
        foreach (var name in rt.ClassNames())
        {
            if (name.StartsWith("Vitomir", StringComparison.Ordinal) || rt.ClassOf(name) is not { } c) continue;
            classes++;
            for (var k = c; k is not null; k = k.getSuperclass())
                foreach (var m in k.getDeclaredMethods())
                    if (m.getName() == "damaged") declaring.Add($"{k.getName()}.damaged");
        }
        Assert.True(classes > 400, $"only {classes} classes");
        Assert.Empty(declaring.Distinct());
    }
}

/// <summary>
/// The test-only class (never shipped, never in the game's tree, its class file deleted the moment it
/// is loaded): <c>VitomirDamageProbe</c> declares the engine's <c>float damaged(IIFII)</c>, writes each
/// call down as "guid damaged thing cause damage type capture" and answers what the test set for its
/// GUID (the damage it was told otherwise). It writes down `created` too, which is how a test learns
/// which instance is the character's own.
/// </summary>
static class DamageProbe
{
    public const string Class = "VitomirDamageProbe";

    const string Source = """
        public class VitomirDamageProbe extends Codex
        {
            public static java.util.Hashtable answers = new java.util.Hashtable();

            public static String heard = "";

            public VitomirDamageProbe() { }

            public void created(int thing) { heard = heard + GetGUID() + " created " + thing + ";"; }

            public float damaged(int guid, int cause, float damage, int type, int captureID)
            {
                heard = heard + GetGUID() + " damaged " + guid + " " + cause + " "
                    + damage + " " + type + " " + captureID + ";";
                Object a = answers.get(new Integer(GetGUID()));
                return a == null ? damage : ((Float)a).floatValue();
            }
        }
        """;

    static readonly Lock gate = new();
    static java.lang.Class? probe;

    /// <summary>Compiles it into the build the runtime loads from, once a run.</summary>
    public static void Ready(Scripts s)
    {
        lock (gate) probe ??= Compile(s);
        Clear();
    }

    static java.lang.Class Compile(Scripts s)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-damage-probe-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Class + ".java");
        var built = Path.Combine(s.Runtime!.Build.OutputDir, Class + ".class");
        File.WriteAllText(file, Source);
        try
        {
            s.Compiler!.Recompile(file);
            return ikvm.runtime.Util.getClassFromObject(s.Runtime.Instantiate(Class));
        }
        finally
        {
            // Loaded now, so out of the shared cache at once (the build is counted class for source).
            File.Delete(built);
            Directory.Delete(dir, true);
        }
    }

    public static List<string> Heard() =>
        [.. ((string)probe!.getField("heard").get(null)).Split(';', StringSplitOptions.RemoveEmptyEntries)];

    public static void Clear()
    {
        probe!.getField("heard").set(null, "");
        ((java.util.Hashtable)probe!.getField("answers").get(null)).clear();
    }

    public static void Answer(int guid, float damage) =>
        ((java.util.Hashtable)probe!.getField("answers").get(null)).put(java.lang.Integer.valueOf(guid), java.lang.Float.valueOf(damage));
}
