// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Modules;

/// <summary>
/// The way the program sets a person's sheet without knowing a module's types (ours; the arena's fighters): a
/// module that keeps sheets registers one as a service of this type (<c>world.Service&lt;IPersonSheets&gt;(…)</c>), and the
/// program asks it, after the person was made (<see cref="PeopleHooks.Made"/>), for a template, a whole sheet in the
/// module's own save format, or a value. The module checks every id and value against its own registry and refuses by
/// name; the program never reads or computes the rules. A value set is a purchased value, as a sheet would hold it.
/// </summary>
public interface IPersonSheets
{
    /// <summary>The module's sheet templates by id (what <see cref="TryTemplate"/> takes), in a fixed order.</summary>
    IReadOnlyList<string> Templates { get; }

    /// <summary>Gives <paramref name="person"/> the template's sheet (replacing what it had); false with the reason.</summary>
    bool TryTemplate(EntityId person, string templateId, out string error);

    /// <summary>Gives <paramref name="person"/> a whole sheet in the module's save format (a sheet file's text); false with the reason.</summary>
    bool TryImport(EntityId person, string sheetText, out string error);

    /// <summary>Sets a trait's purchased value (an Attribute or an Ability, by the registry's id); false with the reason.</summary>
    bool TrySet(EntityId person, string traitId, int value, out string error);

    /// <summary>Learns a power at a rank (a Discipline by the registry's id; 0 unlearns it); false with the reason.</summary>
    bool TryLearn(EntityId person, string powerId, int rank, out string error);

    /// <summary>
    /// Learns a power at a rank with the named options (the rules package's rule that a rank never brings an option;
    /// the arena's <c>powers</c> with <c>options</c>, <c>--learn</c>). Refuses by name an option the module does not know,
    /// and a power at a nonzero rank none of whose options the module's rules read (it would do nothing, silently). An
    /// empty list is <see cref="TryLearn(EntityId, string, int, out string)"/>.
    /// </summary>
    bool TryLearn(EntityId person, string powerId, int rank, IReadOnlyList<string> options, out string error)
    {
        if (options.Count == 0) return TryLearn(person, powerId, rank, out error);
        error = $"this module does not take a power's options ({powerId})";
        return false;
    }

    /// <summary>
    /// A power's options as the module lists them (<c>--learn power=rank</c> with no options learns these,
    /// for convenience); empty when the module does not know the power or takes no options.
    /// </summary>
    IReadOnlyList<string> OptionsOf(string powerId) => [];

    /// <summary>A trait's effective value now, or a power's rank (0 when not learned); false when the id is not the module's.</summary>
    bool TryRead(EntityId person, string id, out int value);

    /// <summary>
    /// Gives <paramref name="person"/> the sheet in <paramref name="text"/> (<c>--sheet who=file</c>, the module says
    /// which forms it reads: the enhanced module a sheet's save or one of its sheet templates), read from
    /// <paramref name="source"/> (named in messages), in place of the one it has. Returns a line saying what it gave. Throws
    /// <see cref="PersonSheetException"/> with every reason it refuses.
    /// </summary>
    string Give(EntityId person, string text, string source);

    /// <summary>
    /// The person's sheet as JSON for a trace (the rules package's "actor purchased and effective values"): the
    /// module's own form — the enhanced module's save, with the effective value of every trait now and its resources.
    /// False when the person has no sheet.
    /// </summary>
    bool TryDescribe(EntityId person, out string json);

    // ---- armour, for the modules that keep it (the enhanced module's combat/armor); none by default.
    /// <summary>The pieces of armour a person may wear (the module's ids, in a fixed order); none for a module without armour.</summary>
    IReadOnlyList<string> ArmorPieces => [];

    /// <summary>Dresses <paramref name="person"/> in <paramref name="pieces"/> (replacing what it wore; none undresses it); false with the reason.</summary>
    bool TryWear(EntityId person, IReadOnlyList<string> pieces, out string error)
    {
        error = "this module keeps no armour";
        return false;
    }

    /// <summary>What <paramref name="person"/> wears (the pieces' ids; none for a module without armour).</summary>
    IReadOnlyList<string> WornBy(EntityId person) => [];
    // ---- end
}

/// <summary>A sheet refused by <see cref="IPersonSheets.Give"/>, with every reason.</summary>
public sealed class PersonSheetException(string source, IReadOnlyList<string> problems)
    : Exception($"{source}: the sheet is refused: {string.Join("; ", problems)}")
{
    /// <summary>Where the sheet came from (a file's path).</summary>
    public string From { get; } = source;
    public IReadOnlyList<string> Problems { get; } = problems;
}
