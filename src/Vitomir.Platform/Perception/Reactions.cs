// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Perception;

/// <summary>
/// What the townsfolk do about what they saw (ours, the Think phase, before the
/// routes): each observer that walks routes (<see cref="RouteWalker"/>) takes the reaction its rung, its traits and the
/// act it saw call for (<see cref="Decide"/>; by design: "different kinds of actions should also elicit different
/// responses") and is sent on it through its walker's errand, so every walk keeps the route-followers' rules (no moves,
/// never over a drop). The rule, with <i>pull</i> = its curiosity (or its duty) × the act's strangeness and <i>fear</i> =
/// the act's threat × (1 − its bravery):
/// <list type="bullet">
/// <item>aware: it stops and turns to <b>look</b> at the place;</item>
/// <item>alerted: a keeper of the peace (duty at least <see cref="PerceptionNumbers.DutyAt"/>) <b>goes to look</b> at the
/// place, then <b>searches</b> about it (spots within <see cref="PerceptionNumbers.SearchRadius"/>, a pause at each) and runs
/// at the one it is wary of if it finds them in sight (alerted or alarmed); anyone else <b>edges closer</b> when the pull reaches
/// <see cref="PerceptionNumbers.LookAt"/> and the fear, <b>backs off</b> when the fear is the greater, else stands and
/// <b>watches</b>;</item>
/// <item>alarmed: a keeper of the peace <b>runs to</b> where it last saw the one who did it and, within
/// <see cref="PerceptionNumbers.ChallengeWalkMetres"/> of them and with them in sight, <b>challenges</b> (no combat yet:
/// it faces them and follows at a walk, standing within <see cref="PerceptionNumbers.ChallengeMetres"/>; it keeps
/// challenging, and runs again only when they are farther than <see cref="PerceptionNumbers.ChallengeRunMetres"/> or out
/// of sight; it searches the place if they are gone); anyone else <b>flees</b> at a run when the fear reaches
/// <see cref="PerceptionNumbers.FleeAt"/>, else stands and watches (perception has an alarmed one with a voice shout);</item>
/// <item>cooled from fleeing, it watches where it stands; cooled to unaware, each goes back to its routine.</item>
/// </list>
/// <para><b>People fight back</b> (where the world registers fights, <see cref="WorldFights"/>):
/// one <b>struck</b> (perception's <see cref="Observer.Struck"/>: a blow on its body or its shield) who can fight
/// (<see cref="WorldFights.CanFight"/>: a blade held) <b>fights</b> the one who struck it, and one who cannot <b>flees</b> from
/// them; a keeper of the peace (duty at least <see cref="PerceptionNumbers.JoinDutyAt"/>) who can fight and whom a blow it saw
/// struck at another (violence seen) has carried to rung <see cref="PerceptionNumbers.JoinRung"/> <b>joins</b>: it fights the one
/// who struck — or, when that was a keeper fighting, that keeper's foe. Fighting, the duelling brain drives it
/// (<see cref="WorldFights.Begin"/>): its routes stand aside within <see cref="PerceptionNumbers.ChaseRouteMetres"/> of its foe
/// and take it to them by a route at a run beyond. A fight ends when the foe is <b>down</b> (stopped, or its stop playing): it
/// <b>stands down</b> and watches while it calms, joining no fight over an act raised before; when the foe has been farther
/// than <see cref="PerceptionNumbers.GiveUpMetres"/> for <see cref="PerceptionNumbers.GiveUpSeconds"/>: it <b>searches</b> where it
/// last had them; the follow-up: when its foe has been out of its sight (perception's own test, on its looks) for
/// <see cref="PerceptionNumbers.GiveUpUnseenSeconds"/>: it searches where it last saw them; when it is Crippled with its bravery under <see cref="PerceptionNumbers.CrippledFleeBravery"/>, or holds no blade
/// any more: it <b>flees</b>. A blow from one fighting someone else is a stray: one who can fight lets it pass. Several may
/// fight one foe; a fighter keeps its foe.</para>
/// Each reaction begun is raised (<see cref="ReactionStarted"/>). Random spots from the world's stream
/// <c>"perception"</c>; deterministic.
/// </summary>
public sealed class ReactionSystem : ISystem
{
    /// <summary>How many spots a fleeing townsman weighs (the one farthest from the place is taken).</summary>
    const int FleeTries = 6;

    public void Run(World world)
    {
        var config = world.Service<PerceptionConfig>();
        if (config.Kinds.Count == 0) return;
        var n = config.Numbers;
        var nav = world.Service<LevelSpace>().Navigation;
        var bodies = world.Store<Body>();
        var walkers = world.Store<RouteWalker>();
        var observers = world.Store<Observer>();
        var rng = world.Random.For("perception");
        bool fights = WorldFights.Registered(world);
        var ents = observers.Entities;
        var data = observers.Data;
        float turn = n.TurnDegreesPerSecond * DetMath.DegToRad * World.TickSeconds;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref var o = ref data[i];
            if (!bodies.Has(e) || !walkers.Has(e) || o.Kind < 0 || o.Kind >= config.Kinds.Count) continue;
            ref readonly var body = ref bodies.Get(e);
            ref var w = ref walkers.Get(e);
            var role = config.RoleOf(o.Kind);
            var kind = config.Kinds[o.Kind].Numbers;
            // Struck since last tick — fight back or flee; fighting — its foe still up, near, and itself fit to fight;
            // a keeper of the peace who saw a blow struck — join.
            if (o.Struck != 0)
            {
                o.Struck = 0;
                Struck(world, ref o, ref w, e, body, role, n, nav, rng, fights);
            }
            bool handled = false;
            if (fights && o.Doing == Reaction.Fight)
            {
                Fight(world, ref o, ref w, e, body, kind, role, n, nav, rng, bodies);
                handled = true;
            }
            else if (fights && o.Doing != Reaction.Flee && Joins(world, o, e, kind, n, out var foe) && WorldFights.Begin(world, e, foe, o.Kind, FightCause.Joined))
            {
                StartFight(world, ref o, ref w, e, body, foe, role, n, nav, rng, bodies);
                handled = true;
            }
            if (!handled)
            {
                // The rung changed on the last tick (perception, after this phase, set its time on the rung to nothing).
                bool fresh = o.OnRung == 0f && o.Level != Awareness.Unaware;
                var next = Decide(o.Level, kind, config.Act(o.Act), n, o.Doing, o.SuspectUnseen, o.DoingSeconds, fresh, o.StoodDownTick != 0);
                if (next == Reaction.Routine) o.StoodDownTick = 0;     // calmed: the stand-down is forgotten
                if (next != o.Doing) Start(world, ref o, ref w, e, body, next, role, n, nav, rng);
            }
            o.DoingSeconds += World.TickSeconds;
            Steer(world, ref o, ref w, e, body, role, n, nav, rng, bodies, turn);
        }
    }

    /// <summary>How long a keeper of the peace keeps after the one it is wary of once it has lost sight of them (s), before it searches.</summary>
    public const float LostSeconds = 3f;

    /// <summary>How long a keeper of the peace searches a spot before it may run at the one it finds in sight (s).</summary>
    public const float FoundSeconds = 2f;

    /// <summary>
    /// The reaction of rung <paramref name="level"/> for an observer of <paramref name="kind"/> who saw an act taken as
    /// <paramref name="act"/>, given what it is doing (and how long), and how long since it saw the one it is wary of
    /// (<see cref="ReactionSystem"/>'s rule). <paramref name="fresh"/>: the rung has just changed (a keeper of the peace
    /// searching when it concludes, alarmed, with the one it is wary of in sight, runs at them at once). An earlier build:
    /// <paramref name="stoodDown"/>, it stood down from a fight (its foe stopped): alerted or alarmed, it watches while it
    /// calms; and a keeper of the peace fleeing (struck and unable to fight, or Crippled) goes on fleeing while alarmed.
    /// A fight is not decided here (<see cref="ReactionSystem"/>'s struck, join and fight steps).
    /// </summary>
    public static Reaction Decide(Awareness level, in ObserverNumbers kind, in ActResponse act, in PerceptionNumbers n, Reaction doing,
        float suspectUnseen = float.PositiveInfinity, float doingSeconds = 0f, bool fresh = false, bool stoodDown = false)
    {
        bool duty = kind.Duty >= n.DutyAt;
        float pull = MathF.Max(kind.Curiosity, kind.Duty) * act.Strangeness;
        float fear = act.Threat * (1f - kind.Bravery);
        bool inSight = suspectUnseen < LostSeconds;
        switch (level)
        {
            case Awareness.Unaware: return Reaction.Routine;
            case Awareness.Aware: return Reaction.Look;
            case Awareness.Alerted or Awareness.Alarmed when stoodDown: return Reaction.Watch;
            case Awareness.Alerted or Awareness.Alarmed when duty:
                // Searching, it finds them in sight (at most every FoundSeconds: one out of reach is not run at each tick).
                if (doing == Reaction.Search && suspectUnseen < 0.5f && doingSeconds > FoundSeconds) return Reaction.RunTo;
                if (level == Awareness.Alarmed)
                {
                    if (doing is Reaction.RunTo or Reaction.Challenge or Reaction.Flee) return doing;     // a keeper fleeing
                    // Searching when it concludes: at them if they are in sight, else it goes on searching.
                    if (doing == Reaction.Search) return fresh && suspectUnseen < SeenSeconds ? Reaction.RunTo : doing;
                    return Reaction.RunTo;
                }
                if (doing is Reaction.RunTo or Reaction.Challenge) return inSight ? doing : Reaction.Search;
                return doing is Reaction.GoLook or Reaction.Search ? doing : Reaction.GoLook;
            case Awareness.Alerted:
                if (doing is Reaction.BackOff or Reaction.EdgeCloser or Reaction.Watch) return doing;
                if (doing == Reaction.Flee) return Reaction.Watch;
                if (pull >= n.LookAt && pull >= fear) return Reaction.EdgeCloser;
                return fear > pull ? Reaction.BackOff : Reaction.Watch;
            default:
                if (doing == Reaction.Flee) return doing;
                return fear >= n.FleeAt ? Reaction.Flee : Reaction.Watch;
        }
    }

    static void Start(World world, ref Observer o, ref RouteWalker w, EntityId e, in Body body, Reaction next, ObserverRole role,
        in PerceptionNumbers n, LevelNavigation? nav, Rng rng)
    {
        o.Doing = next;
        o.DoingSeconds = 0f;
        w.Walking = 0;
        w.Arrived = 0;
        var place = next is Reaction.RunTo or Reaction.Challenge or Reaction.Fight ? o.SuspectPlace : o.Place;
        world.Events<ReactionStarted>().Publish(new ReactionStarted(e, world.Tick, next, place, role));
        var away = Flat(body.Feet - o.Place, body.Yaw + DetMath.Pi);
        switch (next)
        {
            case Reaction.Routine:
                w.Errand = 0;
                w.Wait = 1f;
                o.Searched = 0;
                o.Challenged = 0;
                break;
            case Reaction.Look:
            case Reaction.Watch:
                w.Errand = RouteWalker.StandErrand;
                break;
            case Reaction.BackOff:
                Send(ref o, ref w, body.Feet + Flat3(away) * n.BackOffMetres, RouteWalker.WalkErrand);
                break;
            case Reaction.EdgeCloser:
            {
                float d = Across(body.Feet, o.Place);
                if (d <= n.EdgeCloserMetres) w.Errand = RouteWalker.StandErrand;
                else Send(ref o, ref w, o.Place + Flat3(away) * n.EdgeCloserMetres, RouteWalker.WalkErrand);
                break;
            }
            case Reaction.GoLook:
                Send(ref o, ref w, o.Place, RouteWalker.WalkErrand);
                break;
            case Reaction.Search:
                o.Searched = 0;
                Send(ref o, ref w, SearchSpot(nav, o.Place, n.SearchRadius, rng), RouteWalker.WalkErrand);
                break;
            case Reaction.Flee:
                Send(ref o, ref w, FleeSpot(nav, body.Feet, o.Place, away, n.FleeMetres, rng), RouteWalker.RunErrand);
                break;
            case Reaction.RunTo:
                Send(ref o, ref w, o.SuspectPlace, RouteWalker.RunErrand);
                break;
            case Reaction.Challenge:
                o.Challenged = 1;
                w.Errand = RouteWalker.StandErrand;
                break;
            case Reaction.Fight:
                // The duelling brain drives it (put on by WorldFights.Begin); its routes stand aside until its fight step says.
                o.StoodDownTick = 0;
                w.Errand = RouteWalker.AsideErrand;
                break;
        }
    }

    static void Steer(World world, ref Observer o, ref RouteWalker w, EntityId e, in Body body, ObserverRole role, in PerceptionNumbers n,
        LevelNavigation? nav, Rng rng, Store<Body> bodies, float turn)
    {
        if (o.Doing == Reaction.Routine) return;
        bool suspectHere = !o.Suspect.IsNone && bodies.Has(o.Suspect);
        var suspectFeet = suspectHere ? bodies.Get(o.Suspect).Feet : o.SuspectPlace;
        var lookAt = o.Doing switch
        {
            Reaction.Challenge or Reaction.Fight => suspectFeet,
            Reaction.RunTo => o.SuspectPlace,
            _ => o.Place,
        };
        w.Face = Turn(body.Yaw, lookAt - body.Feet, turn);
        switch (o.Doing)
        {
            case Reaction.BackOff:
            case Reaction.EdgeCloser:
            case Reaction.Flee:
            case Reaction.GoLook:
                if (w.Errand != RouteWalker.StandErrand && w.Arrived != 0)
                {
                    if (o.Doing == Reaction.GoLook) Start(world, ref o, ref w, e, body, Reaction.Search, role, n, nav, rng);
                    else w.Errand = RouteWalker.StandErrand;
                }
                break;
            case Reaction.Search:
                if (w.Errand != RouteWalker.StandErrand && w.Arrived != 0) { w.Errand = RouteWalker.StandErrand; o.DoingSeconds = 0f; }
                else if (w.Errand == RouteWalker.StandErrand && o.DoingSeconds >= n.SearchPauseSeconds)
                {
                    o.Searched++;
                    w.Walking = 0;
                    w.Arrived = 0;
                    Send(ref o, ref w, SearchSpot(nav, o.Place, n.SearchRadius, rng), RouteWalker.WalkErrand);
                }
                break;
            case Reaction.RunTo:
                w.Goal = o.SuspectPlace;
                o.Spot = o.SuspectPlace;
                // Near them and with them in sight, it slows to a walk and challenges.
                if (suspectHere && o.SuspectUnseen < SeenSeconds && Across(body.Feet, suspectFeet) <= n.ChallengeWalkMetres && MathF.Abs(body.Feet.Z - suspectFeet.Z) < 2f)
                    Start(world, ref o, ref w, e, body, Reaction.Challenge, role, n, nav, rng);
                else if (w.Arrived != 0)
                {
                    // At the place the one it saw was last seen, and they are gone: it searches about it.
                    o.Place = o.SuspectPlace;
                    Start(world, ref o, ref w, e, body, Reaction.Search, role, n, nav, rng);
                }
                break;
            case Reaction.Challenge:
            {
                // The challenge holds: it faces them and follows at a walk, standing within ChallengeMetres; it runs
                // again only when they are farther than ChallengeRunMetres or out of sight.
                float d = Across(body.Feet, o.SuspectPlace);
                if (!suspectHere || o.SuspectUnseen >= LostSeconds || d > n.ChallengeRunMetres)
                    Start(world, ref o, ref w, e, body, Reaction.RunTo, role, n, nav, rng);
                else if (w.Errand == RouteWalker.StandErrand ? d > n.ChallengeMetres + FollowSlackMetres : d > n.ChallengeMetres)
                {
                    if (w.Errand != RouteWalker.WalkErrand) { w.Walking = 0; w.Arrived = 0; }
                    Send(ref o, ref w, o.SuspectPlace, RouteWalker.WalkErrand);
                }
                else w.Errand = RouteWalker.StandErrand;
                break;
            }
        }
    }

    // ---- people fight back ----

    /// <summary>How much farther than <see cref="PerceptionNumbers.ChaseRouteMetres"/> a fighter's foe must be before its route takes it again (m), so it does not swap between the route and the brain at every step.</summary>
    const float ChaseSlackMetres = 1f;

    /// <summary>
    /// A blow met it (perception's <see cref="Observer.Struck"/>, by <see cref="Observer.StruckBy"/>): one fighting keeps its
    /// foe; a stray blow (from one fighting someone else) is let pass by one who can fight; otherwise one who can fight fights
    /// the one who struck, and one who cannot flees from them.
    /// </summary>
    static void Struck(World world, ref Observer o, ref RouteWalker w, EntityId e, in Body body, ObserverRole role, in PerceptionNumbers n,
        LevelNavigation? nav, Rng rng, bool fights)
    {
        var by = o.StruckBy;
        o.StruckBy = EntityId.None;
        if (o.Doing == Reaction.Fight || by.IsNone || !world.Alive(by) || !world.Has<Body>(by)) return;
        bool stray = fights && WorldFights.Fighting(world, by, out var theirs) && theirs != e;
        bool can = fights && WorldFights.CanFight(world, e);
        if (stray && can) return;
        if (!stray && can && !WorldFights.Down(world, by) && WorldFights.Begin(world, e, by, o.Kind, FightCause.Struck, n.FightReactSeconds))
        {
            StartFight(world, ref o, ref w, e, body, by, role, n, nav, rng, world.Store<Body>());
            return;
        }
        o.Place = world.Get<Body>(by).Feet;
        if (o.Doing != Reaction.Flee) Start(world, ref o, ref w, e, body, Reaction.Flee, role, n, nav, rng);
    }

    /// <summary>
    /// Whether a keeper of the peace joins a fight it saw (violence: a blow struck at another), and against whom: duty at least
    /// <see cref="PerceptionNumbers.JoinDutyAt"/>, at rung <see cref="PerceptionNumbers.JoinRung"/> or above over an act of
    /// violence raised since it last stood down, able to fight; the one who struck — or, when that was a keeper fighting, its
    /// foe — up and someone else.
    /// </summary>
    static bool Joins(World world, in Observer o, EntityId e, in ObserverNumbers kind, in PerceptionNumbers n, out EntityId foe)
    {
        foe = EntityId.None;
        if (kind.Duty < n.JoinDutyAt || n.JoinRung > (float)Awareness.Alarmed || (float)o.Level < n.JoinRung || o.Act != MasqueradeAct.Violence) return false;
        if (o.StoodDownTick != 0 && o.SawActTick <= o.StoodDownTick) return false;
        var who = o.Suspect;
        if (who.IsNone || who == e) return false;
        if (WorldFights.Fighting(world, who, out var theirs)) who = theirs;     // a keeper fighting is helped, not fought
        if (who == e || WorldFights.Down(world, who) || !WorldFights.CanFight(world, e)) return false;
        foe = who;
        return true;
    }

    /// <summary>Begins a fight against <paramref name="foe"/> (its brain already put on by <see cref="WorldFights.Begin"/>): wary of them, where they stand.</summary>
    static void StartFight(World world, ref Observer o, ref RouteWalker w, EntityId e, in Body body, EntityId foe, ObserverRole role, in PerceptionNumbers n,
        LevelNavigation? nav, Rng rng, Store<Body> bodies)
    {
        o.Suspect = foe;
        o.SuspectPlace = bodies.Get(foe).Feet;
        o.SuspectUnseen = 0f;
        Start(world, ref o, ref w, e, body, Reaction.Fight, role, n, nav, rng);
    }

    /// <summary>
    /// One tick of a fight: it stands down when its foe is down, searches when its foe has been too far too long, flees when
    /// Crippled and not brave enough or when it holds no blade any more (each ends the fight: the brain taken off, the next
    /// reaction begun); otherwise its routes take it to its foe at a run when far, and stand aside for the brain when near.
    /// </summary>
    static void Fight(World world, ref Observer o, ref RouteWalker w, EntityId e, in Body body, in ObserverNumbers kind, ObserverRole role,
        in PerceptionNumbers n, LevelNavigation? nav, Rng rng, Store<Body> bodies)
    {
        var fighters = world.Store<WorldFighter>();
        ref var f = ref fighters.Find(e);
        if (Unsafe.IsNullRef(ref f) || WorldFights.Down(world, f.Foe))
        {
            // Its foe is down (or its brain was taken off): it stands down and watches.
            WorldFights.End(world, e, FightEnd.FoeDown);
            o.StoodDownTick = (int)Math.Max(1, world.Tick);
            Start(world, ref o, ref w, e, body, Reaction.Watch, role, n, nav, rng);
            return;
        }
        var foe = f.Foe;
        var foeFeet = bodies.Get(foe).Feet;
        float d = Across(body.Feet, foeFeet);
        // Out of sight too long (perception's own sight test, on its looks): it gives up and searches where it last saw them.
        f.Unseen += World.TickSeconds;
        if (Sees(world, e, body, foe, o.Kind, f.Unseen < LostSeconds)) { f.Unseen = 0f; f.SeenPlace = foeFeet; }
        else if (f.Unseen >= n.GiveUpUnseenSeconds - World.TickSeconds * 0.5f)
        {
            var seen = f.SeenPlace;
            WorldFights.End(world, e, FightEnd.GaveUp);
            o.Place = seen;
            o.SuspectPlace = seen;
            Start(world, ref o, ref w, e, body, Reaction.Search, role, n, nav, rng);
            return;
        }
        if (d <= n.GiveUpMetres) { f.LastPlace = foeFeet; f.FarSeconds = 0f; }
        else if ((f.FarSeconds += World.TickSeconds) >= n.GiveUpSeconds - World.TickSeconds * 0.5f)
        {
            // Too far too long: it gives up and searches where it last had them.
            var last = f.LastPlace;
            WorldFights.End(world, e, FightEnd.GaveUp);
            o.Place = last;
            o.SuspectPlace = last;
            Start(world, ref o, ref w, e, body, Reaction.Search, role, n, nav, rng);
            return;
        }
        bool disarmed = !WorldFights.CanFight(world, e);
        if (disarmed || kind.Bravery < n.CrippledFleeBravery && Crippled(world, e))
        {
            WorldFights.End(world, e, disarmed ? FightEnd.Disarmed : FightEnd.Crippled);
            o.Place = foeFeet;
            Start(world, ref o, ref w, e, body, Reaction.Flee, role, n, nav, rng);
            return;
        }
        o.Suspect = foe;
        o.SuspectPlace = foeFeet;
        // Its reaction (struck): it turns to face the one who struck it and does nothing more until the brain takes over.
        if (f.ReactLeft > 0f)
        {
            if ((f.ReactLeft -= World.TickSeconds) > World.TickSeconds * 0.5f) { w.Errand = RouteWalker.StandErrand; return; }
            WorldFights.Engage(world, e, o.Kind);
        }
        bool far = w.Errand == RouteWalker.RunErrand ? d > n.ChaseRouteMetres : d > n.ChaseRouteMetres + ChaseSlackMetres;
        if (f.RouteWait > 0f) f.RouteWait -= World.TickSeconds;
        // A route that found none while far (an errand "arrived" far from a goal it never reached): the brain steers straight
        // for a while before a route is asked again. With no navigation at all, the brain always steers.
        if (far && w.Errand == RouteWalker.RunErrand && w.Arrived != 0) f.RouteWait = NoRouteSeconds;
        if (far && nav is not null && f.RouteWait <= 0f)
        {
            if (w.Errand != RouteWalker.RunErrand) { w.Walking = 0; w.Arrived = 0; }
            Send(ref o, ref w, foeFeet, RouteWalker.RunErrand);
        }
        else
        {
            w.Errand = RouteWalker.AsideErrand;
            w.Walking = 0;
            w.Arrived = 0;
        }
    }

    /// <summary>How long a fighter whose route to its foe found none leaves the brain to steer straight before it asks again (s).</summary>
    const float NoRouteSeconds = 1f;

    /// <summary>
    /// Follow-up: whether fighter <paramref name="e"/> sees its foe on this tick's look — perception's own test
    /// (<see cref="PerceptionSystem.Clarity"/>, on the observer's look ticks, <see cref="PerceptionSystem.Looks"/>), with
    /// <paramref name="tracking"/> while it has them in sight; between looks it does not look, and its time unseen runs on.
    /// </summary>
    static bool Sees(World world, EntityId e, in Body body, EntityId foe, int kind, bool tracking)
    {
        var config = world.Service<PerceptionConfig>();
        if (!PerceptionSystem.Looks(e, world.Tick, config.Numbers)) return false;
        var locos = world.Store<Locomotion>();
        ref var loco = ref locos.Find(e);
        if (Unsafe.IsNullRef(ref loco)) return false;
        var (sin, cos) = DetMath.SinCos(body.Yaw);
        return PerceptionSystem.Clarity(config, world.Service<PerceptionState>().Areas, world.Service<LevelSpace>().Collision, world.Store<Body>(), locos,
            kind, body.Feet + new Vector3(0, 0, loco.EyeHeight), new Vector3(cos, sin, 0), foe, e, tracking) > 0f;
    }

    /// <summary>Whether <paramref name="e"/>'s Health is in the Crippled band (the module's <see cref="Powers.Vitals"/>; false without them).</summary>
    static bool Crippled(World world, EntityId e)
    {
        if (!world.TryStore<Powers.Vitals>(out var vitals)) return false;
        ref var v = ref vitals.Find(e);
        return !Unsafe.IsNullRef(ref v) && v.Band == HealthBand.Crippled;
    }

    /// <summary>How recently a keeper of the peace must have seen the one it runs at to stop and challenge them (s).</summary>
    const float SeenSeconds = 0.5f;

    /// <summary>How much farther than <see cref="PerceptionNumbers.ChallengeMetres"/> the one it challenges may be before a standing keeper of the peace walks after them (m), so it does not start and stop at every step.</summary>
    const float FollowSlackMetres = 1f;

    static void Send(ref Observer o, ref RouteWalker w, Vector3 spot, int errand)
    {
        o.Spot = spot;
        w.Goal = spot;
        w.Errand = errand;
    }

    /// <summary>A spot about <paramref name="place"/> within <paramref name="radius"/> that can be walked to, or the place itself.</summary>
    static Vector3 SearchSpot(LevelNavigation? nav, Vector3 place, float radius, Rng rng) =>
        nav is not null && nav.RandomReachable(place, radius, rng, out var spot) ? spot : place;

    /// <summary>
    /// Where a townsman flees from <paramref name="place"/>: of <see cref="FleeTries"/> spots it can reach within
    /// <paramref name="metres"/>, the one farthest from the place — if it is farther than where it stands —, else
    /// <paramref name="metres"/> straight away from it.
    /// </summary>
    static Vector3 FleeSpot(LevelNavigation? nav, Vector3 feet, Vector3 place, Vector2 away, float metres, Rng rng)
    {
        var straight = feet + new Vector3(away, 0) * metres;
        if (nav is null) return straight;
        float best = Across(feet, place) + 3f;
        var spot = straight;
        bool found = false;
        for (int k = 0; k < FleeTries; k++)
        {
            if (!nav.RandomReachable(feet, metres, rng, out var p)) continue;
            float d = Across(p, place);
            if (d > best) { best = d; spot = p; found = true; }
        }
        return found ? spot : straight;
    }

    static float Across(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    /// <summary>The flat unit direction of <paramref name="v"/>, or of <paramref name="yaw"/> when it has none.</summary>
    static Vector2 Flat(Vector3 v, float yaw)
    {
        var f = new Vector2(v.X, v.Y);
        float l = f.Length();
        if (l > 1e-3f) return f / l;
        var (s, c) = DetMath.SinCos(yaw);
        return new Vector2(c, s);
    }

    static Vector3 Flat3(Vector2 v) => new(v, 0);

    /// <summary><paramref name="yaw"/> turned towards <paramref name="to"/> by at most <paramref name="most"/> (radians).</summary>
    static float Turn(float yaw, Vector3 to, float most)
    {
        if (to.X * to.X + to.Y * to.Y < 1e-6f) return yaw;
        float d = DetMath.WrapAngle(DetMath.Atan2(to.Y, to.X) - yaw);
        return DetMath.WrapAngle(yaw + Math.Clamp(d, -most, most));
    }
}
