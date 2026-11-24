// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `.NPC` — the **player-character file**, and the whole of what a new game starts somebody with.
//
// Found by chasing why `--player` put a *vampire* Christof on screen when chapter one
// plays him human. The template was not wrong; it was the wrong file. `misc/Christof.NPC` opens
// with its own description of itself — *"Nihilistic Player Character file — Christof Dark Ages
// human"* — and carries the stats, the bounds, the disciplines, the quick-slot assignments and the
// starting inventory. The `.not` template is the shell (model, motion set, collide shape, AI file);
// this is the character.
//
// Eighteen files ship: three Dark Ages, seven modern-day, eight multiplayer. Thirty-four
// templates point at them with `ACTOR_RPGSTATS`, so most files are shared by several.
//
// The format is plain text, `//` to end of line for comments, one keyword per line, tokens
// separated by whitespace. **The file's own comment names the one ordering rule it has** —
// *"bloodpool MUST come before blood!!!"* — and the reason is in the game rather than in the
// parser: the game clamps a blood write against the blood *pool*, so setting blood
// before the pool exists clamps it to zero. Reading the whole file and applying it afterwards makes
// the rule moot, which is what this does; it is recorded because it tells you the original applied
// each line as it read it.
using System.Globalization;

namespace Vitomir.Formats;

/// <summary>One stat's authored row: the bounds the engine clamps into, and the starting value.</summary>
/// <param name="Min">The floor every base write is held to.</param>
/// <param name="Max">The ceiling.</param>
/// <param name="Current">What the character starts with.</param>
/// <param name="Named">Whether the file has the row (a row the reader makes); an unnamed stat is the default.</param>
public readonly record struct NpcStat(float Min, float Max, float Current, bool Named = true)
{
    /// <summary>
    /// Whether this stat was named at all; an unnamed one keeps what the reset left. A row of three zeros is named — Samuel's `appearance 0 0 0` holds a Nosferatu's
    /// appearance at 0, as the reader writes whatever the row says (the reader took an
    /// all-zero row for a missing one until then).
    /// </summary>
    public bool Authored => Named;
}

/// <summary>
/// A discipline the character begins with. <paramref name="Quick"/> is the power slot it sits in,
/// **0 to 5**, or −1 for one that is known but not slotted — which is where the status bar's six
/// discipline slots get their contents, and is a third independent source for there being six.
/// </summary>
public readonly record struct NpcDiscipline(string Name, int Level, int Quick);

/// <summary>Where a starting item goes.</summary>
public enum NpcPlacement
{
    /// <summary>The pack.</summary>
    Main = 0,
    /// <summary>The five-cell quick-use strip. No shipped file puts more than five here.</summary>
    Belt = 1,
    /// <summary>Worn or held.</summary>
    Equip = 2,
}

/// <summary>An item the character begins with, by template name.</summary>
public readonly record struct NpcItem(string Template, NpcPlacement Where);

/// <summary>A player-character file.</summary>
public sealed class NpcFile
{
    public string File { get; private set; } = "";

    /// <summary>The character's name, which is what the sheet shows.</summary>
    public string Name { get; private set; } = "";
    /// <summary>The clan, on the nine files that name one; empty for the unclanned.</summary>
    public string Clan { get; private set; } = "";
    /// <summary>Starting gold.</summary>
    public int Cash { get; private set; }

    /// <summary>
    /// The **lifetime** figure, which is what the rank ladder walks. Seventeen of
    /// the eighteen files name it.
    /// </summary>
    public int TotalExperience { get; private set; }
    /// <summary>
    /// The **spendable** pool — the character pane draws it in the box called
    /// `charEarnedExp`, whose name reads like the opposite of what it holds. Nine files name it,
    /// and in every one of those it equals <see cref="TotalExperience"/>, which is what an unspent
    /// pool looks like before anything has been spent.
    /// </summary>
    public int Experience { get; private set; }

    /// <summary>
    /// Whether the file has a `totalexperience` line, and an `experience` line: each writes its own
    /// field and nothing else (the lifetime experience and the unspent pool respectively),
    /// so a sheet without the line leaves the field as it was.
    /// </summary>
    public bool NamesTotalExperience { get; private set; }
    /// <inheritdoc cref="NamesTotalExperience"/>
    public bool NamesExperience { get; private set; }

    /// <summary>Health and its maximum, on the seven files that set them; 0 means "not authored".</summary>
    public float Health { get; private set; }
    /// <inheritdoc cref="Health"/>
    public float MaxHealth { get; private set; }

    /// <summary>One row per `ACTOR_STAT_*`, indexed the way the engine indexes them.</summary>
    public NpcStat[] Stats { get; } = new NpcStat[StatCount];

    /// <summary>`ACTOR_NUMSTATS`.</summary>
    public const int StatCount = 19;

    public List<NpcDiscipline> Disciplines { get; } = [];
    public List<NpcItem> Items { get; } = [];

    /// <summary>Lines this reader did not understand, described. Empty for every shipped file.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// The stat keywords, in the order `ACTOR_STAT_*` indexes them. Every one of the nineteen stats is
    /// named by at least one shipped file, so the whole enum is covered by the data.
    /// </summary>
    public static readonly string[] StatNames =
    [
        "strength", "dexterity", "stamina", "perception", "intelligence", "wits",
        "blood", "humanity", "frenzy", "generation", "faith", "charisma",
        "manipulation", "appearance", "frenzyrating", "bloodrating", "bloodpool",
        "mana", "manapool",
    ];

    public static NpcFile Read(byte[] data, string name = "<npc>") =>
        Parse(System.Text.Encoding.Latin1.GetString(data), name);

    // The reader steps line by line, skipping a blank line and one starting `#` or `//`
    // outright; here a `#` line is warned about as an unknown keyword instead of skipped, which no
    // shipped file triggers.
    public static NpcFile Parse(string text, string name = "<npc>")
    {
        var npc = new NpcFile { File = name };
        int lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;

            string key = t[0].ToLowerInvariant();
            int stat = Array.IndexOf(StatNames, key);
            if (stat >= 0)
            {
                // `<stat> <min> <max> <current>`. A row with fewer numbers is not something any
                // shipped file does, so it is reported rather than guessed at.
                if (t.Length < 4) { npc.Warn(lineNumber, line, "a stat row wants min, max and current"); continue; }
                npc.Stats[stat] = new NpcStat(Num(t[1]), Num(t[2]), Num(t[3]));
                continue;
            }

            switch (key)
            {
                case "name": npc.Name = t.Length > 1 ? t[1] : ""; break;
                case "clan": npc.Clan = t.Length > 1 ? t[1] : ""; break;
                case "cash": npc.Cash = (int)Num(t.ElementAtOrDefault(1)); break;
                case "totalexperience": npc.TotalExperience = (int)Num(t.ElementAtOrDefault(1)); npc.NamesTotalExperience = true; break;
                case "experience": npc.Experience = (int)Num(t.ElementAtOrDefault(1)); npc.NamesExperience = true; break;
                case "health": npc.Health = Num(t.ElementAtOrDefault(1)); break;
                case "maxhealth": npc.MaxHealth = Num(t.ElementAtOrDefault(1)); break;

                case "discipline" when t.Length >= 3:
                    npc.Disciplines.Add(new NpcDiscipline(t[1], (int)Num(t[2]),
                                                          t.Length > 3 ? (int)Num(t[3]) : -1));
                    break;

                case "item" when t.Length >= 2:
                    npc.Items.Add(new NpcItem(t[1], Placement(t.ElementAtOrDefault(2))));
                    break;

                default:
                    npc.Warn(lineNumber, line, $"unknown keyword `{t[0]}`");
                    break;
            }
        }
        return npc;
    }

    void Warn(int line, string text, string why) =>
        Warnings.Add($"{File}:{line}: {why} — `{text.Trim()}`");

    /// <summary>
    /// `main`, `belt` or `equip`; anything else is the pack, which is what an unplaced item is.
    /// </summary>
    static NpcPlacement Placement(string? word) => word?.ToLowerInvariant() switch
    {
        "belt" => NpcPlacement.Belt,
        "equip" => NpcPlacement.Equip,
        _ => NpcPlacement.Main,
    };

    /// <summary>A number, tolerating the `1.0` the `bloodrating` rows are written with.</summary>
    static float Num(string? token) =>
        float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    /// <summary>Where the player-character files live in the install.</summary>
    public const string Folder = "misc/";

    /// <summary>
    /// The file a template's `ACTOR_RPGSTATS` names. **Half the templates write the extension and
    /// half do not** — `darkAges.not`'s `Christof` says `Christof`, `Multiplayer.not`'s
    /// `MP_Christof_DA` says `MP_Christof.NPC`, and `MP_Serena` and `MP_Kazi` disagree with each
    /// other about the same file. So the tag is a *name*, and an extension already on it is not a
    /// second one to add.
    /// </summary>
    public static string PathFor(string rpgStats) =>
        Folder + (rpgStats.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                  ? rpgStats[..^Extension.Length] : rpgStats) + Extension;

    public const string Extension = ".npc";
}
