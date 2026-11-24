// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A multiplayer character: who a player brings to a game. 1.1 keeps each one as a whole actor
// record in `Crypt\npf%06d.npf` (the first free number); Vitomir keeps the choices
// that made it — the name, the model's template, the type and the clan — and, once it has played,
// its sheet (`NetSheet`: stats, experience, disciplines, pack), as a small JSON file of its own
// with comments, written and read as the save's parts are (`Save/Jsonc.cs`; ours), and builds the actor from them when the character joins a game.
using System.Text.Json;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// The choices `mpPlayerCreate` makes: a name of at most 32 characters, a
/// template from `misc/mpcharacters.txt`, Kindred, Ghoul or Human, and a
/// clan (none for a human).
/// </summary>
public sealed record NetCharacter
{
    /// <summary>The name the others see, at most <see cref="NameLimit"/> characters (the name box's limit).</summary>
    public string Name { get; init; } = "";
    /// <summary>The model: a template of `mpcharacters.txt`'s (`MP_Christof_DA` …).</summary>
    public string Template { get; init; } = "MP_Christof_DA";
    /// <summary>Kindred 0, Ghoul 1, Human 2 — the radio group's three boxes.</summary>
    public ActorType Type { get; init; } = ActorType.Vampire;
    /// <summary>The clan by name, lower case as `game.ncd` has it; empty for a human.</summary>
    public string Clan { get; init; } = "";
    /// <summary>Experience to spend; 1.1 opens the advancement screen on a new character.</summary>
    public long Experience { get; init; }
    /// <summary>
    /// Money for a character that has never played: Create gives a new one **200** (into the character's own purse). A character that has played carries its
    /// purse in <see cref="Saved"/>.
    /// </summary>
    public int Cash { get; init; }

    /// <summary>What Create puts in a new character's purse.</summary>
    public const int NewCash = 200;

    /// <summary>
    /// The character as it last stood — stats, experience, disciplines and pack — written back after
    /// every advancement and when the player leaves a game (1.1 writes the whole
    /// actor), and brought to the next game in the join. Null for a
    /// character that has never played: the host dresses it from its template (<see cref="Outfit"/>).
    /// </summary>
    public NetSheet? Saved { get; init; }

    /// <summary>The name box's limit (0x20).</summary>
    public const int NameLimit = 32;

    public void Write(NetWriter w)
    {
        w.Str(Name).Str(Template).U8((byte)Type).Str(Clan).I32((int)Math.Min(Experience, int.MaxValue)).I32(Cash);
        w.Bool(Saved is not null);
        Saved?.Write(w);
    }

    public static NetCharacter Read(NetReader r)
    {
        var c = new NetCharacter { Name = r.Str(), Template = r.Str(), Type = (ActorType)r.U8(), Clan = r.Str(), Experience = r.I32(), Cash = r.I32() };
        return r.Bool() ? c with { Saved = NetSheet.Read(r) } : c;
    }

    /// <summary>
    /// A name cut to the limit and trimmed; what the create screen keeps. 1.1's name box copies up
    /// to 32 characters into the character's name on every change; Vitomir cleans it once
    /// at Create to the same limit.
    /// </summary>
    public static string CleanName(string name)
    {
        name = name.Trim();
        return name.Length > NameLimit ? name[..NameLimit] : name;
    }

    /// <summary>
    /// The character made on the joining machine's behalf in the host's world (the host's
    /// half of a join): the template's actor, given the player's name, type and clan. The effects it
    /// is born with follow the chosen type, not the template's (<see cref="NetJoin.Reborn"/>).
    /// </summary>
    public void Apply(WorldThing thing)
    {
        if (thing.Actor is not { } actor) return;
        if (Name.Length > 0) actor.Name = Name;
        actor.Type = Type;
        if (Type == ActorType.Human) actor.Clan = "human";
        else if (Clan.Length > 0) actor.Clan = Clan;
        if (Experience > 0) actor.SpendableExperience = Experience;
        if (Cash > 0) actor.Cash = Cash;
    }

    /// <summary>
    /// <see cref="Apply(WorldThing)"/>, and then the saved sheet when there is one — its numbers, its
    /// disciplines and its pack in place of the template's (the host's half of the join's sheet, disciplines
    /// and pack).
    /// </summary>
    public void Apply(WorldThing thing, Location place)
    {
        Apply(thing);
        NetJoin.Reborn(thing);                                      // born by the chosen type
        Saved?.Apply(thing, place, withPack: true);
    }

    /// <summary>This character with its sheet taken from <paramref name="thing"/> as it now stands — what 1.1 writes back.</summary>
    public NetCharacter Keep(WorldThing thing) => thing.Actor is null ? this : this with { Saved = NetSheet.From(thing) };
}

/// <summary>
/// Where a player's characters live: `Crypt/` beside the saves, one file each (ours — 1.1's is
/// `Crypt\npf%06d.npf` under the install).
/// </summary>
public sealed class Crypt(string folder)
{
    public const string Extension = ".vplayer";
    public string Folder { get; } = folder;

    /// <summary>
    /// How a kept character is read: the save's own lenient reader (<see cref="Vitomir.Engine.Save.Jsonc"/> — comments
    /// and trailing commas) that also takes the capitalised names a file written earlier has.
    /// </summary>
    static readonly JsonSerializerOptions ReadOptions = new(Vitomir.Engine.Save.Jsonc.Options) { PropertyNameCaseInsensitive = true };

    /// <summary>The comment at the head of every kept character (every text file Vitomir writes explains itself).</summary>
    public const string Banner =
        "A Vitomir multiplayer character: who a player brings to a network game (ours; 1.1 keeps a binary\n" +
        "Crypt\\npf%06d.npf). 'name', 'template' (a model of misc/mpcharacters.txt), 'type' (0 Kindred, 1 Ghoul,\n" +
        "2 Human) and 'clan' are the create screen's choices; 'saved' is the sheet as it last stood - stats,\n" +
        "experience, disciplines, pack, the actor flags ('flags', THING_AF_*: 0x800 fast drinker, 0x8000 humanity\n" +
        "track) - written after every advancement and when the player leaves a game.\n" +
        "Comments here are written by the game and rewritten every time the character is kept.";

    /// <summary>`--save-strict-json` for this folder alone (null: <see cref="Vitomir.Engine.Save.Jsonc.Strict"/>).</summary>
    public bool? Strict { get; init; }

    /// <summary>A kept character's text: JSON with the banner, or plain JSON when strict.</summary>
    public string Text(NetCharacter character) => Vitomir.Engine.Save.Jsonc.Write(character, Banner, strict: Strict);

    /// <summary>A kept character read back, leniently; null when the text is not one.</summary>
    public static NetCharacter? Parse(string text) => JsonSerializer.Deserialize<NetCharacter>(text, ReadOptions);

    /// <summary>
    /// Every saved character, by name. 1.1 reads each file's own 0x6c-byte header for the select list
    /// Magic, version 5, name, login name, clan name, experience); here JSON is read
    /// instead (ours).
    /// </summary>
    public IReadOnlyList<NetCharacter> All()
    {
        if (!Directory.Exists(Folder)) return [];
        var found = new List<NetCharacter>();
        foreach (var file in Directory.GetFiles(Folder, "*" + Extension).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Parse(File.ReadAllText(file)) is { Name.Length: > 0 } c) found.Add(c);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>Whether a character of that name is kept (the create screen's `UI_MPPC_OVERWRITE` question).</summary>
    public bool Has(string name) => File.Exists(PathOf(name));

    // 1.1 writes that same header (magic, version 5, the name, the login name, the clan's
    // name, experience); here JSON with comments is written instead, as the save is (ours).
    public void Save(NetCharacter character)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOf(character.Name), Text(character));
    }

    public void Delete(string name)
    {
        if (File.Exists(PathOf(name))) File.Delete(PathOf(name));
    }

    string PathOf(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Folder, safe + Extension);
    }
}
