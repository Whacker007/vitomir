// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: what the CPU's skinning of the models repository's people is spared when several are about.
using System.Numerics;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Characters;

/// <summary>
/// How the frame's people of the models repository are skinned when there are several (the townsfolk on the crowd): the
/// skinning is on the CPU (about 2.5 ms a person a frame on the cloud's processor; the GPU would need a skinning shader, which this
/// project cannot compile in the cloud). Presentation only: none of it reaches the simulation, the hit volumes or a recording.
/// <list type="bullet">
/// <item><b>Only people in view</b> (<see cref="Cull"/>): a person whose bounds, grown by <see cref="ShadowReach"/> for the shadow it casts
/// into the picture, are wholly outside the view's cone is not skinned or drawn.</item>
/// <item><b>Distant people less often</b>: within <see cref="NearMetres"/> of the eye every frame; to <see cref="FarMetres"/> every
/// <see cref="MidEvery"/>th frame, past it every <see cref="FarEvery"/>th; in between, the last skinning is drawn again, carried along by
/// how far the person has moved since (its pose a frame or three old: at that distance a few pixels). Each person's frames are staggered,
/// so the work is even from frame to frame.</item>
/// </list>
/// The viewer's own body (in first person) and 1.1's characters are skinned every frame as before. <see cref="EveryFrame"/> is the earlier build's:
/// every person, every frame.
/// </summary>
public sealed record PeopleSkinning
{
    /// <summary>Whether a person out of view is left out.</summary>
    public bool Cull { get; init; } = true;

    /// <summary>How far past a person's bounds its shadow may reach into the picture (m): the sun low at dusk throws it a few metres.</summary>
    public float ShadowReach { get; init; } = 4f;

    /// <summary>Within this of the eye a person is skinned every frame (m).</summary>
    public float NearMetres { get; init; } = 12f;

    /// <summary>Past this a person is skinned every <see cref="FarEvery"/>th frame (m).</summary>
    public float FarMetres { get; init; } = 30f;

    /// <summary>Between <see cref="NearMetres"/> and <see cref="FarMetres"/>, a skinning every this many frames.</summary>
    public int MidEvery { get; init; } = 2;

    /// <summary>Past <see cref="FarMetres"/>, a skinning every this many frames.</summary>
    public int FarEvery { get; init; } = 4;

    /// <summary>The defaults (above).</summary>
    public static readonly PeopleSkinning Default = new();

    /// <summary>Every person skinned every frame, in view or not.</summary>
    public static readonly PeopleSkinning EveryFrame = new() { Cull = false, MidEvery = 1, FarEvery = 1 };

    /// <summary>How often a person <paramref name="metres"/> from the eye is skinned (1: every frame).</summary>
    public int Every(float metres) => Math.Max(1, metres <= NearMetres ? 1 : metres <= FarMetres ? MidEvery : FarEvery);
}

/// <summary>
/// A frame's characters on the CPU: the skinning (and the batch's other work) and the upload of their vertices to the GPU
/// (ms), the vertices made, and the people skinned, drawn again from an earlier skinning, and left out of view.
/// </summary>
public readonly record struct CharacterCost(double SkinMilliseconds, double UploadMilliseconds, int Vertices, int Skinned, int Reused, int Culled);

/// <summary>The view a frame's people are skinned for (<see cref="PeopleSkinning"/>): the eye and the cone round its direction, in metres.</summary>
internal readonly struct SkinView
{
    readonly Vector3 forward;
    readonly float cosLimit, sinLimit;

    /// <summary>The eye (metres).</summary>
    public Vector3 Eye { get; }

    /// <summary>Whether there is a view (a default one sees everything, at no distance).</summary>
    public bool Has { get; }

    /// <summary>The view <paramref name="view"/> on a picture <paramref name="aspect"/> wide to its height: its cone the picture's corners' angle.</summary>
    public SkinView(ViewPoint view, float aspect)
    {
        Eye = view.Eye;
        forward = view.Forward;
        float vertical = (view.FieldOfViewDegrees > 0f ? view.FieldOfViewDegrees : 64.4f) * MathF.PI / 180f;
        float t = MathF.Tan(vertical * 0.5f) * MathF.Sqrt(1f + MathF.Max(aspect, 0.1f) * MathF.Max(aspect, 0.1f));
        float half = MathF.Atan(t);
        cosLimit = MathF.Cos(half);
        sinLimit = MathF.Sin(half);
        Has = true;
    }

    /// <summary>Whether a sphere (metres) is at least partly within the cone (or round the eye).</summary>
    public bool Sees(Vector3 centre, float radius)
    {
        if (!Has) return true;
        var d = centre - Eye;
        float along = Vector3.Dot(d, forward);
        float distance = d.Length();
        if (distance <= radius) return true;
        // The sphere is outside the cone when its centre is further from the cone's surface than its radius.
        float across = MathF.Sqrt(MathF.Max(0f, distance * distance - along * along));
        float outside = across * cosLimit - along * sinLimit;
        return outside <= radius;
    }

    /// <summary>The distance from the eye (metres; 0 with no view).</summary>
    public float Distance(Vector3 point) => Has ? Vector3.Distance(point, Eye) : 0f;
}

/// <summary>A person's last skinning, kept to be drawn again (carried by how far it has moved) until its next.</summary>
internal sealed class SkinCache
{
    public ModernVertex[] Vertices = [];
    public PeopleMesh? Mesh;
    public long Frame = long.MinValue;
    /// <summary>Where its model stood when it was skinned (the renderer's units).</summary>
    public Vector3 Origin;
    public Vector3 Min, Max;
}

/// <summary>
/// The characters' work on the CPU alone, as the renderer does it each frame (<see cref="CharacterBatch"/>: the skinning, the
/// people in view, the distant less often), without a GPU: for measuring that cost where the picture cannot be drawn many times over (the
/// cloud's software GPU runs out of memory mappings after a few dozen pictures). The textures are not looked up.
/// </summary>
public sealed class CharacterSkinningMeasure : IDisposable
{
    sealed class NoTextures : ICharacterTextures
    {
        public int Texture(string? name, bool srgb, float cutout = 0f) => -1;
    }

    readonly CharacterBatch batch = new(new NoTextures(), []);

    /// <summary>How the people are skinned (<see cref="PeopleSkinning"/>).</summary>
    public PeopleSkinning Skinning { get => batch.Skinning; set => batch.Skinning = value; }

    /// <summary>One frame's characters skinned for <paramref name="view"/> on a picture <paramref name="aspect"/> wide to its height; what it cost (no upload).</summary>
    public CharacterCost Frame(IReadOnlyList<DrawnCharacter> characters, ViewPoint view, float aspect)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        batch.Build(characters, new SkinView(view, aspect));
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var (skinned, reused, culled) = batch.LastPeople;
        return new CharacterCost(ms, 0, batch.Vertices.Length, skinned, reused, culled);
    }

    /// <summary>Whether the helper threads were found the faster (null while still timing, or with no large person yet).</summary>
    public bool? HelpersFaster => batch.HelpersFaster;

    public void Dispose() => batch.Dispose();
}
