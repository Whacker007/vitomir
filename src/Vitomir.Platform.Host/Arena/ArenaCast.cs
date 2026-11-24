// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Host.Arena;

/// <summary>
/// A value set on a fighter's sheet: a trait's purchased value (an Attribute or an Ability), or a power's rank; a
/// power's options learned with it (<see cref="Vitomir.Platform.Modules.IPersonSheets.TryLearn(Core.EntityId, string, int, IReadOnlyList{string}, out string)"/>),
/// or null for the rank alone (the options the sheet already has are kept, and at least one must be there).
/// </summary>
public sealed record CastValue(string Id, int Value, bool Power = false, List<string>? Options = null);

/// <summary>A fighter's sheet as the arena asks the module for it: a template, or a whole sheet (a file's text), then values over it.</summary>
public sealed record CastSheet
{
    /// <summary>The module's sheet template by id, or null for the sheet the module gives anyone it makes.</summary>
    public string? Template { get; init; }
    /// <summary>A whole sheet in the module's save format (the spec's file, read), or null.</summary>
    public string? Text { get; init; }
    /// <summary>Values set over it, in order (the swept value last).</summary>
    public List<CastValue> Values { get; init; } = [];
}

/// <summary>What drives a fighter: the duelling brain, a dummy, or a script.</summary>
public enum CastBrain { Duel, Stand, Block, Script, Archer }   // Archer (the archer brain; a scenario's control.ai "archer")

/// <summary>The archer brain's numbers a scenario gives (null: the module's <c>ranged.json</c> <c>archer_brain</c>).</summary>
public sealed record CastArcher(float? KeepMetres = null, float? SwordMetres = null, float? HoldSeconds = null, float? ErrorScale = null)
{
    /// <summary>These over <paramref name="module"/>'s.</summary>
    public Combat.ArcherBrainSettings Over(Combat.ArcherBrainSettings module) => new(KeepMetres ?? module.KeepMetres, SwordMetres ?? module.SwordMetres,
        HoldSeconds ?? module.HoldSeconds, ErrorScale ?? module.ErrorScale);
}

/// <summary>One fighter of a bout, everything the session needs to make it.</summary>
public sealed record CastFighter
{
    public required string Name { get; init; }
    /// <summary>A 1.1 character: a template (<c>rudolfGuard</c>) or the catalogue's (<c>Christof</c>).</summary>
    public required string Template { get; init; }
    /// <summary>A 1.1 weapon template (<c>--weapon</c>'s names), or null for the character's own.</summary>
    public string? Weapon { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float Z { get; init; }
    /// <summary>The facing, degrees counter-clockwise from +X.</summary>
    public required float Facing { get; init; }
    public CastBrain Brain { get; init; } = CastBrain.Duel;
    /// <summary>The duelling brain's numbers (SANDBOX), or null for the defaults.</summary>
    public DuelBrainSettings? Numbers { get; init; }
    /// <summary>A scripted fighter's actions, <c>--npc-actions</c>'s grammar without the who: <c>attack@1,block@2+1</c>.</summary>
    public string? Script { get; init; }
    /// <summary>The foe's index in the cast.</summary>
    public int Foe { get; init; }
    public CastSheet Sheet { get; init; } = new();
    /// <summary>Its Blood at the start (the module's <c>blood</c>, set after its sheet), or null for the module's (full).</summary>
    public int? Blood { get; init; }
    /// <summary>The quick slots its brain may use (1 to 8), or null for every slot that shows a power.</summary>
    public List<int>? Slots { get; init; }
    /// <summary>Whether it carries a shield (its weapon's held model holds one: 1.1's shield is part of the weapon, rudolfGuard's <c>weapRapier_shield</c>), checked when the bout is made; null for whatever its weapon holds.</summary>
    public bool? Shield { get; init; }
    /// <summary>The armour it wears (the module's pieces by id), or null for none — a fighter wears what its spec says and nothing else, whatever its 1.1 template wears in play.</summary>
    public List<string>? Armor { get; init; }
    /// <summary>Its side (one against several: the duelling brains choose a foe of another side, the bout ends when one side is left), or null for none — a cast without sides is the cast earlier, text for text.</summary>
    public string? Side { get; init; }
    /// <summary>Its Health at the start (the module's <c>health</c>, set after its sheet; 1 to its maximum), or null for the module's (full) — a scenario's <c>health</c>; the arena's casts never set it, so their text is unchanged.</summary>
    public int? Health { get; init; }
    /// <summary>The bow it carries (the module's ranged weapons by id: <c>war_bow</c>), put away at the start, or null for none — a scenario's <c>gear.ranged.weapon</c>; a world whose cast carries a bow registers the bow (<see cref="Combat.BowServices"/>), one without none of it, so the arena's casts and worlds are unchanged.</summary>
    public string? Ranged { get; init; }
    /// <summary>The arrows in its quiver at the start (<c>gear.ranged.arrows</c>), or null with no bow.</summary>
    public int? Arrows { get; init; }
    /// <summary>How it moves — a movement profile's name (<c>human</c>, <c>neonate</c>, <c>elder</c>) or <c>template</c> (its 1.1 template's walk and run, read from the install), or null for the human's as before (the cast's text unchanged).</summary>
    public string? Movement { get; init; }
    /// <summary>Its own combat tempo in place of the global (a scenario's <c>overrides.actorTempo</c>; <see cref="Combat.ActorTempo"/>), or null for the global; the arena's casts never set it, so their text is unchanged.</summary>
    public float? Tempo { get; init; }
    /// <summary>Its clips' own play rates (a scenario's <c>overrides.clipRate</c>), or null for none: its character is registered as a kind of its own with those clips' rates and busy times scaled (<see cref="ClipRates"/>).</summary>
    public List<CastClipRate>? ClipRates { get; init; }
    /// <summary>The archer brain's numbers (with <see cref="CastBrain.Archer"/>), or null for the module's; the arena's casts never set it.</summary>
    public CastArcher? Archer { get; init; }
}

/// <summary>One clip's play rate for one fighter: a clip's name (1.1's, a stitched or wind-up blow's as the log names it) or an action kind (<c>quick</c>, <c>strong</c>, <c>block</c>, <c>hurt</c>), and the rate it is multiplied by.</summary>
public sealed record CastClipRate(string Clip, float Rate);

/// <summary>
/// One bout's cast: the fighters as the spec and the bout's swept values make them. It is the session's
/// <see cref="SessionOptions.Arena"/> (as JSON), carried in a tape's head, so <c>--replay-check</c> builds the same bout.
/// </summary>
public sealed record ArenaCast
{
    public List<CastFighter> Fighters { get; init; } = [];

    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The cast as compact JSON (the order and numbers fixed, so the same cast writes the same text).</summary>
    public string Write() => JsonSerializer.Serialize(this, Json);

    /// <summary>A cast from its JSON; <see cref="ArenaSpecException"/> (<see cref="ArenaSpecError.CastUnreadable"/>) when it is not one.</summary>
    public static ArenaCast Read(string text)
    {
        try
        {
            var cast = JsonSerializer.Deserialize<ArenaCast>(text, Json) ?? throw new JsonException("empty");
            if (cast.Fighters.Count < 2) throw new JsonException("fewer than two fighters");
            return cast;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArenaSpecException(ArenaSpecError.CastUnreadable, $"arena: the cast is not readable ({e.Message})");
        }
    }

    /// <summary>Whether the cast's fighters have sides.</summary>
    [JsonIgnore] public bool HasSides => Fighters.Exists(f => f.Side is not null);

    /// <summary>Whether a cast's text gives any fighter a bow (false for none, or a text that is no cast).</summary>
    public static bool BowsIn(string? castText)
    {
        if (castText is null || !castText.Contains("\"Ranged\"", StringComparison.Ordinal)) return false;
        try { return Read(castText).Fighters.Exists(f => f.Ranged is not null); }
        catch (ArenaSpecException) { return false; }
    }

    /// <summary>Whether a cast's text gives any fighter 1.1's approach (false for none, or a text that is no cast).</summary>
    public static bool OneOneIn(string? castText)
    {
        if (castText is null || !castText.Contains("\"OneOne\"", StringComparison.Ordinal)) return false;
        try { return Read(castText).Fighters.Exists(f => f.Numbers?.Approach == DuelApproach.OneOne); }
        catch (ArenaSpecException) { return false; }
    }

    /// <summary>Whether a cast's text gives any fighter the archer brain (false for none, or a text that is no cast).</summary>
    public static bool ArchersIn(string? castText)
    {
        if (castText is null || !castText.Contains("\"Archer\"", StringComparison.Ordinal)) return false;
        try { return Read(castText).Fighters.Exists(f => f.Brain == CastBrain.Archer); }
        catch (ArenaSpecException) { return false; }
    }

    /// <summary>Whether a cast's text gives its fighters sides (false for none, or a text that is no cast).</summary>
    public static bool SidesIn(string? castText)
    {
        if (castText is null) return false;
        try { return Read(castText).HasSides; }
        catch (ArenaSpecException) { return false; }
    }

    /// <summary>Whether a cast's text gives any fighter its own combat tempo (false for none, or a text that is no cast).</summary>
    public static bool TemposIn(string? castText)
    {
        if (castText is null || !castText.Contains("\"Tempo\"", StringComparison.Ordinal)) return false;
        try { return Read(castText).Fighters.Exists(f => f.Tempo is not null); }
        catch (ArenaSpecException) { return false; }
    }

    /// <summary>The key a cast fighter's character is registered under: its template and weapon.</summary>
    public static string KindKey(string template, string? weapon) => $"arena:{template}|{weapon ?? ""}";

    /// <summary>The key a fighter's character is registered under: <see cref="KindKey(string, string?)"/>, with its clip rates when it has any (a kind of its own).</summary>
    public static string KindKey(CastFighter f) =>
        f.ClipRates is not { Count: > 0 } rates ? KindKey(f.Template, f.Weapon)
        : KindKey(f.Template, f.Weapon) + "|rates:" + string.Join(",", rates.Select(r => r.Clip + "=" + r.Rate.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The characters a cast needs, as one text (what decides the loaded content), or empty for none.</summary>
    public static string KindsKey(string? castText)
    {
        if (castText is null) return "";
        try { return string.Join(";", Read(castText).Fighters.Select(f => KindKey(f))); }   // with a fighter's clip rates
        catch (ArenaSpecException) { return castText; }
    }
}
