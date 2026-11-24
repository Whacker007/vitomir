// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The chronicle's money bags: the metafile's CASH line and the pick money dropped makes from it.
namespace Vitomir.Engine.World;

/// <summary>
/// **Which bag dropped money comes in is the chronicle's.** The chronicle's metafile names its money
/// templates on a <c>CASH</c> line — <c>GoldBag goldCoins</c> in Prague and Vienna, <c>cashUK</c> in
/// London, <c>cashUS</c> in New York — and its reader keeps up to three of them, each only
/// when the template exists, in the chronicle with a
/// count. Money dropped (the pack's coin button and the
/// <c>dropcash</c> command) then makes one of those at random — <c>rand()</c> × 1/32767 × count,
/// truncated, a roll made even when there is only one to choose — and falls
/// back to <c>GoldBag</c>, with no roll, only when no chronicle is loaded or it named none.
/// So a Londoner's dropped purse is a bundle of notes, and a Prague one is a bag or a pile of coins.
/// Found by a comparison with 1.1: Vitomir had always made a <c>GoldBag</c>.
/// </summary>
public static class ChronicleCash
{
    /// <summary>How many money templates the chronicle keeps: three slots.</summary>
    public const int Slots = 3;

    /// <summary>The template made when the chronicle names none.</summary>
    public const string Fallback = "GoldBag";

    /// <summary>
    /// The chronicle's money templates in the order its <c>CASH</c> line gives them: the line's
    /// first three words (the metafile reader reads three and no more — <c>MP_Salon.nmf</c> names four and loses
    /// <c>cashUK</c>), each kept only when a template of that name exists. Empty with no chronicle,
    /// no metafile or no line. A second <c>CASH</c> line would write past the three slots in 1.1; none
    /// ships, and here its names simply follow the first line's and are cut at three (ours).
    /// </summary>
    public static IReadOnlyList<string> Templates(Chronicle? chronicle, TemplateSet? templates)
    {
        if (chronicle?.Meta is not { } meta || templates is null) return [];
        var kept = new List<string>(Slots);
        foreach (var name in meta.Cash.Take(Slots))
            if (templates.Has(name)) kept.Add(name);
        return kept;
    }

    /// <summary>
    /// The pick: with a table, one roll on the shared stream,
    /// <c>trunc(rand() × 1/32767 × count)</c> at more than single precision (exact here in a double), and that
    /// entry; without one, <see cref="Fallback"/> and no roll. <b>The maximum roll lands one past the
    /// table</b> (<c>rand() = 32767</c> makes the factor 1.0000000240), where 1.1 finds an empty slot
    /// or the count itself and asserts (line 0x207); Vitomir takes the last entry there
    /// instead (ours: one roll in 32,768). With no stream to roll on (a place never awakened), the
    /// first entry (ours).
    /// </summary>
    public static string Choose(IReadOnlyList<string> table, Ai.Rng? rng)
    {
        if (table.Count == 0) return Fallback;
        if (rng is null) return table[0];
        int i = (int)((double)rng.Next() * Ai.Rng.Scale * table.Count);
        return table[Math.Min(i, table.Count - 1)];
    }

    /// <summary>The template money dropped in <paramref name="place"/> makes, rolling when the chronicle names any.</summary>
    public static string For(Location place) =>
        Choose(Templates(place.Session?.Chronicle, place.Templates), place.Minds?.Random);
}
