// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The callbacks found as 1.1 finds them**. 1.1 resolves every callback once per class with
/// JNI's `GetMethodID` and the full `{name, signature}` of its callback table — the
/// return type included — and Vitomir's lookup does the same (<see cref="ScriptRuntime.TryInvoke"/>).
/// Before, it matched the name and the parameters only (and, for the oldest callbacks, public methods
/// only). This scan of **every class of the build** — each shipped script and the Codex classes they
/// extend — against all fifty-eight entries pins that the change moves exactly what it should: the two
/// shipped <c>void soaked</c>, and nothing else (the scan said the same).
/// </summary>
[Collection("scripts")]
public class CallbackLookupTests(Scripts s)
{
    /// <summary>The callbacks sent through the host's oldest helpers, which found public methods only until then.</summary>
    static readonly HashSet<string> PublicOnlyBefore =
        ["beginscene", "timer", "arrived", "clicked", "killed", "entered", "exited", "awaken",
         "convreached", "convchosen", "convended", "pathended", "endscene"];

    /// <summary>1.1's callback table: fifty-eight entries, soaked's at slot 42.</summary>
    [Fact]
    public void The_table_is_the_original_game_s_fifty_eight_with_soaked_returning_float_at_slot_42()
    {
        Assert.Equal(58, CallbackTable.All.Length);
        Assert.Equal(("soaked", "(IIFIFI)F"), CallbackTable.All[42]);
        Assert.Equal(0xd0, 0x28 + 4 * 42);
        Assert.Equal(3, CallbackTable.All.Count(e => e.Name == "cast"));
        Assert.True(CallbackTable.Holds("pickup", "(III)Z"));
        Assert.False(CallbackTable.Holds("soaked", "(IIFIFI)V"));
    }

    [Fact]
    public void Every_class_resolves_every_callback_as_before_but_the_two_void_soaked()
    {
        s.Ready();
        var rt = s.Runtime!;
        var moved = new List<string>();
        int classes = 0, found = 0;
        foreach (var name in rt.ClassNames())
        {
            if (rt.ClassOf(name) is not { } c) continue;
            classes++;
            foreach (var (callback, signature) in CallbackTable.All)
            {
                var types = ScriptRuntime.TypesOf(signature);
                var before = ByParameters(c, callback, types);
                bool was = before is not null
                           && (!PublicOnlyBefore.Contains(callback) || java.lang.reflect.Modifier.isPublic(before.getModifiers()));
                bool now = rt.Resolves(name, callback, signature);
                if (now) found++;
                if (was != now) moved.Add($"{name}.{callback}{signature}");
            }
        }
        Assert.True(classes > 400, $"only {classes} classes");
        Assert.True(found > 1000, $"only {found} callbacks resolved");
        Assert.Equal(["EffectDiscFortitude.soaked(IIFIFI)F", "EffectDiscSkinOfTheAdder.soaked(IIFIFI)F"], moved.Order());
    }

    /// <summary>A mistyped signature for a callback is refused, not silently never found.</summary>
    [Fact]
    public void A_callback_asked_with_a_signature_not_in_the_table_is_refused()
    {
        s.Ready();
        var script = s.Runtime!.NewThingRef("CodexThing", 0)!;
        Assert.Throws<ArgumentException>(() => s.Runtime.TryInvoke(script, "pickup", "(III)V", [null, null, null], out _));
        Assert.False(s.Runtime.TryInvoke(script, "pickup", "(III)Z", [null, null, null], out _));
    }

    /// <summary>The lookup: the name and the parameters, any return, up the class chain.</summary>
    static java.lang.reflect.Method? ByParameters(java.lang.Class? c, string name, java.lang.Class[] types)
    {
        for (; c is not null; c = c.getSuperclass())
            foreach (var m in c.getDeclaredMethods())
                if (m.getName() == name && Same(m.getParameterTypes(), types)) return m;
        return null;
    }

    static bool Same(java.lang.Class[] a, java.lang.Class[] b) =>
        a.Length == b.Length && a.Zip(b).All(p => p.First == p.Second);
}
