// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: where the swing bones' simulator (SwingRule, the character models' reference simulator ported) runs for the characters.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// How the swing bones are simulated (<see cref="SwingRule"/>) — the numbers <b>SANDBOX</b>, ours, measured with the proving ground's
/// townsfolk. Presentation only: none of it reaches the simulation, the hit volumes or a digest. Off (<see cref="Off"/>:
/// <c>--swing-off</c>, the setting <c>swingOff</c>) the world has no simulator and the swing bones are drawn as before
/// (<see cref="SkirtRule"/> for a skirt hung from the thighs, the rest at their rest).
/// </summary>
public sealed record PersonSwingSettings
{
    /// <summary>Whether the simulator runs (not).</summary>
    public bool On { get; init; } = true;

    /// <summary>
    /// Steps a second near the eye (60, the models repository's: the capsules and the stretch limit are corrections made once a step, so the result
    /// depends a little on how many; 60 whatever the tick or the frame rate: with the 120 Hz tick, a step every second tick).
    /// </summary>
    public int StepsPerSecond { get; init; } = 60;

    /// <summary>Within this of the eye (m) a person is stepped <see cref="StepsPerSecond"/> times a second. <c>swingNear</c>, <c>--swing-near</c>.</summary>
    public float NearMetres { get; init; } = 15f;

    /// <summary>
    /// Past <see cref="NearMetres"/> to this (m), <see cref="FarStepsPerSecond"/>; past it the simulator holds off and the person's swing bones
    /// are drawn as before (a skirt from the thighs by <see cref="SkirtRule"/>, a long one at rest), its state dropped and begun again
    /// at rest when it comes nearer. <c>swingFar</c>, <c>--swing-far</c>.
    /// </summary>
    public float FarMetres { get; init; } = 30f;

    /// <summary>Steps a second between <see cref="NearMetres"/> and <see cref="FarMetres"/> (30: the same seconds in twice as long steps).</summary>
    public int FarStepsPerSecond { get; init; } = 30;

    /// <summary>
    /// Between steps (with the 120 Hz tick, every second tick) the tails carried by their heads are kept out of their colliders where the
    /// body has them now, as a step keeps them (<see cref="SwingRule.Pose"/>); the earlier build's (off: <c>--swing-keep-out-off</c>, the setting
    /// <c>swingKeepOutOff</c>) drew them carried alone, and a running thigh came through the tunic on those ticks.
    /// </summary>
    public bool KeepOutBetweenSteps { get; init; } = true;

    /// <summary>A body that moves further than this in one tick (m) has been put somewhere: its cloth begun again at rest.</summary>
    public float TeleportMetres { get; init; } = 1f;

    /// <summary>The defaults (above).</summary>
    public static readonly PersonSwingSettings Default = new();

    /// <summary>No simulator.</summary>
    public static readonly PersonSwingSettings Off = new() { On = false };

    /// <summary>Steps a second for a person <paramref name="metres"/> from the eye: 0 past <see cref="FarMetres"/> (held).</summary>
    public int RateAt(float metres) => metres <= NearMetres ? StepsPerSecond : metres <= FarMetres ? FarStepsPerSecond : 0;

    /// <summary>
    /// How many steps of <paramref name="rate"/> a second fall in tick <paramref name="tick"/> of a world stepped <paramref name="ticksPerSecond"/>
    /// times a second: the steps whose times (k / rate) are in the tick's span, so a second has <paramref name="rate"/> of them whatever the tick
    /// (120 Hz and 60: one every second tick; 240: every fourth; 30: two a tick).
    /// </summary>
    public static int Due(long tick, int rate, int ticksPerSecond)
    {
        if (rate <= 0 || ticksPerSecond <= 0) return 0;
        return (int)((tick + 1) * rate / ticksPerSecond - tick * rate / ticksPerSecond);
    }
}

/// <summary>
/// Each character's swing (<see cref="SwingState"/>), kept outside anything digested: a service the <see cref="PersonSwingSystem"/>
/// steps and <see cref="CharacterPoses"/> draws from. A character's state is made when it is first met wearing something with swing chains (or
/// in another outfit), begun again at rest when it reappears after being held off, is put somewhere (<see cref="PersonSwingSettings.TeleportMetres"/>)
/// or its slot is another entity's.
/// </summary>
public sealed class PersonSwings(PersonSwingSettings settings)
{
    internal struct Entry
    {
        public SwingState? State;
        public int Generation, Kind;
        public long Seen;
        public Vector3 Feet;
        public bool Live;
    }

    Entry[] entries = [];

    /// <summary>The settings it runs by.</summary>
    public PersonSwingSettings Settings { get; } = settings;

    /// <summary>The last tick's count of people stepped (at least once), begun again at rest, and held off by distance.</summary>
    public (int Stepped, int Reset, int Held) LastTick { get; internal set; }

    /// <summary>Steps taken in all (every person's), since the world began.</summary>
    public long Steps { get; internal set; }

    /// <summary>The entity's swing when it is simulated now (drawn from it), else null (drawn as before).</summary>
    public SwingState? Live(EntityId e)
    {
        if (e.Index >= entries.Length) return null;
        ref var en = ref entries[e.Index];
        return en.Live && en.Generation == e.Generation ? en.State : null;
    }

    /// <summary>The entity's record (made room for the first time it is asked).</summary>
    internal ref Entry Slot(EntityId e)
    {
        if (e.Index >= entries.Length) Array.Resize(ref entries, Math.Max(e.Index + 1, Math.Max(16, entries.Length * 2)));
        return ref entries[e.Index];
    }
}

/// <summary>
/// Steps the people's swing (<see cref="SwingRule"/>) in the deterministic tick, at the end of it (<see cref="Phase.Post"/>, after the camera:
/// the pose as the tick leaves it, after the IK, the turn chain, the bend and twist rules, where <see cref="SkirtRule"/> was), on the drawn pose
/// (<see cref="CharacterPoses"/>), with the ground under the body in the skeleton's space (its stair line's, by its scale; none in the air), at
/// the rate its distance from the eye gives (<see cref="PersonSwingSettings.RateAt"/>; each person's steps staggered by its slot). Nothing simulated
/// reads it; it allocates nothing once a person's state is made.
/// </summary>
public sealed class PersonSwingSystem(CharacterAssets assets, PersonSwings swings) : ISystem
{
    /// <summary>Its name and place in <see cref="Phase.Post"/> (after the camera, 100, and the reticle, 110).</summary>
    public const string Name = "platform.person-swing";
    public const int Order = 900;

    public void Run(World world)
    {
        var settings = swings.Settings;
        var anims = world.Store<CharacterAnimation>();
        world.TryStore<Body>(out var bodies);
        bool view = world.TryService<ViewState>(out var viewState) && viewState.HasView;
        var eye = view ? viewState.View.Eye : default;
        var poses = world.Service<CharacterPoses>();
        long tick = world.Tick;
        int stepped = 0, reset = 0, held = 0;
        var entities = anims.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            int kindIndex = anims.Data[i].Kind;
            var kind = assets.Kind(kindIndex);
            var rule = kind.Person?.Swing;
            if (rule is null && swings.Live(e) is null) continue;
            ref var en = ref swings.Slot(e);
            bool hasBody = bodies is not null && bodies.Has(e);
            var feet = hasBody ? bodies!.Get(e).Feet : Vector3.Zero;
            int rate = rule is null ? 0 : settings.RateAt(view && hasBody ? Vector3.Distance(eye, feet) : 0f);
            if (rate <= 0)
            {
                // No swing (now): drawn as before.
                if (en.Live && en.Generation == e.Generation) { en.Live = false; poses.Reswing(world, e); }
                if (rule is not null) held++;
                en.Seen = tick;
                continue;
            }
            bool fresh = en.State is null || !ReferenceEquals(en.State.Rule, rule);
            if (fresh) en.State = rule!.NewState();
            bool begin = fresh || !en.Live || en.Generation != e.Generation || en.Kind != kindIndex || tick - en.Seen > 1
                         || (hasBody && Vector3.Distance(feet, en.Feet) > settings.TeleportMetres);
            var state = en.State!;
            if (begin)
            {
                en.Live = false;   // the body without the swing (only its own bones are read)
                rule!.Reset(state, poses.Bones(world, e), CharacterPoses.Lift(world, e));
                en.Live = true;
                en.Generation = e.Generation;
                en.Kind = kindIndex;
                poses.Reswing(world, e);
                reset++;
            }
            else if (PersonSwingSettings.Due(tick + e.Index, rate, World.TicksPerSecond) is var due and > 0)
            {
                var body = poses.Bones(world, e);
                // The swing's space: the model's lifted to the stairs' line (the body drawn on it; SwingRule.Step), the ground its level; none in the air.
                float lift = CharacterPoses.Lift(world, e);
                float? ground = hasBody && bodies!.Get(e).State == BodyState.Ground ? 0f : null;
                for (int k = 0; k < due; k++) rule!.Step(state, body, 1f / rate, ground, lift);
                swings.Steps += due;
                poses.Reswing(world, e);
                stepped++;
            }
            en.Seen = tick;
            en.Feet = feet;
        }
        swings.LastTick = (stepped, reset, held);
    }
}
