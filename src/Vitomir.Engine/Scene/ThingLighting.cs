// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Every model's light worked out again every frame, as 1.1 does it (World.ModelLight has the rules,
// with lighting a thing that has none of its own).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Scene;

/// <summary>
/// **The thing lighting, every frame.** 1.1 lights each model in software every frame it draws it
/// (then `rlDirect.dll`'s vertex lighting on every mesh): the lights are chosen again
/// at the thing's position, at this frame's intensity, with the lights other things carry among
/// them — so a torch carried past a barrel lights it, a fireball lights the people it flies past,
/// and a character in the flicker of a wall torch flickers with it.
///
/// Vitomir keeps one vertex list for a place, so this works on it in two halves:
/// <list type="bullet">
/// <item>**A character that animates** is re-posed and uploaded every frame anyway; its colours are
/// worked out again every frame too, from its posed normals, into the vertices that go up.</item>
/// <item>**A thing standing still** is lit again only when something it is lit by has changed — its
/// chosen lights, their intensity or place, its base light, or where it is. The lights are chosen
/// every frame (the picker is cheap); the vertices are lit and sent up only then. That is the one
/// economy here, and it changes nothing on screen: a vertex lit from the same lights at the same
/// place comes out the same colour.</item>
/// </list>
/// The lights are taken into the frame the model's vertices were built in, rather than the other
/// way round — which is what the renderer does too (it turns each light into each bone's
/// space).
/// </summary>
public sealed class ThingLighting
{
    /// <summary>How many lights a thing keeps (`maxThingLights`): eight, the maxed-out preset's (<see cref="Ui.DetailPresets.Maxed"/>).</summary>
    public int MaxLights { get; set; } = Ui.DetailPresets.Maxed.MaxThingLights;

    readonly ModelLightPicker picker = new();

    /// <summary>A thing's own lighting this frame, and the base light its model keeps.</summary>
    sealed class ThingState
    {
        public readonly PickedLight[] Lights = new PickedLight[ModelLightPicker.Most];
        public int Count;
        public Vector3 Base;
        public bool HasBase;
        public Matrix4x4 ToBuilt = Matrix4x4.Identity;
        public int Frame = -1;
    }

    /// <summary>What a standing-still slice was last lit from.</summary>
    sealed class SliceState
    {
        public readonly PickedLight[] Lights = new PickedLight[ModelLightPicker.Most];
        public int Count = -1;
        public Vector3 Base;
        public Matrix4x4 ToBuilt;
    }

    readonly Dictionary<(WorldThing, bool), ThingState> things = [];
    readonly Dictionary<SceneMesh.LitSlice, SliceState> slices = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<uint> moving = [];
    readonly PickedLight[] local = new PickedLight[ModelLightPicker.Most];
    int frame;

    /// <summary>The standing-still slices lit again this frame, whose <see cref="SceneMesh.LitSlice.Run"/> a renderer sends up.</summary>
    public List<SceneMesh.LitSlice> Changed { get; } = [];

    /// <summary>How many vertices were lit last frame, for the diagnostics.</summary>
    public int VerticesLit { get; private set; }

    /// <summary>How long the last frame's lighting took, in milliseconds, for the diagnostics.</summary>
    public double LastMs { get; private set; }

    /// <summary>
    /// The animating things this frame posed, when it posed only the ones its view reaches
    /// (<c>WorldRenderer.Reached</c>); null for all of them. The others keep the light they had.
    /// </summary>
    public IReadOnlyList<AnimatedThing>? Posed { get; set; }

    /// <summary>Forgets what every slice was last lit from, so that all of them are lit and sent up again.</summary>
    public void Invalidate() => slices.Clear();

    /// <summary>
    /// One frame. <paramref name="drawn"/> is what the mesh's thing indices resolve against
    /// (<see cref="Location.Drawn"/>); <paramref name="glow"/> answers a thing's hover-glow
    /// intensity (the model's glow intensity), or null for none. Call after the animating things have been
    /// posed for this frame.
    /// </summary>
    public void Relight(SceneMesh mesh, Location place, IReadOnlyList<WorldThing> drawn, Func<WorldThing, float>? glow = null)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        frame++;
        Changed.Clear();
        VerticesLit = 0;
        moving.Clear();

        // The animating models: what each is lit by is worked out here, in the engine's order (the
        // picker's array is one, as the engine's is); the vertices, which are most of the work and
        // touch nothing shared, are lit after, side by side when there are enough of them.
        jobs.Clear();
        int count = mesh.Animated.Count;
        for (int i = 0; i < count; i++)
        {
            var a = mesh.Animated[i];
            moving.Add(a.FirstVertex);
            foreach (var part in a.Carried) moving.Add(part.FirstVertex);
        }
        var animated = Posed ?? mesh.Animated;
        count = animated.Count;
        for (int i = 0; i < count; i++)
        {
            var a = animated[i];
            if (a.Thing < 0 || a.Thing >= drawn.Count || drawn[a.Thing] is not { } thing || !Drawn(thing)) continue;
            if (mesh.LitAt(a.FirstVertex) is { } body)
                jobs.Add(Job(a.Vertices, a.Animation.Model, a, body, Prepare(thing, place, body, glow)));
            foreach (var part in a.Carried)
                if (part.Shown && mesh.LitAt(part.FirstVertex) is { } held)
                    jobs.Add(Job(part.Vertices, part.Model, a, held, Prepare(thing, place, held, glow)));
        }
        int work = 0;
        foreach (var job in jobs) work += job.Vertices.Length;
        if (work >= ParallelFrom && jobs.Count > 1) Parallel.ForEach(jobs, Run);
        else foreach (var job in jobs) Run(job);
        VerticesLit += work;

        foreach (var slice in mesh.Lit)
        {
            if (moving.Contains(slice.First)) continue;
            if (slice.Thing < 0 || slice.Thing >= drawn.Count || drawn[slice.Thing] is not { } thing || !Drawn(thing)) continue;
            if ((slice.ModelFlags & ModelLight.FullyLit) != 0) continue;           // drawn at full white whatever its colours
            var state = Prepare(thing, place, slice, glow);
            if (!slices.TryGetValue(slice, out var was)) slices[slice] = was = new SliceState();
            if (Same(was, state)) continue;
            slice.Run ??= mesh.Copy(slice.First, slice.Normals.Length);
            Light(slice.Run, slice.Normals, slice, state);
            Remember(was, state);
            mesh.WriteColors(slice.First, slice.Run);
            Changed.Add(slice);
        }
        LastMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>
    /// How many animating vertices a frame takes before they are lit on more than one core. **Ours**;
    /// the engine lit them one mesh at a time on its one thread.
    /// </summary>
    public const int ParallelFrom = 8_000;

    /// <summary>One animating model's lighting this frame, with the buffers it keeps between frames.</summary>
    sealed class AnimatedJob
    {
        public SceneVertex[] Vertices = [];
        public Anim.ThingAnimation Animation = null!;
        public Formats.NodModel Model = null!;
        public Matrix4x4 Built;
        public SceneMesh.LitSlice Slice = null!;
        public ThingState State = null!;
        public Vector3[] Normals = [];
        public readonly PickedLight[] Local = new PickedLight[ModelLightPicker.Most];
    }

    readonly List<AnimatedJob> jobs = [];
    readonly Dictionary<SceneVertex[], AnimatedJob> jobFor = new(ReferenceEqualityComparer.Instance);

    AnimatedJob Job(SceneVertex[] vertices, Formats.NodModel model, AnimatedThing a, SceneMesh.LitSlice slice, ThingState state)
    {
        if (!jobFor.TryGetValue(vertices, out var job)) jobFor[vertices] = job = new AnimatedJob();
        job.Vertices = vertices;
        job.Animation = a.Animation;
        job.Model = model;
        job.Built = a.World;
        job.Slice = slice;
        job.State = state;
        if (job.Normals.Length < model.Vertices.Length) job.Normals = new Vector3[model.Vertices.Length];
        return job;
    }

    static void Run(AnimatedJob job)
    {
        job.Animation.PoseNormals(job.Model, job.Normals);
        var rotation = job.Built with { M41 = 0, M42 = 0, M43 = 0 };
        var lights = Local(job.State, job.Local);
        var vertices = job.Vertices;
        int n = Math.Min(vertices.Length, job.Slice.Normals.Length);
        for (int i = 0; i < n; i++)
        {
            var normal = Vector3.TransformNormal(job.Normals[i], rotation);
            float len = normal.Length();
            if (len > 0f) normal /= len;
            vertices[i].Color = Colour(lights, vertices[i].Position, normal, job.Slice, i, job.State);
        }
    }

    /// <summary>Whether the draw would light it: in the world, and neither `DON'T RENDER` nor `REFLECTION ONLY`.</summary>
    static bool Drawn(WorldThing thing) => thing.InWorld && !thing.ModelHidden;

    /// <summary>
    /// The thing's lights and base for this frame, worked out once however many models it has.
    /// The base is the sector's for a thing riding nothing; one riding something keeps
    /// the base it had — the sector's draw passes it over — and starts from nothing. Either way the
    /// glow raises it and the raise is kept.
    /// </summary>
    ThingState Prepare(WorldThing thing, Location place, SceneMesh.LitSlice slice, Func<WorldThing, float>? glow)
    {
        bool prelit = slice.Baked is not null;
        // A prelit model is chosen lights without the sector's and the sun, so a thing
        // whose models differ in that keeps one state for each.
        if (!things.TryGetValue((thing, prelit), out var s)) things[(thing, prelit)] = s = new ThingState();
        if (s.Frame == frame) return s;
        s.Frame = frame;
        var level = place.Level;
        if ((slice.ModelFlags & (ModelLight.FullyLit | ModelLight.ConstantLit)) != 0)
            s.Count = 0;                                                    // No lights at all
        else
        {
            ModelLight.Pick(picker, MaxLights, thing, level, place.Styles, place.Lights, prelit);
            s.Count = picker.Count;
            picker.Lights.CopyTo(s.Lights);
        }
        if (thing.AttachedTo is null)
        {
            var sector = thing.Sector >= 0 && thing.Sector < level.Sectors.Length ? level.Sectors[thing.Sector] : null;
            s.Base = ModelLight.Base(level, sector, thing.Type);
        }
        else if (!s.HasBase) s.Base = Vector3.Zero;
        s.HasBase = true;
        if (glow?.Invoke(thing) is float g and not 0f) s.Base = ModelLight.Glow(s.Base, g);

        // From the world back into the frame the model was built in: the inverse of what the
        // renderer draws it with (the difference between where the scene put it and where it is).
        bool scaled = thing.Fx is { Scale: var sc } && sc != Vector3.One;
        s.ToBuilt = Matrix4x4.Identity;
        if ((thing.Moved || scaled) && Matrix4x4.Invert(thing.Base, out var inverse))
        {
            var world = scaled ? Matrix4x4.CreateScale(thing.Fx!.Scale) * thing.World : thing.World;
            if (Matrix4x4.Invert(inverse * world, out var back)) s.ToBuilt = back;
        }
        return s;
    }

    static bool Same(SliceState was, ThingState now)
    {
        if (was.Count != now.Count || was.Base != now.Base || was.ToBuilt != now.ToBuilt) return false;
        for (int i = 0; i < now.Count; i++) if (!was.Lights[i].Equals(now.Lights[i])) return false;
        return true;
    }

    static void Remember(SliceState was, ThingState now)
    {
        was.Count = now.Count;
        was.Base = now.Base;
        was.ToBuilt = now.ToBuilt;
        Array.Copy(now.Lights, was.Lights, now.Count);
    }

    /// <summary>The lights of a thing in the frame its vertices are in.</summary>
    static Span<PickedLight> Local(ThingState s, Span<PickedLight> into)
    {
        var lights = into[..s.Count];
        for (int i = 0; i < s.Count; i++)
        {
            var l = s.Lights[i];
            if (s.ToBuilt.IsIdentity) { lights[i] = l; continue; }
            var dir = Vector3.TransformNormal(l.Direction, s.ToBuilt);
            lights[i] = l with
            {
                Position = Vector3.Transform(l.Position, s.ToBuilt),
                Direction = dir == Vector3.Zero ? dir : Vector3.Normalize(dir),
            };
        }
        return lights;
    }

    /// <summary>A model standing still: the normals it was built with.</summary>
    void Light(SceneVertex[] vertices, Vector3[] built, SceneMesh.LitSlice slice, ThingState s)
    {
        var lights = Local(s, local);
        int n = Math.Min(vertices.Length, built.Length);
        for (int i = 0; i < n; i++)
            vertices[i].Color = Colour(lights, vertices[i].Position, built[i], slice, i, s);
        VerticesLit += n;
    }

    /// <summary>
    /// One vertex: a prelit model with no light on it keeps its baked colour exactly —
    /// the base is not added; otherwise the lights are summed onto black or onto the baked colour,
    /// and the base added and the whole packed.
    /// </summary>
    static uint Colour(ReadOnlySpan<PickedLight> lights, Vector3 p, Vector3 n, SceneMesh.LitSlice slice, int i,
                       ThingState s)
    {
        if (slice.Baked is { } baked && i < baked.Length)
        {
            if (lights.Length == 0) return SceneVertex.FromBgra(baked[i]);
            return ModelLight.Pack(ModelLight.Vertex(lights, p, n, ModelLight.Baked(baked[i])), s.Base);
        }
        return ModelLight.Pack(ModelLight.Vertex(lights, p, n, Vector3.Zero), s.Base);
    }
}
