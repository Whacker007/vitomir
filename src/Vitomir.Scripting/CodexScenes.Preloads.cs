// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The scripts of a place a chronicle loads before its first (`World.Campaign.Preload`).
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// **A place loaded before the party goes there**: the scene's reading makes
    /// every thing's script with its CODEXPARAMS and finalizes the thing, which
    /// tells its script <c>created</c> — and **no scene begins**: <c>beginscene</c>
    /// waits for the party's arrival. So the scripts are made here, as a host's
    /// place opened for a player alone is (<see cref="Open"/>), and put aside as a place the party has
    /// left is: the first visit hands the same objects <c>beginscene</c> (<see cref="Enter"/>, a place
    /// come back to), and a timer set meanwhile that fell due while the party was elsewhere is thrown
    /// away unfired, as 1.1 throws away one whose thing is not in the party's place — the place's
    /// clock stopped the moment it was loaded (<see cref="LeftBehind"/>). Whatever place the scripts
    /// were working in is taken up again, and <paramref name="story"/>'s current location is left
    /// where it was: the chronicle's current location moves only when the party arrives.
    /// </summary>
    public void Preload(Location place, Chronicle? story)
    {
        if (Runs(place) || kept.ContainsKey(place)) return;
        int current = story?.Current ?? -1;
        try { Open(place); }
        finally { if (story is not null) story.Current = current; }
        if (!held.Remove(place, out var aside)) return;
        kept[place] = aside.State;
        LeftBehind(place);
    }

    /// <summary>Whether a place's scripts are made and put aside for the party's arrival — loaded early, or left.</summary>
    public bool Waiting(Location place) => kept.ContainsKey(place);

    /// <summary>The script objects put aside for a place (<see cref="Waiting"/>), or none — for the log and the tests.</summary>
    public IReadOnlyList<object> WaitingScripts(Location place) =>
        kept.TryGetValue(place, out var k) ? [.. k.Live.Select(l => l.Script)] : [];

    /// <summary>The current place's script objects, in the order they were made — for the log and the tests.</summary>
    public IReadOnlyList<object> RunningScripts => [.. live.Select(l => l.Script)];
}
