// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Modules.Enhanced.Sheets;

/// <summary>
/// A resource on a sheet (the rules package's D1 resource schema, its first four fields): how much there is
/// now, the capacity, and the two spend limits, kept apart because the package wants capacity and
/// throughput developed separately. Always <c>0 ≤ Current ≤ Maximum</c>; the limits are not
/// below zero. Spending is the Blood ledger's; here a resource is only kept.
/// </summary>
public readonly record struct ResourceState(string Id, long Current, long Maximum, long PerActionSpendLimit, long PerCycleSpendLimit);

/// <summary>
/// A learned power (a Discipline, a Thaumaturgy path, or the Common commands): its rank, in the registry's
/// discipline domain (0..10), and the options learned, each by name (the registry: "option learned
/// separately"; options are purchased choices, never every option below the rank). Options are
/// kept in ordinal order.
/// </summary>
public sealed record LearnedPower(string PowerId, int Rank, IReadOnlyList<string> Options);

/// <summary>
/// A temporary modifier of one trait's effective value (the rules package's <c>ActorEffective</c>: explicit,
/// typed, expiring): who or what gives it, by how much, and the world tick it ends at (live while the
/// world's tick is below it). The purchased value never changes; the effective value is the purchased one
/// plus the live modifiers, and only there may an Attribute go above its purchased cap. One source gives a
/// trait one modifier: applying it again replaces it (the package: "do not … stack duplicate effects").
/// </summary>
public readonly record struct TemporaryModifier(string TraitId, int Delta, string SourceId, long ExpiresAtTick);

/// <summary>
/// A cast power's record on the sheet (the package's <c>ActiveEffect</c>, saved and digested): which power and
/// option, at what rank it was paid for, the tick it began and the tick it ends (live while the world's tick is below
/// <see cref="EndTick"/>), the tick its recast ends, the ledger's action id that paid for it, and whether its end has been
/// told (<see cref="Ended"/>: the record stays, not live, while the recast runs on). One record per power: a renewal
/// replaces it (no stacking).
/// </summary>
public readonly record struct LivePower(string PowerId, string Option, int Rank, long StartTick, long EndTick, long RecastTick, long ActionId, bool Ended)
{
    /// <summary>Whether it acts at <paramref name="tick"/>.</summary>
    public bool LiveAt(long tick) => !Ended && tick < EndTick;
}

/// <summary>
/// One actor sheet as data: what <see cref="ActorSheets.Export"/> gives, what <see cref="ActorSheets.Import"/>
/// takes, and what a save holds (<see cref="SheetJson"/>). Every trait of the layout is present. The
/// metadata (<see cref="Species"/> is a rules input; <see cref="Generation"/> and <see cref="Era"/> are
/// metadata only, read by no rule) and <see cref="Template"/> (which template the sheet was made from, if
/// any) are plain text, empty for none. Two sheets are equal when they say the same thing.
/// </summary>
public sealed record SheetData(
    string Species,
    string Generation,
    string Era,
    string Template,
    IReadOnlyDictionary<string, int> Purchased,
    IReadOnlyDictionary<string, double> Familiarity,
    IReadOnlyList<ResourceState> Resources,
    IReadOnlyList<LearnedPower> Powers,
    IReadOnlyDictionary<string, string> Conditions,
    IReadOnlyList<TemporaryModifier> Modifiers,
    IReadOnlyList<LivePower>? Live = null)
{
    /// <summary>The cast powers' records (none when null).</summary>
    public IReadOnlyList<LivePower> LivePowers => Live ?? [];

    /// <summary>Equal when their canonical saves are equal (the save is canonical: keys and lists in a fixed order).</summary>
    public bool Equals(SheetData? other) =>
        other is not null && string.Equals(SheetJson.Write(this, indented: false), SheetJson.Write(other, indented: false), StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(SheetJson.Write(this, indented: false));
}
