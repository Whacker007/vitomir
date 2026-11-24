// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `soaked`, the soak message: what a character's scripts are told when
// a blow's soak roll comes out above nothing, and what they may do to it. Sent
// ("Yes"), as 1.1 sends it — which, with the shipped classes, is to nobody —
// and, under the Fix `soakedvoid`, to the two shipped handlers 1.1 never finds.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>The engine's signature for it, class slot 42 of the callback table.</summary>
    const string SoakedSig = "(IIFIFI)F";

    /// <summary>
    /// What the two shipped classes declare instead (<c>EffectDiscFortitude</c>,
    /// <c>EffectDiscSkinOfTheAdder</c>): the same name and parameters returning <c>void</c>, which 1.1's
    /// lookup never finds. Called only under the Fix <c>soakedvoid</c> (ours).
    /// </summary>
    const string SoakedVoidSig = "(IIFIFI)V";

    /// <summary>
    /// `soaked(thingGuid, attackerGuid, damage, damageType, soak, captureId) → float`: a
    /// character's soak roll came out other than 0, and its scripts may change it. The
    /// character's own script hears it first with a capture id of 0, and **its answer is
    /// the soak from then on**; then each script holding the character, front of the list first, with
    /// that hold's id — but before each, **a soak below 0 ends the walk and the answer is 0**.
    /// With no holds the own script's answer stands, negative or
    /// not. A class without the method is passed over and the soak stands. `damage` is the blow as it
    /// stands after `damaged`, `damageType` its type (the soak's
    /// index), and the attacker 0 when there is none.
    /// <para>The engine's answer is then taken off the blow: more than the blow soaks it all
    /// away, a negative soak adds to it.</para>
    /// <para>**No shipped class answers it**: the only two that declare `soaked` return `void`, so 1.1's
    /// `(IIFIFI)F` never resolves for them and a shipped game sends it to nobody — which is what
    /// Vitomir does too, since its lookup matches the return type (<see cref="ScriptRuntime.TryInvoke"/>).
    /// A mod's class declaring it with the `float` return is heard, as in 1.1.</para>
    /// <para>**The Fix `soakedvoid`** (<see cref="Fixes.SoakedVoid"/>, ours; *"call that bug
    /// fix, that's clearly not intended behavior"*): a class that declares the `void` shape instead is
    /// called too, in the same place of the walk, and — having no answer — leaves the soak as it was.
    /// That is how Fortitude's and Skin of the Adder's handlers come to flash their white shell on a
    /// blow soaked away whole, as their authors wrote them.</para>
    /// </summary>
    /// <returns>The soak after every script that answered.</returns>
    public float Soaked(WorldThing thing, WorldThing? attacker, float damage, int type, float soak)
    {
        bool voids = (thing.Place?.Fixes ?? Fixes.Current).SoakedVoid;
        foreach (var (name, script) in Own(thing).Take(1))
            soak = SoakedOne(name, script, 0, soak);
        foreach (var hold in thing.Captures.ToArray())
        {
            if (soak < 0f) return 0f;                                        // the walk stops
            if (ScriptOf(hold.Script) is { } s) soak = SoakedOne(s.Name, s.Script, hold.Id, soak);
        }
        return soak;

        float SoakedOne(string name, object script, int capture, float soak)
        {
            object?[] args = [CodexHost.Box(thing.Guid), CodexHost.Box(attacker?.Guid ?? 0), CodexHost.Box(damage),
                              CodexHost.Box(type), CodexHost.Box(soak), CodexHost.Box(capture)];
            try
            {
                if (runtime.TryInvoke(script, "soaked", SoakedSig, args, out var answer))
                    return answer is java.lang.Float f ? f.floatValue() : soak;
                // The Fix: the `void` handler 1.1 never finds, heard where the engine's would be; no answer, no change.
                if (voids) runtime.TryInvoke(script, "soaked", SoakedVoidSig, args, out _, offTable: true);
                return soak;
            }
            catch (Exception e)
            {
                // A script that throws answers what the JNI call leaves once the exception is cleared:
                // not read, and taken here as the soak unchanged (ours, as `damaged`).
                Engine.Log($"{name}.soaked failed: {ScriptException.From(name, e).JavaType}");
                return soak;
            }
        }
    }
}
