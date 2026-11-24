// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Rule 3 (ours): a mod that does not tolerate a rules feature in force is refused, by name. Built and tested with
// invented mods; nothing calls it yet, because there is no mod manifest to read a declaration from.
namespace Vitomir.Engine.Features;

/// <summary>
/// What a mod says of itself for rule 3 (ours): its name and the rules features (by <see cref="Feature.Id"/>) it
/// was written to work with. **A stand-in** until a mod's manifest carries this.
/// </summary>
public sealed record ModDeclaration(string Name, IReadOnlyList<string> Tolerates);

/// <summary>A mod refused, with every rules feature in force that it does not tolerate.</summary>
/// <param name="Mod">The mod.</param>
/// <param name="NotTolerated">The features' ids, in the order they were given.</param>
/// <param name="Sentence">The refusal in plain English, naming the mod and each feature.</param>
public sealed record ModRefusal(ModDeclaration Mod, IReadOnlyList<string> NotTolerated, string Sentence);

public sealed partial class FeatureRegistry
{
    /// <summary>
    /// **Rule 3**: the mods among <paramref name="mods"/> that cannot be loaded into a game played with
    /// <paramref name="rulesOn"/> (a save's <c>rules</c>, or <see cref="RulesOn(IEnumerable{Feature})"/> of what is
    /// in force), each with the features it does not tolerate. A mod is asked only about features that count
    /// (<see cref="Feature.Counts"/>): one that is presentation is allowed with any mod, as it is with the
    /// faithful rules (rule 4), and so is a quality-of-life switch, which an older save may name. A name in
    /// <paramref name="rulesOn"/> that this registry does not hold is a rules feature of some other build's, and
    /// no mod here can have declared it tolerable, so it refuses every mod that does not name it.
    /// </summary>
    public IReadOnlyList<ModRefusal> Refusals(IEnumerable<ModDeclaration> mods, IEnumerable<string> rulesOn)
    {
        var rules = rulesOn.Where(id => Find(id) is not { Counts: false }).ToList();
        var refused = new List<ModRefusal>();
        foreach (var mod in mods)
        {
            var not = rules.Where(id => !mod.Tolerates.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (not.Count > 0)
                refused.Add(new ModRefusal(mod, not, $"the mod {mod.Name} does not work with {string.Join(", ", not)}, which this game is played with"));
        }
        return refused;
    }
}
