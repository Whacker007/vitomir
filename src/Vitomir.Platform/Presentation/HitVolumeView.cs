// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the developer's overlay of the hit volumes.
using System.Numerics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Presentation;

/// <summary>
/// One hit volume as the overlay draws it: the capsule's ends and radius in the world, and its region (its colour):
/// whether it was struck a moment ago (<see cref="StruckVolumes"/>: drawn white and thicker) — a shield's edges and the
/// mark of a blow on the level are drawn so too, as thin capsules.
/// </summary>
public readonly record struct DrawnVolume(Vector3 A, Vector3 B, float Radius, BodyRegion Region, bool Struck = false);

/// <summary>
/// The hit volumes' overlay (ours, <c>--show-hit-volumes</c>, the action <c>hitVolumes</c>, no key by default):
/// each character's volumes on its pose, stood where the picture stands its body (the same interpolated placement),
/// so the outlines sit on the drawn body. Presentation only: it reads the simulation and changes nothing.
/// </summary>
public static class HitVolumeView
{
    /// <summary>Adds <paramref name="entity"/>'s volumes stood by <paramref name="place"/> to <paramref name="into"/>; nothing when it has none or the world no volumes.</summary>
    public static void Add(World world, EntityId entity, in Matrix4x4 place, List<DrawnVolume> into)
    {
        if (!world.TryService<HitVolumes>(out var volumes)) return;
        world.TryService<StruckVolumes>(out var struck);
        int i = 0;
        uint missing = volumes.Missing(world, entity, out var collapse);   // a part cut off is not drawn (nor met)
        foreach (var v in volumes.Posed(world, entity, place))
        {
            bool marked = struck?.Marked(entity, i++, world.Tick) ?? false;
            if (missing != 0 && collapse is not null && collapse.VolumeGone(missing, v.Region, v.Bone)) continue;
            into.Add(new DrawnVolume(v.Capsule.A, v.Capsule.B, v.Capsule.Radius, v.Region, marked));
        }
        if (struck is not null) AddBlade(world, entity, struck, into);
        // A shield struck a moment ago, its box's twelve edges.
        if (struck is not null && struck.Marked(entity, -1, world.Tick) && Animation.HeldItems.Shield(world, entity, out var box))
        {
            Span<Vector3> corners = stackalloc Vector3[8];
            for (int c = 0; c < 8; c++)
                corners[c] = box.Centre + box.AxisX * ((c & 1) == 0 ? -box.HalfSize.X : box.HalfSize.X)
                                        + box.AxisY * ((c & 2) == 0 ? -box.HalfSize.Y : box.HalfSize.Y)
                                        + box.AxisZ * ((c & 4) == 0 ? -box.HalfSize.Z : box.HalfSize.Z);
            for (int a = 0; a < 8; a++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((a & bit) == 0) into.Add(new DrawnVolume(corners[a], corners[a | bit], 0f, BodyRegion.Head, true));
        }
    }

    /// <summary> A parrying blade struck a moment ago, drawn as its capsule.</summary>
    static void AddBlade(World world, EntityId entity, StruckVolumes struck, List<DrawnVolume> into)
    {
        if (struck.Marked(entity, -2, world.Tick) && Animation.HeldItems.Blade(world, entity, out var blade))
            into.Add(new DrawnVolume(blade.Guard, blade.Tip, MathF.Max(0.01f, blade.Radius), BodyRegion.Head, true));
    }

    /// <summary>The marks of blows on the level a moment ago: a small cross at each point met, drawn white.</summary>
    public static void AddLevelMarks(World world, List<DrawnVolume> into)
    {
        if (!world.TryService<StruckVolumes>(out var struck)) return;
        foreach (var p in struck.LevelPoints(world.Tick))
        {
            const float arm = 0.08f;
            into.Add(new DrawnVolume(p - Vector3.UnitX * arm, p + Vector3.UnitX * arm, 0f, BodyRegion.Head, true));
            into.Add(new DrawnVolume(p - Vector3.UnitY * arm, p + Vector3.UnitY * arm, 0f, BodyRegion.Head, true));
            into.Add(new DrawnVolume(p - Vector3.UnitZ * arm, p + Vector3.UnitZ * arm, 0f, BodyRegion.Head, true));
        }
    }
}
