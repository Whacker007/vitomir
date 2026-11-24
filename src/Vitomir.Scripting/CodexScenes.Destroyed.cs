// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A destroyed thing's scripts: `destroyed`, and the script freed (a comparison with 1.1).
//
// The destroy only marks a thing (`THING_FLAG_REMOVE`); the thing's own next tick
// deletes it, and the deletion's teardown does three things to the scripts,
// in this order, before anything else of the thing is freed:
//
//  1. `destroyed(thingGuid, captureId)` — class slot 16 (the 17th entry of
//     the callback table, `(II)V`) — to the thing's own script with capture id 0, then to
//     every script holding it, front of the list first, each with its own capture id. No shipped
//     class declares it, so in the shipped game nobody hears it; a mod's class can.
//  2. every hold on it released: those scripts stop hearing it.
//  3. its own script freed: the script's timers taken off the list unfired, every hold
//     it has on other things released, its object and constructor parameters let go.
//
// Earlier, Vitomir did none of this outside a chapter swap: a destroyed thing's script lived on,
// its timers fired and the levers it had captured went on reaching it.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>Things destroyed in the place the party is in, waiting for their deletion.</summary>
    readonly List<WorldThing> doomed = [];

    /// <summary><see cref="Location.Doomed"/>: a thing of this place destroyed, to be deleted at its tick.</summary>
    void Doom(WorldThing thing)
    {
        if (ReferenceEquals(thing.Place, location) && !doomed.Contains(thing)) doomed.Add(thing);
    }

    /// <summary>
    /// The deletions due at the things' tick (the thing's deleting destructor), in the order the things were destroyed. 1.1 deletes each at its own place in the
    /// world's walk — this frame for a thing after the destroyer in the list, the next for one before
    /// it; Vitomir, which runs the frame stage by stage, deletes them all as the scripts' part of the
    /// tick begins (ours). A thing destroyed in the timers' stage is deleted at the next frame's
    /// tick, as it is in 1.1.
    /// </summary>
    void DeleteDoomed()
    {
        if (doomed.Count == 0) return;
        var due = doomed.ToArray();
        doomed.Clear();
        foreach (var thing in due) Deleted(thing);
    }

    /// <summary>
    /// Deletes these at once, without waiting for the tick — the chapter swap's things, which 1.1
    /// destroys and deletes in the one pass (then the deletion).
    /// </summary>
    void DeleteNow(IEnumerable<WorldThing> things)
    {
        foreach (var thing in things)
        {
            doomed.Remove(thing);
            Deleted(thing);
        }
    }

    /// <summary>
    /// One thing's teardown as the scripts see it: `destroyed` to
    /// its own script and its holders, the holds on it released, then its own
    /// script freed (<see cref="Freed"/>).
    /// </summary>
    void Deleted(WorldThing thing)
    {
        var own = live.Where(l => l.Thing == thing.Guid).ToArray();
        // The thing's own script first, with capture id 0...
        foreach (var l in own) SayDestroyed(l.Name, l.Script, thing.Guid, 0);
        // ...then every capture, down the thing's list, each with its id.
        foreach (var hold in thing.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } holder) SayDestroyed(holder.Name, holder.Script, thing.Guid, hold.Id);
        // Every hold on it released.
        thing.Captures.Clear();
        // Its own script freed — its timers, its holds on other things, the script.
        if (location is null) return;
        foreach (var l in own)
        {
            Freed(l.Guid, location);
            live.Remove(l);
        }
    }

    /// <summary>`destroyed(thingGuid, captureId)`, class slot 16.</summary>
    void SayDestroyed(string name, object script, int thing, int captureId)
    {
        try { runtime.TryInvoke(script, "destroyed", "(II)V", [CodexHost.Box(thing), CodexHost.Box(captureId)], out _); }
        catch (Exception e) { Engine.Log($"{name}.destroyed failed: {ScriptException.From(name, e).JavaType}"); }
    }
}
