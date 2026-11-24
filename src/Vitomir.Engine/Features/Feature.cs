// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The feature registry's nouns (ours; 1.1 has no switches): a switch point, a datum, a feature.
namespace Vitomir.Engine.Features;

/// <summary>
/// **A switch point** (ours): a named place where the game's behaviour can differ, with exactly one version in
/// force at a time. 1.1's version is always one of them, and it is the one in force when no feature that
/// <see cref="Feature.Takes">takes</see> the point is on. Named after the method the branch lives in
/// (<c>Player.RateFor</c>).
/// </summary>
/// <param name="Name">The method the branch lives in.</param>
/// <param name="What">What is decided there, as the rest of a sentence ("how fast a locomotion cycle plays"), with
/// 1.1's address where it has one. The Fixes page puts it into the reason two features cannot both be on.</param>
/// <param name="Rules">Whether what is decided there is the game's rules — what happens, to whom, and what a save
/// or another machine is told — or only what one machine shows, sounds or takes as input.</param>
public sealed record SwitchPoint(string Name, string What, bool Rules);

/// <summary>
/// **A piece of data** (ours) that features read or change: named so that two declarations can meet on it.
/// </summary>
/// <param name="Name">A short dotted name (<c>item.cell</c>).</param>
/// <param name="What">What it is, as a noun phrase ("the cell an item lies in"): the page names it between two
/// features that meet on it.</param>
/// <param name="RulesRead">Whether 1.1's rules read it. A feature that changes one changes the rules, wherever
/// its switch point is.</param>
public sealed record Datum(string Name, string What, bool RulesRead);

/// <summary>What kind of thing a feature is. The first three exist; the rest are room for what is to come.</summary>
public enum FeatureKind
{
    /// <summary>A fault of 1.1's the player may switch off: off by default (<see cref="World.Fixes"/>).</summary>
    Fix,
    /// <summary>Something 1.1 never had: on by default, with two exceptions (<see cref="World.Qol"/>).</summary>
    Qol,
    /// <summary>A defect always corrected, with no switch (<see cref="World.Repairs"/>).</summary>
    Repair,
    /// <summary>A modification of the engine's own. None is declared yet.</summary>
    Modification,
    /// <summary>A ruleset flag: a rule changed for good, chosen at a new game. None is declared yet.</summary>
    RulesetFlag,
    /// <summary>A mod. None is declared yet (<see cref="ModDeclaration"/> stands in until there is a manifest).</summary>
    Mod,
}

/// <summary>When a feature may be switched.</summary>
public enum FeatureWhen
{
    /// <summary>At any time: the player's own to turn on and off, in the middle of a game too.</summary>
    AnyTime,
    /// <summary>At a new game only: a save made with it holds it, and a load puts it in force as the save has it.</summary>
    NewGame,
}

/// <summary>Whose a feature is in a network game (<see cref="Net.HostSwitches"/>).</summary>
public enum FeatureWhose
{
    /// <summary>Each machine its own: what one player sees or hears, or how their own controls reach the game.</summary>
    Machine,
    /// <summary>The host's for everyone: what the game does.</summary>
    Host,
}

/// <summary>Which side of the line a feature falls: computed (<see cref="Feature.Axis"/>), never written by hand.</summary>
public enum FeatureAxis
{
    /// <summary>It takes no rules switch point and changes nothing the rules read: allowed with the faithful rules.</summary>
    Presentation,
    /// <summary>It takes a rules switch point or changes data the rules read: a game played with it is not 1.1's rules.</summary>
    Rules,
}

/// <summary>
/// **A feature** (ours): anything that changes the game — a Fix, a quality-of-life switch, a repair, and in time a
/// modification, a ruleset flag or a mod. One declaration says everything the registry needs to answer whether a
/// combination is allowed, why not, and what a game uses (<see cref="FeatureRegistry"/>).
///
/// Other features are named by <see cref="Id"/> (<c>fixes.movespeed</c>), not held, so two declarations can name
/// each other whichever is written first; the registry checks every name when it is made.
/// </summary>
public sealed class Feature(FeatureKind kind, string name, string what)
{
    /// <summary>The name the ini, the command line, a save's header and a network game use (<c>matchedgait</c>): a key, never changed.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// The name a player reads, on the Fixes page and in its sentences (<c>Matched Gait</c>): words in Title Case
    /// (ours, *"proper capitalization across words for in menu options"*). <see cref="Name"/> when
    /// none is given.
    /// </summary>
    public string Title { get => title ?? Name; init => title = value; }
    readonly string? title;

    public FeatureKind Kind { get; } = kind;

    /// <summary>The one line the Fixes page shows for it.</summary>
    public string What { get; } = what;

    readonly bool byDefault;

    /// <summary>Whether it is on until the player says otherwise. A repair always is.</summary>
    public bool Default { get => Kind == FeatureKind.Repair || byDefault; init => byDefault = value; }

    /// <summary>The switch points it replaces 1.1's version of.</summary>
    public IReadOnlyList<SwitchPoint> Takes { get; init; } = [];

    /// <summary>
    /// The data whose 1.1 meaning it relies on — not everything its code touches. A feature that changes one of
    /// these is blocked beside this one unless either tolerates the other (rule 2).
    /// </summary>
    public IReadOnlyList<Datum> Reads { get; init; } = [];

    /// <summary>The data it alters.</summary>
    public IReadOnlyList<Datum> Changes { get; init; } = [];

    /// <summary>Features (by <see cref="Id"/>) that must be on for this one to be.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>Features (by <see cref="Id"/>) that must be off for this one to be on.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>
    /// The one feature (by <see cref="Id"/>) this one **refines**, or null: it needs that one switched on, and
    /// where it applies it takes over at the switch point the two share — the one declared way two features may
    /// both take a switch point (<i>"Yes you can make one feature dependent. Maybe indent it
    /// slightly."</i>). The Fixes page draws it indented under the one it refines.
    /// </summary>
    public string? Refines { get; init; }

    /// <summary>Features (by <see cref="Id"/>) whose changes to what this one reads, or whose reading of what this one changes, it is written to live with.</summary>
    public IReadOnlyList<string> Tolerates { get; init; } = [];

    public FeatureWhen When { get; init; } = FeatureWhen.AnyTime;

    /// <summary>The host's unless a declaration says it is each machine's: a new feature that changes the game is safe by default.</summary>
    public FeatureWhose Whose { get; init; } = FeatureWhose.Host;

    /// <summary>Whether the player can switch it: everything but a repair.</summary>
    public bool Switchable => Kind != FeatureKind.Repair;

    /// <summary>The register it belongs to: the ini's section and the first half of its <see cref="Id"/>.</summary>
    public string Register => Kind switch
    {
        FeatureKind.Fix => "fixes",
        FeatureKind.Qol => "qol",
        FeatureKind.Repair => "repairs",
        FeatureKind.Modification => "modifications",
        FeatureKind.RulesetFlag => "rules",
        _ => "mods",
    };

    /// <summary>Its name across registers, as a save writes it and another declaration names it: <c>fixes.gunreach</c>.</summary>
    public string Id => Register + "." + Name;

    /// <summary>
    /// Rule 4: **rules** when it takes a rules switch point or changes a datum the rules read, else
    /// **presentation**. Computed from the declaration, so nobody can call a feature harmless without saying where
    /// it acts and what it changes.
    /// </summary>
    public FeatureAxis Axis =>
        Takes.Any(p => p.Rules) || Changes.Any(d => d.RulesRead) ? FeatureAxis.Rules : FeatureAxis.Presentation;

    /// <summary>
    /// **Whether it makes a game modded** (ours): a switch that is on, changes the rules, and is no
    /// quality-of-life switch. The quality-of-life switches are left out by decision, not by computation:
    /// the quality-of-life switches do not count towards a modded save; they change how the
    /// player can interact with the game, but not materially combat or other
    /// outcomes. Three of them are on the <see cref="FeatureAxis.Rules"/> axis all the same —
    /// <c>quickequip</c>, <c>driving</c>, <c>dropcells</c> — because the host is the machine that carries them out,
    /// and that stays the host's to send. What they change is the way to an outcome 1.1 has (an item worn, a
    /// character walked, a cell an item lies in), not the outcome. A repair cannot be switched, so it never counts.
    /// </summary>
    public bool Counts => Switchable && Kind != FeatureKind.Qol && Axis == FeatureAxis.Rules;

    public override string ToString() => Id;
}

/// <summary>Which of the registry's rules stands in a feature's way.</summary>
public enum FeatureRule
{
    /// <summary>It refines a feature that is off.</summary>
    Refines,
    /// <summary>It requires a feature that is off.</summary>
    Requires,
    /// <summary>It and a feature that is on exclude each other.</summary>
    Excludes,
    /// <summary>Rule 1: it and a feature that is on take the same switch point, and neither refines the other.</summary>
    SwitchPoint,
    /// <summary>Rule 2: one of the two changes data the other reads, and neither tolerates the other.</summary>
    Data,
}

/// <summary>
/// One reason a feature cannot be on beside the others, as a value the Fixes page, the ini's reader and the
/// command line's can each show.
/// </summary>
/// <param name="Rule">Which rule.</param>
/// <param name="Feature">The feature refused.</param>
/// <param name="Other">The feature it needs, or the one it cannot be beside.</param>
/// <param name="Between">The switch point's or the datum's name between the two; null where the rule names none.</param>
/// <param name="Sentence">The reason in plain English, naming both.</param>
public sealed record Conflict(FeatureRule Rule, Feature Feature, Feature Other, string? Between, string Sentence)
{
    /// <summary>
    /// The same sentence with the features by their <see cref="Feature.Title"/>s, as the Fixes page shows it;
    /// <see cref="Sentence"/>, by their keys, is what the ini's and the command line's complaints say.
    /// </summary>
    public string Shown { get => shown ?? Sentence; init => shown = value; }
    readonly string? shown;

    public override string ToString() => Sentence;
}
