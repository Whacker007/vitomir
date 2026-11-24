// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A motion set: which animation a character plays for each thing it can be doing.
//
// Plain text under 3D/Motions, named by a template's MOTIONSET tag (tag 9). The loader
// reads the file a line at a time, skips a line whose FIRST character is
// `;`, and splits the rest on " \r\t\n". The first word decides what the line is:
//
//   `tag`    a named attachment point: one of 43 names, the bone it rides on, and an
//            optional x y z offset. An unknown name drops the line.
//   `mouth`  a phoneme: one of the nine letters a-i, the jaw bone, and the angles that
//            shape it, written `(x,y,z)`. Anything else drops the line.
//   else     a mode: the name is looked up in the engine's table of 257 mode names and then, for a
//            head model's set, in its own table of 8. A name in neither is not a mode and the line
//            is dropped. The next word is the animation, and every word after it that matches a
//            flag name sets that flag; words that match nothing (a trailing `; comment` among them)
//            are ignored.
//
//     stand           Ahzra_stand.nad      lowpriority
//     deathslow       Ahzra_death.nad      noloop endpause
//     tag             Head                 20
//     mouth c         22 (-7.0,0.0,0.0)
//
// The engine also drops a mode whose animation will not load, so a set's modes are the ones that
// can actually play. This reader keeps them and leaves that to the caller, which is the only place
// that knows what is on disk.
//
// All 214 shipped sets parse. `stand` is what a character holds when it is doing nothing, and 169
// of the 214 have one.
using System.Globalization;

namespace Vitomir.Formats;

/// <summary>What a mode's flags say about how its animation plays (the engine's own bits).</summary>
[Flags]
public enum NmsFlags : uint
{
    None = 0,
    /// <summary>Play once and stop, rather than loop.</summary>
    NoLoop = 0x1,
    /// <summary>
    /// Marks a locomotion cycle whose playback should follow how fast the character is moving —
    /// and **the 1.1 engine never reads it**. It is on exactly the three locomotion families and
    /// nothing else (<c>walk</c> 479 of 482, <c>run</c> 479 of 482, <c>limp</c> 306 of 306), so what
    /// it is for is not in doubt; but the flags word only ever reaches the mode lookup, which tests
    /// <see cref="Special"/>, and the mixer channel, which tests everything except this,
    /// <see cref="HighPriority"/>, <see cref="LowPriority"/> and <see cref="NoFadeOut"/>. The rate a
    /// channel plays at is written in exactly one place and that place looks
    /// only at <see cref="Variable"/>.
    /// </summary>
    MoveSpeed = 0x2,
    /// <summary>Do not let an ordinary animation interrupt it. Unread by 1.1; no shipped set sets it.</summary>
    HighPriority = 0x4,
    /// <summary>Any other animation may replace it freely. Unread by 1.1 (818 shipped modes set it).</summary>
    LowPriority = 0x8,
    /// <summary>Hold the last frame when it ends, instead of falling back to the idle.</summary>
    EndPause = 0x10,
    /// <summary>Tell the script when it finishes.</summary>
    EndTag = 0x20,
    /// <summary>Start on this animation rather than blending into it.</summary>
    NoFadeIn = 0x40,
    /// <summary>Stop dead rather than blending out. Unread by 1.1 (22 shipped modes set it).</summary>
    NoFadeOut = 0x80,
    /// <summary>
    /// Roll the playback rate once when the animation starts: the engine takes
    /// <c>rate += rate × (rand() × (1/32767) × 0.5 − 0.25)</c>, so a <c>variable</c> mode
    /// runs anywhere from 0.75× to 1.25× speed and stays there for that play.
    /// </summary>
    Variable = 0x100,
    /// <summary>
    /// A variant the mode lookup will only choose when it is asked for (its first pass) —
    /// a weapon's special attack. All 113 shipped instances are attack modes, and the ordinary roll
    /// between a mode's duplicates steps over them.
    /// </summary>
    Special = 0x400,
    /// <summary>An idle flourish, played now and then over the standing pose.</summary>
    Fidget = 0x2000,
}

/// <summary>Which table a mode's name came from; the two number their modes separately.</summary>
public enum NmsModeKind { Body, Head }

/// <summary>One thing a character can be doing, and the animation it plays for it.</summary>
/// <param name="Index">The engine's mode number, within <paramref name="Kind"/>'s table.</param>
public sealed record NmsMode(int Index, NmsModeKind Kind, string Name, string Animation, NmsFlags Flags)
{
    /// <summary>The animation file, with the extension a line may leave off.</summary>
    public string File => Animation.Contains('.') ? Animation : Animation + ".nad";
    public bool Has(NmsFlags flag) => (Flags & flag) != 0;
}

/// <summary>A named attachment point: where a weapon, a shield or a head rides on the skeleton.</summary>
/// <param name="Index">Its place in the engine's table of tag names (1-43).</param>
public sealed record NmsTag(int Index, string Name, int Bone, System.Numerics.Vector3 Offset);

/// <summary>A phoneme: the jaw bone, and the angles that shape the mouth for it.</summary>
/// <param name="Letter">a-i; the engine has exactly these nine.</param>
public sealed record NmsMouth(char Letter, int Bone, System.Numerics.Vector3 Angles);

public sealed class NmsMotionSet
{
    public string Name { get; init; } = "";
    public List<NmsMode> Modes { get; } = [];
    public List<NmsTag> Tags { get; } = [];
    public List<NmsMouth> Mouths { get; } = [];
    /// <summary>Lines the engine would drop, kept so a check over the shipped files can say so.</summary>
    public List<string> Dropped { get; } = [];

    /// <summary>What a character holds when it is doing nothing.</summary>
    public const string Idle = "stand";

    /// <summary>The mode of that name, case-insensitively, or null.</summary>
    public NmsMode? Mode(string name) =>
        Modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The mode with that number, or null. A script asks by number — the MOTION_* constants in the
    /// game's own <c>Codex.java</c> are this table's indices — and no shipped set mixes body modes
    /// with head modes, so the number alone is unambiguous.
    /// </summary>
    public NmsMode? Mode(int index) => Modes.FirstOrDefault(m => m.Index == index);

    public NmsMode? Stand => Mode(Idle);

    /// <summary>True when its modes came from the head table: this set poses a head, not a body.</summary>
    public bool IsHead => Modes.Count > 0 && Modes.TrueForAll(m => m.Kind == NmsModeKind.Head);

    public static NmsMotionSet Read(ReadOnlySpan<byte> data, string fileName)
    {
        var set = new NmsMotionSet { Name = Path.GetFileNameWithoutExtension(fileName) };
        foreach (var raw in System.Text.Encoding.Latin1.GetString(data).Split('\n'))
        {
            // The engine tests the line's first character, before any trimming.
            if (raw.Length > 0 && raw[0] == ';') continue;
            var w = new Words(raw);
            if (w.Next() is not { } first) continue;

            if (first.Equals("tag", StringComparison.OrdinalIgnoreCase)) { Tag(set, w, raw); continue; }
            if (first.Equals("mouth", StringComparison.OrdinalIgnoreCase)) { Mouth(set, w, raw); continue; }

            int index = Index(BodyModes, first);
            var kind = NmsModeKind.Body;
            if (index == 0) { index = Index(HeadModes, first); kind = NmsModeKind.Head; }
            // Not a mode name: the engine leaves the line's index at zero and moves on.
            if (index == 0) { set.Dropped.Add(raw.Trim()); continue; }
            if (w.Next() is not { } animation) { set.Dropped.Add(raw.Trim()); continue; }
            var flags = NmsFlags.None;
            while (w.Next() is { } word) if (Flag(word) is { } f) flags |= f;
            set.Modes.Add(new NmsMode(index, kind, first, animation, flags));
        }
        return set;
    }

    static void Tag(NmsMotionSet set, Words w, string raw)
    {
        // Name, bone, then an optional offset — all three of it or none, or the line is dropped.
        if (w.Next() is not { } name) { set.Dropped.Add(raw.Trim()); return; }
        int index = Index(TagNames, name);
        if (index == 0) { set.Dropped.Add(raw.Trim()); return; }
        if (w.Next() is not { } boneWord) { set.Dropped.Add(raw.Trim()); return; }
        var offset = System.Numerics.Vector3.Zero;
        if (w.Next() is { } x)
        {
            if (w.Next() is not { } y || w.Next() is not { } z) { set.Dropped.Add(raw.Trim()); return; }
            offset = new System.Numerics.Vector3(F(x), F(y), F(z));
        }
        set.Tags.Add(new NmsTag(index, TagNames[index], Atoi(boneWord), offset));
    }

    static void Mouth(NmsMotionSet set, Words w, string raw)
    {
        // The phoneme is one letter; the engine folds it to upper case and keeps a-i.
        if (w.Next() is not { Length: > 0 } phoneme) { set.Dropped.Add(raw.Trim()); return; }
        char letter = char.ToLowerInvariant(phoneme[0]);
        if (letter is < 'a' or > 'i') { set.Dropped.Add(raw.Trim()); return; }
        if (w.Next() is not { } boneWord) { set.Dropped.Add(raw.Trim()); return; }
        // The angles are one token, scanned as "(%f,%f,%f)"; all three or the line stops here.
        if (w.Next() is not { } angles || Triple(angles) is not { } v) { set.Dropped.Add(raw.Trim()); return; }
        set.Mouths.Add(new NmsMouth(letter, Atoi(boneWord), v));
    }

    /// <summary>Where a name sits in one of the engine's tables, or 0 for "not one of them".</summary>
    static int Index(string[] table, string name)
    {
        for (int i = 1; i < table.Length; i++)
            if (string.Equals(table[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    static NmsFlags? Flag(string word)
    {
        for (int i = 0; i < FlagNames.Length; i++)
            if (string.Equals(FlagNames[i].Name, word, StringComparison.OrdinalIgnoreCase)) return FlagNames[i].Flag;
        return null;
    }

    /// <summary>A token split the way the engine's does: on spaces, tabs, carriage returns and newlines.</summary>
    struct Words(string line)
    {
        readonly string line = line;
        int at = 0;

        public string? Next()
        {
            while (at < line.Length && Space(line[at])) at++;
            if (at >= line.Length) return null;
            int start = at;
            while (at < line.Length && !Space(line[at])) at++;
            return line[start..at];
        }

        static bool Space(char c) => c is ' ' or '\r' or '\t' or '\n';
    }

    /// <summary><c>(x,y,z)</c> as sscanf would read it, or null when it does not match.</summary>
    static System.Numerics.Vector3? Triple(string token)
    {
        int open = token.IndexOf('(');
        if (open != 0) return null;
        var parts = token[1..].Split(',');
        if (parts.Length < 3) return null;
        return new System.Numerics.Vector3(F(parts[0]), F(parts[1]), F(parts[2]));
    }

    /// <summary>atof: the longest leading run that is a number, else zero (so "-2.2.0" is -2.2).</summary>
    static float F(string s)
    {
        int i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) i++; }
        return float.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    /// <summary>atoi: the leading digits, else zero.</summary>
    static int Atoi(string s)
    {
        int i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return int.TryParse(s[..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    static readonly (string Name, NmsFlags Flag)[] FlagNames =
    [
        ("noloop", NmsFlags.NoLoop), ("movespeed", NmsFlags.MoveSpeed), ("highpriority", NmsFlags.HighPriority),
        ("lowpriority", NmsFlags.LowPriority), ("endpause", NmsFlags.EndPause), ("endtag", NmsFlags.EndTag),
        ("variable", NmsFlags.Variable), ("special", NmsFlags.Special), ("nofadeout", NmsFlags.NoFadeOut),
        ("nofadein", NmsFlags.NoFadeIn), ("fidget", NmsFlags.Fidget),
    ];

    /// <summary>The 43 attachment-point names, in the engine's order; index 0 is "no tag".</summary>
    public static readonly string[] TagNames =
    [
        "", "Root", "LHip", "LKnee", "LAnkle", "LFoot", "RHip", "RKnee", "RAnkle", "RFoot",
        "BTunic1", "BTunic2", "BTunic3", "FTunic1", "FTunic2", "Back1", "Back2", "Chest", "Neck", "Head",
        "Helmet", "Jaw", "RShoulder", "RElbow", "RWrist", "RFingers", "Weapon", "LShoulder", "LElbow",
        "LWrist", "LFingers", "Shield", "MCape1", "Scabbard", "User0", "User1", "User2", "User3", "User4",
        "User5", "User6", "User7", "User8", "User9",
    ];

    /// <summary>A head model's eight modes; index 0 is "no mode".</summary>
    public static readonly string[] HeadModes =
    [
        "", "head_idle", "head_hurt", "head_torpor", "head_feed", "head_staked", "head_frenzy",
        "head_impact", "head_bite",
    ];

    /// <summary>
    /// The engine's 257 body modes, in its own order — the mode number a script or the combat code
    /// asks for is an index into this. Index 0 is "no mode". ("deahtsun" is the original's spelling.)
    /// </summary>
    public static readonly string[] BodyModes =
    [
        "",
        "walk", "walk_onehand", "walk_axe2", "walk_sword2", "walk_polearm", "walk_gun", "walk_gun2",
        "walk_bow", "walk_shotgun", "walk_crossbow", "walk_rocket", "walk_chainsaw", "walk_chaingun",
        "walk_util", "walk_util_onehand", "walk_util_gun",
        "run", "run_onehand", "run_axe2", "run_sword2", "run_polearm", "run_gun", "run_gun2", "run_bow",
        "run_shotgun", "run_crossbow", "run_rocket", "run_chainsaw", "run_chaingun", "run_util",
        "run_util_onehand", "run_util_gun",
        "stand", "stand_onehand", "stand_axe2", "stand_sword2", "stand_polearm", "stand_gun", "stand_gun2",
        "stand_bow", "stand_shotgun", "stand_crossbow", "stand_rocket", "stand_chainsaw", "stand_chaingun",
        "stand_util", "stand_util_onehand", "stand_util_gun",
        "idle", "idle_onehand", "idle_axe2", "idle_sword2", "idle_polearm", "idle_gun", "idle_gun2",
        "idle_bow", "idle_shotgun", "idle_crossbow", "idle_rocket", "idle_chainsaw", "idle_chaingun",
        "idle_util", "idle_util_onehand", "idle_util_gun",
        "claw", "swordslash", "swordslash2", "swordthrust", "swordthrust2", "bluntbash", "bluntbash2",
        "bow", "crossbow", "daggerstab", "daggercut", "swing", "pistol", "rifleaimed", "shotgun",
        "machinegun", "rocket", "stake", "throw", "polethrust", "poleslash", "bluntslash", "bluntslash2",
        "clawspecial", "punch", "chainsaw", "toss", "uzi", "flamethrower", "chaingun",
        "util_swordslash", "util_swordthrust", "util_bluntbash", "util_daggerstab", "util_daggercut",
        "util_swing", "util_pistol", "util_stake", "util_throw", "util_bluntslash", "util_punch",
        "util_toss", "util_uzi",
        "block1h", "block2h", "parry1h", "parry2h", "shield", "duck",
        "damagelight", "damagemed", "damageheavy", "talk", "listen", "feed", "embraced",
        "deathquick", "deathslow", "deathdramatic", "deahtsun", "deathstaked", "deathclawed", "create",
        "feeding", "draining", "feedrelease", "feederdrained", "deathdrained", "feedreleased",
        "feedwrist", "embracedwrist", "feedingwrist", "drainingwrist", "feedreleasewrist",
        "feedreleasedwrist",
        "special1", "special2", "special3", "special4", "special5",
        "spell", "handsspell", "throwspell", "spitspell", "awaken", "unstake", "unstaked",
        "gesture1", "gesture2", "gesture3", "gesture4", "gesture5",
        "action1", "action2", "action3", "action4", "action5", "action6", "action7", "action8", "action9",
        "action10", "confused", "crouch",
        "dodge", "dodge_onehand", "dodge_axe2", "dodge_sword2", "dodge_polearm", "dodge_gun", "dodge_gun2",
        "dodge_bow", "dodge_shotgun", "dodge_crossbow", "dodge_rocket", "dodge_chainsaw", "dodge_chaingun",
        "dodge_util", "dodge_util_onehand", "dodge_util_gun",
        "special6", "special7", "special8", "special9", "special10",
        "gesture6", "gesture7", "gesture8", "gesture9", "gesture10",
        "limp", "limp_onehand", "limp_axe2", "limp_sword2", "limp_polearm", "limp_gun", "limp_gun2",
        "limp_bow", "limp_shotgun", "limp_crossbow", "limp_rocket", "limp_chainsaw", "limp_chaingun",
        "limp_util", "limp_util_onehand", "limp_util_gun",
        "special11", "special12", "special13", "special14", "special15", "special16", "special17",
        "special18", "special19", "special20",
        "hurt", "hurt_onehand", "hurt_axe2", "hurt_sword2", "hurt_polearm", "hurt_gun", "hurt_gun2",
        "hurt_bow", "hurt_shotgun", "hurt_crossbow", "hurt_rocket", "hurt_chainsaw", "hurt_chaingun",
        "frenzy", "humanitylost", "feedfailed",
        "throw1", "throw2", "throw3", "throw4", "throw5",
        "thrown1", "thrown2", "thrown3", "thrown4", "thrown5", "throwmiss",
        "standup", "turnleft", "turnright",
        "special21", "special22", "special23", "special24", "special25", "staked", "hidden",
    ];
}
