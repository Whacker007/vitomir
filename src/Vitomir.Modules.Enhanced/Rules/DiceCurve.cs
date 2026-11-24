// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.RegularExpressions;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>A formula text of the profile this module cannot read (refused by name, never guessed at).</summary>
public sealed class FormulaTextException(string key, string text)
    : Exception($"value '{key}' is the formula '{text}', which is not one this module can read (it reads 'raw/N' and 'raw/N for raw<=T; A+C*ln(1+(raw-T)/S) beyond')")
{
    public string Key { get; } = key;
    public string Text { get; } = text;
}

/// <summary>
/// A raw score turned into dice (ours): one of the profile's formula texts, read with its numbers, so the core
/// hard-codes none of them (the rules package: "hardcode none of /20 … in the core"). Two shapes are known, the two the sandbox uses:
/// <list type="bullet">
/// <item><c>raw/N</c> (<c>raw_to_damage_dice</c>, the combat rules' research-only trial <c>φ(E) = E/20</c>);</item>
/// <item><c>raw/N for raw&lt;=T; A+C*ln(1+(raw-T)/S) beyond</c> (<c>raw_to_soak_dice</c>, the combat rules' lower-range trial and
/// upper stress, both unapproved).</item>
/// </list>
/// Any other text is refused by name (<see cref="FormulaTextException"/>). The logarithm is the platform's
/// deterministic one (<see cref="DetMath.Log"/>), so a pool is the same on every machine. The result may be fractional:
/// the dice's own rule picks a whole pool from it (<see cref="Dice.Pool"/>).
/// </summary>
public sealed class DiceCurve
{
    static readonly Regex Plain = new(@"^raw/(?<d>[0-9.]+)$", RegexOptions.CultureInvariant);
    static readonly Regex Piecewise = new(@"^raw/(?<d>[0-9.]+)forraw<=(?<t>[0-9.]+);(?<a>[0-9.]+)\+(?<c>[0-9.]+)\*ln\(1\+\(raw-(?<t2>[0-9.]+)\)/(?<s>[0-9.]+)\)beyond$", RegexOptions.CultureInvariant);

    DiceCurve(string key, string text, double divisor, double? threshold, double a, double c, double scale)
    {
        Key = key; Text = text; Divisor = divisor; Threshold = threshold; A = a; C = c; Scale = scale;
    }

    /// <summary>The profile value it was read from, and its text as written.</summary>
    public string Key { get; }
    public string Text { get; }

    public double Divisor { get; }

    /// <summary>Where the logarithmic part begins (null for the plain shape), and its numbers.</summary>
    public double? Threshold { get; }
    public double A { get; }
    public double C { get; }
    public double Scale { get; }

    /// <summary>Reads a formula text; throws <see cref="FormulaTextException"/> for one it does not know.</summary>
    public static DiceCurve Parse(string key, string text)
    {
        string t = string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));
        static double N(Match m, string g) => double.Parse(m.Groups[g].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        static bool Ok(Match m, params string[] groups) =>
            groups.All(g => double.TryParse(m.Groups[g].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v));
        var plain = Plain.Match(t);
        if (plain.Success && Ok(plain, "d") && N(plain, "d") > 0)
            return new DiceCurve(key, text, N(plain, "d"), null, 0, 0, 1);
        var piece = Piecewise.Match(t);
        if (piece.Success && Ok(piece, "d", "t", "a", "c", "t2", "s") && N(piece, "d") > 0 && N(piece, "s") > 0 && N(piece, "t") == N(piece, "t2"))
            return new DiceCurve(key, text, N(piece, "d"), N(piece, "t"), N(piece, "a"), N(piece, "c"), N(piece, "s"));
        throw new FormulaTextException(key, text);
    }

    /// <summary>The dice for a raw score (0 for a score at or below 0).</summary>
    public double Of(double raw)
    {
        if (!(raw > 0)) return 0;
        if (Threshold is not { } t || raw <= t) return raw / Divisor;
        return A + C * DetMath.Log((float)(1.0 + (raw - t) / Scale));
    }
}
