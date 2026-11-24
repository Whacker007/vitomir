// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `hit` sent as 1.1 sends it, and, under the Fix `berserker`, the Berserker weapon's strongest frenzy
// finished where its script throws.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>The Berserker weapon's script (`Codex\ItemWeaponBerserker.java`).</summary>
    public const string BerserkerClass = "ItemWeaponBerserker";

    /// <summary>What its `hit` gives the wielder, and the shell it shows, as the script writes them.</summary>
    const string BerserkerEffect = "ef_item_frenzy", BerserkerShell = "redCloudShell";

    /// <summary>
    /// One `hit` said to one script, as <c>Say</c> says any callback — a throw is logged and the blow
    /// goes on — except that under the Fix `berserker` the Berserker script's one throw is finished for it
    /// (<see cref="Berserker"/>).
    /// </summary>
    void SayHit(string what, object script, object?[] args, WorldThing wielder, WorldThing victim)
    {
        try { runtime.TryInvoke(script, "hit", "(IIFI)V", args, out _); }
        catch (Exception e)
        {
            var thrown = ScriptException.From(what, e);
            if (Berserker(what, thrown.JavaType, wielder, victim)) return;
            Engine.Log($"{what}.hit failed: {thrown.JavaType}");
        }
    }

    /// <summary>
    /// **The Fix `berserker`** (<see cref="Fixes.Berserker"/>, ours; *"Yes go ahead and fix it
    /// with a toggle"*). `ItemWeaponBerserker.hit` rolls whether the frenzy goes off, then its level, 0 to 5,
    /// and reads `FRENZY_DURATIONS[level]` — five entries — before it does anything else, so the only thing
    /// that can throw there is level 5, and it throws with nothing yet done and both rolls already drawn.
    /// This does the rest of the script as written, with the table's last length for the missing sixth:
    /// `ef_item_frenzy` on the wielder at level 5 (the effect table's level 5, strength 10; an effect's
    /// level is held to at most 5), the struck one as its creator, and the red cloud shell — through the same natives
    /// the script calls, so a network game's rules for them hold. Off, the throw is logged as in 1.1.
    /// </summary>
    bool Berserker(string what, string javaType, WorldThing wielder, WorldThing victim)
    {
        if (what != BerserkerClass || javaType != "java.lang.ArrayIndexOutOfBoundsException") return false;
        if (!(wielder.Place?.Fixes ?? location?.Fixes ?? Fixes.Current).Berserker) return false;
        int ms = BerserkerLongest();
        if (runtime.NewThingRef("CodexActor", wielder.Guid) is not { } damager) return false;
        Engine.Handler("CodexActor.AddActorEffectByLevel")?.Invoke(damager,
            [BerserkerEffect, CodexHost.Box(ms), CodexHost.Box(5), CodexHost.Box(victim.Guid), CodexHost.Box(0)]);
        Engine.Handler("CodexThing.SetShell")?.Invoke(damager,
            [BerserkerShell, CodexHost.Box(0x5000), CodexHost.Box(0f), CodexHost.Box(.9f), CodexHost.Box(1f), CodexHost.Box(1f)]);
        return true;
    }

    /// <summary>The last of the script's own `FRENZY_DURATIONS` (10,500 ms as shipped), read from the class.</summary>
    int BerserkerLongest()
    {
        try
        {
            if (runtime.ClassOf(BerserkerClass)?.getField("FRENZY_DURATIONS").get(null) is int[] { Length: > 0 } lengths)
                return lengths[^1];
        }
        catch (Exception) { }
        return 10500;
    }
}
