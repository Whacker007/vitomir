// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours. The tables are SANDBOX data: the rules package's anatomy per creature class replaces them.
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A part of a person's body a hit volume belongs to (ours): what a contact
/// reports as its <c>AnatomySite</c> until the rules package's anatomy arrives. <b>SANDBOX</b>: the rules decide
/// what a part means (armour, the heart, severing); the engine only says which was met.
/// </summary>
public enum BodyRegion : byte
{
    None = 0,
    Head, Neck, Chest, Belly, Hips,
    LeftUpperArm, RightUpperArm, LeftForearm, RightForearm, LeftHand, RightHand,
    LeftThigh, RightThigh, LeftShin, RightShin, LeftFoot, RightFoot,
}

/// <summary>The body's regions' names and order (ours).</summary>
public static class BodyRegions
{
    /// <summary>How many there are, <see cref="BodyRegion.None"/> included.</summary>
    public const int Count = 18;

    static readonly string[] Names =
    [
        "", "head", "neck", "chest", "belly", "hips",
        "left upper arm", "right upper arm", "left forearm", "right forearm", "left hand", "right hand",
        "left thigh", "right thigh", "left shin", "right shin", "left foot", "right foot",
    ];

    /// <summary>Every region a body is expected to have (all but <see cref="BodyRegion.None"/>).</summary>
    public static ReadOnlySpan<BodyRegion> All =>
    [
        BodyRegion.Head, BodyRegion.Neck, BodyRegion.Chest, BodyRegion.Belly, BodyRegion.Hips,
        BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm, BodyRegion.LeftForearm, BodyRegion.RightForearm, BodyRegion.LeftHand, BodyRegion.RightHand,
        BodyRegion.LeftThigh, BodyRegion.RightThigh, BodyRegion.LeftShin, BodyRegion.RightShin, BodyRegion.LeftFoot, BodyRegion.RightFoot,
    ];

    /// <summary>The region's name as the status and the pointer say it ("head", "left forearm"); empty for none.</summary>
    public static string Name(BodyRegion region) => (uint)region < (uint)Names.Length ? Names[(int)region] : "";

    /// <summary>Whether the region is on the body's left side.</summary>
    public static bool IsLeft(BodyRegion r) => r is BodyRegion.LeftUpperArm or BodyRegion.LeftForearm or BodyRegion.LeftHand
                                                  or BodyRegion.LeftThigh or BodyRegion.LeftShin or BodyRegion.LeftFoot;

    /// <summary>Whether the region is on the body's right side.</summary>
    public static bool IsRight(BodyRegion r) => r is BodyRegion.RightUpperArm or BodyRegion.RightForearm or BodyRegion.RightHand
                                                   or BodyRegion.RightThigh or BodyRegion.RightShin or BodyRegion.RightFoot;
}

/// <summary>
/// Which region each bone of one skeleton belongs to (ours, <b>SANDBOX</b> until the rules package's anatomy): a skeleton is known by its
/// bones' parents (1.1's models name no bones), and a bone given <see cref="BodyRegion.None"/> gets no hit volume —
/// cloth (a tunic's flaps, a cape, a skirt), hair, and the empty mounts for a helmet, a weapon or a shield.
/// </summary>
public sealed class AnatomyTable
{
    public required string Name { get; init; }

    /// <summary>Each bone's parent (−1 for the root): the skeleton this table is for.</summary>
    public required int[] Parents { get; init; }

    /// <summary>Each bone's region.</summary>
    public required BodyRegion[] Regions { get; init; }

    /// <summary>Whether <paramref name="skeleton"/> is the one this table is for (the same bones with the same parents).</summary>
    public bool Matches(Skeleton skeleton)
    {
        if (skeleton.Count != Parents.Length) return false;
        for (int b = 0; b < Parents.Length; b++)
            if (skeleton.Bones[b].Parent != Parents[b]) return false;
        return true;
    }

    /// <summary>The bone's region, <see cref="BodyRegion.None"/> beyond the table.</summary>
    public BodyRegion Of(int bone) => (uint)bone < (uint)Regions.Length ? Regions[bone] : BodyRegion.None;

    /// <summary>
    /// For a table of <b>one volume a region</b> (the models repository's people, <see cref="PeopleAnatomy"/>), each
    /// bone's volume — the bone its region's capsule rides on (−1 for none), fitted from the skin of every bone that names it
    /// together (<see cref="HitVolumeFit"/>); null for 1.1's tables, one volume a bone, fitted as before.
    /// </summary>
    public int[]? Volumes { get; init; }
}

/// <summary>
/// The anatomy tables of the skeletons the platform draws today (ours, <b>SANDBOX</b> data until the rules
/// package's anatomy): 1.1's man (forty bones: Christof, the guard, the night watchman, the old man, the merchants,
/// the monks, the townsman, the drunk, the beggar), 1.1's woman (forty-eight bones: the young lady, the milkmaid, the
/// townswoman) and 1.1's szlachta (thirty-four bones). Read from each bone's place and vertices in the bind pose (the man's facing +Y, his right +X) and the
/// motion sets' attach points (<c>LFoot</c>, <c>RFoot</c>, <c>LFingers</c>, <c>RFingers</c>, <c>Head</c>).
/// </summary>
public static class AnatomyTables
{
    const BodyRegion N = BodyRegion.None, Hd = BodyRegion.Head, Nk = BodyRegion.Neck, Ch = BodyRegion.Chest, Be = BodyRegion.Belly, Hp = BodyRegion.Hips,
        LUa = BodyRegion.LeftUpperArm, RUa = BodyRegion.RightUpperArm, LFa = BodyRegion.LeftForearm, RFa = BodyRegion.RightForearm,
        LHa = BodyRegion.LeftHand, RHa = BodyRegion.RightHand, LTh = BodyRegion.LeftThigh, RTh = BodyRegion.RightThigh,
        LSh = BodyRegion.LeftShin, RSh = BodyRegion.RightShin, LFo = BodyRegion.LeftFoot, RFo = BodyRegion.RightFoot;

    /// <summary>
    /// 1.1's man (skeleton 0, Christof's): hips 0; left leg 1 thigh, 2 shin, 3 foot, 4 toes; right leg 5–8; the tunic's
    /// flaps 9–13 (cloth); spine 14 (belly), 15 and 16 (chest); 17 neck; 18 head, 19 the helmet's mount and 20 the jaw
    /// (head); right arm 21 upper, 22 forearm, 23 hand, 24 fingers and 25 the weapon's mount (hand); left arm 26–29,
    /// and 30, the shield's mount on the forearm; the cape 31–38 (cloth); 39 a mount off the hips (none).
    /// </summary>
    public static readonly AnatomyTable OneOneMan = new()
    {
        Name = "1.1 man (40 bones)",
        Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 0, 12, 0, 14, 15, 16, 17, 18, 18, 16, 21, 22, 23, 23, 16, 26, 27, 28, 27, 16, 31, 32, 33, 32, 35, 32, 37, 0],
        Regions =
        [
            Hp, LTh, LSh, LFo, LFo, RTh, RSh, RFo, RFo, N,
            N, N, N, N, Be, Ch, Ch, Nk, Hd, Hd,
            Hd, RUa, RFa, RHa, RHa, RHa, LUa, LFa, LHa, LHa,
            LFa, N, N, N, N, N, N, N, N, N,
        ],
    };

    /// <summary>
    /// 1.1's woman: hips 0; right leg 1–4 and left leg 5–8 (her motion set's <c>RFoot</c> is 4, <c>LFoot</c> 8); spine
    /// 9 (belly), 10 and 11 (chest); right arm 12 upper, 13 forearm, 14 hand and its fingers and weapon mount 15–17,
    /// 18–20 a mount on the forearm; 21 the neck (her motion set's <c>Head</c> tag), 22 the head, 23 the jaw, 28 the
    /// crown (head); 24–27 hair down the back (none); left arm 29–32 with 33 and 34 the fingers, 35–38 mounts on the
    /// forearm; 39 the bust (chest); the skirt 40–47 (cloth).
    /// </summary>
    public static readonly AnatomyTable OneOneWoman = new()
    {
        Name = "1.1 woman (48 bones)",
        Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 12, 13, 14, 15, 14, 13, 18, 19, 11, 21, 22, 22, 24, 25, 26, 22, 11, 29, 30, 31, 32, 31, 30, 35, 36, 30, 11, 0, 40, 41, 42, 41, 44, 41, 46],
        Regions =
        [
            Hp, RTh, RSh, RFo, RFo, LTh, LSh, LFo, LFo, Be,
            Ch, Ch, RUa, RFa, RHa, RHa, RHa, RHa, RFa, RFa,
            RFa, Nk, Hd, Hd, N, N, N, N, Hd, LUa,
            LFa, LHa, LHa, LHa, LHa, LFa, LFa, LFa, LFa, Ch,
            N, N, N, N, N, N, N, N,
        ],
    };

    /// <summary>
    /// 1.1's szlachta (thirty-four bones; the template <c>Szlachta</c>'s <c>SZLACHTA.NOD</c>, the Tzimisce
    /// war-ghoul Christof fights as a human), read from its bones' places and vertices in the bind pose (facing +Y, its right
    /// +X, as the man): hips 0; right leg 1 thigh, 2 shin, 3 foot, 4 toes; left leg 5–8; spine 9 (belly), 10 and 11 (chest,
    /// the shoulders on 11); <b>the long neck 12 to 17</b>, six bones reaching forward and down from the shoulders and up to
    /// the head (about 0.64 m along the bones, a man's about 0.1; 5 to 14 vertices each, 6 to 9 cm about); 18 the head,
    /// 19 the jaw (head); right arm 20 upper, 21 forearm, 22 hand, 23–25 its fingers and thumb (hand); left arm 26–31 the
    /// same; 32 a flap in front of the belly (belly); 33 a flap behind the hips (none: no volume). SANDBOX, ours.
    /// </summary>
    public static readonly AnatomyTable OneOneSzlachta = new()
    {
        Name = "1.1 szlachta (34 bones)",
        Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 11, 20, 21, 22, 23, 22, 11, 26, 27, 28, 29, 28, 9, 0],
        Regions =
        [
            Hp, RTh, RSh, RFo, RFo, LTh, LSh, LFo, LFo, Be,
            Ch, Ch, Nk, Nk, Nk, Nk, Nk, Nk, Hd, Hd,
            RUa, RFa, RHa, RHa, RHa, RHa, LUa, LFa, LHa, LHa,
            LHa, LHa, Be, N,
        ],
    };

    /// <summary>The tables known, in the order they are tried.</summary>
    public static readonly IReadOnlyList<AnatomyTable> All = [OneOneMan, OneOneWoman, OneOneSzlachta];

    /// <summary>
    /// The table for <paramref name="skeleton"/>, or null when it is none the platform knows (it then has no hit volumes but its
    /// body's capsule). after 1.1's, a skeleton with the models repository's bone names gets its table made by name
    /// (<see cref="PeopleAnatomy.For"/>: their skeletons differ by their outfits' swing and strap bones).
    /// </summary>
    public static AnatomyTable? For(Skeleton skeleton)
    {
        foreach (var t in All)
            if (t.Matches(skeleton)) return t;
        return PeopleAnatomy.For(skeleton);
    }
}
