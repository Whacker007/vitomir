// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Collections;
using System.Numerics;
using System.Reflection;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Compares two object graphs field by field — every public property of every record and class,
/// every element of every array — and counts the leaves it compared. Floats are compared by their
/// bits, so a NaN, a negative zero or a rounding would all show. Made for the L2 lossless test
/// (<see cref="L2LosslessTests"/>): the models are trees of records and arrays, with no cycles.
/// </summary>
public sealed class ModelComparer
{
    /// <summary>Leaf values compared so far (numbers, strings, flags, each float of a vector).</summary>
    public long Fields { get; private set; }
    /// <summary>The first differences found, as paths.</summary>
    public List<string> Differences { get; } = [];

    static readonly Dictionary<Type, PropertyInfo[]> Props = [];

    public void Compare(object? a, object? b, string path)
    {
        if (Differences.Count >= 20) return;
        if (a is null || b is null)
        {
            Fields++;
            if (a is not null || b is not null) Differences.Add($"{path}: {a ?? "null"} vs {b ?? "null"}");
            return;
        }
        var t = a.GetType();
        if (t != b.GetType()) { Differences.Add($"{path}: {t.Name} vs {b.GetType().Name}"); return; }
        switch (a)
        {
            case float f: Leaf(BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits((float)b), path, a, b); return;
            case Vector2 v: Compare(v.X, ((Vector2)b).X, path + ".X"); Compare(v.Y, ((Vector2)b).Y, path + ".Y"); return;
            case Vector3 v:
                var w = (Vector3)b;
                Compare(v.X, w.X, path + ".X"); Compare(v.Y, w.Y, path + ".Y"); Compare(v.Z, w.Z, path + ".Z"); return;
            case string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or Enum:
                Leaf(a.Equals(b), path, a, b); return;
            case byte[] x:
                var y = (byte[])b;
                Fields += x.Length;
                if (!x.AsSpan().SequenceEqual(y)) Differences.Add($"{path}: {x.Length} bytes vs {y.Length}, differing"); return;
            case ushort[] x:
                var z = (ushort[])b;
                Fields += x.Length;
                if (!x.AsSpan().SequenceEqual(z)) Differences.Add($"{path}: {x.Length} u16s vs {z.Length}, differing"); return;
            case IList list:
                var other = (IList)b;
                Fields++;
                if (list.Count != other.Count) { Differences.Add($"{path}: {list.Count} vs {other.Count} elements"); return; }
                for (int i = 0; i < list.Count; i++) Compare(list[i], other[i], $"{path}[{i}]");
                return;
        }
        if (!Props.TryGetValue(t, out var props))
            Props[t] = props = [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0)
                                    .Where(p => p.Name != "EqualityContract")];
        if (props.Length == 0) throw new InvalidOperationException($"{path}: no way to compare a {t.Name}");
        foreach (var p in props) Compare(p.GetValue(a), p.GetValue(b), $"{path}.{p.Name}");
    }

    void Leaf(bool same, string path, object a, object b)
    {
        Fields++;
        if (!same) Differences.Add($"{path}: {a} vs {b}");
    }
}
