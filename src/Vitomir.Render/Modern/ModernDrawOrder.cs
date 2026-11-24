// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Render.Modern;

/// <summary>
/// **Ours: a frame's draws in the order its passes issue them, without making a list a
/// frame**. The renderer used to ask LINQ each frame — which of
/// the scene's draws the camera sees, those grouped by sidedness and ordered by material for the prepass and
/// again for the surfaces, the blended ones ordered far to near, the sun's casters filtered and grouped once
/// a cascade — and every such question made lists, groups and sort buffers of the whole town's draws: a
/// third of a megabyte a frame standing still, at 144 frames a second fifty megabytes a second for the
/// garbage collector. The lists here belong to the renderer, are emptied and filled again each frame and
/// keep their room; a sort is of numbers in an array that is kept too.
///
/// The orders are exactly LINQ's, so the same draws are issued in the same order:
/// <list type="bullet">
/// <item><see cref="Opaque"/>: single-sided then double-sided, each by material, draws of one material in the
/// scene's order (<c>GroupBy(DoubleSided).OrderBy(Key)</c>, then <c>OrderBy(Material)</c>, which is stable).</item>
/// <item><see cref="Blended"/>: farthest first by the square of the distance from the eye to the draw's
/// centre, draws as far as each other in the scene's order (<c>OrderByDescending</c>, stable).</item>
/// <item><see cref="Casters"/>: the scene's casters that are not blended, in the scene's order; and for a
/// cascade (<see cref="Within"/>) those its frustum meets, the sidedness of the first one met first
/// (<c>GroupBy</c> yields its groups in the order their keys first appear).</item>
/// </list>
/// </summary>
public sealed class ModernDrawOrder
{
    /// <summary>The scene's draws the camera's frustum meets, in the scene's order.</summary>
    public List<ModernScene.Draw> Seen { get; } = [];
    /// <summary>The seen draws that are not blended: [0] single-sided, [1] double-sided, each by material.</summary>
    public List<ModernScene.Draw>[] Opaque { get; } = [[], []];
    /// <summary>The seen draws that are blended, farthest first.</summary>
    public List<ModernScene.Draw> Blended { get; } = [];
    /// <summary>The scene's casters that are not blended, in the scene's order (<see cref="Cast"/>).</summary>
    public List<ModernScene.Draw> Casters { get; } = [];
    /// <summary>A cascade's casters by sidedness, [0] single-sided, [1] double-sided, each in the scene's order (<see cref="Within"/>).</summary>
    public List<ModernScene.Draw>[] Cascade { get; } = [[], []];

    long[] keys = [];
    (float Distance, int Index)[] far = [];
    bool castersCurrent;
    /// <summary>The material read for an index that names none, as <see cref="ModernScene.Material"/> has it: made once, only read.</summary>
    static readonly GltfMaterial Fallback = GltfMaterial.Default();

    static GltfMaterial Material(GltfScene materials, int index) =>
        index >= 0 && index < materials.Materials.Count ? materials.Materials[index] : Fallback;

    /// <summary>
    /// A new frame: which of <paramref name="draws"/> the frustum <paramref name="planes"/> meets, sorted for
    /// the prepass, the surfaces and the blended pass by their <paramref name="materials"/>.
    /// </summary>
    public void See(IReadOnlyList<ModernScene.Draw> draws, GltfScene materials, ReadOnlySpan<Vector4> planes, Vector3 eye)
    {
        castersCurrent = false;
        Seen.Clear(); Opaque[0].Clear(); Opaque[1].Clear(); Blended.Clear();
        for (int i = 0; i < draws.Count; i++)
        {
            var d = draws[i];
            if (ModernMath.BoxVisible(planes, d.Min, d.Max)) Seen.Add(d);
        }
        Sort(materials, eye);
    }

    /// <summary><see cref="Seen"/> into <see cref="Opaque"/> and <see cref="Blended"/>, in LINQ's orders.</summary>
    void Sort(GltfScene materials, Vector3 eye)
    {
        int n = Seen.Count;
        if (keys.Length < n) keys = new long[Math.Max(n, keys.Length * 2)];
        // By sidedness, then material, then place in the list: one number a draw, sorted as numbers — bit 62 the
        // sidedness, 32 bits of the material (offset so that −1, the default, sorts first as it does as an int),
        // 30 bits of the draw's place among the seen.
        int opaque = 0, blended = 0;
        for (int i = 0; i < n; i++)
        {
            var m = Material(materials, Seen[i].Material);
            if (m.AlphaMode == GltfAlphaMode.Blend) { blended++; continue; }
            keys[opaque++] = ((m.DoubleSided ? 1L : 0L) << 62) | (((long)Seen[i].Material - int.MinValue) << 30) | (long)i;
        }
        keys.AsSpan(0, opaque).Sort();
        for (int k = 0; k < opaque; k++)
            Opaque[(int)(keys[k] >> 62)].Add(Seen[(int)(keys[k] & 0x3fffffffL)]);
        if (blended == 0) return;
        if (far.Length < blended) far = new (float, int)[Math.Max(blended, far.Length * 2)];
        int b = 0;
        for (int i = 0; i < n; i++)
            if (Material(materials, Seen[i].Material).AlphaMode == GltfAlphaMode.Blend)
                far[b++] = (Vector3.DistanceSquared(Seen[i].Centre, eye), i);
        far.AsSpan(0, blended).Sort(static (x, y) => x.Distance != y.Distance ? y.Distance.CompareTo(x.Distance) : x.Index.CompareTo(y.Index));
        for (int k = 0; k < blended; k++) Blended.Add(Seen[far[k].Index]);
    }

    /// <summary>The frame's sun casters (<see cref="Casters"/>), found once a frame however many passes ask.</summary>
    public List<ModernScene.Draw> Cast(IReadOnlyList<ModernScene.Draw> from, GltfScene materials)
    {
        if (castersCurrent) return Casters;
        Casters.Clear();
        for (int i = 0; i < from.Count; i++)
        {
            var d = from[i];
            if (Material(materials, d.Material).AlphaMode != GltfAlphaMode.Blend) Casters.Add(d);
        }
        castersCurrent = true;
        return Casters;
    }

    /// <summary>
    /// The draws of <paramref name="from"/> that are not blended and that the frustum meets, into
    /// <see cref="Cascade"/> by sidedness, each in the list's order; answers the sidedness to draw first (that
    /// of the first one met), or −1 for none. For a pass with a list of its own (the depth the game's own
    /// things are drawn against): the frame's casters are not touched.
    /// </summary>
    public int OpaqueWithin(IReadOnlyList<ModernScene.Draw> from, GltfScene materials, ReadOnlySpan<Vector4> planes)
    {
        Cascade[0].Clear(); Cascade[1].Clear();
        int first = -1;
        for (int i = 0; i < from.Count; i++)
        {
            var d = from[i];
            var m = Material(materials, d.Material);
            if (m.AlphaMode == GltfAlphaMode.Blend || !ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
            int side = m.DoubleSided ? 1 : 0;
            if (first < 0) first = side;
            Cascade[side].Add(d);
        }
        return first;
    }

    /// <summary>
    /// The casters a cascade's frustum meets, into <see cref="Cascade"/>; answers the sidedness to draw first
    /// (0 single, 1 double: that of the first caster met), or −1 when the cascade meets none.
    /// </summary>
    public int Within(IReadOnlyList<ModernScene.Draw> from, GltfScene materials, ReadOnlySpan<Vector4> planes)
    {
        Cascade[0].Clear(); Cascade[1].Clear();
        int first = -1;
        var casters = Cast(from, materials);
        for (int i = 0; i < casters.Count; i++)
        {
            var d = casters[i];
            if (!ModernMath.BoxVisible(planes, d.Min, d.Max)) continue;
            int side = Material(materials, d.Material).DoubleSided ? 1 : 0;
            if (first < 0) first = side;
            Cascade[side].Add(d);
        }
        return first;
    }
}
