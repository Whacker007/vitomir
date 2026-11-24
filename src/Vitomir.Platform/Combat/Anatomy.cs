// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

// The contract between the rules (the enhanced module's severance), contact and the body's presentation (the
// platform). Shared records: the code that owns a producer may add fields at the end, never change
// or reorder these.

/// <summary>
/// What a person's body is missing (the rules package: local anatomy is state, apart from the
/// global Health): one bit per <see cref="BodyRegion"/>. Written by the rules when a part is severed (<see cref="PartSevered"/>);
/// read by the platform — the part's bones drawn collapsed, its hit volumes skipped by contact, an item held in it dropped,
/// an attack refused without the weapon hand. Absent means whole, so a world without it is the same world, digest for
/// digest. Digested.
/// </summary>
public struct MissingParts
{
    public uint Bits;

    public readonly bool Lacks(BodyRegion region) => region != BodyRegion.None && (Bits & (1u << (int)region)) != 0;

    public void Lose(BodyRegion region)
    {
        if (region != BodyRegion.None) Bits |= 1u << (int)region;
    }

    public readonly bool Any => Bits != 0;
}

/// <summary>The anatomy's registration.</summary>
public static class AnatomyServices
{
    /// <summary>Registers <see cref="MissingParts"/> and <see cref="PartSevered"/> unless registered already (each party calls it).</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        var w = b.Peek;
        if (!w.TryStore<MissingParts>(out _)) b.Component<MissingParts>();
        if (!w.TryService<EventQueue<PartSevered>>(out _)) b.Event<PartSevered>();
        return b;
    }

    /// <summary>Whether <paramref name="e"/> lacks <paramref name="region"/> (false for a person with no <see cref="MissingParts"/>).</summary>
    public static bool Lacks(World world, EntityId e, BodyRegion region)
    {
        if (!world.TryStore<MissingParts>(out var store)) return false;
        ref var m = ref store.Find(e);
        return !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref m) && m.Lacks(region);
    }
}
