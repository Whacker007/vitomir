// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;

namespace Vitomir.Modules.Enhanced.Sheets;

/// <summary>What a trait is: the rules package's registry distinguishes purchased Attributes and Abilities; a module may define others.</summary>
public enum TraitKind { Attribute, Ability, Other }

/// <summary>
/// The values one trait may take (the registry's <c>attribute_domain</c> and <c>ability_domain</c>):
/// a purchased value is a whole number in <see cref="Min"/>..<see cref="MaxPurchased"/>; an effective value
/// (purchased plus temporary modifiers) is never below <see cref="Min"/> and may go above
/// <see cref="MaxPurchased"/> only when <see cref="EffectiveOvercap"/> says so (the rules package: "1000 is an
/// actual permanent cap … allow separately authorized temporary effective values above 1000").
/// </summary>
public readonly record struct TraitDomain(int Min, int MaxPurchased, bool EffectiveOvercap)
{
    public bool Allows(int purchased) => purchased >= Min && purchased <= MaxPurchased;

    /// <summary>An effective value from a purchased one and the sum of its live modifiers.</summary>
    public int Effective(int purchased, long modifierSum)
    {
        long v = purchased + modifierSum;
        if (v < Min) v = Min;
        if (!EffectiveOvercap && v > MaxPurchased) v = MaxPurchased;
        return v > int.MaxValue ? int.MaxValue : (int)v;
    }

    public override string ToString() => $"{Min}..{MaxPurchased}{(EffectiveOvercap ? " (temporary overcap allowed)" : "")}";
}

/// <summary>One trait of a layout: its id (as the registry names it), what it is, its display name and its domain.</summary>
public sealed record TraitDef(string Id, TraitKind Kind, string Display, string Category, TraitDomain Domain);

/// <summary>A purchased value of the module's own, beside the registry's traits (<c>blood_pool</c>): its id, its domain and where its numbers come from.</summary>
public sealed record PurchaseDef(string Id, TraitDomain Domain, string Source);

/// <summary>
/// The traits an actor sheet has, in a fixed order, as data (the registry says "the engine must support
/// arbitrary traits"): nothing in the sheet knows there are nine Attributes and thirty Abilities. Also the
/// familiarity families (a percentage each, 0..100 in the registry) and the domain of a learned power's rank.
/// The order is the registry's (Attributes, then Abilities), and it is the order sheets store, save and
/// digest in.
/// </summary>
public sealed partial class TraitLayout
{
    readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> familyIndex = new(StringComparer.Ordinal);

    public TraitLayout(IReadOnlyList<TraitDef> traits, IReadOnlyList<string> familiarityFamilies,
                       double familiarityMin, double familiarityMax, int powerRankMin, int powerRankMax)
    {
        if (traits.Count == 0) throw new ArgumentException("a layout needs at least one trait", nameof(traits));
        for (int i = 0; i < traits.Count; i++)
        {
            var t = traits[i];
            if (!IsId(t.Id)) throw new ArgumentException($"trait id '{t.Id}' is not a lower-case identifier", nameof(traits));
            if (t.Domain.MaxPurchased < t.Domain.Min) throw new ArgumentException($"trait '{t.Id}' has an empty domain {t.Domain}", nameof(traits));
            if (!index.TryAdd(t.Id, i)) throw new ArgumentException($"trait '{t.Id}' appears twice", nameof(traits));
        }
        for (int i = 0; i < familiarityFamilies.Count; i++)
        {
            if (!IsId(familiarityFamilies[i])) throw new ArgumentException($"familiarity family '{familiarityFamilies[i]}' is not a lower-case identifier", nameof(familiarityFamilies));
            if (!familyIndex.TryAdd(familiarityFamilies[i], i)) throw new ArgumentException($"familiarity family '{familiarityFamilies[i]}' appears twice", nameof(familiarityFamilies));
        }
        if (!(familiarityMin <= familiarityMax)) throw new ArgumentException("the familiarity domain is empty");
        if (powerRankMax < powerRankMin) throw new ArgumentException("the power rank domain is empty");
        Traits = [.. traits];
        Families = [.. familiarityFamilies];
        FamiliarityMin = familiarityMin;
        FamiliarityMax = familiarityMax;
        PowerRankMin = powerRankMin;
        PowerRankMax = powerRankMax;
    }

    /// <summary>The traits in the layout's order.</summary>
    public IReadOnlyList<TraitDef> Traits { get; }

    public int Count => Traits.Count;

    /// <summary>The familiarity families in the layout's order.</summary>
    public IReadOnlyList<string> Families { get; }

    public double FamiliarityMin { get; }
    public double FamiliarityMax { get; }
    public int PowerRankMin { get; }
    public int PowerRankMax { get; }

    /// <summary>A trait's place in the layout, or -1.</summary>
    public int IndexOf(string traitId) => index.TryGetValue(traitId, out int i) ? i : -1;

    /// <summary>A familiarity family's place in the layout, or -1.</summary>
    public int FamilyIndexOf(string family) => familyIndex.TryGetValue(family, out int i) ? i : -1;

    /// <summary>A trait's place, or a named refusal.</summary>
    public int Require(string traitId) =>
        IndexOf(traitId) is int i and >= 0 ? i : throw new UnknownTraitException(traitId);

    readonly List<PurchaseDef> purchases = [];

    /// <summary>
    /// The module's own purchased values beside the registry's traits (<c>blood_pool</c>, the purchased capacity of
    /// <c>rules/blood</c>): each with its domain. A sheet holds one only when it is set (a save's and a template's
    /// <c>purchased</c> carry it); it is no trait (no effective value, no modifier).
    /// </summary>
    public IReadOnlyList<PurchaseDef> Purchases => purchases;

    /// <summary>Adds a purchased value of the module's own (configuration, before any sheet is made); refuses one named as a trait or twice.</summary>
    public void AddPurchase(PurchaseDef p)
    {
        if (!IsId(p.Id)) throw new ArgumentException($"purchase id '{p.Id}' is not a lower-case identifier");
        if (index.ContainsKey(p.Id)) throw new ArgumentException($"purchase '{p.Id}' is also a trait of the registry");
        if (purchases.Exists(x => x.Id == p.Id)) throw new ArgumentException($"purchase '{p.Id}' appears twice");
        if (p.Domain.MaxPurchased < p.Domain.Min) throw new ArgumentException($"purchase '{p.Id}' has an empty domain {p.Domain}");
        purchases.Add(p);
    }

    /// <summary>A purchase of the module's own by id, or null.</summary>
    public PurchaseDef? Purchase(string id)
    {
        foreach (var p in purchases) if (p.Id == id) return p;
        return null;
    }

    /// <summary>Whether a text is a module identifier: lower-case letters, digits, '_', '.' and '-', starting with a letter.</summary>
    public static bool IsId(string? s) => s is not null && IdPattern().IsMatch(s);

    [GeneratedRegex("^[a-z][a-z0-9_.\\-]*$")]
    private static partial Regex IdPattern();
}

/// <summary>A sheet asked about a trait its layout does not have.</summary>
public sealed class UnknownTraitException(string traitId)
    : Exception($"the sheet has no trait '{traitId}'")
{
    public string TraitId { get; } = traitId;
}

/// <summary>
/// A value outside its domain, refused by name (fail closed: a purchased Attribute of 1,001 or −1, an Ability
/// of 11, a familiarity of 101 %, a resource above its maximum …). Nothing is clamped silently.
/// </summary>
public sealed class SheetValueException(string what, string value, string domain)
    : Exception($"{what} = {value} is refused: the domain is {domain}")
{
    /// <summary>What was being set, by name (for example <c>purchased strength</c>).</summary>
    public string What { get; } = what;
    public string Value { get; } = value;
    public string Domain { get; } = domain;
}
