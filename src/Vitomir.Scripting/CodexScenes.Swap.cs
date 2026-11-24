// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The live chapter swap as the scripts see it (World/ChapterSwap.cs).
//
// The old chapter's things are deleted, and a thing's deletion frees its script: the script's timers come off the list and its captures are released. It hears no
// `endscene` — the scene has not ended. The new chapter's things are made with their scripts, each
// constructed from its CODEXPARAMS against the new chapter's own list and told `created` as the thing
// is finalized, one thing after another in file order.
// None hears `beginscene`: that goes out when a scene next starts — for `CNVT_3_1`, the
// `Jump` back into the convent the same frame.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// A place's chapter has been swapped (<see cref="Location.SwapChapter"/>). The place the party
    /// is in has its old chapter's scripts freed and its new chapter's made and told `created` now;
    /// a place kept loaded while the party is elsewhere has the old ones freed now and the new ones
    /// made as the party next comes (<see cref="Location.AwaitingScripts"/>, ours — Vitomir's natives
    /// act on the place the party is in). A place with no scripts yet has nothing to free.
    /// </summary>
    public void Swapped(ChapterSwap swap)
    {
        var place = swap.Place;
        var gone = swap.Gone.Select(t => t.Guid).ToHashSet();
        int count = swap.To?.Things.Length ?? 0;
        if (ReferenceEquals(place, location))
        {
            DeleteNow(swap.Gone);                                                   // `destroyed`, then freed (CodexScenes.Destroyed.cs)
            foreach (var l in live.Where(l => gone.Contains(l.Thing)).ToArray()) Freed(l.Guid, place);
            live.RemoveAll(l => gone.Contains(l.Thing));
            thingGuids = Renumbered(thingGuids, swap);
            worldCount = swap.WorldCount;
            chapterCount = count;
            // Thing by thing: its script made from its parameters, then `created`.
            foreach (var t in swap.Arrived)
            {
                if (t.Script.Length == 0 || !place.AwaitingScripts.Remove(t)) continue;
                if (Attach(t, worldCount, chapterCount) is { } l) SayCreated(l, t);
            }
            return;
        }
        if (!kept.TryGetValue(place, out var k)) return;
        foreach (var l in k.Live.Where(l => gone.Contains(l.Thing)).ToArray())
        {
            k.Timers.RemoveAll(t => t.Guid == l.Guid);
            k.ScriptThing.Remove(l.Guid);
            Freed(l.Guid, place);
        }
        k.Live.RemoveAll(l => gone.Contains(l.Thing));
        kept[place] = k with { ThingGuids = Renumbered(k.ThingGuids, swap), WorldCount = swap.WorldCount, ChapterCount = count };
    }

    /// <summary>A script freed with its thing: its timers off the list, its captures released.</summary>
    void Freed(int script, Location place)
    {
        timers.RemoveAll(t => t.Guid == script);
        carriedTimers.RemoveAll(t => t.Timer.Guid == script);
        scriptThing.Remove(script);
        foreach (var thing in place.Things) thing.Captures.RemoveAll(c => c.Script == script);
    }

    /// <summary>
    /// The GUIDs a script's CODEXPARAMS are read from, by scene index: the level scene's as they were,
    /// then the new chapter's.
    /// </summary>
    static int[] Renumbered(int[] was, ChapterSwap swap)
    {
        var now = new int[swap.WorldCount + (swap.To?.Things.Length ?? 0)];
        Array.Copy(was, now, Math.Min(swap.WorldCount, was.Length));
        foreach (var t in swap.Arrived) now[t.Index] = t.Guid;
        return now;
    }

    /// <summary>
    /// As a place is entered: the new chapter things a swap left waiting are made by now (the
    /// entry's own pass over the things) and told `created` — which a place a load is bringing back
    /// does not do for its other things, but these were never finalized with it (the swap comes after
    /// the file).
    /// </summary>
    void Awaited(Location loc, List<(Live Script, WorldThing Thing)> made, bool told)
    {
        if (loc.AwaitingScripts.Count == 0) return;
        if (!told)
            foreach (var (l, thing) in made)
                if (loc.AwaitingScripts.Contains(thing)) SayCreated(l, thing);
        loc.AwaitingScripts.Clear();
    }
}
