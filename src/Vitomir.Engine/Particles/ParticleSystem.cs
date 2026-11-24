// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The particle system of one place: the emitter prototypes from `misc/game.npd`, the emitters on its
// things, the live list of 128, and the once-a-frame update and draw.
//
// **The public API**, for anything that wants a particle effect (explosions, spells, scripts):
//
//   var fx = location.Particles;                  // made on first use, one per place
//   Emitter? e = fx.Attach(thing, "flametest");   // a `game.npd` block on a thing; null if unknown
//   fx.Of(thing)                                  // the emitter a thing has, if any
//   fx.Detach(thing)                              // the thing is gone: stop spawning, fade, delete
//   fx.Prototype("blood1")                        // a block's numbers, without making anything
//
// A thing whose template names a PARTICLENAME (tag 119) gets its emitter by itself the first time
// the system sees it — the scene's own things when the system is made, anything spawned later on
// the next step — so a spawned explosion template with a particle name needs nothing more. An
// emitter follows its thing; to put an effect somewhere with nothing there, spawn a thing (an
// EMITTER-flagged template, as the engine's own `_particle` templates are) and attach to that.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Particles;

/// <summary>A run of sprites drawn with one material, in draw order (one call to the renderer's sprite entry).</summary>
/// <param name="Material">The material cache index.</param>
/// <param name="First">The first sprite in <see cref="ParticleSystem.Sprites"/> (or quad, or streak).</param>
/// <param name="Count">How many.</param>
/// <param name="Emitter">Whose they are.</param>
/// <param name="Run">Which list <see cref="First"/> and <see cref="Count"/> index.</param>
public readonly record struct ParticleBatch(int Material, int First, int Count, Emitter Emitter, ParticleRun Run = ParticleRun.Sprites)
{
    /// <summary>
    /// A run of quads (a trail's, a shock's, a pad's): <see cref="First"/> and <see cref="Count"/>
    /// are in <see cref="ParticleSystem.Quads"/>.
    /// </summary>
    public bool Ribbon => Run == ParticleRun.Quads;
}

/// <summary>
/// One place's particles. Made by <see cref="Location.Particles"/>; stepped once a frame by
/// <see cref="Simulation"/>; read by the renderer through <see cref="Sprites"/> and
/// <see cref="Batches"/>.
///
/// **What is the engine's and what is ours.** The engine updates and draws its emitters from the
/// world renderer, once per drawn frame with that frame's time, and only
/// emitters whose thing was drawn within the last 60 frames. Vitomir steps them from the
/// simulation's tick instead — the renderer steps the simulation once per frame, so the rhythm is
/// the same — and, having no portal visibility yet, counts **every thing in the world as drawn**
/// unless its render flags say not to draw it (the one test the thing draw makes before it registers the
/// emitter). The 128-slot list, the 60-frame drop-out and the order (slot order, update then draw,
/// emitter by emitter, nothing sorted) are the engine's.
/// </summary>
public sealed class ParticleSystem
{
    readonly ILogger log = Diag.For<ParticleSystem>();
    readonly Location? location;
    readonly Dictionary<string, EmitterSettings> prototypes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<WorldThing, Emitter> byThing = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<WorldThing> seen = new(ReferenceEqualityComparer.Instance);
    readonly Emitter?[] live = new Emitter?[Slots];
    readonly HashSet<string> unknown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The live list's size (128 pointers).</summary>
    public const int Slots = 128;
    /// <summary>How many frames an emitter's thing may go unseen before it drops out (frame − 60).</summary>
    public const int UnseenFrames = 60;
    /// <summary>
    /// <c>particleLOD</c> (the renderer's options; `masquerade.ini`'s shipped value is
    /// 1.0): what every particle count and birth interval is scaled by. The shipped value, which is
    /// also normal's, high's and the maxed-out look's — left here rather
    /// than read from <see cref="Ui.DetailPresets.Maxed"/>: in 1.1 it decides how much the births draw
    /// from the shared streams.
    /// </summary>
    public const float Lod = 1f;
    /// <summary>The tag a template names its emitter with (`PARTICLENAME`, 119).</summary>
    public const uint ParticleNameTag = 119;
    /// <summary><c>THING_RENDERFLAG</c> DON'T RENDER: the thing draw returns before it touches the emitter.</summary>
    const uint DontRender = 0x2;

    /// <summary>
    /// The engine's <c>rand()</c> as the particles draw it. **Ours: a stream of its own.** In 1.1
    /// the particles share <c>rand()</c> with everything else, and the "shared generator" below with
    /// the treasure rolls and the shops (<see cref="Rpg.TreasureRng"/>); the first starts at 1 when
    /// the program starts (its <c>srand</c>, has no caller) and the second from the clock,
    /// and both run on through everything, so no sequence repeats there. Giving the particles their
    /// own keeps a chest's contents and a fight's rolls independent of how many torches were on
    /// screen, which the tests rely on. In a game both are the launch's (<see cref="Ai.Chance"/>):
    /// made once from its seed and carried on from place to place and load to load.
    /// </summary>
    public Ai.Rng Rand { get; } = Ai.Chance.Game?.Particles ?? new(1);   // a game's: the launch's, carried on
    /// <summary>The shared generator as the particles step it; see <see cref="Rand"/>.</summary>
    public Rpg.TreasureRng Lcg { get; } = Ai.Chance.Game?.ParticleLcg ?? new(0x1234);

    /// <summary>
    /// The clump the liquids' births are at: one for every liquid emitter in
    /// 1.1, a global; here one per system.
    /// </summary>
    internal int LiquidCursor;

    /// <summary>The engine's millisecond clock at the step being run.</summary>
    public uint Now { get; private set; }
    /// <summary>The frame counter — one per step.</summary>
    public int Frame { get; private set; }
    /// <summary>
    /// The console's <c>particles off</c> (raster options bit <c>0x80</c>): nothing is drawn,
    /// and everything still runs.
    /// </summary>
    public bool Off { get; set; }

    /// <summary>What was drawn at the last step, in draw order.</summary>
    public List<ParticleSprite> Sprites { get; } = [];
    /// <summary>What the trails, shocks and pads drew at the last step: quads already laid out in the world.</summary>
    public List<ParticleQuad> Quads { get; } = [];
    /// <summary>What the rain drew at the last step: streaks for the renderer to widen along its camera's right.</summary>
    public List<ParticleStreak> Streaks { get; } = [];

    /// <summary>
    /// Where the camera is: what the rain centres its box on (it reads the
    /// camera's position). **Ours:** the frame renderer sets it from the camera it
    /// is about to draw with, before it steps the world; with no renderer it stays where it was
    /// last put (the world's origin to begin with).
    /// </summary>
    public System.Numerics.Vector3 Eye { get; set; }
    /// <summary>The same, cut into one run per emitter.</summary>
    public List<ParticleBatch> Batches { get; } = [];

    /// <summary>The live list, in slot order (empty slots included), for tests and tools.</summary>
    public IReadOnlyList<Emitter?> Live => live;
    /// <summary>Every emitter on a thing, live or not.</summary>
    public IReadOnlyCollection<Emitter> All => byThing.Values;

    /// <summary>The place's weapons' blade trails, watched at every step; null with no place.</summary>
    public BladeTrails? Blades { get; }

    /// <summary>A system for a place: the prototypes from its install, and every thing it already has.</summary>
    public ParticleSystem(Location location)
    {
        this.location = location;
        Blades = new BladeTrails(location);
        if (location.Files is { } files && files.TryRead("misc/game.npd", out var data))
            Load(NpdFile.Read(data, "misc/game.npd"));
        Notice();
    }

    /// <summary>A system with no place, from a file already read — what the tests use.</summary>
    public ParticleSystem(NpdFile npd) => Load(npd);

    void Load(NpdFile npd)
    {
        foreach (var block in npd.Emitters) prototypes[block.Name] = EmitterSettings.From(block);
    }

    /// <summary>A prototype's numbers by name (case-insensitive, as the original looks it up), or null.</summary>
    public EmitterSettings? Prototype(string name) => prototypes.GetValueOrDefault(name);

    /// <summary>The emitter a thing has, or null.</summary>
    public Emitter? Of(WorldThing thing) => byThing.GetValueOrDefault(thing);

    /// <summary>
    /// Gives <paramref name="thing"/> the emitter <paramref name="name"/>: a copy of the
    /// prototype, with its particle array and its material. Null when no block has that name — the
    /// engine then leaves the thing without one. A thing that already has one keeps it (the engine
    /// asserts <c>NULL == pEmitter</c>).
    /// </summary>
    public Emitter? Attach(WorldThing thing, string name)
    {
        seen.Add(thing);
        if (byThing.TryGetValue(thing, out var have)) return have;
        if (Prototype(name) is not { } proto)
        {
            if (unknown.Add(name)) log.LogDebug("no particle description '{Name}' (thing {Template})", name, thing.Template);
            return null;
        }
        var emitter = Make(proto);
        emitter.Thing = thing;
        if (location is not null && emitter.Settings.Material.Length > 0)
            emitter.Material = location.Materials.Named(emitter.Settings.Material) ?? -1;
        // A beam measures its texture's scroll by the bitmap's height.
        if (emitter is BeamEmitter beam && emitter.Material >= 0 && location!.Materials.Items[emitter.Material].Image is { } image)
            beam.TextureHeight = image.Height;
        byThing[thing] = emitter;
        log.LogTrace("emitter {Name} ({Kind}) on {Template} #{Guid}", name, emitter.Kind, thing.Template, thing.Guid);
        // A rain emitter goes into the live list the moment it is made (the lookup's kind-6 case).
        if (emitter.Kind == EmitterKind.Rain) Register(emitter);
        return emitter;
    }

    /// <summary>
    /// A new emitter from a prototype, with no thing: the kind's clone (its virtual clone), which copies the
    /// numbers and sizes the particle array. Kinds Vitomir does not simulate yet make a
    /// <see cref="DormantEmitter"/>.
    /// </summary>
    public Emitter Make(EmitterSettings proto)
    {
        var s = proto.Clone();
        return s.Kind switch
        {
            EmitterKind.Gas => new GasEmitter(s, this),
            EmitterKind.Fire => new FireEmitter(s, this),
            EmitterKind.Flame => new FlameEmitter(s, this),
            EmitterKind.Trail => new TrailEmitter(s, this),
            EmitterKind.Sparks => new SparksEmitter(s, this),
            EmitterKind.Spurt => new SpurtEmitter(s, this),
            EmitterKind.Rain => new RainEmitter(s, this),
            EmitterKind.Shock => new ShockEmitter(s, this),
            EmitterKind.Pad => new PadEmitter(s, this),
            EmitterKind.Ring => new RingEmitter(s, this),
            EmitterKind.Magic => new MagicEmitter(s, this),
            EmitterKind.Beam => new BeamEmitter(s, this),
            EmitterKind.Object => new ObjectEmitter(s, this) { Place = location },
            EmitterKind.Liquid => new LiquidEmitter(s, this),
            _ => new DormantEmitter(s, this),
        };
    }

    /// <summary>
    /// The thing is being destroyed: a live emitter stops spawning — its next
    /// birth is put at now — lets go of the thing and is deleted once its last particle dies; one
    /// that is not live is deleted at once.
    /// </summary>
    public void Detach(WorldThing thing)
    {
        if (!byThing.Remove(thing, out var emitter)) return;
        if (emitter.Registered)
        {
            emitter.Dying = true;
            emitter.NextBirth = Now;
            emitter.Thing = null;
        }
    }

    /// <summary>
    /// One frame: new things given their emitters, destroyed things' emitters let go, every thing
    /// in the world counted as drawn (see the class summary), then the live list walked —
    /// each emitter dropped if unseen for 60 frames, else updated, deleted if finished, else drawn.
    /// </summary>
    /// <param name="seconds">The frame's time.</param>
    /// <param name="milliseconds">The engine's clock after the step.</param>
    public void Step(float seconds, int milliseconds)
    {
        Now = unchecked((uint)milliseconds);
        Frame++;
        Blades?.Watch();
        Notice();
        if (location is not null)
            foreach (var thing in location.Things)
                if (byThing.TryGetValue(thing, out var e) && (thing.RenderFlags & DontRender) == 0 && Seen(thing))
                    Touch(e);

        Sprites.Clear();
        Quads.Clear();
        Streaks.Clear();
        Batches.Clear();
        for (int i = 0; i < Slots; i++)
        {
            if (live[i] is not { } e) continue;
            if (!e.Dying && e.SeenFrame < Frame - UnseenFrames)
            {
                e.Registered = false;
                live[i] = null;
                continue;
            }
            if (!e.Step(seconds, Now))
            {
                // Finished: a dying one (or one with no thing) is deleted; any other drops out.
                if (e.Thing is not null && !e.Dying) e.Registered = false;
                live[i] = null;
                continue;
            }
            if (Off || e.Hidden) continue;
            int first = Sprites.Count, quads = Quads.Count, streaks = Streaks.Count;
            e.Collect(Sprites);
            e.CollectQuads(Quads);
            e.CollectStreaks(Streaks);
            if (Sprites.Count > first) Batches.Add(new ParticleBatch(e.Material, first, Sprites.Count - first, e));
            if (Quads.Count > quads) Batches.Add(new ParticleBatch(e.Material, quads, Quads.Count - quads, e, ParticleRun.Quads));
            if (Streaks.Count > streaks) Batches.Add(new ParticleBatch(e.Material, streaks, Streaks.Count - streaks, e, ParticleRun.Streaks));
        }
    }

    /// <summary>
    /// The renderer's last portal walk (<see cref="PortalView"/>), or null — then every thing in the
    /// place counts as drawn. The engine touches an emitter as it draws its thing, and
    /// draws only the things of the sectors its walk reached (the sector draw over the
    /// visible list), a thing riding another with the one it rides. **Ours:** the walk is last
    /// frame's (the frame renderer walks after it has stepped the world), and a thing in no
    /// sector counts as drawn.
    /// </summary>
    public PortalView? View
    {
        get => view;
        set { view = value; AwaitingWalk = false; }
    }
    PortalView? view;

    /// <summary>
    /// **A renderer is drawing this place and has not walked it yet**: until the first walk is
    /// handed over (<see cref="View"/>), nothing counts as drawn, so no emitter takes a slot. 1.1
    /// registers an emitter only as its thing is drawn — the registration's one caller is the thing
    /// draw, which runs over the sectors the frame's walk reached — so on a place's first
    /// frame only what that walk reaches is in the list. Vitomir steps the world before it walks (the
    /// walk is last frame's), and on the first frame, with no walk yet, had counted every thing as
    /// drawn: in Monastery 1 the 34 torches' `flametest` flames out of sight took slots beside the 92
    /// flames in view, the 128 were full, and a `damageme` fire got none until the unseen dropped out
    /// 60 frames on. Set by the frame renderer before its first step
    /// (<see cref="BeforeFirstDraw"/>); a run with no renderer (the tests, <c>--settle</c>) never
    /// sets it and every thing counts as drawn, as before.
    /// </summary>
    public bool AwaitingWalk { get; set; }

    /// <summary>A renderer's first frame of this place is about to step it: with no walk yet, nothing counts as drawn (<see cref="AwaitingWalk"/>).</summary>
    public void BeforeFirstDraw()
    {
        if (view is null) AwaitingWalk = true;
    }

    bool Seen(WorldThing thing)
    {
        if (View is not { } view) return !AwaitingWalk;
        var root = thing;
        while (root.AttachedTo is { } up) root = up;
        return root.Sector < 0 || view.Sees((int)root.Sector);
    }

    /// <summary>
    /// The thing was drawn this frame: the frame is noted, and an emitter not yet live
    /// goes into the first free slot — with its clock set to now and its last position to where the
    /// thing is — or nowhere, when all 128 are taken.
    /// </summary>
    public void Touch(Emitter e)
    {
        e.SeenFrame = Frame;
        if (!e.Registered) Register(e);
    }

    void Register(Emitter e)
    {
        int free = Array.IndexOf(live, null);
        if (free < 0) return;
        e.LastUpdate = Now;
        if (e.Thing is { } t) e.LastOrigin = t.Position;
        live[free] = e;
        e.Registered = true;
    }

    /// <summary>
    /// The things the system has not looked at yet get their emitter from their tags, and things
    /// that have been destroyed let theirs go. The engine does the first as the template is applied
    /// and the second in the destructor; here both are noticed at the next step.
    /// </summary>
    void Notice()
    {
        if (location is null) return;
        if (location.Templates is { } templates)
            foreach (var thing in location.Things)
            {
                if (!seen.Add(thing)) continue;
                var tags = templates.Resolve(thing.Scene);
                if (tags.TryGetValue(ParticleNameTag, out var tag) && tag.AsString() is { Length: > 0 } name)
                    Attach(thing, name);
            }
        List<WorldThing>? gone = null;
        foreach (var thing in byThing.Keys)
            if (thing.Removed) (gone ??= []).Add(thing);
        if (gone is not null) foreach (var thing in gone) Detach(thing);
    }
}
