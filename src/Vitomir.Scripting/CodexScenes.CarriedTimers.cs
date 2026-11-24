// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A carried item's timers, thrown away as they come due (a comparison with 1.1's gap 3).
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// Whether a timer come due is **thrown away unfired** because its script's thing is carried.
    /// 1.1 fires a due timer only when the game is a network host, or its script has
    /// no thing (the chronicle's), or the thing is in the current world (the thing's
    /// world against the current location's) — and otherwise frees it, unfired. An item
    /// in a pack, on the cursor, in a chest or in a trader's stock is in no world (its world is null,
    /// <see cref="WorldThing.InWorld"/>), so its script's timers go this way: set while it lay on the
    /// floor or while it was carried, a timer due while it is carried is gone, and one due after it is
    /// put down again fires. A worn utility item is in the world (attached to the hand), so
    /// its timers fire.
    ///
    /// Vitomir fired them until then. The rest of the rule — a place the party has left — is kept by
    /// other means (each place's own stopped clock, <c>CodexScenes.Away.cs</c>). Unreachable with the
    /// shipped files: the one item class that sets a timer, `ItemAmulet`, refuses its own pick-up
    /// first; a mod's may not.
    /// </summary>
    bool ThrownAway(int scriptGuid) =>
        netServer is null
        && ThingOfScript(scriptGuid) is { Item: not null, InWorld: false } thing
        && (thing.Owner is not null || thing.Holder is not null);

    /// <summary>
    /// The thing a script belongs to (the Codex object's thing): a scene script's thing, or the
    /// owner an engine-made one was made for — an item's own script (<see cref="Own"/>), an effect's
    /// or a discipline's character. Null for the chronicle's and a scene-wide script.
    /// </summary>
    WorldThing? ThingOfScript(int scriptGuid)
    {
        if (live.FirstOrDefault(l => l.Guid == scriptGuid) is { } l)
            return l.Thing > 0 ? location?.Thing(l.Thing) : null;
        return loose.TryGetValue(scriptGuid, out var e) ? e.Owner : null;
    }
}
