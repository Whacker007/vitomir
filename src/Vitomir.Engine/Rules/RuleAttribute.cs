// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the rules file says of each of its keys (ours): the line of documentation the shipped rules.cfg carries above it,
// and the few checks a value must pass before it is taken.
namespace Vitomir.Engine.Rules;

/// <summary>
/// **One key of the rules file**, on a public field of a section (<see cref="RuleSectionAttribute"/>). The field's name is the key,
/// its type the value's (<c>int</c>, <c>long</c>, <c>float</c>, <c>double</c>, <c>bool</c>, <c>string</c>, or an array of one of those: a list on one
/// line, comma separated), and its initial value the default, taken from the engine's own named constant: 1.1's number unless the
/// key says <see cref="Ours"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RuleAttribute(string what) : Attribute
{
    /// <summary>What the number does, in a line or two: the shipped file's comment above the key.</summary>
    public string What { get; } = what;

    /// <summary>The default is Vitomir's own, not a number of 1.1's (a repair or a guard of ours).</summary>
    public bool Ours { get; init; }

    /// <summary>
    /// A number the game's structure is built on (an array's size, a sentinel of a file format, an identity, the random stream's
    /// definition): the key is listed and read, but a value other than the default is said and refused, and the default kept.
    /// </summary>
    public bool Fixed { get; init; }

    /// <summary>Why it is fixed, said when a value is refused (with <see cref="Fixed"/>).</summary>
    public string? FixedWhy { get; init; }

    /// <summary>What is not known about it, written as a <c>; unsure:</c> comment on the key.</summary>
    public string? Unsure { get; init; }

    /// <summary>A list's exact number of values; 0 for any.</summary>
    public int Count { get; init; }

    /// <summary>The smallest value taken (each value of a list); NaN for none.</summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>The largest value taken (each value of a list); NaN for none.</summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>The value must be a power of two (a mask's width).</summary>
    public bool PowerOfTwo { get; init; }

    /// <summary>What each value of a list is, in order, for the comment (<c>rank 1, rank 2, …</c>).</summary>
    public string? Values { get; init; }
}

/// <summary>A section of the rules file: a class whose fields are its keys, named <c>[Name]</c> in the file.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RuleSectionAttribute(string name, string what) : Attribute
{
    public string Name { get; } = name;
    public string What { get; } = what;
}
