// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The places a chronicle loads before its first, built as 1.1 builds them (`Preloads.cs`).
using Microsoft.Extensions.Logging;

namespace Vitomir.Engine.World;

public sealed partial class Campaign
{
    /// <summary>
    /// Places loaded early (<see cref="Preload"/>) that the party has not been in and that no journey or
    /// teleporter has written to their file since — which 1.1 has no `.nsw` for, by location index.
    /// </summary>
    readonly HashSet<int> unentered = [];

    /// <summary>Places loaded early whose scripts the script layer has not made yet (<see cref="TakePreloaded"/>).</summary>
    readonly List<Location> unopened = [];

    /// <summary>
    /// Whether a loaded place was loaded early and has no file yet: the party has not left it (a journey
    /// writes the place it leaves) and no teleporter's making wrote it.
    /// A save leaves such a place out — 1.1's save copies the files in <c>__Current__</c> and writes only
    /// the place the party is in — and letting it go leaves nothing to read back.
    /// </summary>
    public bool Unentered(int index) => unentered.Contains(index);

    /// <summary>
    /// **The places a chronicle loads before its first**: once the player is made,
    /// every location flagged <see cref="Preloads.Flag"/> is loaded in the chronicle's order (the location load
    /// for each) before the first is entered — for a new game and for a chronicle changed to
    /// (state 7). In Prague that is the haven, `P1_HAVN` with `HAVN_7_7`; London's
    /// bridge, Vienna's church and Prague2's haven are the others (all also never freed, flag 0x4).
    ///
    /// What "loaded" is, as in 1.1:
    /// <list type="bullet">
    /// <item>**Built whole.** The level and the scene: its things made on
    /// the numbers <see cref="Preloads"/> set aside, and each one finalized: its
    /// script constructed with its CODEXPARAMS, a character's born effects and fallback weapon
    /// — and its script told <c>created</c>. The harness makes the
    /// scripts (<see cref="TakePreloaded"/>); here the place is built by <see cref="Build"/> and woken.</item>
    /// <item>**Counted among the loaded.** Marked loaded, stamped with the clock and counted,
    /// so it is one of the six (<c>CampaignUnload.cs</c>) — first in, but never let go in any shipped
    /// chronicle, each such place being flagged never to be freed.</item>
    /// <item>**Not begun, and not run.** No <c>beginscene</c>: a scene begins only when the party enters.
    /// In single player the world tick runs the current location's world alone
    /// (the chronicle ticks the current one's; a network host ticks every loaded one), so nothing
    /// in it thinks or moves while the party is elsewhere, and a timer its <c>created</c> set that
    /// comes due meanwhile is thrown away unfired — the away clock of a place left
    /// (<c>CodexScenes.Away.cs</c>), from the moment it was loaded.</item>
    /// <item>**Entered as a place already loaded.** The first journey there finds its world (the location load
    /// does nothing for a location with one): no loading plaque (<see cref="Builds"/>), nothing built,
    /// nothing counted; the party is stood on the entrance and the scene begins.</item>
    /// <item>**Not in a save until it has a file** (<see cref="Unentered"/>): A new-game save
    /// `SaveGames\SP5` has the convent, the convent by day and Old Town (locations 0, 1, 2) and no
    /// haven (17), loaded since the start. A load does not load it again (a load runs no new-chronicle start):
    /// the first visit after a load builds it fresh, numbered from the counter then.</item>
    /// </list>
    /// <paramref name="start"/> is the location about to be entered, which is not loaded here. Answers the
    /// places loaded, in order.
    /// </summary>
    public IReadOnlyList<Location> Preload(int start)
    {
        unentered.Clear();
        unopened.Clear();
        var made = new List<Location>();
        for (int i = 0; i < Chronicle.Count; i++)
        {
            if (i == start || resident.ContainsKey(i)) continue;
            if (Chronicle.At(i) is not { } at || (at.Flags & Preloads.Flag) == 0 || at.LevelName.Length == 0) continue;
            string? scene = at.SceneName.Length > 0 ? at.SceneName : null;
            // On the numbers set aside for it (ThingGuids.Reserved), its stand-in on the leader's.
            var place = Numbered(() => Build(at.LevelName, scene, -1), saved: false);
            // Nobody of the party is in it: the stand-in the build stood at the first start goes out of
            // the world until a journey carries the party onto it (as Visit does, CampaignPlaces.cs).
            if (place.Player?.Thing is { InWorld: true } standIn) { Rpg.Utility.Leave(standIn); place.Withdraw(standIn); }
            place.Session ??= Here.Session;                                     // one game (ours: the harness sets it on entry)
            place.Milliseconds = Here.Milliseconds;                             // its clock stops at the game's now (ours)
            resident[i] = place;
            unentered.Add(i);
            unopened.Add(place);
            Loaded(i);                                                          // stamped and counted
            made.Add(place);
            log.LogInformation("load: {Place} loaded before the chronicle's first ({Level}{Scene}), {Things} things numbered {First}..{Last}",
                               at.Name, at.LevelName, scene is null ? "" : "/" + scene, place.Things.Count,
                               place.SceneGuids.First, place.SceneGuids.End - 1);
        }
        return made;
    }

    /// <summary>
    /// The places <see cref="Preload"/> built whose scripts are still to be made — handed over once,
    /// for the script layer to construct them and tell them <c>created</c> without beginning a scene
    /// (<c>CodexScenes.Preload</c>). The harness takes them after the old place's scripts have ended and
    /// before the first place's begin, 1.1's order.
    /// </summary>
    public IReadOnlyList<Location> TakePreloaded()
    {
        var taken = unopened.ToArray();
        unopened.Clear();
        return taken;
    }
}
