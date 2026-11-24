// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Core;

/// <summary>
/// An entity: a slot in the world and the generation of that slot, so an id kept after its entity is
/// gone never reaches whatever is made in the slot next.
/// Everything an entity is lives in the world's component stores under this id.
/// </summary>
public readonly record struct EntityId(int Index, int Generation)
{
    /// <summary>No entity.</summary>
    public static EntityId None => new(-1, 0);

    public bool IsNone => Index < 0;

    /// <summary>The id as one number, for traces and saves: the generation in the high half.</summary>
    public ulong Packed => ((ulong)(uint)Generation << 32) | (uint)Index;

    public static EntityId FromPacked(ulong packed) => new((int)(uint)packed, (int)(uint)(packed >> 32));

    public override string ToString() => IsNone ? "none" : $"e{Index}.{Generation}";
}
