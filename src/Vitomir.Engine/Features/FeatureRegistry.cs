// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The feature registry (ours): every declaration, and the three questions it answers.
namespace Vitomir.Engine.Features;

/// <summary>
/// **The feature registry** (ours). It holds every
/// <see cref="Feature"/>'s declaration, in the order the Fixes page lists them, and answers three questions: *is
/// this combination allowed* (<see cref="Check"/>), *if not, why* (<see cref="Blocks"/>), and *what does this game
/// use* (<see cref="Profile"/>, <see cref="RulesOn(IEnumerable{Feature})"/> — what a save writes).
///
/// The rules it applies:
///
/// 1. Two features on that **take** the same switch point conflict: only one version of a place can be in force.
///    The one way out is declared: a feature may <see cref="Feature.Refines">refine</see> another, which it then
///    needs on, and the two may share the point.
/// 2. A feature that **changes** a datum another feature on **reads** is blocked, unless either declares it
///    <see cref="Feature.Tolerates">tolerates</see> the other, or one refines the other.
/// 3. A mod that does not tolerate a rules feature in force is refused, by name (<see cref="Refusals"/>).
/// 4. A feature that takes no rules switch point and changes nothing the rules read is presentation
///    (<see cref="Feature.Axis"/>).
///
/// And the plain ones: what a feature <see cref="Feature.Requires">requires</see> (and what it refines) must be
/// on; what it <see cref="Feature.Excludes">excludes</see> must be off.
///
/// A feature with no switch (a repair) is always on, and counts in every answer without being named by the caller.
/// The game's own registry is <see cref="GameFeatures.Registry"/>; a test makes one of invented features.
/// </summary>
public sealed partial class FeatureRegistry
{
    readonly Feature[] all;
    readonly Dictionary<string, Feature> byId;
    readonly Dictionary<Feature, int> order;

    /// <summary>
    /// A registry of <paramref name="features"/>, in the order given. A declaration that cannot be right is
    /// refused here, by name, rather than found by a player: two features of one name, a feature that names one
    /// the registry does not hold, one that refines a feature it shares no switch point with (or, by way of
    /// others, itself), a switch point or a datum declared twice with different words, or two always-on
    /// features that conflict.
    /// </summary>
    public FeatureRegistry(IEnumerable<Feature> features)
    {
        all = [.. features];
        byId = new Dictionary<string, Feature>(StringComparer.OrdinalIgnoreCase);
        order = [];
        foreach (var f in all)
        {
            if (!byId.TryAdd(f.Id, f)) throw new ArgumentException($"the feature {f.Id} is declared twice");
            order[f] = order.Count;
        }
        foreach (var f in all)
        {
            foreach (var id in f.Requires.Concat(f.Excludes).Concat(f.Tolerates).Append(f.Refines).OfType<string>())
            {
                if (!byId.TryGetValue(id, out var named)) throw new ArgumentException($"the feature {f.Id} names {id}, which is not declared");
                if (ReferenceEquals(named, f)) throw new ArgumentException($"the feature {f.Id} names itself");
            }
            if (f.Refines is { } refined && !f.Takes.Intersect(byId[refined].Takes).Any())
                throw new ArgumentException($"the feature {f.Id} refines {refined} but shares no switch point with it");
        }
        foreach (var f in all)
        {
            // What a feature refines leads, in the end, to one that refines nothing.
            int steps = 0;
            for (var up = f; up.Refines is { } id; up = byId[id])
                if (++steps > all.Length) throw new ArgumentException($"the feature {f.Id} refines itself, by way of others");
        }
        foreach (var same in Points.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            throw new ArgumentException($"the switch point {same.Key} is declared more than once, in different words");
        foreach (var same in Data.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            throw new ArgumentException($"the datum {same.Key} is declared more than once, in different words");
        if (Check([]) is { Count: > 0 } broken)
            throw new ArgumentException($"two features that are always on conflict: {broken[0].Sentence}");
    }

    /// <summary>Every declaration, in the order the Fixes page lists them.</summary>
    public IReadOnlyList<Feature> All => all;

    /// <summary>The declarations of one kind, in order.</summary>
    public IEnumerable<Feature> Of(FeatureKind kind) => all.Where(f => f.Kind == kind);

    /// <summary>The features with no switch: on in every game.</summary>
    public IEnumerable<Feature> AlwaysOn => all.Where(f => !f.Switchable);

    /// <summary>A feature by its register (<c>fixes</c>, <c>qol</c>, <c>repairs</c>) and name, whatever the case; null when there is none.</summary>
    public Feature? Find(string register, string name) => Find(register + "." + name);

    /// <summary>A feature by its <see cref="Feature.Id"/> (<c>fixes.gunreach</c>); null when there is none.</summary>
    public Feature? Find(string id) => byId.GetValueOrDefault(id);

    /// <summary>Every switch point a declaration takes, once each, in the order first met.</summary>
    public IEnumerable<SwitchPoint> Points => all.SelectMany(f => f.Takes).Distinct();

    /// <summary>Every datum a declaration reads or changes, once each, in the order first met.</summary>
    public IEnumerable<Datum> Data => all.SelectMany(f => f.Reads.Concat(f.Changes)).Distinct();

    /// <summary>
    /// **If not, why**: null when <paramref name="candidate"/> can be switched on beside the features
    /// <paramref name="on"/> (and the always-on ones), else the first reason it cannot — what it needs first, then
    /// what excludes it, then a switch point shared (rule 1), then data changed under a reader (rule 2).
    /// </summary>
    public Conflict? Blocks(Feature candidate, IEnumerable<Feature> on)
    {
        var others = With(on);
        others.Remove(candidate);
        return Reasons(candidate, others, others, null).FirstOrDefault();
    }

    /// <summary>
    /// **Is this combination allowed**: every conflict among the features <paramref name="on"/> (and the always-on
    /// ones), each pair once, named from the feature later in the registry; empty when the set is allowed.
    /// </summary>
    public IReadOnlyList<Conflict> Check(IEnumerable<Feature> on)
    {
        var set = With(on);
        var found = new List<Conflict>();
        var earlier = new HashSet<Feature>();
        foreach (var f in all)
        {
            if (!set.Contains(f)) continue;
            found.AddRange(Reasons(f, set, earlier, null));
            earlier.Add(f);
        }
        return found;
    }

    /// <summary>
    /// **A set made allowed**: of the features <paramref name="on"/>, the ones that can stay. Taken in the
    /// registry's order, a feature that cannot be on beside the ones kept before it — or that needs one that is
    /// off — is switched off and the reason handed to <paramref name="refused"/> (the ini's and the command line's
    /// complaint); and again until nothing more goes, since one switched off may have been another's need. Of two
    /// that conflict the earlier in the registry stays. A feature with no switch is never switched off: what
    /// conflicts with it goes. Settling a settled set changes nothing.
    /// </summary>
    /// <param name="speaksFor">Which features <paramref name="on"/> speaks for, when it is one register's reading
    /// and not the whole game's: a need for a feature outside it is not judged (the whole is settled when the
    /// registers meet). Null: every feature.</param>
    /// <param name="held">Features that are not to be switched off, as though they had no switch — what a save
    /// holds a game to. What conflicts with one goes. Null: none.</param>
    public IReadOnlyList<Feature> Settle(IEnumerable<Feature> on, Action<Conflict>? refused = null,
                                         Func<Feature, bool>? speaksFor = null, Func<Feature, bool>? held = null)
    {
        var set = With(on);
        bool Fixed(Feature f) => !f.Switchable || (held is not null && held(f));
        for (bool changed = true; changed;)
        {
            changed = false;
            var kept = new HashSet<Feature>(set.Where(Fixed));
            foreach (var f in all)
            {
                if (Fixed(f) || !set.Contains(f)) continue;
                if (Reasons(f, set, kept, speaksFor).FirstOrDefault() is { } why)
                {
                    set.Remove(f);
                    refused?.Invoke(why);
                    changed = true;
                }
                else kept.Add(f);
            }
        }
        return [.. all.Where(f => f.Switchable && set.Contains(f))];
    }

    /// <summary>The name of a game played with no switchable rules feature on: 1.1's rules.</summary>
    public const string Faithful = "faithful";
    /// <summary>
    /// The name of a game played with at least one switchable rules feature on (<i>"I think
    /// all we need is 'faithful' vs 'modded'."</i>).
    /// </summary>
    public const string Modded = "modded";
    /// <summary>What <see cref="Modded"/> was called for one build, whose saves say it: read as <see cref="Modded"/>.</summary>
    public const string ModdedBefore = "changed";

    /// <summary>
    /// A profile as a save's header gives it, in today's words: an older <c>changed</c> is <see cref="Modded"/>;
    /// anything else — <see cref="Faithful"/>, the empty word of a save that does not say — is itself.
    /// </summary>
    public static string ProfileRead(string? written) =>
        string.Equals(written, ModdedBefore, StringComparison.OrdinalIgnoreCase) ? Modded : written ?? "";

    /// <summary>
    /// **What does this game use**, in a word: <see cref="Faithful"/> when no feature that counts
    /// (<see cref="Feature.Counts"/>) is on, else <see cref="Modded"/>. What counts is a switch that changes the
    /// rules and is no quality-of-life switch: today the gameplay Fixes, later a mod's rules. The repairs do not
    /// count: they are in every game, and the registry lists them. Nor does a presentation feature, whatever its
    /// kind. Nor a quality-of-life switch, whatever its axis — so a game played as it comes is faithful.
    /// </summary>
    public string Profile(IEnumerable<Feature> on) => RulesFeatures(on).Count == 0 ? Faithful : Modded;

    /// <summary>
    /// **What does this game use**, by name: the features that count among <paramref name="on"/>, as
    /// <c>fixes.gunreach</c>, in the registry's order. What a save writes.
    /// </summary>
    public IReadOnlyList<string> RulesOn(IEnumerable<Feature> on) => [.. RulesFeatures(on).Select(f => f.Id)];

    /// <summary>
    /// The same as declarations: the features that count among <paramref name="on"/>, in the registry's order —
    /// the switches that make a game <see cref="Modded"/>, which the Fixes page names.
    /// </summary>
    public IReadOnlyList<Feature> RulesFeatures(IEnumerable<Feature> on)
    {
        var set = new HashSet<Feature>(on);
        return [.. all.Where(f => f.Counts && set.Contains(f))];
    }

    /// <summary>
    /// **A save's label as today's rule reads its record** (ours): what a list of saves should show. The word a
    /// save's header carries is the word of the build that wrote it, and two builds counted the
    /// quality-of-life switches: a game played as it comes was written <c>modded</c> with <c>qol.quickequip</c>,
    /// <c>qol.driving</c> and <c>qol.dropcells</c> as its rules. Read by its names instead, such a save is
    /// <see cref="Faithful"/>: it is <see cref="Modded"/> only when a name in its <paramref name="rules"/> counts
    /// (<see cref="Feature.Counts"/>) or is one this build does not know — which may be a rule, so it is not
    /// called faithful. Empty when the save does not say (<paramref name="profile"/> empty and no names).
    /// </summary>
    public string ProfileOf(string? profile, IReadOnlyCollection<string> rules)
    {
        if (string.IsNullOrEmpty(profile) && rules.Count == 0) return "";
        return rules.Any(id => Find(id) is not { } known || known.Counts) ? Modded : Faithful;
    }

    HashSet<Feature> With(IEnumerable<Feature> on)
    {
        var set = new HashSet<Feature>(on);
        set.UnionWith(AlwaysOn);
        return set;
    }

    /// <summary>
    /// Every reason <paramref name="f"/> cannot be on: its needs judged against <paramref name="present"/> (all
    /// that is on), the rules between two features against <paramref name="beside"/> (the ones it is weighed
    /// against). <paramref name="f"/> itself is never one of the others.
    /// </summary>
    IEnumerable<Conflict> Reasons(Feature f, HashSet<Feature> present, HashSet<Feature> beside, Func<Feature, bool>? speaksFor)
    {
        bool Missing(Feature needed) => !present.Contains(needed) && (speaksFor is null || speaksFor(needed));

        if (f.Refines is { } refinedId && byId[refinedId] is var refined && Missing(refined))
            yield return Said(FeatureRule.Refines, f, refined, null, n => $"{n(f)} needs {n(refined)}: it refines it");
        foreach (var id in f.Requires)
            if (byId[id] is var needed && Missing(needed))
                yield return Said(FeatureRule.Requires, f, needed, null, n => $"{n(f)} needs {n(needed)}");

        foreach (var other in all)
        {
            if (ReferenceEquals(other, f) || !beside.Contains(other)) continue;
            if (Names(f.Excludes, other) || Names(other.Excludes, f))
                yield return Said(FeatureRule.Excludes, f, other, null, n => $"{n(f)} cannot be on with {n(other)}");
        }
        bool refines(Feature a, Feature b) => a.Refines is { } id && ReferenceEquals(byId[id], b);
        foreach (var other in all)
        {
            if (ReferenceEquals(other, f) || !beside.Contains(other)) continue;
            if (refines(f, other) || refines(other, f)) continue;         // the declared way to share a point
            foreach (var point in f.Takes.Intersect(other.Takes))
                yield return Said(FeatureRule.SwitchPoint, f, other, point.Name,
                    n => $"{n(f)} and {n(other)} both decide {point.What}: only one can be on");
        }
        foreach (var other in all)
        {
            if (ReferenceEquals(other, f) || !beside.Contains(other)) continue;
            if (refines(f, other) || refines(other, f)) continue;
            if (Names(f.Tolerates, other) || Names(other.Tolerates, f)) continue;
            foreach (var datum in f.Changes.Intersect(other.Reads))
                yield return Said(FeatureRule.Data, f, other, datum.Name, n => $"{n(f)} changes {datum.What}, which {n(other)} reads");
            foreach (var datum in other.Changes.Intersect(f.Reads))
                yield return Said(FeatureRule.Data, f, other, datum.Name, n => $"{n(other)} changes {datum.What}, which {n(f)} reads");
        }
    }

    /// <summary>A conflict said once in words, with the features named by their keys and again by their titles.</summary>
    static Conflict Said(FeatureRule rule, Feature f, Feature other, string? between, Func<Func<Feature, string>, string> words) =>
        new(rule, f, other, between, words(x => x.Name)) { Shown = words(x => x.Title) };

    static bool Names(IReadOnlyList<string> ids, Feature feature)
    {
        foreach (var id in ids) if (id.Equals(feature.Id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
