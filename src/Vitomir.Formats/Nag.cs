// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A foley file (`misc/Foley/*.nag`): which recordings a thing makes for which event, read the way
// the game reads one.
//
// ```
// ; a comment, but only when the semicolon is the first character of the line
// walk_stone   footStepStone_01.wav   0.6  1.0  200 1000  linktothing
// ```
//
// One line is one entry: an **event name**, a **recording**, then up to four numbers and any number
// of flag words, in any order, separated by spaces, tabs and line ends. The numbers are positional
// among themselves only — the first number is the volume wherever it sits on the line, the second the
// rate, the third the near distance and the fourth the far one; a fifth is read and dropped.
namespace Vitomir.Formats;

/// <summary>One line of a foley file.</summary>
/// <param name="Event">
/// Index into <see cref="Nag.ThingEvents"/>, or into <see cref="Nag.UiEvents"/> for `ui.nag` — the
/// engine keeps both in the same 151 slots, so `BUTTONDEPRESS` shares slot 0 with `create`.
/// </param>
/// <param name="Sound">The recording, as written.</param>
/// <param name="Volume">0..1; default 0.75.</param>
/// <param name="Rate">What the recording's own sample rate is multiplied by; default 1.</param>
/// <param name="MinDist">Where it starts to fall off; default 200.</param>
/// <param name="MaxDist">Where it is gone; default 1000.</param>
/// <param name="Flags">The mixer flags (<see cref="Nag.FlagWords"/>); default <c>linktopos</c>.</param>
public sealed record NagEntry(int Event, string Sound, float Volume, float Rate, float MinDist, float MaxDist, uint Flags);

public static class Nag
{
    /// <summary>The engine's defaults for a new entry.</summary>
    public const float DefaultVolume = 0.75f, DefaultRate = 1f, DefaultMin = 200f, DefaultMax = 1000f;
    /// <summary>A new entry is <c>linktopos</c> until a flag word says otherwise.</summary>
    public const uint DefaultFlags = 2;

    /// <summary>How many event slots a foley set has (<c>0x97</c>).</summary>
    public const int Slots = 151;

    /// <summary>
    /// The nine surfaces a footstep event names. A material's <c>sound</c> keyword counts them in
    /// the same order under its own words (carpet, dirt, metal, <b>wet</b>, snow, stone,
    /// street, wood, <b>creaky</b>, and a tenth, <c>silent</c>, which has no event of its own).
    /// </summary>
    public static readonly string[] Surfaces = ["carpet", "dirt", "metal", "puddle", "snow", "stone", "street", "wood", "woodcreaky"];

    /// <summary>The 151 thing events, in the engine's order (its own table's).</summary>
    public static readonly string[] ThingEvents = Build();

    static string[] Build()
    {
        var e = new List<string>
        {
            "create", "activate", "startmove", "stopmove", "moving", "swing", "swing2",
            "hitflesh", "hitstone", "hitmetal", "hitleather", "hitcloth", "hitmail", "hitmagic", "hitkevlar",
            "hitwood", "hitbone", "outofammo", "drop", "pickup", "wear", "remove", "use", "kiss", "kissattack",
            "detach", "suck", "bloodpointdrain",
        };
        foreach (var m in new[] { "walk", "run", "scuff" })
            foreach (var s in Surfaces) e.Add($"{m}_{s}");
        string[] harm = ["lethal", "aggravated", "electric", "fire", "sun", "holy", "cold", "poison", "disease"];
        e.Add("damage"); foreach (var h in harm) e.Add("damage_" + h);
        e.Add("death"); foreach (var h in harm) e.Add("death_" + h);
        e.AddRange(["death_bodyfall", "death_torpor", "death_awaken", "anim_idle", "anim_idle2",
            "vox_alert", "vox_greet", "vox_fear", "vox_bored", "vox_taunt", "vox_worried", "vox_attack",
            "vox_critical", "vox_frenzy", "vox_unfrenzy", "vox_nearfrenzy"]);
        e.Add("vox_damage"); foreach (var h in harm) e.Add("vox_damage_" + h);
        e.Add("vox_death"); foreach (var h in harm) e.Add("vox_death_" + h);
        e.AddRange(["staked", "unstaked", "vox_staked", "vox_unstaked", "flap", "vox_losthumanity",
            "vox_lostallhumanity", "vox_gainhumanity", "vox_lowhumanity", "vox_notenoughblood", "vox_lowblood",
            "vox_endfeed", "vox_endfeedwrist", "vox_endfeedkill", "vox_endfeedkillinnocent", "vox_cannotfeed",
            "vox_sated", "vox_feedchase", "vox_partyfeedok", "vox_partyfeedno", "vox_partyfeedstop",
            "vox_regroup", "vox_startfollow", "vox_stopfollow", "vox_gotcash", "vox_attackmode", "vox_neutral",
            "vox_defendmode", "vox_attackmodeok", "vox_neutralok", "vox_defendmodeok", "vox_goodhaven",
            "vox_badhaven", "vox_inventoryfull", "vox_cannotafford", "vox_newquest", "vox_questcomplete",
            "vox_newplayer", "vox_cannotwear", "vox_wakeup"]);
        if (e.Count != Slots) throw new InvalidOperationException($"foley event table has {e.Count} names");
        return [.. e];
    }


    /// <summary>The 22 interface events `ui.nag` names, in the engine's order.</summary>
    public static readonly string[] UiEvents =
    [
        "BUTTONDEPRESS", "BUTTONRELEASE", "BUTTONROLLOVER", "LISTSELECTCHANGED", "LISTCURRENTCHANGED", "NEGATIVE",
        "VOLUMETEST", "AFTERPURCHASEDA", "AFTERPURCHASEMD", "AFTERSELLDA", "AFTERSELLMD", "CANTBUY",
        "CHARACTERCHANGE", "NEWDISCIPLINE", "DISCIPLINESELECT", "MPMESSAGE1", "MPMESSAGE2", "STMESSAGE1",
        "STMESSAGE2", "NEWRANK", "STPOSSESS", "STCREATEOBJ",
    ];

    /// <summary>The flag words and the mixer bits they set.</summary>
    public static readonly (string Word, uint Flag)[] FlagWords =
    [
        ("looping", 0x8), ("hipriority", 0x1), ("linktothing", 0x4), ("linktopos", 0x2),
        ("local", 0x10), ("true3d", 0x20), ("voice", 0x100),
    ];

    /// <summary>The slot an event name fills: the thing table first, then the interface one; −1 for neither.</summary>
    public static int EventSlot(string name)
    {
        for (int i = 0; i < ThingEvents.Length; i++)
            if (string.Equals(ThingEvents[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        for (int i = 0; i < UiEvents.Length; i++)
            if (string.Equals(UiEvents[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    static readonly char[] Separators = [' ', '\r', '\t', '\n'];

    /// <summary>Every entry, in file order. The engine prepends as it reads; the set does that part.</summary>
    public static List<NagEntry> Read(string text)
    {
        var entries = new List<NagEntry>();
        foreach (var line in text.Split('\n'))
            if (line.Length > 0 && line[0] != ';' && Parse(line) is { } entry) entries.Add(entry);
        return entries;
    }

    static NagEntry? Parse(string line)
    {
        var tokens = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return null;
        int slot = EventSlot(tokens[0]);
        if (slot < 0) return null;
        float volume = DefaultVolume, rate = DefaultRate, min = DefaultMin, max = DefaultMax;
        uint flags = DefaultFlags;
        int number = 0;
        for (int i = 2; i < tokens.Length; i++)
        {
            if (ScanFloat(tokens[i], out float v))
            {
                switch (number)
                {
                    case 0: volume = Math.Clamp(v, 0f, 1f); break;
                    case 1: rate = v; break;
                    case 2: min = v; break;
                    case 3: max = v; break;
                }
                number++;
                continue;
            }
            foreach (var (word, bit) in FlagWords)
            {
                if (!string.Equals(word, tokens[i], StringComparison.OrdinalIgnoreCase)) continue;
                if (bit == 0x10) flags &= ~6u;             // `local` drops both links
                if ((bit & 6) != 0) flags &= ~0x10u;        // a link drops `local`
                flags |= bit;
                break;
            }
        }
        return new NagEntry(slot, tokens[1], volume, rate, min, max, flags);
    }

    /// <summary>
    /// <c>sscanf("%f")</c>: a number at the start of the token counts even with letters after it,
    /// and a token that starts with anything else is not a number.
    /// </summary>
    public static bool ScanFloat(string token, out float value)
    {
        int i = 0, n = token.Length;
        if (i < n && (token[i] == '+' || token[i] == '-')) i++;
        int digits = 0;
        while (i < n && char.IsAsciiDigit(token[i])) { i++; digits++; }
        if (i < n && token[i] == '.') { i++; while (i < n && char.IsAsciiDigit(token[i])) { i++; digits++; } }
        if (digits == 0) { value = 0; return false; }
        int end = i;
        if (i < n && (token[i] == 'e' || token[i] == 'E'))
        {
            int j = i + 1;
            if (j < n && (token[j] == '+' || token[j] == '-')) j++;
            int ed = 0;
            while (j < n && char.IsAsciiDigit(token[j])) { j++; ed++; }
            if (ed > 0) end = j;
        }
        value = float.Parse(token.AsSpan(0, end), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}
