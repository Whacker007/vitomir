// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vitomir.Platform.Core;

/// <summary>What every component store can do without knowing its type.</summary>
public interface IStore
{
    Type ComponentType { get; }
    int Count { get; }
    bool Has(EntityId e);
    void Remove(EntityId e);
    /// <summary>Whether the store's bytes go into the world's per-tick digest (simulation state, not caches).</summary>
    bool Digested { get; }
    /// <summary>Feeds the store's live entities and their components, in dense order, to a digest.</summary>
    void Digest(ref Fnv1a64 hash);
}

/// <summary>
/// One component type's data for every entity that has it: a <b>sparse set</b> — a dense array of the
/// components and of their entities, read in order, and a sparse index from an entity's slot to its
/// place in the dense arrays. Adding puts a component at the end; removing moves the
/// last one into the hole. Both are fixed operations, so the dense order — the order systems visit
/// entities in — is the same on every run that does the same things.
/// </summary>
public sealed class Store<T> : IStore where T : struct
{
    int[] sparse = [];            // entity index -> dense index + 1 (0: absent)
    EntityId[] entities = new EntityId[16];
    T[] data = new T[16];
    int count;

    public Store(bool digested = true) { Digested = digested; }

    public Type ComponentType => typeof(T);
    public bool Digested { get; }
    public int Count => count;

    /// <summary>The components, in dense order. Valid until the next add or remove.</summary>
    public Span<T> Data => data.AsSpan(0, count);

    /// <summary>The entities of <see cref="Data"/>, in the same order.</summary>
    public ReadOnlySpan<EntityId> Entities => entities.AsSpan(0, count);

    public bool Has(EntityId e) =>
        e.Index >= 0 && e.Index < sparse.Length && sparse[e.Index] != 0 && entities[sparse[e.Index] - 1] == e;

    /// <summary>The entity's component, by reference. Throws if it has none.</summary>
    public ref T Get(EntityId e)
    {
        if (!Has(e)) throw new KeyNotFoundException($"{e} has no {typeof(T).Name}");
        return ref data[sparse[e.Index] - 1];
    }

    /// <summary>The entity's component, or a null reference when it has none (test with <see cref="Unsafe.IsNullRef{T}(ref readonly T)"/>).</summary>
    public ref T Find(EntityId e)
    {
        if (!Has(e)) return ref Unsafe.NullRef<T>();
        return ref data[sparse[e.Index] - 1];
    }

    /// <summary>Gives the entity the component (replacing one it has) and returns it by reference.</summary>
    public ref T Set(EntityId e, in T value)
    {
        if (e.IsNone) throw new ArgumentException("no entity", nameof(e));
        if (Has(e))
        {
            ref T slot = ref data[sparse[e.Index] - 1];
            slot = value;
            return ref slot;
        }
        if (e.Index >= sparse.Length) Array.Resize(ref sparse, Math.Max(e.Index + 1, sparse.Length * 2));
        if (count == data.Length)
        {
            Array.Resize(ref data, count * 2);
            Array.Resize(ref entities, count * 2);
        }
        data[count] = value;
        entities[count] = e;
        sparse[e.Index] = count + 1;
        return ref data[count++];
    }

    public void Remove(EntityId e)
    {
        if (!Has(e)) return;
        int at = sparse[e.Index] - 1;
        int last = count - 1;
        if (at != last)
        {
            data[at] = data[last];
            entities[at] = entities[last];
            sparse[entities[at].Index] = at + 1;
        }
        data[last] = default;
        entities[last] = default;
        sparse[e.Index] = 0;
        count--;
    }

    public void Digest(ref Fnv1a64 hash)
    {
        hash.Add(count);
        for (int i = 0; i < count; i++) hash.Add(entities[i].Packed);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            throw new InvalidOperationException($"{typeof(T).Name} holds references and cannot be digested; mark its store undigested");
        hash.Add(MemoryMarshal.AsBytes(data.AsSpan(0, count)));
    }
}
