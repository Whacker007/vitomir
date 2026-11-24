// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The five callbacks only a mod answers ("Yes."): no shipped class declares
// `keytag`, `beginswing`, `actorworn`, `actorcanuse` or `actorunworn`, so in the shipped game nobody
// hears them — but 1.1 sends each, and a mod's class may declare it. Each is sent from where 1.1
// sends it, with its arguments and its two loops, and two of them with the engine's own mistake in
// the second loop. `soaked` stays unsent: its two shipped declarations return void, so 1.1's
// `(IIFIFI)F` id never resolves and it never calls them.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// `keytag(thingGuid, n, captureId)` — class slot 41:
    /// a thing's animation passed its `Codex1`, `Codex2` or `Codex3` tag (animation
    /// tags 13..15, `n` 0..2; <see cref="Vitomir.Engine.Anim.CodexTags"/>). The plain two loops: the
    /// thing's own script with capture 0, then every script holding the thing, newest
    /// hold first, each with its own id.
    /// </summary>
    void KeyTag(WorldThing thing, int n)
    {
        const string sig = "(III)V";
        object?[] Args(int capture) => [CodexHost.Box(thing.Guid), CodexHost.Box(n), CodexHost.Box(capture)];
        foreach (var (name, script) in Own(thing).Take(1)) Say(name, script, "keytag", sig, Args(0));
        foreach (var hold in thing.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s) Say(s.Name, s.Script, "keytag", sig, Args(hold.Id));
    }

    /// <summary>
    /// `actorworn(actorGuid, itemGuid, captureId)` — class slot 46: the
    /// **wearer's** scripts told an item went on, right after the item's own `worn` (the wear sends the
    /// item's message, then this one). The wearer's own script with capture 0,
    /// then every script holding the wearer with its id.
    /// </summary>
    void ActorWorn(WorldThing actor, WorldThing item) => Tell3("actorworn", actor, item);

    /// <summary>
    /// `actorcanuse(actorGuid, itemGuid, captureId) → boolean` — class slot 53:
    /// the **user's** scripts asked whether it may use or wear the item, in the use
    /// test once the item's own `canuse` has said yes. The shape every boolean
    /// sender shares (<see cref="Ask"/>): true to begin with; the user's own script with capture 0,
    /// and a false ends it; then each script holding the user, with its id, while the
    /// answer is true. A false refuses the use, the equip and the scroll alike.
    /// </summary>
    bool ActorCanUse(WorldThing actor, WorldThing item) => Ask("actorcanuse", actor, item);

    /// <summary>
    /// `actorunworn(actorGuid, itemGuid, captureId) → boolean` — class slot 47:
    /// the **wearer's** scripts asked, in the take-off, once the
    /// item's own `unworn` has said yes; a false keeps it on. The wearer's own script is asked with
    /// capture 0, and a false ends it.
    ///
    /// **The second loop carries the original game's mistake**, which we reproduce: a holder is
    /// picked for declaring `actorunworn`, but what is called is the method id of
    /// the holder's **`actorworn`** — through `CallBooleanMethod`. So a holding script
    /// never hears `actorunworn`: it hears `actorworn(actorGuid, itemGuid, captureId)` instead, as the
    /// item comes off, and cannot keep it on. What a `void` method answers through
    /// `CallBooleanMethod` is whatever the Java VM leaves behind; Vitomir takes it as **true**
    /// (ours), so the walk goes on to every holder and the item comes off. A holder declaring
    /// `actorunworn` but not `actorworn` has a null method called in 1.1, which crashes; that is
    /// logged and skipped.
    /// </summary>
    bool ActorUnworn(WorldThing actor, WorldThing item)
    {
        const string sig = "(III)Z", worn = "(III)V";
        foreach (var (name, script) in Own(actor).Take(1))
            if (!AskOne(name, script, "actorunworn", sig, actor, item, 0)) return false;
        foreach (var hold in actor.Captures.ToArray())
        {
            if (ScriptOf(hold.Script) is not { } holder || !runtime.Declares(holder.Script, "actorunworn", sig)) continue;
            if (!runtime.Declares(holder.Script, "actorworn", worn))
            {
                Engine.Log($"actorunworn: {holder.Name} holds {actor.Template} and has no actorworn for 1.1 to call (the original crashes here)");
                continue;
            }
            Say(holder.Name, holder.Script, "actorworn", worn,
                [CodexHost.Box(actor.Guid), CodexHost.Box(item.Guid), CodexHost.Box(hold.Id)]);   // the holder's actorworn, as 1.1 calls it
        }
        return true;
    }

    /// <summary>
    /// `beginswing(actorGuid, targetGuid, swingMs, captureId)` — class slot 43:
    /// a swing at a thing begins (after the swing's motion is
    /// played and a host's strike relayed, only when there is a target thing, never for a special
    /// move's throw). `swingMs` is how long the swing's motion runs in
    /// milliseconds (its frames over the rate, times 1000), −1 when the motion set has
    /// neither the swing nor `swordslash`, 0 with no model.
    ///
    /// The swinger's own script with capture 0. Then **the original game's mistake**, the one
    /// `hit` carries, which we reproduce: for each script holding the swinger whose class declares `beginswing`, the
    /// **swinger's own script is called again**, with that hold's id. With no script of its own,
    /// 1.1 dereferences null there; that is logged and skipped.
    /// </summary>
    void BeginSwing(WorldThing actor, WorldThing target, int swingMs)
    {
        const string sig = "(IIII)V";
        object?[] Args(int capture) =>
            [CodexHost.Box(actor.Guid), CodexHost.Box(target.Guid), CodexHost.Box(swingMs), CodexHost.Box(capture)];
        (string Name, object Script)? own = Own(actor) is { Length: > 0 } o ? o[0] : null;
        if (own is { } first) Say(first.Name, first.Script, "beginswing", sig, Args(0));
        foreach (var hold in actor.Captures.ToArray())
        {
            if (ScriptOf(hold.Script) is not { } holder || !runtime.Declares(holder.Script, "beginswing", sig)) continue;
            if (own is not { } mine)
            {
                Engine.Log($"beginswing: {holder.Name} holds {actor.Template}, which has no script (the original crashes here)");
                continue;
            }
            Say(mine.Name, mine.Script, "beginswing", sig, Args(hold.Id));
        }
    }
}
