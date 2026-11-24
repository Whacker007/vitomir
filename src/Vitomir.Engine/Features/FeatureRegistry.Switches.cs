// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The registry's answers asked of the two registers of switches the game holds (ours).
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Features;

public sealed partial class FeatureRegistry
{
    /// <summary>
    /// The features on under a pair of registers: each Fix and quality-of-life switch by its name in its register,
    /// and every feature with no switch. (A feature of a kind that has no register yet is off.)
    /// </summary>
    public IEnumerable<Feature> On(Fixes fixes, Qol qol) => all.Where(f => f.Kind switch
    {
        FeatureKind.Fix => fixes[f.Name],
        FeatureKind.Qol => qol[f.Name],
        _ => !f.Switchable,
    });

    /// <summary>
    /// <see cref="Profile(IEnumerable{Feature})"/> of the switches in force, and of the rules file's numbers (<paramref name="numbers"/>,
    /// <see cref="Rules.GameRules.Current"/> when not given): a game whose rules differ from 1.1's is <see cref="Modded"/> too.
    /// </summary>
    public string Profile(Fixes fixes, Qol qol, Rules.GameRules? numbers = null) =>
        RulesFeatures(On(fixes, qol)).Count == 0 && RulesFileName(numbers) is null ? Faithful : Modded;

    /// <summary>
    /// <see cref="RulesOn(IEnumerable{Feature})"/> of the switches in force: what a save writes as its <c>rules</c>; and, when the rules
    /// file's numbers differ from 1.1's, their name last (<see cref="RulesFileName"/>), which this registry does not know, so a save
    /// read later is modded (<see cref="ProfileOf"/>).
    /// </summary>
    public IReadOnlyList<string> RulesOn(Fixes fixes, Qol qol, Rules.GameRules? numbers = null) =>
        RulesFileName(numbers) is { } file ? [.. RulesOn(On(fixes, qol)), file] : RulesOn(On(fixes, qol));

    /// <summary>The prefix of the rules file's name in a save's <c>rules</c> and the features line: <c>rulesfile.</c> and the rules' short hash.</summary>
    public const string RulesFilePrefix = "rulesfile.";

    /// <summary>
    /// The rules file's numbers by name when they differ from 1.1's (<c>rulesfile.</c> and their short hash, <see cref="Rules.GameRules.ShortHash"/>);
    /// null when every key holds its default.
    /// </summary>
    public static string? RulesFileName(Rules.GameRules? numbers = null)
    {
        numbers ??= Rules.GameRules.Current;
        return numbers.IsDefault ? null : RulesFilePrefix + numbers.ShortHash();
    }

    /// <summary>
    /// The run's one line of what it is played with, for the console and the log beside the Fixes and
    /// quality-of-life lines: the profile, the features on that make it modded (and the rules file's numbers, when not 1.1's), and how
    /// many repairs every game has.
    /// </summary>
    public string Line(Fixes fixes, Qol qol, Rules.GameRules? numbers = null)
    {
        var rules = RulesOn(fixes, qol, numbers);
        return $"{Profile(fixes, qol, numbers)} rules{(rules.Count == 0 ? "" : " (" + string.Join(", ", rules) + ")")}; {AlwaysOn.Count()} repairs always on";
    }

    /// <summary>What the ini's reader and the command line's say of a switch the registry refused.</summary>
    public static string LeftOff(Conflict refused) => $"[{refused.Feature.Register}] {refused.Feature.Name} is left off: {refused.Sentence}";

    /// <summary>
    /// The Fixes alone made allowed, in place (<see cref="Settle(IEnumerable{Feature}, Action{Conflict}?, Func{Feature, bool}?, Func{Feature, bool}?)"/>):
    /// what <see cref="Fixes.Parse"/> ends with. A Fix's need for a switch of another register is left for
    /// <see cref="Settle(Fixes, Qol, Action{string}?, Func{Feature, bool}?)"/>, when the two meet.
    /// </summary>
    public void Settle(Fixes fixes, Action<string>? complain = null)
    {
        Settle(Of(FeatureKind.Fix).Where(f => fixes[f.Name]),
               refused => { fixes[refused.Feature.Name] = false; complain?.Invoke(LeftOff(refused)); },
               speaksFor: f => f.Kind == FeatureKind.Fix || !f.Switchable);
    }

    /// <summary>The quality-of-life switches alone made allowed, in place: what <see cref="Qol.Parse"/> ends with.</summary>
    public void Settle(Qol qol, Action<string>? complain = null)
    {
        Settle(Of(FeatureKind.Qol).Where(f => qol[f.Name]),
               refused => { qol[refused.Feature.Name] = false; complain?.Invoke(LeftOff(refused)); },
               speaksFor: f => f.Kind == FeatureKind.Qol || !f.Switchable);
    }

    /// <summary>
    /// Both registers made allowed together, in place: a switch the registry refuses beside the rest is switched
    /// off and complained about by name with the reason. The program does this once the ini, the command line and
    /// the session's choices have been read; a player's machine after the host's switches are laid over its own.
    /// </summary>
    /// <param name="held">Switches that are not to go off (what a save holds the game to); what conflicts with one goes.</param>
    public void Settle(Fixes fixes, Qol qol, Action<string>? complain = null, Func<Feature, bool>? held = null)
    {
        Settle(On(fixes, qol), refused =>
        {
            Put(fixes, qol, refused.Feature, false);
            complain?.Invoke(LeftOff(refused));
        }, held: held);
    }

    /// <summary>One switch put on or off in whichever register holds it.</summary>
    public static void Put(Fixes fixes, Qol qol, Feature feature, bool on)
    {
        if (feature.Kind == FeatureKind.Fix) fixes[feature.Name] = on;
        else if (feature.Kind == FeatureKind.Qol) qol[feature.Name] = on;
    }

    /// <summary>
    /// The differences between what a save's header records and the switches in force
    /// (<see cref="Differences(string?, IReadOnlyCollection{string}, IEnumerable{Feature})"/>).
    /// </summary>
    public IReadOnlyList<SwitchDifference> Differences(SaveHeader header, Fixes fixes, Qol qol) =>
        Differences(header.Profile, header.Rules, On(fixes, qol));
}
