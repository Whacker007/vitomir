// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a load does with the switches a save records (ours).
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Features;

/// <summary>
/// **A load, and the switches the save was played with** (ours; <i>"the save loading will
/// report any differences in switches, fixes, mods, etc. The player can have the option to turn them on or off,
/// with the exception of any of those that are hard dependencies for the save. I think Factorio has a good way of
/// doing this."</i>). A save's header names the rules features that were on when it was written
/// (<see cref="SaveHeader.Rules"/>); a load compares them with the ones in force and says each difference.
///
/// **The player's own switches stand** — a difference is reported, not acted on — with one exception: a feature
/// chosen at a new game only (<see cref="FeatureWhen.NewGame"/>) is the save's to decide, and is put in force as
/// the save has it. No such feature is declared yet. The dialog that lets the player choose is not built: the
/// list this returns is what it will show.
/// </summary>
public static class LoadSwitches
{
    /// <summary>
    /// Compares <paramref name="header"/>'s record with <paramref name="fixes"/> and <paramref name="qol"/>, says
    /// each difference as a line (<c>load: the save was played with gunreach on; it is off here</c>) to
    /// <paramref name="say"/> and to the log, and forces what the save holds the game to — in place, since the
    /// program holds on to the two registers. Nothing is said when the two agree or the save does not say.
    /// </summary>
    /// <param name="registry">The game's own unless a test says otherwise.</param>
    /// <returns>The differences, as found before anything was forced.</returns>
    public static IReadOnlyList<SwitchDifference> Report(SaveHeader header, Fixes fixes, Qol qol,
                                                         Action<string>? say = null, FeatureRegistry? registry = null)
    {
        registry ??= GameFeatures.Registry;
        var found = registry.Differences(header, fixes, qol);
        void Say(string line)
        {
            say?.Invoke(line);
            Diag.For("Vitomir").LogInformation("{Line}", line);
        }
        bool forced = false;
        foreach (var difference in found)
        {
            if (difference is { Forced: true, Feature: { } feature })
            {
                FeatureRegistry.Put(fixes, qol, feature, difference.InSave);
                forced = true;
            }
            Say("load: " + difference.Sentence);
        }
        // What the save holds the game to stays; one of the player's own switches that cannot stand beside it goes.
        if (forced)
            registry.Settle(fixes, qol, m => Say("load: " + m), held: f => f.When == FeatureWhen.NewGame);
        return found;
    }
}
