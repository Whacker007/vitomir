// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How a register's typed properties are tied to their declarations: by name, once (ours).
using System.Reflection;

namespace Vitomir.Engine.Features;

/// <summary>
/// Ties a boolean property of <see cref="World.Fixes"/> or <see cref="World.Qol"/> to the feature of that name in
/// the registry (ours). The property is what code and tests read and set (<c>fixes.GunReach</c>); the declaration
/// is everything else about the switch. <see cref="SwitchTable{T}"/> reads these once.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SwitchAttribute(string name) : Attribute
{
    /// <summary>The feature's name within its register (<c>freecamera</c> for <c>Qol.FreeCameraKey</c>).</summary>
    public string Name => name;
}

/// <summary>
/// A register's switches as the registry lists them, each with the property that holds it (ours): made once for a
/// register's class, from its <see cref="SwitchAttribute"/>s and the declarations of its kind. It is where
/// <c>All</c>, the indexer and <c>Known</c> of <see cref="World.Fixes"/> and <see cref="World.Qol"/> come from, so
/// neither keeps a list of names of its own.
///
/// A property that names no declaration, or a switchable declaration with no property, stops the program the
/// first time the register is used, by name: the two cannot drift apart unnoticed.
/// </summary>
public sealed class SwitchTable<T> where T : class
{
    /// <summary>One switch: its declaration, and how to read and set it on a register.</summary>
    public readonly record struct Bound(Feature Feature, Func<T, bool> Get, Action<T, bool> Set);

    readonly Bound[] all;
    readonly Dictionary<string, Bound> byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The table of <typeparamref name="T"/>'s switches against the features of <paramref name="kind"/> in <paramref name="registry"/>.</summary>
    public SwitchTable(FeatureKind kind, FeatureRegistry registry)
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<SwitchAttribute>() is not { } tie) continue;
            if (registry.Of(kind).FirstOrDefault(f => f.Name.Equals(tie.Name, StringComparison.OrdinalIgnoreCase)) is not { } feature)
                throw new InvalidOperationException($"{typeof(T).Name}.{property.Name} names the switch {tie.Name}, which the feature registry does not declare");
            var bound = new Bound(feature, property.GetMethod!.CreateDelegate<Func<T, bool>>(), property.SetMethod!.CreateDelegate<Action<T, bool>>());
            if (!byName.TryAdd(feature.Name, bound))
                throw new InvalidOperationException($"{typeof(T).Name} has two properties for the switch {tie.Name}");
        }
        foreach (var feature in registry.Of(kind))
            if (!byName.ContainsKey(feature.Name))
                throw new InvalidOperationException($"the feature {feature.Id} has no property on {typeof(T).Name}");
        all = [.. registry.Of(kind).Select(f => byName[f.Name])];
    }

    /// <summary>Every switch of the register, in the registry's order (the Fixes page's).</summary>
    public IReadOnlyList<Bound> All => all;

    /// <summary>A switch by name, whatever the case; null when the register has none of that name.</summary>
    public Bound? Find(string name) => byName.TryGetValue(name, out var bound) ? bound : null;
}
