// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a load finds between the switches a save was played with and the ones in force (ours).
namespace Vitomir.Engine.Features;

/// <summary>
/// One difference between the rules features a save records and the ones in force when it is loaded — the item a
/// dialog will list (<i>"the save loading will report any differences in switches, fixes, mods,
/// etc. The player can have the option to turn them on or off, with the exception of any of those that are hard
/// dependencies for the save."</i>). The dialog is not built; a load says each in the console and the log.
/// </summary>
/// <param name="Id">The feature as the save names it (<c>fixes.gunreach</c>).</param>
/// <param name="Feature">Its declaration; null when this build has no such feature.</param>
/// <param name="InSave">Whether the save was played with it on.</param>
/// <param name="InForce">Whether it is on now, before anything is forced.</param>
/// <param name="Forced">Whether the save decides: a feature chosen at a new game only (<see cref="FeatureWhen.NewGame"/>)
/// is put in force as the save has it, and is not the player's to change for this game.</param>
/// <param name="Sentence">The difference in plain English.</param>
public sealed record SwitchDifference(string Id, Feature? Feature, bool InSave, bool InForce, bool Forced, string Sentence);

public sealed partial class FeatureRegistry
{
    /// <summary>
    /// The differences between a save's record and the features <paramref name="on"/>: one item for every
    /// feature that counts (<see cref="Feature.Counts"/>) and is on in one and off in the other, in the registry's
    /// order, then one for every name in the save this build does not know. **Empty when the save does not say** —
    /// one written before saves recorded their switches has no <paramref name="profile"/> and no
    /// <paramref name="rules"/>. A name the save holds that this build knows and does not count — a
    /// quality-of-life switch, which older saves named — is passed over: it is the player's own
    /// setting, not the save's, and no difference.
    /// </summary>
    /// <param name="profile">The save header's <c>profile</c>.</param>
    /// <param name="rules">The save header's <c>rules</c>.</param>
    public IReadOnlyList<SwitchDifference> Differences(string? profile, IReadOnlyCollection<string> rules, IEnumerable<Feature> on)
    {
        if (string.IsNullOrEmpty(profile) && rules.Count == 0) return [];
        var saved = new HashSet<string>(rules, StringComparer.OrdinalIgnoreCase);
        var now = new HashSet<Feature>(on);
        var found = new List<SwitchDifference>();
        foreach (var f in all)
        {
            if (!f.Counts) continue;
            bool inSave = saved.Contains(f.Id), inForce = now.Contains(f);
            if (inSave == inForce) continue;
            bool forced = f.When == FeatureWhen.NewGame;
            string was = inSave ? "on" : "off", here = inForce ? "on" : "off";
            found.Add(new SwitchDifference(f.Id, f, inSave, inForce, forced, forced
                ? $"the save was played with {f.Name} {was}, and it is chosen at a new game only: it is {was} for this game"
                : $"the save was played with {f.Name} {was}; it is {here} here"));
        }
        foreach (var id in rules)
            if (Find(id) is null)
                found.Add(new SwitchDifference(id, null, true, false, false, $"the save was played with {id} on, which this build does not have"));
        return found;
    }
}
