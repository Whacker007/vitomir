// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's rule numbers as one object (ours): every number of 1.1's that lives in its program rather than its data, by
// section, read where the engine's constants were read. Built once a run from the layers (RulesFile.Resolve) and put in force
// as Current; the engine's named constants stay as the defaults' one source.
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Vitomir.Engine.Rules;

/// <summary>
/// **The rules in force** (ours): one object of typed sections (<see cref="Stats"/>, <see cref="Combat"/>, …), each a class of public
/// fields, one a key of the rules file (<see cref="RuleAttribute"/>). A new one holds the defaults: 1.1's numbers, from the engine's
/// named constants. The engine reads <see cref="Current"/>'s fields where it read the constants (a field, never a lookup by name), so
/// with the defaults every result is the one it was. <see cref="RulesFile"/> makes one from the layers; a replay puts a tape's in force.
/// </summary>
public sealed class GameRules
{
    public readonly StatRules Stats = new();
    public readonly RankRules Ranks = new();
    public readonly HealingRules Healing = new();
    public readonly AdvancementRules Advancement = new();
    public readonly CombatRules Combat = new();
    public readonly RangedRules Ranged = new();
    public readonly WoundRules Wounds = new();
    public readonly StakeRules Stake = new();
    public readonly CastingRules Casting = new();
    public readonly FeedingRules Feeding = new();
    public readonly FrenzyRules Frenzy = new();
    public readonly DeathRules Death = new();
    public readonly MovementRules Movement = new();
    public readonly SightRules Sight = new();
    public readonly AiRules Ai = new();
    public readonly ItemRules Items = new();
    public readonly TimeRules Time = new();
    public readonly PartyRules Party = new();

    /// <summary>
    /// The rules the game plays by: the defaults until a run's start resolves its layers (<see cref="RulesFile.Resolve"/>) or a replay
    /// puts its tape's in force. One for the process, as the Fixes are (the engine's statics: one Classic per process). A test that
    /// changes it runs alone (a collection that does not run in parallel) and puts it back.
    /// </summary>
    public static GameRules Current { get; set; } = new();

    /// <summary>A fresh copy of the defaults (1.1's numbers).</summary>
    public static GameRules Defaults() => new();

    /// <summary>A copy of these rules (lists copied, so changing one changes nothing of the other).</summary>
    public GameRules Copy()
    {
        var copy = new GameRules();
        foreach (var key in RuleKey.All) key.Set(copy, key.Clone(key.Get(this)));
        return copy;
    }

    /// <summary>The keys whose value differs from the default, in the file's order.</summary>
    public IReadOnlyList<RuleKey> Changed()
    {
        var defaults = Defaults();
        return [.. RuleKey.All.Where(k => k.Format(this) != k.Format(defaults))];
    }

    /// <summary>Whether every key holds its default: a game played by these rules is 1.1's (faithful, as far as the rules go).</summary>
    public bool IsDefault => Changed().Count == 0;

    /// <summary>
    /// **The rules as one hash**: SHA-256, hex, over every key as <c>Section.Key=value</c>, a line each, in the file's order, the values as
    /// the file writes them (<see cref="RuleKey.Format"/>: a float to the bit). Two machines with the same rules agree, whatever files
    /// they came from.
    /// </summary>
    public string Hash()
    {
        var text = new StringBuilder();
        foreach (var key in RuleKey.All) text.Append(key.Id).Append('=').Append(key.Format(this)).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The hash's first sixteen hex digits: what a tape, a save and the join name the rules by.</summary>
    public string ShortHash() => Hash()[..16];
}

/// <summary>
/// **One key of the rules file**: its section and name (<see cref="Id"/>, <c>Combat.CriticalMargin</c>), the field it is, what the
/// field's attribute says of it, and how a value of it is read and written.
/// </summary>
public sealed class RuleKey
{
    public string Section { get; }
    public string Name { get; }
    /// <summary><c>Section.Name</c>: how a tape, a save and the warnings name it.</summary>
    public string Id => Section + "." + Name;
    public RuleAttribute Rule { get; }
    public RuleSectionAttribute SectionRule { get; }
    public Type Type => info.FieldType;
    readonly FieldInfo sectionField, info;

    RuleKey(FieldInfo sectionField, RuleSectionAttribute section, FieldInfo field, RuleAttribute rule)
    {
        this.sectionField = sectionField;
        info = field;
        SectionRule = section;
        Section = section.Name;
        Name = field.Name;
        Rule = rule;
    }

    /// <summary>Every key, in the file's order: the sections as <see cref="GameRules"/> declares them, the keys as each section does.</summary>
    public static IReadOnlyList<RuleKey> All => all.Value;
    static readonly Lazy<RuleKey[]> all = new(() =>
    {
        var keys = new List<RuleKey>();
        foreach (var s in typeof(GameRules).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken))
        {
            var section = s.FieldType.GetCustomAttribute<RuleSectionAttribute>()
                ?? throw new InvalidOperationException($"{s.FieldType.Name} has no [RuleSection]");
            foreach (var f in s.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken))
                if (f.GetCustomAttribute<RuleAttribute>() is { } rule)
                {
                    if (!Readable(f.FieldType)) throw new InvalidOperationException($"{section.Name}.{f.Name}: a rule of a type the file cannot hold ({f.FieldType.Name})");
                    keys.Add(new RuleKey(s, section, f, rule));
                }
        }
        return [.. keys];
    });

    static readonly Dictionary<string, RuleKey> byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The key of a section and a name, matched without regard to case; null when the game has none.</summary>
    public static RuleKey? Find(string section, string name) => Find(section + "." + name);

    /// <summary>The key of an id (<c>Section.Name</c>), matched without regard to case; null when the game has none.</summary>
    public static RuleKey? Find(string id)
    {
        lock (byId)
        {
            if (byId.Count == 0) foreach (var k in All) byId[k.Id] = k;
            return byId.GetValueOrDefault(id);
        }
    }

    static bool Readable(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) || t == typeof(bool) || t == typeof(string)
        || t.IsArray && t.GetElementType() is { } e && e != typeof(bool) && Readable(e);

    public object Get(GameRules rules) => info.GetValue(sectionField.GetValue(rules))!;
    public void Set(GameRules rules, object value) => info.SetValue(sectionField.GetValue(rules), value);

    /// <summary>A copy of a value (a list's own array).</summary>
    public object Clone(object value) => value is Array a ? a.Clone() : value;

    /// <summary>The value as the file writes it: a float in its shortest form that reads back to the bit, a list comma separated.</summary>
    public string Format(GameRules rules) => FormatValue(Get(rules));

    static string FormatValue(object value) => value switch
    {
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        string s => s,
        Array a => string.Join(", ", a.Cast<object>().Select(FormatValue)),
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// A value read from the file into the key's own type (a float as a float, so 1.4 is 1.4f and the defaults' arithmetic is
    /// the constants' to the bit), or null with why it is refused: not a number of the type, a list of the wrong length, out of
    /// the key's range.
    /// </summary>
    public object? Parse(string text, out string? why)
    {
        why = null;
        text = text.Trim();
        if (Type.IsArray)
        {
            var e = Type.GetElementType()!;
            var parts = text.Length == 0 ? [] : text.Split(',').Select(p => p.Trim()).ToArray();
            if (Rule.Count > 0 && parts.Length != Rule.Count) { why = $"{Rule.Count} values are wanted, comma separated, and it has {parts.Length}"; return null; }
            var array = Array.CreateInstance(e, parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (One(e, parts[i], out why) is not { } v) { why = $"its value {i + 1} ({parts[i]}): {why}"; return null; }
                array.SetValue(v, i);
            }
            return array;
        }
        return One(Type, text, out why);
    }

    object? One(Type t, string text, out string? why)
    {
        why = null;
        object? value = null;
        double number = double.NaN;
        if (t == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) (value, number) = (i, i);
        else if (t == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) (value, number) = (l, l);
        else if (t == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f)) (value, number) = (f, f);
        else if (t == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) (value, number) = (d, d);
        else if (t == typeof(bool))
        {
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text is "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase)) value = true;
            else if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text is "0" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase)) value = false;
        }
        else if (t == typeof(string)) value = text.Length > 0 ? text : null;
        if (value is null)
        {
            why = t == typeof(int) || t == typeof(long) ? "not a whole number" : t == typeof(float) || t == typeof(double) ? "not a number"
                : t == typeof(bool) ? "not true or false" : "empty";
            return null;
        }
        if (!double.IsNaN(Rule.Min) && number < Rule.Min) { why = $"below its least, {Rule.Min.ToString(CultureInfo.InvariantCulture)}"; return null; }
        if (!double.IsNaN(Rule.Max) && number > Rule.Max) { why = $"above its most, {Rule.Max.ToString(CultureInfo.InvariantCulture)}"; return null; }
        if (Rule.PowerOfTwo && (number < 1 || (((long)number) & ((long)number - 1)) != 0)) { why = "not a power of two"; return null; }
        return value;
    }

    public override string ToString() => Id;
}
