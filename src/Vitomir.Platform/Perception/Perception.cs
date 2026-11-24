// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Perception;

/// <summary>What an observer is to the town: a townsman, who looks and flees, or a keeper of the peace (a guard, a watchman: <see cref="ObserverNumbers.Duty"/> at least <see cref="PerceptionNumbers.DutyAt"/>), who goes to look and acts.</summary>
public enum ObserverRole
{
    Townsman = 0,
    Guard = 1,
}

/// <summary>
/// An observer's awareness of something supernatural (its ladder): <b>unaware</b>;
/// <b>aware</b>, "the person has to process and start to understand what they're seeing"; <b>alerted</b>, "a reasonable
/// idea of what they're looking at"; <b>alarmed</b>, "they've fully concluded what they're looking at, and it's not
/// normal or right".
/// </summary>
public enum Awareness
{
    Unaware = 0,
    Aware = 1,
    Alerted = 2,
    Alarmed = 3,
}

/// <summary>Why an observer's awareness changed: it saw an act, it heard a shout, or nothing more was seen and it cooled.</summary>
public enum AwarenessCause
{
    Seen = 0,
    Heard = 1,
    Cooled = 2,
    /// <summary>It was struck (a blow met its body or its shield): alarmed at once, about the one who struck.</summary>
    Struck = 3,
}

/// <summary>
/// What an observer does about what it saw (ours, the townsfolk's routines, simple):
/// its <b>routine</b> (wandering, or standing at its post); aware, it stops and <b>looks</b>; alerted, a townsman
/// <b>backs off</b> or <b>edges closer</b> by its nerve and afterwards <b>watches</b>, a guard <b>goes to look</b> and
/// then <b>searches</b> about the place; alarmed, a townsman <b>flees</b> shouting, a guard <b>runs to</b> where it last
/// saw the one who did it and <b>challenges</b> them. one who can fight <b>fights</b> the one who struck it, or (a keeper
/// of the peace) the one it saw strike another (<see cref="ReactionSystem"/>; the duelling brain on it, <see cref="Combat.WorldFights"/>).
/// </summary>
public enum Reaction
{
    Routine = 0,
    Look = 1,
    BackOff = 2,
    EdgeCloser = 3,
    Watch = 4,
    GoLook = 5,
    Search = 6,
    Flee = 7,
    RunTo = 8,
    Challenge = 9,
    /// <summary>It fights the one who struck it (or whom it saw strike another): the duelling brain drives it, its routes stand aside.</summary>
    Fight = 10,
}

/// <summary>
/// The numbers and traits of one kind of observer (ours; by design:
/// "each person will have a number of traits like visual acuity, bravery, if their job is to patrol and keep safe, and so
/// on"; SANDBOX until the rules package prices them). Traits run 0 to 1 unless said.
/// <list type="bullet">
/// <item><see cref="SightRange"/> (m, in daylight, open ground, a running body), <see cref="ConeDegrees"/> (the view's half-angle);</item>
/// <item><see cref="Eyesight"/>, sharp or poor (scales the range), and <see cref="NightEyes"/>, how much of the dark its eyes
/// win back (<see cref="PerceptionNumbers.NightEyesWeight"/>);</item>
/// <item><see cref="Attention"/>, alert or absorbed (drunk): scales what it takes in; <see cref="Excitability"/>, how fast its
/// awareness climbs toward alarm;</item>
/// <item><see cref="Bravery"/>, approach or back off and flee; <see cref="Curiosity"/>, goes to look at strange things;
/// <see cref="Duty"/>, keeping the peace is its job (investigate, challenge, rouse others, never flee);
/// <see cref="Voice"/>, whether it shouts and how far (× <see cref="PerceptionNumbers.ShoutRadius"/>);</item>
/// <item>how long each step up the ladder takes ("it might take some relative time for the observer to
/// make out what they're looking at then make a decision about it"): <see cref="NoticeSeconds"/> unaware to aware (turning
/// to look), <see cref="MakeOutSeconds"/> aware to alerted (making out what it is), <see cref="ConcludeSeconds"/> alerted to
/// alarmed (concluding it is not right) — each waited on the rung below while the evidence is at or over the next rung's
/// threshold;</item>
/// <item>how long it takes to calm down ("a cool-down runs while you are not seen"): <see cref="HoldSeconds"/>, how long
/// the evidence holds after the last sight of anything, and <see cref="CoolDownSeconds"/>, how long it then takes to fall
/// from the ceiling to nothing.</item>
/// </list>
/// Plain floats, read from a module's file by their names (<see cref="NumberFields{T}"/>).
/// </summary>
public struct ObserverNumbers
{
    public float SightRange;
    public float ConeDegrees;
    public float Eyesight;
    public float NightEyes;
    public float Attention;
    public float Bravery;
    public float Curiosity;
    public float Duty;
    public float Excitability;
    public float Voice;
    public float NoticeSeconds;
    public float MakeOutSeconds;
    public float ConcludeSeconds;
    public float HoldSeconds;
    public float CoolDownSeconds;

    /// <summary>An ordinary townsman by day (the enhanced module's <c>townsman</c>, SANDBOX).</summary>
    public static ObserverNumbers Ordinary => new()
    {
        SightRange = 25f, ConeDegrees = 60f, Eyesight = 0.8f, NightEyes = 0.2f, Attention = 0.7f, Bravery = 0.4f, Curiosity = 0.3f,
        Duty = 0.1f, Excitability = 0.5f, Voice = 0.8f,
        NoticeSeconds = 0.6f, MakeOutSeconds = 2.5f, ConcludeSeconds = 3f, HoldSeconds = 20f, CoolDownSeconds = 150f,
    };

    /// <summary>The wait on rung <paramref name="from"/> before the step up from it (s): notice, make out, conclude; 0 from alarmed.</summary>
    public readonly float StepSeconds(Awareness from) => from switch
    {
        Awareness.Unaware => NoticeSeconds,
        Awareness.Aware => MakeOutSeconds,
        Awareness.Alerted => ConcludeSeconds,
        _ => 0f,
    };
}

/// <summary>
/// One kind of observer as a module's data gives it (ours): its id (<c>enhanced:perception/guard</c>), a line for
/// people, the 1.1 characters that are of this kind (by name: <c>OldMan</c>, <c>rudolfGuard</c>, <c>Civ07drunk</c>), its
/// numbers and traits, and whether they are research (SANDBOX) values.
/// <para><see cref="Duel"/>, how one of this kind fights when it does (the duelling brain's settings, the kind's
/// file's <c>duel</c>; null for the brain's defaults).</para>
/// </summary>
public sealed record ObserverKind(AssetId Id, string Title, IReadOnlyList<string> Characters, ObserverNumbers Numbers, bool Sandbox,
    Combat.DuelBrainSettings? Duel = null)
{
    /// <summary>The kind's short name, the last part of its id's path (<c>guard</c>), as <c>--witness</c> takes it.</summary>
    public string Name => Id.Path[(Id.Path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// How a kind of act is taken (ours: different kinds of actions elicit different responses and
/// levels of curiosity or alarm): its <see cref="Strangeness"/> (the curiosity it stirs; it scales the awareness it raises),
/// its <see cref="Threat"/> (the fear it stirs) and the <see cref="Highest"/> rung it raises an observer to by itself (a
/// shout or a second act may take it further).
/// </summary>
public readonly record struct ActResponse(float Strangeness, float Threat, Awareness Highest);

/// <summary>
/// Perception's numbers for the whole world (ours, SANDBOX). How visible a body is:
/// the light (by day or by night, or an area's own), its concealment (an area's), its motion and how much of it shows
/// past cover; the ladder; the shout; the areas' heat; the reaction rule; the reactions' distances and pace. Plain floats
/// by name (<see cref="NumberFields{T}"/>); a module's file gives every one.
/// </summary>
public struct PerceptionNumbers
{
    /// <summary>The light everywhere by day and by night, 0 (dark) to 1 (daylight), where no area says its own.</summary>
    public float DayLight, NightLight;
    /// <summary>How much of the dark an observer with perfect night eyes wins back (a share).</summary>
    public float NightEyesWeight;
    /// <summary>How much a body's motion draws the eye: standing still, crouched and moving, walking, running (or in the air), sprinting.</summary>
    public float MotionStill, MotionCrouched, MotionWalk, MotionRun, MotionSprint;
    /// <summary>The least share of a body's points (head, chest, hips, knees) in clear sight for it to be seen at all.</summary>
    public float CoverFloor;
    /// <summary>How often an observer looks (s; each a little later than the last, so not all on one tick).</summary>
    public float LookSeconds;
    /// <summary>The awareness at which an observer is aware, alerted and alarmed, and the most it holds.</summary>
    public float AwareAt, AlertedAt, AlarmedAt, Ceiling;
    /// <summary>The least time a rung is kept before it steps down (s; so nobody flicks between rungs).</summary>
    public float MinRungSeconds;
    /// <summary>Awareness gained per second seeing an act of a human's size at point-blank range, all of the body showing, by an attentive observer of middling excitability.</summary>
    public float GainPerSecond;
    /// <summary>An act counts as big as its measure over a human's limit, up to this many times.</summary>
    public float BiggestAct;
    /// <summary>How long an act stays in view after it is raised (s), at least; a jump while its body is in the air, a speed while it lasts.</summary>
    public float ActSeconds;
    /// <summary>How far a shout of a full voice is heard (m).</summary>
    public float ShoutRadius;
    /// <summary>The heat an alarm adds to its area (heat runs 0 to 1), how long a full heat takes to cool (s), how much faster it cools while the player is not in the area, and how much a full heat lowers the observers' thresholds there (a share).</summary>
    public float HeatPerAlarm, HeatCoolSeconds, HeatAwayFactor, HeatWariness;
    /// <summary>The reaction rule: an observer of at least <see cref="DutyAt"/> duty keeps the peace; one whose curiosity (or duty) × the act's strangeness reaches <see cref="LookAt"/> goes to look; an alarmed one whose fear (the act's threat × (1 − bravery)) reaches <see cref="FleeAt"/> flees.</summary>
    public float DutyAt, LookAt, FleeAt;
    /// <summary>How far a fearful observer backs off (m), how near a curious one edges (m), how far a fleeing one runs (m).</summary>
    public float BackOffMetres, EdgeCloserMetres, FleeMetres;
    /// <summary>How far round the place a keeper of the peace searches (m), and how long it stands at each spot it searches (s).</summary>
    public float SearchRadius, SearchPauseSeconds;
    /// <summary>How near a keeper of the peace comes to the one it challenges (m): it stands within this, and walks after them beyond it.</summary>
    public float ChallengeMetres;
    /// <summary>
    /// The challenge that holds: a keeper of the peace running at the one it is wary of slows to a walk and
    /// challenges within <see cref="ChallengeWalkMetres"/> of them, in sight; challenging, it faces them and follows at a
    /// walk, and runs again only when they are farther than <see cref="ChallengeRunMetres"/> or out of sight (m).
    /// </summary>
    public float ChallengeWalkMetres, ChallengeRunMetres;
    /// <summary>How fast an observer turns to look (degrees a second).</summary>
    public float TurnDegreesPerSecond;
    /// <summary>
    /// People fight back (<see cref="ReactionSystem"/>): a keeper of the peace whose duty reaches <see cref="JoinDutyAt"/>
    /// and who can fight joins a fight it only saw — a blow struck at another (violence seen) — once that sight has carried it
    /// to rung <see cref="JoinRung"/> (2 alerted, 3 alarmed; 4 or more never).
    /// </summary>
    public float JoinDutyAt, JoinRung;
    /// <summary>A fighter whose foe stays farther than <see cref="GiveUpMetres"/> for <see cref="GiveUpSeconds"/> gives up and searches where it last had them (m, s).</summary>
    public float GiveUpMetres, GiveUpSeconds;
    /// <summary>A fighter Crippled (its Health's band) whose bravery is below this flees from its foe.</summary>
    public float CrippledFleeBravery;
    /// <summary>A fighter farther than this from its foe runs to them by a route (round walls); nearer, the duelling brain steers it (m).</summary>
    public float ChaseRouteMetres;
    /// <summary>
    /// Follow-up: how long one struck takes to answer before its duelling brain takes over (s): its reaction is a
    /// fight at once, but it only turns to face the one who struck it meanwhile. Ours; to be read from Wits later. A keeper
    /// joining on sight or on a shout keeps its own timing (the ladder's wait), with no delay of this.
    /// </summary>
    public float FightReactSeconds;
    /// <summary>Follow-up: a fighter whose foe has been out of its sight this long (s; perception's own sight test) gives up and searches where it last saw them.</summary>
    public float GiveUpUnseenSeconds;

    /// <summary>The enhanced module's numbers (SANDBOX), for tests and for a world no module gave any.</summary>
    public static PerceptionNumbers Sandbox => new()
    {
        DayLight = 1f, NightLight = 0.3f, NightEyesWeight = 0.5f,
        MotionStill = 0.5f, MotionCrouched = 0.6f, MotionWalk = 0.8f, MotionRun = 1f, MotionSprint = 1.3f,
        CoverFloor = 0.2f, LookSeconds = 0.15f,
        AwareAt = 0.1f, AlertedAt = 0.4f, AlarmedAt = 1f, Ceiling = 1.5f, MinRungSeconds = 10f, GainPerSecond = 4f, BiggestAct = 4f, ActSeconds = 1f,
        ShoutRadius = 15f, HeatPerAlarm = 1f, HeatCoolSeconds = 180f, HeatAwayFactor = 3f, HeatWariness = 0.5f,
        DutyAt = 0.5f, LookAt = 0.3f, FleeAt = 0.15f,
        BackOffMetres = 4f, EdgeCloserMetres = 6f, FleeMetres = 25f, SearchRadius = 5f, SearchPauseSeconds = 1.5f,
        ChallengeMetres = 2.5f, ChallengeWalkMetres = 6f, ChallengeRunMetres = 10f, TurnDegreesPerSecond = 240f,
        JoinDutyAt = 0.5f, JoinRung = 2f, GiveUpMetres = 25f, GiveUpSeconds = 8f, CrippledFleeBravery = 0.8f, ChaseRouteMetres = 4f,
        FightReactSeconds = 0.3f, GiveUpUnseenSeconds = 8f,
    };

    /// <summary>The threshold of rung <paramref name="rung"/> (× <paramref name="wary"/>, the area's heat): 0 for unaware.</summary>
    public readonly float Threshold(Awareness rung, float wary = 1f) => rung switch
    {
        Awareness.Aware => AwareAt * wary,
        Awareness.Alerted => AlertedAt * wary,
        Awareness.Alarmed => AlarmedAt * wary,
        _ => 0f,
    };

    /// <summary>The enhanced module's act table (SANDBOX): a leap very strange and somewhat threatening; a burst of speed strange but little threat, never past alerted by itself; a fall landed unharmed and a move beyond a human's strange and threatening; violence little strange and very threatening.</summary>
    public static ActResponse[] SandboxActs =>
    [
        new(0.9f, 0.5f, Awareness.Alarmed),   // Jump
        new(0.6f, 0.15f, Awareness.Alerted),  // Speed
        new(0.7f, 0.3f, Awareness.Alarmed),   // Fall
        new(0.8f, 0.4f, Awareness.Alarmed),   // Move
        new(0.3f, 0.9f, Awareness.Alarmed),   // Violence: ordinary, not strange; very threatening
    ];
}

/// <summary>
/// A struct's public <c>float</c> fields by their names (ours, as <see cref="LocomotionFields"/> does for the
/// movement numbers), so a module's file names each as the struct does and a field added later is wanted from every
/// file at once. For configuration only, never during a tick.
/// </summary>
public static class NumberFields<T> where T : struct
{
    static readonly FieldInfo[] Fields =
        [.. typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(float)).OrderBy(f => f.MetadataToken)];

    /// <summary>The names, in the struct's order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Fields.Select(f => f.Name)];

    public static bool Has(string name) => Array.FindIndex(Fields, f => f.Name == name) >= 0;

    public static float Get(in T numbers, string name) => (float)(Find(name).GetValue(numbers) ?? 0f);

    public static T With(in T numbers, string name, float value)
    {
        object boxed = numbers;
        Find(name).SetValue(boxed, value);
        return (T)boxed;
    }

    /// <summary>The numbers in words, <c>SightRange 25, ConeDegrees 60, …</c>.</summary>
    public static string Words(in T numbers)
    {
        var n = numbers;
        return string.Join(", ", Fields.Select(f => $"{f.Name} {((float)(f.GetValue(n) ?? 0f)).ToString("0.###", CultureInfo.InvariantCulture)}"));
    }

    static FieldInfo Find(string name) =>
        Array.Find(Fields, f => f.Name == name) ?? throw new ArgumentException($"{typeof(T).Name} has no number '{name}'");
}

/// <summary>
/// The game's perception (ours): the kinds of observer the modules give (in load
/// order), the world's numbers, the act table, whether it is night (the session's time of day, on a recording's head),
/// and the kind a 1.1 character is (<see cref="KindOf"/>). A service of every world that
/// registers perception (<see cref="PerceptionServices.Register"/>); configuration, not state — each observer's state is
/// its <see cref="Observer"/>, which the world digests. With no kinds given, perception does nothing.
/// </summary>
public sealed class PerceptionConfig
{
    readonly List<ObserverKind> kinds = [];
    float[] cosHalf = [];

    /// <summary>Every kind, in the order added.</summary>
    public IReadOnlyList<ObserverKind> Kinds => kinds;

    /// <summary>The world's numbers (<see cref="PerceptionNumbers.Sandbox"/> until a module sets its own).</summary>
    public PerceptionNumbers Numbers { get; private set; } = PerceptionNumbers.Sandbox;

    /// <summary>How each kind of act is taken (<see cref="ActResponse"/>), by <see cref="MasqueradeAct"/>.</summary>
    public ActResponse[] Acts { get; } = PerceptionNumbers.SandboxActs;

    /// <summary>Whether it is night (<c>--time night</c>, the default): the light where no area says its own is <see cref="PerceptionNumbers.NightLight"/>, else <see cref="PerceptionNumbers.DayLight"/>.</summary>
    public bool Night { get; set; } = true;

    /// <summary>The light where no area says its own: <see cref="LightLevel"/> when given, else the time's.</summary>
    public float Light => LightLevel ?? (Night ? Numbers.NightLight : Numbers.DayLight);

    /// <summary>A light level 0 to 1 in place of the time's (a scenario's <c>light</c>, <see cref="LightSchedule"/>), or null for the time's.</summary>
    public float? LightLevel { get; set; }

    /// <summary>How act <paramref name="act"/> is taken.</summary>
    public ActResponse Act(MasqueradeAct act) => (uint)act < (uint)Acts.Length ? Acts[(int)act] : new ActResponse(1f, 1f, Awareness.Alarmed);

    /// <summary>Whether the numbers are research (SANDBOX) values.</summary>
    public bool Sandbox { get; private set; } = true;

    /// <summary>The kind a character no kind names is (its short name or id), or null: such a character observes nothing.</summary>
    public string? DefaultKind { get; set; }

    /// <summary>Sets the world's numbers.</summary>
    public void Set(in PerceptionNumbers numbers, bool sandbox)
    {
        Numbers = numbers;
        Sandbox = sandbox;
    }

    /// <summary>Adds a kind (an id added twice is refused) and answers its index.</summary>
    public int Add(ObserverKind kind)
    {
        if (kinds.Any(k => k.Id == kind.Id)) throw new InvalidOperationException($"observer kind {kind.Id} is added twice");
        kinds.Add(kind);
        cosHalf = [.. kinds.Select(k => DetMath.Cos(MathF.Min(180f, MathF.Max(0f, k.Numbers.ConeDegrees)) * DetMath.DegToRad))];
        return kinds.Count - 1;
    }

    /// <summary>The index of the kind named <paramref name="nameOrId"/> (its short name or its whole id), or −1.</summary>
    public int IndexOf(string nameOrId)
    {
        for (int i = 0; i < kinds.Count; i++) if (kinds[i].Id.ToString() == nameOrId) return i;
        for (int i = 0; i < kinds.Count; i++) if (kinds[i].Name == nameOrId) return i;
        return -1;
    }

    /// <summary>The kind of the 1.1 character <paramref name="character"/> (a catalogue name): the first kind naming it, else <see cref="DefaultKind"/>, else −1.</summary>
    public int KindOf(string character)
    {
        for (int i = 0; i < kinds.Count; i++)
            foreach (var c in kinds[i].Characters)
                if (c.Equals(character, StringComparison.OrdinalIgnoreCase)) return i;
        return DefaultKind is { } d ? IndexOf(d) : -1;
    }

    /// <summary>The names, for a message: <c>oldman, townsman, guard</c>.</summary>
    public string Names => kinds.Count == 0 ? "none" : string.Join(", ", kinds.Select(k => k.Name));

    /// <summary>The cosine of kind <paramref name="kind"/>'s half-angle (worked out when it was added).</summary>
    public float CosHalf(int kind) => cosHalf[kind];

    /// <summary>
    /// How far kind <paramref name="kind"/> sees a body in light <paramref name="light"/> (m), before the body's concealment
    /// and motion: its daylight range × its eyesight × the light as its eyes take it — the light, and of the dark the share
    /// its night eyes win back (× <see cref="PerceptionNumbers.NightEyesWeight"/>).
    /// </summary>
    public float Sight(int kind, float light)
    {
        var k = kinds[kind].Numbers;
        float seen = light + (1f - light) * k.NightEyes * Numbers.NightEyesWeight;
        return k.SightRange * k.Eyesight * seen;
    }

    /// <summary>The role of kind <paramref name="kind"/>: a keeper of the peace when its duty reaches <see cref="PerceptionNumbers.DutyAt"/>.</summary>
    public ObserverRole RoleOf(int kind) => kinds[kind].Numbers.Duty >= Numbers.DutyAt ? ObserverRole.Guard : ObserverRole.Townsman;
}

/// <summary>
/// One observer (ours, a component on every townsperson): its kind (an index of <see cref="PerceptionConfig.Kinds"/>),
/// its rung of the ladder and the evidence under it (<see cref="Value"/>), how long it has been on its rung
/// (<see cref="OnRung"/>) and how long the evidence has been at or over the next rung's threshold there (<see cref="Climb"/>,
/// The step up waits its kind's time), how long since it last saw anything (<see cref="Unseen"/>: the evidence holds,
/// then cools), what it last saw and where (<see cref="Place"/>: the feet of the one who did it), who that was and where it last
/// saw them (<see cref="SuspectPlace"/>, <see cref="SuspectUnseen"/> ago), the area it stands in, whether it has shouted in this alarm, and its reaction
/// (<see cref="Doing"/>, how long it has done it, the spot it walks to, how many spots it has searched, whether it has
/// challenged). when it last stood down from a fight (<see cref="StoodDownTick"/>) and when the last act it saw was
/// raised (<see cref="SawActTick"/>). Plain four-byte fields, digested.
/// </summary>
public struct Observer
{
    public int Kind;
    public Awareness Level;
    public float Value;
    public float OnRung, Climb, Unseen;
    public Vector3 Place;
    public MasqueradeAct Act;
    public TraversalKind Move;
    public EntityId Suspect;
    public Vector3 SuspectPlace;
    /// <summary>How long since it last saw the one it is wary of (s).</summary>
    public float SuspectUnseen;
    public int Area;
    /// <summary>1 when its last look saw an act, or the one it is wary of (its evidence holds while it sees, and for its kind's hold after).</summary>
    public int Seeing;
    public int Shouted;
    public Reaction Doing;
    public float DoingSeconds;
    public Vector3 Spot;
    public int Searched, Challenged;
    /// <summary>
    /// The tick it stood down from a fight (its foe stopped), 0 for never. Stood down, it watches while it calms (alerted
    /// or alarmed) and joins no fight over an act raised before then.
    /// </summary>
    public int StoodDownTick;
    /// <summary>The tick the last act it saw was raised (an act seen again keeps its tick).</summary>
    public int SawActTick;
    /// <summary>
    /// 1 when a blow met its body or its shield since its reactions last looked (perception marks it, with who struck,
    /// <see cref="StruckBy"/>; its reactions answer it — fight back or flee — and clear it).
    /// </summary>
    public int Struck;
    public EntityId StruckBy;

    /// <summary>A new observer of kind <paramref name="kind"/>, unaware, about its routine.</summary>
    public static Observer Of(int kind) => new() { Kind = kind, Suspect = EntityId.None, StruckBy = EntityId.None };
}

/// <summary>An observer's awareness changed: who, when, from and to, why, the act and where it was (or where the shout said), and the value now.</summary>
public readonly record struct AwarenessChanged(EntityId Observer, long Tick, Awareness From, Awareness To, AwarenessCause Cause, MasqueradeAct Act, TraversalKind Move, Vector3 Where, float Value);

/// <summary>An alarmed townsman shouted: who, when, where it stood, the place it shouts about, how far it is heard, and how many heard it.</summary>
public readonly record struct ShoutRaised(EntityId Who, long Tick, Vector3 Where, Vector3 About, float Radius, int Heard);

/// <summary>An observer began a reaction: who, when, what, the place it is about (for a guard's run and challenge, where it last saw the one who did it), and its role.</summary>
public readonly record struct ReactionStarted(EntityId Who, long Tick, Reaction Doing, Vector3 Place, ObserverRole Role);

/// <summary>An area's heat rose (an alarm in it): which area (an index of <see cref="LevelAreas"/>), when, the heat now (0 to 1), and where the alarm was.</summary>
public readonly record struct AreaHeatRose(int Area, long Tick, float Heat, Vector3 Where);

/// <summary>An area's heat has cooled to nothing: which, when, and how long it was hot (s).</summary>
public readonly record struct AreaHeatGone(int Area, long Tick, float Seconds);
