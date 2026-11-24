// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The seventeen minds a character can be in, and the file that describes one.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Ai;

/// <summary>
/// What a character's AI is doing. The engine dispatches on this number every time it thinks,
/// and each mode has a handler of its own.
/// </summary>
public enum AiMode
{
    /// <summary>No handler: the AI does nothing at all.</summary>
    None = 0,
    /// <summary>Follows the player. Taken only while THING_AF_PARTY is set.</summary>
    Party = 1,
    Pet = 2,
    /// <summary>Fighting someone.</summary>
    Enemy = 3,
    /// <summary>Looking for someone to fight. <c>searchStyle</c> picks which of the two handlers runs.</summary>
    EnemySearch = 4,
    /// <summary>No handler.</summary>
    Asleep = 5,
    /// <summary>No handler.</summary>
    Neutral = 6,
    /// <summary>Walks between the scene's interest spots.</summary>
    PedWander = 7,
    /// <summary>Flies straight to them instead, if it can see them.</summary>
    PedFly = 8,
    Frenzied = 9,
    Mesmerized = 10,
    Dazed = 11,
    Afraid = 12,
    /// <summary>Spelt <c>posessing</c> in the engine's own table.</summary>
    Possessing = 13,
    Majesty = 14,
    /// <summary>No handler. What a <c>behaviorClass 4</c> character starts in.</summary>
    DoNothing = 15,
    /// <summary>
    /// Walks up to a thing and turns into <see cref="Enemy"/> once it is in reach.
    /// The engine's own name table has no entry for it.
    /// </summary>
    Approach = 16,
}

/// <summary>
/// The AI's one global switch word, which a script reaches through
/// <c>AISetMainFlags</c> / <c>AIClearMainFlags</c>. `Codex.java` names the bits and what
/// each does in 1.1 bears the name out, so every name here is confirmed twice.
///
/// Two of them settle an open reading: <see cref="NoEnemySearch"/> gates the `searchStyle 0`
/// handler and <see cref="NoGuardSearch"/> gates the `searchStyle 1` one — so style 1 really is
/// the **guard** search, which is where the crime test lives.
/// </summary>
public static class AiMainFlag
{
    /// <summary>Nobody thinks at all. `Codex.AIOff()` sets it and `AIOn()` clears it.</summary>
    public const uint Paused = 0x1;
    /// <summary>Turns the engine's own AI trace on.</summary>
    public const uint Trace = 0x2;
    /// <summary>
    /// Stops the think exactly as <see cref="Paused"/> does — the gate tests the two together
    /// (<c>(byte)flags &amp; 5</c>) — and `Codex.java` gives it no name.
    /// </summary>
    public const uint Unnamed4 = 0x4;
    /// <summary>
    /// The two quick-slot filters, and the player's Control Options menu is what sets them
    /// (`masquerade.ini`'s <c>UseQuickDisc</c> and <c>UseQuickItems</c>). They bind only a
    /// **player-type** character: with one set, an AI-driven party member may use only what is in
    /// its six quick discipline slots or its five quick item slots, and never
    /// reach into the pack. An enemy ignores them entirely.
    /// </summary>
    public const uint QuickDisciplinesOnly = 0x100, QuickItemsOnly = 0x200;
    /// <summary>Stops <c>pedwander</c> and <c>pedfly</c>.</summary>
    public const uint StopPeds = 0x2000, StopFlyPeds = 0x4000;
    /// <summary>Stops the two <c>enemysearch</c> handlers: style 0, style 1.</summary>
    public const uint NoEnemySearch = 0x8000, NoGuardSearch = 0x10000;
    /// <summary>
    /// Stops the hunt an **Attack**-stance party member runs — a 2,500-unit sweep every
    /// 300 ms for the nearest thing it is allowed to fight. This is why an aggressive party goes
    /// after civilians unprompted.
    /// </summary>
    public const uint NoPartySearch = 0x20000;
    /// <summary>Stops an enemy or a party member attacking.</summary>
    public const uint NoEnemyCombat = 0x40000, NoPartyCombat = 0x80000;
    /// <summary>Stops casting. Each pair is chosen by whether the caster is a THING_TYPE_PLAYER.</summary>
    public const uint NoEnemyCast = 0x100000, NoPartyCast = 0x200000,
                      NoEnemyCastHeal = 0x400000, NoPartyCastHeal = 0x800000,
                      NoEnemyCastBuff = 0x1000000, NoPartyCastBuff = 0x2000000,
                      NoEnemyCastDamage = 0x4000000, NoPartyCastDamage = 0x8000000,
                      NoEnemyCastSpecial = 0x10000000, NoPartyCastSpecial = 0x20000000;
}

/// <summary>
/// What a party member is told to do about fighting, one per member in <c>rpgParty</c>
/// (one per member, set by the stance buttons). Eleven places in the AI read it.
/// </summary>
public enum PartyStance
{
    /// <summary>Hunts on its own — see <see cref="AiMainFlag.NoPartySearch"/>. Follow leash x2.5.</summary>
    Attack = 0,
    /// <summary>Fights what the leader fights, and nothing else. Follow leash x1.0.</summary>
    Neutral = 1,
    /// <summary>Does not even join the leader's fight. Follow leash x0.8.</summary>
    Defense = 2,
}

/// <summary>The 39 <c>.nai</c> keys, and what the engine calls each mode.</summary>
public static class AiModes
{
    public const int Count = 17;

    /// <summary>
    /// What the engine's debug log calls each mode — reproduced with the fault it ships with.
    ///
    /// The names live in one <c>char*[17]</c>, and a comma is missing between the
    /// first two: the compiler concatenated <c>"none" "party"</c> into the single string
    /// <c>"noneparty"</c>, so the array holds 15 names where 16 were meant, every mode from 1 up
    /// prints the name of the mode below it, and modes 15 and 16 print from two null pointers.
    /// The table is used only by the <c>AI: %s (%d) changed mode</c> line, which is off unless the
    /// AI debug flag is set, so the fault never reaches the game — but it is a good check that the
    /// mode numbers here are the engine's: the mind's set-up sets mode 7 and logs "pedfly", sets mode 8 and
    /// logs "frenzied", sets mode 4 and logs "asleep".
    /// </summary>
    public static readonly string?[] LogNames =
    [
        "noneparty", "pet", "enemy", "enemysearch", "asleep", "neutral", "pedwander", "pedfly",
        "frenzied", "mesmerized", "dazed", "afraid", "posessing", "majesty", "donothing", null, null,
    ];

    /// <summary>What each mode is actually called, one per mode — what the table was meant to hold.</summary>
    public static readonly string?[] Names =
    [
        "none", "party", "pet", "enemy", "enemysearch", "asleep", "neutral", "pedwander", "pedfly",
        "frenzied", "mesmerized", "dazed", "afraid", "posessing", "majesty", "donothing", null,
    ];

    /// <summary>The name the engine would print for a mode, faults and all.</summary>
    public static string Logged(AiMode mode) =>
        (int)mode >= 0 && (int)mode < LogNames.Length ? LogNames[(int)mode] ?? "(null)" : "(null)";

    public static string Name(AiMode mode) =>
        (int)mode >= 0 && (int)mode < Names.Length ? Names[(int)mode] ?? "mode16" : "?";
}

/// <summary>
/// A loaded <c>.nai</c> file, kept the way the engine keeps one: one instance per name, shared by
/// every character that asks for it, found by the lower-cased name cut to 31 characters.
/// A character's template names the file with tag 56, ACTOR_AI.
/// </summary>
public sealed class AiClasses
{
    readonly GameFiles files;
    readonly Dictionary<string, NaiFile?> cache = new(StringComparer.Ordinal);
    readonly ILogger log = Diag.For<AiClasses>();

    /// <summary>Where the engine's file category 0x10 finds them.</summary>
    public const string Folder = "misc/ai/";

    public AiClasses(GameFiles files) => this.files = files;

    /// <summary>How many distinct files have been asked for, loaded or not.</summary>
    public int Count => cache.Count;
    /// <summary>The names that were asked for and are not in the game's files.</summary>
    public IEnumerable<string> Missing => cache.Where(e => e.Value is null).Select(e => e.Key);

    /// <summary>
    /// The class a template names, or null when there is no such file. The engine asks with the
    /// name exactly as the template writes it, so <c>parChristof.nai</c> and <c>PARCHRISTOF.NAI</c>
    /// are one entry.
    /// </summary>
    public NaiFile? Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string key = NaiFile.CacheName(name);
        if (cache.TryGetValue(key, out var hit)) return hit;

        NaiFile? loaded = null;
        if (files.TryRead(Folder + key, out var data)) loaded = NaiFile.Read(data, key);
        else log.LogDebug("no AI file {Name}", key);
        cache[key] = loaded;
        return loaded;
    }

    /// <summary>
    /// What mode a character starts in. A THING_TYPE_PLAYER is always <see cref="AiMode.Party"/>;
    /// everyone else is decided by <c>behaviorClass</c>, and anything the engine does not have a
    /// case for — which is every shipped file that says 0 — starts searching for a fight.
    /// </summary>
    public static AiMode StartMode(bool isPlayerThing, int behaviorClass) =>
        isPlayerThing ? AiMode.Party
        : behaviorClass switch
        {
            1 => AiMode.PedWander,
            3 => AiMode.PedFly,
            4 => AiMode.DoNothing,
            _ => AiMode.EnemySearch,
        };
}
