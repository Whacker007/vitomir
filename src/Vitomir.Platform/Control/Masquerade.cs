// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Control;

/// <summary>
/// How a body moves (ours): in the <b>masquerade</b>, as a human would (the
/// human profile's numbers, whatever the body could do), or <b>unbound</b>, with its own profile, powers and all.
/// </summary>
public enum MasqueradeMode
{
    Masquerade = 0,
    Unbound = 1,
}

/// <summary>Why a body's mode changed: the player's key, or the body entering a place of the other kind.</summary>
public enum MasqueradeCause
{
    Key = 0,
    EnteredPublic = 1,
    EnteredFree = 2,
    /// <summary>The catch let go because someone struck the body: a blow or a shield met, by another.</summary>
    Attacked = 3,
}

/// <summary>What a body did that no human body could (<see cref="MasqueradeRisk"/>).</summary>
public enum MasqueradeAct
{
    /// <summary>A jump higher than a human's (metres the feet rise).</summary>
    Jump = 0,
    /// <summary>A speed over the ground faster than a human's sprint (m/s).</summary>
    Speed = 1,
    /// <summary>A fall landed faster than a human takes without harm (metres fallen).</summary>
    Fall = 2,
    /// <summary>A traversal a human could not make: a hop, hurdle, vault, climb, mantle or grab beyond the human's reach (metres).</summary>
    Move = 3,
    /// <summary>
    /// Violence: a blow that met a person — their body or their shield — in a public place, raised for the one who
    /// struck, by contact, whatever their mode or profile (it is ordinary violence, not the supernatural: the act table makes
    /// it little strange and very threatening, so townsfolk flee and the watch comes). Its measure is the blow's impact
    /// speed (m/s); it has no human limit.
    /// </summary>
    Violence = 4,
}

/// <summary>
/// A body's masquerade (ours): its mode, its own movement profile (an index of the world's
/// <see cref="MovementProfiles"/>), the place its feet were settled in (public or free) and the place they are
/// passing into, and what the risk check remembers of the act in progress (a speed beyond a human's: whether it
/// is still rising or was reported; the last traversal seen). Given by <see cref="MovementProfiles.Give"/>. Plain
/// four-byte fields, digested.
/// </summary>
public struct MasqueradeState
{
    public MasqueradeMode Mode;
    public int Profile;
    /// <summary>The place the feet are settled in, and one they have been in for <see cref="PendingSeconds"/> but not yet long enough to count.</summary>
    public PlaceKind Place, PendingPlace;
    public float PendingSeconds;
    /// <summary>A speed act: 0 none, 1 over a human's and still rising, 2 reported, 3 at its top where it was no risk (free, or in the masquerade).</summary>
    public int SpeedAct;
    /// <summary>The speed act's top so far (m/s), how long it has not risen (s), and how long the speed has been back within a human's (s).</summary>
    public float SpeedPeak, FlatSeconds, CalmSeconds;
    /// <summary>The traversal seen last tick and how far through it was.</summary>
    public TraversalKind Move;
    public float MoveProgress;
}

/// <summary>
/// The masquerade's settings for the simulation (ours), recorded in a tape's head:
/// <see cref="AutoSwitch"/>, by design: the mode switches by itself as the player's body
/// passes into a place of the other kind, on in a public place, unbound in a free one ("If you go into a combat
/// zone, you should expect that you'll need to do what you need to do in order to accomplish your goal and
/// survive"); the key toggles it in between. On by default.
/// </summary>
public sealed class MasqueradeSettings
{
    public bool AutoSwitch { get; set; } = true;
}

/// <summary>A body's mode changed: who, when, to what, why, and the place and feet then.</summary>
public readonly record struct MasqueradeModeChanged(EntityId Entity, long Tick, MasqueradeMode Mode, MasqueradeCause Cause, PlaceKind Place, Vector3 Feet);

/// <summary>
/// A risk to the Masquerade (ours): a body unbound, in a public place, did what no
/// human body could — who, when, what (<see cref="Act"/>, and for a traversal which <see cref="Move"/>), where
/// (the feet), what it measured and the most a human's numbers allow, in the act's unit (metres, or m/s for a
/// speed). Raised once per act. Whether anyone saw it and what it costs is perception's and the rules package's.
/// </summary>
public readonly record struct MasqueradeRisk(EntityId Entity, long Tick, MasqueradeAct Act, TraversalKind Move, Vector3 Where, float Measured, float HumanLimit)
{
    /// <summary>How far beyond a human's it went.</summary>
    public float Beyond => Measured - HumanLimit;

    /// <summary>The act in words: <c>a jump of 3.0 m (a human's: 0.5 m)</c>.</summary>
    public string Words => Act switch
    {
        MasqueradeAct.Jump => Line("a jump of", "m"),
        MasqueradeAct.Speed => Line("a speed of", "m/s"),
        MasqueradeAct.Fall => Line("a fall of", "m, landed unharmed"),
        MasqueradeAct.Violence => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"violence, a blow struck at {Measured:0.0} m/s"),
        MasqueradeAct.Move when Move == TraversalKind.WallOn => "a climb up a face with nothing to hold (no human can)",
        _ => Line($"a {Move.ToString().ToLowerInvariant()} of", "m"),
    };

    string Line(string what, string unit) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{what} {Measured:0.0#} {unit} (a human's at most {HumanLimit:0.0#})");
}

/// <summary>
/// The masquerade (ours): the numbers a body is held to, the
/// <see cref="MasqueradeSystem"/> (the key and the places switch the mode, its numbers change between ticks) and
/// the <see cref="MasqueradeRiskSystem"/> (what no human could do, reported).
/// </summary>
public static class Masquerade
{
    /// <summary>How long the feet must be in a place of the other kind before it counts as entered (s): a body standing in a doorway never flickers.</summary>
    public const float SettleSeconds = 0.25f;

    /// <summary>The numbers a body keeps its own of in the masquerade: its size, its gravity, its grace times (and, its charge's length).</summary>
    public static readonly IReadOnlyList<string> KeptOwn =
    [
        nameof(Locomotion.Radius), nameof(Locomotion.Height), nameof(Locomotion.EyeHeight), nameof(Locomotion.CrouchHeight),
        nameof(Locomotion.Gravity), nameof(Locomotion.CoyoteSeconds), nameof(Locomotion.JumpBufferSeconds),
        nameof(Locomotion.ChargeSeconds),
    ];

    /// <summary>
    /// The prefix of the numbers that are the body's own power, which the masquerade never lowers (the
    /// powered acts have inputs of their own that work anywhere, at the risk of being seen): <c>PoweredJumpImpulse</c>,
    /// <c>PoweredStandingReach</c>, <c>PoweredVaultMax</c>, <c>PoweredMantleMax</c>.
    /// </summary>
    public const string PoweredPrefix = "Powered";

    /// <summary>
    /// The numbers of a body of <paramref name="own"/> with its worked-out ones filled: the powered jump and reach
    /// its own (<see cref="Locomotion.WithOwnPowers"/>), and its tap a human's jump — <paramref name="human"/>'s tap, no
    /// higher than its own jump ("a human-sized jump for every body, everywhere"). Configuration, not a tick.
    /// </summary>
    public static Locomotion WithHumanTap(in Locomotion own, in Locomotion human)
    {
        var l = own.WithOwnPowers();
        l.TapJumpImpulse = MathF.Min(l.JumpImpulseBudget, human.WithOwnPowers().TapImpulse);
        return l;
    }

    /// <summary>The capabilities where less is more (a mantle's least depth and its length): held to the greater.</summary>
    public static readonly IReadOnlyList<string> LessIsMore = [nameof(Locomotion.MantleMinDepth), nameof(Locomotion.MantleSeconds)];

    /// <summary>
    /// The powered pace the masquerade does not lower: the body's resting speed, its supernatural speed a
    /// double tap of sprint gives anywhere — in public a risk (<see cref="MasqueradeAct.Speed"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> KeptPowered = [nameof(Locomotion.RestingSpeed)];

    /// <summary>
    /// The numbers of a body of <paramref name="own"/> held to <paramref name="human"/>'s: the body keeps its own size
    /// (radius, height, eyes, crouched height), its gravity and its grace times (<see cref="KeptOwn"/>); every other
    /// number is a capability and is the lesser of its own and the human's (so a body weaker than a human is never made
    /// stronger) — the paces, speeding up and stopping, the jump, the step, the steepest ground, steering in the air,
    /// the sprint's endurance, the crouched pace, the reach, the hurdle, the vault, the mantle, the grip, the fall
    /// tolerance, the turning, and any number the body gains later — but for the two where less is more
    /// (<see cref="LessIsMore"/>), the greater; and the body's own power (the numbers named <see cref="PoweredPrefix"/>…),
    /// kept, so a charged jump reaches the body's own jump in either mode. By name (<see cref="LocomotionFields"/>):
    /// for configuration, never during a tick (<see cref="MovementProfiles"/> works them out when profiles are added).
    /// </summary>
    public static Locomotion HeldToHuman(in Locomotion own, in Locomotion human)
    {
        var l = own;
        foreach (var name in LocomotionFields.Names)
        {
            if (KeptOwn.Contains(name) || KeptPowered.Contains(name) || name.StartsWith(PoweredPrefix, StringComparison.Ordinal)) continue;
            float o = LocomotionFields.Get(own, name), h = LocomotionFields.Get(human, name);
            l = LocomotionFields.With(l, name, LessIsMore.Contains(name) ? MathF.Max(o, h) : MathF.Min(o, h));
        }
        return l;
    }

    /// <summary>
    /// The half-life of a body's starts and stops held to <paramref name="human"/>'s (s; the spring): the slower of the two (a
    /// longer half-life is a body slower to start and stop, as a lesser acceleration is); a body on the ramp (0) stays on it, and a
    /// human on the ramp holds nothing.
    /// </summary>
    public static float HeldHalfLife(float own, float human) => own <= 0f ? own : human <= 0f ? own : MathF.Max(own, human);

    /// <summary>How high a jump of <paramref name="numbers"/> lifts the feet (m): v²/2g.</summary>
    public static float JumpHeight(in Locomotion numbers) =>
        numbers.Gravity > 0 ? numbers.JumpImpulseBudget * numbers.JumpImpulseBudget / (2f * numbers.Gravity) : 0f;

    /// <summary>The highest drop <paramref name="numbers"/> land without harm (m): the fall tolerance's speed, v²/2g.</summary>
    public static float SafeFall(in Locomotion numbers) =>
        numbers.Gravity > 0 ? numbers.FallTolerance * numbers.FallTolerance / (2f * numbers.Gravity) : 0f;

    /// <summary>
    /// The most a human's numbers allow for a traversal (m; the human's bands, as ours): a hop up to the
    /// vault's lowest (0.6 m), a sprint vault to <see cref="Locomotion.SprintVaultMax"/> and a run's step-over to
    /// <see cref="Locomotion.StepOverMax"/>, a vault or a climb onto to
    /// <see cref="Locomotion.VaultMax"/>, a mantle to <see cref="Locomotion.MantleMax"/>, a ledge grabbed to the
    /// standing reach plus the jump (above the ground left); +∞ for the moves that follow another (hang, pull up, drop).
    /// </summary>
    public static float HumanReach(TraversalKind kind, in Locomotion human) => kind switch
    {
        TraversalKind.HopUp => human.VaultMin,
        TraversalKind.Hurdle => human.SprintVaultMax,
        TraversalKind.StepOver => human.StepOverMax,
        TraversalKind.Vault or TraversalKind.ClimbOnto => human.VaultMax,
        TraversalKind.Mantle => human.MantleMax,
        TraversalKind.Grab => human.StandingReach + JumpHeight(human),
        // Onto a wall: its measure (Traversal.Height) is nought on a wall with holds (anyone may climb it), 1 on a face with nothing to hold.
        TraversalKind.WallOn => 0f,
        _ => float.PositiveInfinity,
    };

    /// <summary>What a body may go beyond a human's limit by before it is a risk: numbers' noise, never an act (m, and m/s for a speed).</summary>
    public const float HeightMargin = 0.05f, SpeedMargin = 0.1f;

    /// <summary>How long a speed back within a human's ends the act (s): a hop mid-sprint does not make two.</summary>
    public const float CalmSeconds = 0.5f;

    /// <summary>How long a speed act's speed must stop rising before its top is taken (s): a tick's brush with a wall is not its top.</summary>
    public const float TopSeconds = 0.1f;
}

/// <summary>
/// The Input phase's masquerade (ours, after the player's control): for each controlled body with a
/// <see cref="MasqueradeState"/>, the place its feet are in is settled (after <see cref="Masquerade.SettleSeconds"/>
/// there), and — when <see cref="MasqueradeSettings.AutoSwitch"/> is on — passing into a public place puts it in the
/// masquerade and into a free place unbinds it; then a press of the masquerade action toggles the mode. A
/// changed mode gives the body the numbers of its profile in that mode (<see cref="MovementProfiles.Numbers"/>)
/// before it moves this tick, and is raised (<see cref="MasqueradeModeChanged"/>). Bodies not controlled switch by the
/// place alone (the masquerade governs every body; the key is the player's). Deterministic: the press is on the tape, the places are the level's.
/// </summary>
public sealed class MasqueradeSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryService<MovementProfiles>(out var profiles) || profiles.All.Count == 0) return;
        var input = world.Service<TickInput>();
        var settings = world.Service<MasqueradeSettings>();
        var places = world.Service<LevelSpace>().Places;
        bool toggle = (input.Pressed & Buttons.Masquerade) != 0;
        var states = world.Store<MasqueradeState>();
        var tags = world.Store<Controlled>();
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var changed = world.Events<MasqueradeModeChanged>();
        var ents = states.Entities;
        var data = states.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            // Every body's mode follows the place it enters, the player's only when the setting says so;
            // only the player's key toggles it. Bodies nobody controls switch by the place alone (an arena's fighters
            // in a free yard are unbound; a townsman's human numbers are the same either way).
            bool mine = tags.Has(e);
            if (!bodies.Has(e) || !locos.Has(e)) continue;
            ref var st = ref data[i];
            ref readonly var body = ref bodies.Get(e);
            var mode = st.Mode;
            var cause = MasqueradeCause.Key;

            var here = places.At(body.Feet);
            if (here == st.Place) { st.PendingPlace = here; st.PendingSeconds = 0; }
            else
            {
                if (here != st.PendingPlace) { st.PendingPlace = here; st.PendingSeconds = 0; }
                st.PendingSeconds += World.TickSeconds;
                if (st.PendingSeconds >= Masquerade.SettleSeconds - World.TickSeconds * 0.5f)
                {
                    st.Place = here;
                    st.PendingSeconds = 0;
                    if (settings.AutoSwitch || !mine)
                    {
                        mode = here == PlaceKind.Public ? MasqueradeMode.Masquerade : MasqueradeMode.Unbound;
                        cause = here == PlaceKind.Public ? MasqueradeCause.EnteredPublic : MasqueradeCause.EnteredFree;
                    }
                }
            }
            if (toggle && mine)
            {
                mode = mode == MasqueradeMode.Masquerade ? MasqueradeMode.Unbound : MasqueradeMode.Masquerade;
                cause = MasqueradeCause.Key;
            }
            if (mode == st.Mode) continue;
            st.Mode = mode;
            if (st.Profile >= 0 && st.Profile < profiles.All.Count && st.Profile != profiles.HumanIndex)
                locos.Get(e) = profiles.Numbers(st.Profile, mode);
            changed.Publish(new MasqueradeModeChanged(e, world.Tick, mode, cause, st.Place, body.Feet));
        }
    }
}

/// <summary>
/// The risk check (ours, <see cref="Phase.Perceive"/>, before any perception): for every body with a
/// <see cref="MasqueradeState"/> that is <b>unbound</b> and whose own profile is not the human's, each act beyond
/// the human profile's numbers done in a <b>public</b> place is raised once as a <see cref="MasqueradeRisk"/> — a
/// jump taken off faster than a human's (at take-off), a landing faster than a human's fall tolerance (on landing),
/// a speed over the ground beyond a human's sprint (once per stretch over it, at its top — once it has not risen
/// for <see cref="Masquerade.TopSeconds"/> —, or when it first reaches a public place still over it), and a traversal beyond a human's band (as it starts; <see cref="Traversal"/>,
/// written by the body, when the world has that component). A free place, the masquerade mode and a human body
/// never raise one — but for <b>the powered leap</b>: a charged jump higher than a human's
/// (<see cref="JumpLaunched"/>), and a move from it beyond a human's band, are risks in either mode.
/// </summary>
public sealed class MasqueradeRiskSystem : ISystem
{
    Store<Traversal>? traversals;
    Store<Controlled>? controlled;
    Store<Powers.PaceScale>? paces;
    bool looked;

    public void Run(World world)
    {
        if (!world.TryService<MovementProfiles>(out var profiles) || profiles.Human is not { } humanProfile) return;
        if (!looked)
        {
            looked = true;
            foreach (var s in world.Stores) if (s is Store<Traversal> t) traversals = t;
            foreach (var s in world.Stores) if (s is Store<Controlled> c) controlled = c;
            foreach (var s in world.Stores) if (s is Store<Powers.PaceScale> p) paces = p;
        }
        var human = humanProfile.Numbers;
        int humanIndex = profiles.HumanIndex;
        var places = world.Service<LevelSpace>().Places;
        var states = world.Store<MasqueradeState>();
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        var risks = world.Events<MasqueradeRisk>();

        // Jumps and landings, from the body's own events this tick. A charged jump (the powered leap) is a risk in
        // either mode; a tap is a human's jump, so only numbers with a tap of their own (no human's) make one unbound.
        var launched = world.Events<JumpLaunched>().ThisTick;
        for (int i = 0; i < launched.Count; i++)
        {
            var j = launched[i];
            bool charged = j.Kind == JumpKind.Charged;
            if (!(charged ? NotHuman(states, j.Entity, humanIndex) : AtRisk(states, j.Entity, humanIndex)) || places.At(j.Feet) != PlaceKind.Public) continue;
            float limit = Masquerade.JumpHeight(human);
            if (j.Height > limit + Masquerade.HeightMargin)
                risks.Publish(new MasqueradeRisk(j.Entity, world.Tick, MasqueradeAct.Jump, TraversalKind.None, j.Feet, j.Height, limit));
        }
        var landed = world.Events<Landed>().ThisTick;
        for (int i = 0; i < landed.Count; i++)
        {
            var l = landed[i];
            if (!AtRisk(states, l.Entity, humanIndex) || places.At(l.Feet) != PlaceKind.Public) continue;
            if (l.ImpactSpeed > human.FallTolerance + Masquerade.SpeedMargin)
                risks.Publish(new MasqueradeRisk(l.Entity, world.Tick, MasqueradeAct.Fall, TraversalKind.None, l.Feet, l.FallHeight, Masquerade.SafeFall(human)));
        }

        // Speeds and traversals, from each body's state.
        var ents = states.Entities;
        var data = states.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!bodies.Has(e)) continue;
            ref var st = ref data[i];
            ref readonly var body = ref bodies.Get(e);
            bool risky = st.Mode == MasqueradeMode.Unbound && st.Profile != humanIndex;
            // Supernatural speed is a powered act anywhere, the masquerade's mode or not.
            bool powered = controlled is { } cs && cs.Has(e) && cs.Get(e).Supernatural != 0;
            // A body unbound under a pace scale (Celerity) goes past a human's sprint whatever its profile: a risk in public.
            bool paced = st.Mode == MasqueradeMode.Unbound && paces is { } ps && ps.Has(e) && ps.Get(e).Factor > 1f;
            Speed(world, ref st, e, body, human.SprintSpeed, (risky || powered || paced) && places.At(body.Feet) == PlaceKind.Public, risks);
            // A move from a charged jump reaches with the body's own numbers in either mode, and is a risk so.
            risky |= body.Powered != 0 && st.Profile != humanIndex;
            if (traversals is { } ts && ts.Has(e))
            {
                ref readonly var t = ref ts.Get(e);
                bool started = t.Kind != TraversalKind.None && (t.Kind != st.Move || t.Progress < st.MoveProgress);
                if (started && risky && places.At(t.From) == PlaceKind.Public)
                {
                    float limit = Masquerade.HumanReach(t.Kind, human);
                    float measured = t.Kind == TraversalKind.Grab ? t.Lip.Z - body.TakeOffZ : t.Height;
                    if (measured > limit + Masquerade.HeightMargin)
                        risks.Publish(new MasqueradeRisk(e, world.Tick, MasqueradeAct.Move, t.Kind, t.From, measured, limit));
                }
                st.Move = t.Kind;
                st.MoveProgress = t.Progress;
            }
        }
    }

    /// <summary>Whether <paramref name="e"/> has a profile and it is not the human's (its powered acts are risks in either mode).</summary>
    static bool NotHuman(Store<MasqueradeState> states, EntityId e, int humanIndex) => states.Has(e) && states.Get(e).Profile != humanIndex;

    static bool AtRisk(Store<MasqueradeState> states, EntityId e, int humanIndex)
    {
        if (!states.Has(e)) return false;
        ref readonly var st = ref states.Get(e);
        return st.Mode == MasqueradeMode.Unbound && st.Profile != humanIndex;
    }

    /// <summary>One stretch over a human's sprint is one act: started on the ground, reported at its top (or when it first reaches a public place unbound, still over), ended by half a second back within a human's.</summary>
    static void Speed(World world, ref MasqueradeState st, EntityId e, in Body body, float limit, bool risky, EventQueue<MasqueradeRisk> risks)
    {
        var v = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = v.Length();
        if (st.SpeedAct == 0)
        {
            if (body.State == BodyState.Ground && speed > limit + Masquerade.SpeedMargin)
            {
                st.SpeedAct = 1;
                st.SpeedPeak = speed;
                st.FlatSeconds = 0;
                st.CalmSeconds = 0;
            }
            return;
        }
        if (speed <= limit)
        {
            st.CalmSeconds += World.TickSeconds;
            if (st.CalmSeconds >= Masquerade.CalmSeconds - World.TickSeconds * 0.5f)
            {
                if (st.SpeedAct == 1 && risky) Report(world, e, body, st.SpeedPeak, limit, risks);
                st.SpeedAct = 0;
                st.CalmSeconds = 0;
            }
            return;
        }
        st.CalmSeconds = 0;
        if (st.SpeedAct == 1)
        {
            if (speed > st.SpeedPeak + 1e-3f) { st.SpeedPeak = speed; st.FlatSeconds = 0; return; }
            st.FlatSeconds += World.TickSeconds;
            if (st.FlatSeconds < Masquerade.TopSeconds - World.TickSeconds * 0.5f) return;
            if (risky) { Report(world, e, body, st.SpeedPeak, limit, risks); st.SpeedAct = 2; }
            else st.SpeedAct = 3;
        }
        else if (st.SpeedAct == 3 && risky && speed > limit + Masquerade.SpeedMargin)
        {
            Report(world, e, body, speed, limit, risks);
            st.SpeedAct = 2;
        }
    }

    static void Report(World world, EntityId e, in Body body, float speed, float limit, EventQueue<MasqueradeRisk> risks) =>
        risks.Publish(new MasqueradeRisk(e, world.Tick, MasqueradeAct.Speed, TraversalKind.None, body.Feet, speed, limit));
}

/// <summary>The catch let go (any body, not only the player's): it was struck by <paramref name="Attacker"/> (contact <paramref name="ContactId"/>), and the masquerade mode, which was on, is now unbound.</summary>
public readonly record struct CatchLetGo(EntityId Entity, long Tick, EntityId Attacker, long ContactId);

/// <summary>Letting the masquerade's catch go when attacked.</summary>
public static class MasqueradeCatch
{
    /// <summary>
    /// Unbinds <paramref name="e"/>'s masquerade mode when it is on (the catch lets go when someone attacks you):
    /// its own profile's unbound numbers from the next move, <see cref="MasqueradeModeChanged"/> with
    /// <see cref="MasqueradeCause.Attacked"/>, and <see cref="CatchLetGo"/> naming the attacker. False when the mode was
    /// not on (once per attack: a second blow finds it already unbound). The key, or entering a public place, puts it back.
    /// </summary>
    public static bool LetGo(World world, EntityId e, EntityId attacker, long contactId)
    {
        if (!world.TryService<MovementProfiles>(out var profiles)) return false;
        var states = world.Store<MasqueradeState>();
        ref var st = ref states.Find(e);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref st) || st.Mode != MasqueradeMode.Masquerade) return false;
        st.Mode = MasqueradeMode.Unbound;
        var bodies = world.Store<Body>();
        var feet = bodies.Has(e) ? bodies.Get(e).Feet : Vector3.Zero;
        var locos = world.Store<Locomotion>();
        if (st.Profile >= 0 && st.Profile < profiles.All.Count && st.Profile != profiles.HumanIndex && locos.Has(e))
            locos.Get(e) = profiles.Numbers(st.Profile, MasqueradeMode.Unbound);
        world.Events<MasqueradeModeChanged>().Publish(new MasqueradeModeChanged(e, world.Tick, MasqueradeMode.Unbound, MasqueradeCause.Attacked, st.Place, feet));
        if (world.TryService<EventQueue<CatchLetGo>>(out var let)) let.Publish(new CatchLetGo(e, world.Tick, attacker, contactId));
        return true;
    }
}

/// <summary>The masquerade registered in one call (ours), after the movement (<see cref="MovementServices.Register"/>).</summary>
public static class MasqueradeServices
{
    /// <summary>The systems' places: just after the player's control, and first in the Perceive phase.</summary>
    public const int ModeOrder = MovementServices.ControlOrder + 10, RiskOrder = 10;

    /// <summary>
    /// Registers, in this order (the digest's): the component <see cref="MasqueradeState"/>; the events
    /// <see cref="MasqueradeModeChanged"/> and <see cref="MasqueradeRisk"/>; the services <see cref="MovementProfiles"/>
    /// (empty, for the modules to fill) and <see cref="MasqueradeSettings"/> (<paramref name="settings"/> or the
    /// defaults); the systems <c>Input/110 platform.masquerade</c> and <c>Perceive/10 platform.masquerade-risk</c>.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, MasqueradeSettings? settings = null)
    {
        b.Component<MasqueradeState>();
        b.Event<MasqueradeModeChanged>().Event<MasqueradeRisk>();
        b.Service(new MovementProfiles()).Service(settings ?? new MasqueradeSettings());
        b.System(Phase.Input, ModeOrder, "platform.masquerade", new MasqueradeSystem());
        b.System(Phase.Perceive, RiskOrder, "platform.masquerade-risk", new MasqueradeRiskSystem());
        return b;
    }
}
