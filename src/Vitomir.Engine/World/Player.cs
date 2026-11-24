// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Someone to be.
//
// The player is a thing like any other — the chronicle's metafile names its template
// (`PLAYER Christof`), and that template carries a model, a motion set, a radius and the walk and
// run speeds the character actually moves at. What makes it the player is that input drives it
// instead of a script.
//
// Walking is done on the level's walkable ground (<see cref="WalkMap"/>) rather than against the
// level's geometry: the compiler already worked out which surfaces a person can stand on and which
// of them touch, so following that graph gives floors, stairs and doorways for free. Colliding
// with walls that are not the edge of walkable ground, and with other things, is the collision
// phase and is not this.
//
// There are two ways to drive the character. **Click-to-move** is the original's: a point and a
// piece of ground go to <see cref="WalkTo"/>, which searches the path graph and leaves a route
// behind, and <see cref="Follow"/> then walks it a leg at a time the way the engine's walker does.
// **Free movement** is ours — <see cref="Move"/>, driven by the keyboard — and it cancels any route
// the moment it is used, because two things steering one character is how a walker gets stuck.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>The player character: where it is, what it is doing, and what it is standing on.</summary>
public sealed class Player
{
    public required WorldThing Thing { get; init; }
    public required WalkMap Map { get; init; }
    /// <summary>What is solid here, which is what keeps the character out of walls.</summary>
    public Collision? Solid { get; init; }
    /// <summary>The things here with a body, which is what keeps the character out of the barrels.</summary>
    public Bodies? Props { get; init; }
    /// <summary>The level's path graph, which is what click-to-move searches.</summary>
    public PathFinder? Path { get; init; }
    /// <summary>The animations it plays from, when its model has a skeleton and a motion set.</summary>
    public MotionSets? Motions { get; init; }

    /// <summary>Which piece of walkable ground it is standing on, or −1 when it is nowhere.</summary>
    public int Node { get; private set; } = -1;
    /// <summary>
    /// How fast the character has been *told* to move, in units a second — the engine's own
    /// the walker's speed, which is what its step distance and its animation are both taken from.
    /// Sliding along a wall does not change it; see <see cref="Achieved"/>.
    /// </summary>
    public float Speed { get; private set; }

    /// <summary>
    /// Walked by a player's keyboard from another machine (`GameServer.Steer`, ours): its follow, which has no route, leaves the
    /// walk to the push (<see cref="Follow"/>).
    /// </summary>
    public bool Driven { get; set; }

    /// <summary>
    /// How far the character actually got last step, as a speed. Ours, for the walk reports: a
    /// walker grinding along a wall covers less ground than it was told to, and this is the number
    /// that says so. The engine keeps no such field.
    /// </summary>
    public float Achieved { get; private set; }

    /// <summary>
    /// Whether the character is wounded, which is what makes it <c>limp</c> instead of walking and
    /// puts a head model on <c>head_hurt</c>. The engine reads bit 1 of a word at
    /// the same flag word for both (the body's locomotion and the head's). That word is the
    /// second actor flags word and the bit is <c>THING_AF2_LIMPING</c>, which the effect recompute
    /// keeps up to date (<c>EffectRuntime.Recompute</c>: MOVESPEED at or under 0.8). A thing with
    /// no actor never limps.
    /// </summary>
    public bool Hurt => Thing.Actor?.Has2(Actor.Flag2.Limping) == true;
    /// <summary>Whether it is running rather than walking.</summary>
    public bool Running { get; set; }

    /// <summary>
    /// A speed the caller named for this route, overriding the walk/run pair until the route ends.
    /// See <see cref="WalkTo"/>; zero when nobody named one.
    /// </summary>
    float commanded;
    /// <summary>True when the last move was stopped or had to slide along something.</summary>
    public bool Blocked { get; private set; }

    public Vector3 Position => Thing.Position;
    /// <summary>Which way it is facing: the thing's own heading about +Z, in degrees.</summary>
    public float Heading
    {
        get => Thing.Orientation.Z;
        set => Thing.Orientation = Thing.Orientation with { Z = Wrap(value) };
    }

    /// <summary>The direction it faces, as a unit vector. A thing with no turn on it faces +Y.</summary>
    public Vector2 Facing
    {
        get
        {
            float r = Heading * (MathF.PI / 180f);
            return new Vector2(-MathF.Sin(r), MathF.Cos(r));
        }
    }

    /// <summary>
    /// How fast the character turns to face where the keyboard is pushing it, in degrees a second.
    /// Driving only: a route turns the character all at once at the start of each leg, as 1.1 does
    /// (<see cref="Aim"/>).
    /// </summary>
    public float TurnRate { get; set; } = 540f;

    /// <summary>How wide it is, which is what keeps it out of walls. Christof's is 32.</summary>
    public float Radius => Thing.Radius;

    /// <summary>Its own speeds, from the template (Christof walks at 115 and runs at 250).</summary>
    public float WalkSpeed => Thing.Actor?.WalkSpeed > 0 ? Thing.Actor.WalkSpeed : Rules.GameRules.Current.Movement.DefaultWalkRunSpeed[0];
    public float RunSpeed => Thing.Actor?.RunSpeed > 0 ? Thing.Actor.RunSpeed : Rules.GameRules.Current.Movement.DefaultWalkRunSpeed[1];

    /// <summary>What a character with no speeds of its own gets — the `_actor` template's.</summary>
    public const float DefaultWalkSpeed = 150f, DefaultRunSpeed = 250f;

    /// <summary>How high the eye sits above the character's feet.</summary>
    public const float EyeHeight = Location.EyeHeight;

    /// <summary>
    /// Finds the ground under where it is and remembers what that was. With
    /// <paramref name="snap"/> it also drops the character onto that ground, which is what walking
    /// wants; without it, only the record is refreshed and <b>the character is left exactly where
    /// it is</b>.
    ///
    /// That is the engine's own argument, and it is load-bearing: the ground test tests it first thing and
    /// jumps clean over the height calculation when it is set (to a tail that does no more than
    /// re-find the node under the feet). <c>TeleportPartyToConversation</c> passes 1, and
    /// so has to here — a conversation start may be somewhere the walk map does not reach, and the
    /// convent's is: it puts Christof <b>14 units above the floor, in his bed</b>. Snapping him to
    /// the walk map sank him into the mattress.
    /// </summary>
    public bool Ground(bool snap = true)
    {
        Node = Map.Find(Thing.Position);
        if (Node < 0) return false;
        var p = Thing.Position;
        if (snap) Thing.Position = new Vector3(p.X, p.Y, Map.Nodes[Node].Height(p.X, p.Y));
        long was = Thing.Sector;
        // The sector of the feet, from the one the thing is in.
        Sector = SectorSearch.Of(Solid, Thing.Position, (int)was);
        Thing.Sector = Sector;
        Props?.Moved(Thing, was, Sector);
        Thing.Place?.Regions?.Moved(Thing, was);
        return true;
    }

    /// <summary>
    /// A walker newly made for its thing: on the ground it stands on — unless the thing came into
    /// the world at run time (<see cref="WorldThing.Unsurfaced"/>), which the engine leaves on no
    /// surface at all until its first step finds one. Its sector is known either way.
    /// </summary>
    public void Settle()
    {
        if (!Thing.Unsurfaced) { Ground(); return; }
        Ground(snap: false);
        Node = -1;
    }

    /// <summary>
    /// Which sector it is standing in — the sector of its **feet**, as `SetPosition` finds it
    /// (<see cref="SectorSearch"/>), which is the thing's own sector:
    /// what it is lit from, and where the body tests start looking. Earlier, this was
    /// the sector of a point 16 units up (ours), which put a character standing where a sector ends a
    /// few units over the floor into the one above — the monastery's corridors, where 1.1's save has
    /// the party in the lower one.
    /// </summary>
    public int Sector { get; private set; } = -1;

    /// <summary>Whether the character's body is clear of everything solid if it stands here.</summary>
    public bool Fits(Vector3 feet) => Solid?.Fits(feet, Radius, Sector) ?? true;

    /// <summary>
    /// The walker's move direction, the unit direction of the leg, in three
    /// dimensions, as <c>StartWalk</c> sets it and a slide re-aims it. The
    /// thing test needs it, because a thing only blocks when it is ahead of the move —
    /// which is how you walk back out of something you are already standing in. The slide's own
    /// step does **not** change it: the engine asks about the slid-to point with the leg's direction
    /// (until then this was the slide's). The keyboard's step, which is ours, sets it flat.
    /// </summary>
    Vector3 moveDirection;

    /// <summary>What is stopping the character right now, when something is. Null the rest of the time.</summary>
    public WorldThing? BlockedBy { get; private set; }

    /// <summary>
    /// Raised when a step has been refused and the slide could not get round whatever refused it —
    /// 1.1's own event. It carries nothing, because 1.1's answer to it
    /// needs nothing: it looks only at the blocked character's own AI mode. <see
    /// cref="Ai.Minds"/> wires this up; a walker with no mind simply never raises it.
    /// </summary>
    public Action? Jammed { get; set; }

    /// <summary>
    /// Whether the character can stand here: clear of the level, and clear of everything placed in
    /// it. This is the predicate the walk map slides against, so a barrel turns a straight walk into
    /// a walk along the barrel rather than a stop.
    /// </summary>
    bool Clear(Vector3 feet)
    {
        // Surfaces are not tested at all, either way round: the body test keeps only the *thing*
        // results out of its collision query and throws the surfaces away, because the level
        // compiler already guaranteed the walk graph is walkable and marked which ends of which
        // edges are tight (link flags 1 and 2, which pull the crossing point in by the walker's own
        // radius). Nor is the ground: a route step goes where the leg goes (`WalkMap.Stride`),
        // so what keeps a walker out of a wall is the route itself. The keyboard's step, which is
        // ours, still stops where the ground runs out (`WalkMap.Move`).
        //
        // The keyboard used to test the body as well. Measured, that refuses 42.5% of the walkable
        // ground in the game — every staircase among it, because the test rings the feet at radius
        // 32 and a stair's own riser is inside that ring — while the walk graph alone puts a
        // walker's centre inside solid geometry 0.8% of the time. It is available as a Fix for
        // anyone who wants the margin, its body standing on the steps (DriveBody).
        if (!onRoute && Fixes.Current.BodyCollision && Solid is not null && !DriveBody.Fits(Solid, Thing.Position, feet, Radius, Sector)) return false;
        if (Props is null) return true;
        // The flood starts from the sector **the new feet** are in (the body test's sector search, then
        // The walker's own when its planes still hold the point, else the first in the
        // level's order that does, else its own again — `SectorSearch.Of`. Earlier, this asked
        // `Collision.SectorAt` sixteen units up, which is ours and in the first monastery's hall
        // answers a sector whose flood never reaches the one both characters stand in — so a
        // Cappadocian walked straight through Christof and stood inside him (a play-test).
        var blocker = Ahead(feet);
        if (blocker is not null) BlockedBy = blocker;
        return blocker is null;
    }

    /// <summary>
    /// The engine's body test on its own: the nearest thing that would stop the walker
    /// at <paramref name="feet"/> — ahead of where it stands **now** along its move direction — with
    /// the flood started from the sector those feet are in.
    /// </summary>
    WorldThing? Ahead(Vector3 feet)
    {
        if (Props is null) return null;
        int sector = Solid is null ? Sector : SectorSearch.Of(Solid, feet, Sector);
        return Props.Blocker(Thing, feet, Thing.Position, moveDirection, sector);
    }

    /// <summary>The route being walked, or empty. Set by <see cref="WalkTo"/>.</summary>
    public Waypoint[] Route { get; private set; } = [];
    /// <summary>Which waypoint of <see cref="Route"/> is being walked to.</summary>
    public int Step { get; private set; }
    /// <summary>Where the route ends — the clicked point, not the last waypoint.</summary>
    public Vector3 Destination { get; private set; }

    /// <summary>
    /// How many times this walker, made with no ground under it, looked for a route once it found its feet (ours, the
    /// quality of life <c>spawnwalls</c>, <see cref="Qol.SpawnWalls"/>); 1.1 walks on along its straight leg. For the tests and the reports.
    /// </summary>
    public int Replanned { get; private set; }
    /// <summary>
    /// True while there is somewhere to walk to — including a script's walk that is standing and
    /// waiting to try again (<see cref="persistent"/>), which 1.1 counts as walking too: its walk
    /// bit stays set while it retries.
    /// </summary>
    public bool Walking => leg > 0f || retryIn > 0f;
    /// <summary>
    /// **Moving at all** — what the engine's own test asks (a walk in progress, or a
    /// physics velocity): a route (<see cref="Walking"/>), or the keyboard driving the character,
    /// which is ours and has no route (<see cref="Move"/> sets <see cref="Speed"/> while a key is
    /// held). The idle flourish is refused while this is true.
    /// </summary>
    public bool Moving => Walking || Speed > 0f;
    /// <summary>On a detour round something, with the real walk held (moveWalk flag 8).</summary>
    public bool Detouring => detouring;
    /// <summary>Standing still inside a walk — waiting for a person, or to try again (moveWalk flag 0x2000).</summary>
    public bool Waiting => wait > 0f || retryIn > 0f;
    /// <summary>The goal a detour is holding, while one runs.</summary>
    public Vector3? HeldGoal => detouring ? detourGoal : null;
    /// <summary>How much of the current leg is left, and the way it goes. The engine keeps exactly
    /// these two and re-aims only when something gets in the way.</summary>
    float leg;
    Vector2 legDirection;
    /// <summary>The climb in the current leg's unit direction: what carries a step onto a stair.</summary>
    float legRise;
    bool onRoute;
    /// <summary>On a leg through the air (<see cref="FlyTo"/>): no ground under it or along it.</summary>
    bool flying;
    /// <summary>Whether the leg it is on is a flight (<see cref="FlyTo"/>).</summary>
    public bool Flying => flying;
    /// <summary>
    /// The engine's <c>moveWalk</c> flag bit 2: there are still waypoints to cross
    /// before the destination itself. It is what tells the end of a leg from the end of the walk —
    /// <c>NextWaypoint</c> clears it when the waypoints run out and starts the last leg
    /// to the destination, and the walker's update stops the character when a leg ends
    /// with the bit already clear. Without it a character that cannot quite reach its destination
    /// re-aims at it for ever and walks on the spot, which is what it did here until then.
    /// </summary>
    bool following;
    int stalls;

    /// <summary>
    /// How many half-second waits it takes to give up on a route. **The engine has no such number**
    /// — a walker it cannot get anywhere stands and retries for ever, which is what an enemy that
    /// never reaches you is doing. This one is ours, so that a test finishes — and it is the player's:
    /// a click, the held button and the host's order for a client's character give up after six (the
    /// decision).
    /// </summary>
    public const int GiveUpAfter = 6;

    /// <summary>
    /// How many half-second waits an **AI's** walk takes to give up — every walk a mind orders through
    /// the move order (<see cref="Ai.Minds"/>' <c>Go</c>, which passes <c>mind: true</c>). **Ours**:
    /// 1.1's walker never gives up, and Vitomir's gives up at ten
    /// over the player's six until an overhauled AI, a switch for later, settles it.
    /// </summary>
    public const int MindGiveUpAfter = 10;

    /// <summary>
    /// The give-up count of the walk under way: <see cref="GiveUpAfter"/>, or
    /// <see cref="MindGiveUpAfter"/> for a mind's. Set by each order (<see cref="WalkTo"/>) and carried
    /// across its detours, which call the walker straight.
    /// </summary>
    int patience = GiveUpAfter;

    /// <summary>The give-up count of the walk under way (<see cref="GiveUpAfter"/> or <see cref="MindGiveUpAfter"/>).</summary>
    public int Patience => patience;

    /// <summary>
    /// A walk that never gives up, which is every walk to the engine. Ours only for the walks a
    /// script orders (`SendActorToPos`): nothing else will ever order that walk again, so giving up
    /// on it stops a patrol for good where 1.1's would stand and retry until whatever was in the
    /// way moved. The AI's walks and the player's clicks keep <see cref="GiveUpAfter"/>, because
    /// the mind and the player simply order another.
    /// </summary>
    bool persistent;

    /// <summary>What a persistent walk was told, so it can be told again (<see cref="TryAgain"/>).</summary>
    (Vector3 Point, int Node, float Speed) order;

    /// <summary>A persistent walk with no way through, standing: seconds until it asks again.</summary>
    float retryIn;

    /// <summary>
    /// A persistent walk that has lost its route — a detour it could not come back from — stands
    /// for half a second (the engine's own wait) and orders itself again from
    /// wherever it is, for as long as it takes. The engine's exact bookkeeping at that point is not
    /// read; "stand, and try again" is its behaviour everywhere else a walker cannot get on.
    /// </summary>
    void StandAndRetry()
    {
        var again = order;
        Clear();
        order = again;
        persistent = true;
        retryIn = Rules.GameRules.Current.Movement.WaitBeforeRetry[1];
    }

    void TryAgain()
    {
        retryIn = 0f;
        var again = order;
        if (!WalkTo(again.Point, again.Node, speed: again.Speed, clamp: false, persist: true)) StandAndRetry();
    }

    /// <summary>
    /// How far away a destination has to be before a character runs to it rather than walking.
    /// It is the engine's own number and it is why a short click
    /// is a stroll and a long one is a sprint.
    /// </summary>
    public const float RunBeyond = 300f;

    /// <summary>
    /// Start walking to <paramref name="point"/> on <paramref name="node"/>: the parts of
    /// `rpgAction::MoveTo` that come before the walker — the hand to the host, the
    /// free-spot test, the ground under a walker that has none and the clamp — and then the
    /// walker's own `simWalker::WalkTo` (<see cref="Walk"/>). Three outcomes, all the
    /// original's: the goal is on the ground the character already stands on, so it walks straight
    /// at it; a route came back; or nothing came back, and **nothing is touched** — the character
    /// goes on doing whatever it was doing, the route it was walking included.
    ///
    /// <paramref name="run"/> overrides the 300-unit rule (<see cref="RunBeyond"/>): the
    /// move order's own flags decide instead.
    ///
    /// <paramref name="refuseOccupied"/> is **off by default, which is what the engine's own WalkTo
    /// does**: it asserts, gates on the walker's state, runs the search and starts the leg, and it
    /// never asks whether anything is standing at the destination. That question belongs one level
    /// up, in the action code's **move order**: asked when the caller does **not** set flag
    /// `0x100`, a body query at the point (<see cref="FreeSpot"/>)
    /// whose nearest hit, if it is not the walker, refuses the order **before anything is touched**.
    /// Flag `0x8`, which this comment once had as the test, only turns the character to face the
    /// point. Of the sixteen callers of the move order only the approach
    /// carries `0x100`; every other one asks (the AI's walks through
    /// <see cref="Ai.Minds"/>' <c>Go</c>, which says which is which, the held-button walk, the
    /// click and the host's order for a client's character). The close-in walk
    /// (<see cref="Rpg.Actions"/>) is not a move order and never asks: its target always stands on
    /// its destination.
    ///
    /// <paramref name="speed"/> is the engine's own third argument to `simWalker::WalkTo`, in units
    /// a second, and when it is given it decides the pace outright instead of the walk/run pair.
    /// Every caller hands one down now — the click, the AI's walks and the approaches work it out
    /// from the gait and the character's MOVESPEED — and `CodexActor.SendActorToPos` takes one from
    /// the script. Zero means "the usual rule".
    ///
    /// <paramref name="mind"/> says a mind ordered the walk, which gives up after
    /// <see cref="MindGiveUpAfter"/> waits rather than the player's <see cref="GiveUpAfter"/> (ours).
    /// </summary>
    public bool WalkTo(Vector3 point, int node, bool? run = null, bool refuseOccupied = false,
                       float speed = 0f, bool clamp = true, bool persist = false, bool mind = false)
    {
        // The move order's free-spot test comes before anything is touched — the clamp,
        // the cancel, the hand to the host, the walker — so a refused walk leaves the walker walking
        // its last route (FreeSpot). It is asked at the order's own point, not the clamped one.
        Occupant = refuseOccupied ? FreeSpot.Taken(this, point) : null;
        if (Occupant is not null) return false;
        // A client's move order goes to the host instead (kind 1). **Ours**: on a player's machine
        // that predicts its own character (Net.OwnWalk), the same walk is started here too, at once, along the path the host will
        // find from the same place; the host's snapshots correct it (the host still decides where she is).
        if (Net.NetOrders.Forward(Thing, Net.OrderKind.Move, point: point, flags: run == true ? Net.ServerOrders.MoveRun : 0))
        {
            if (Net.NetOrders.Predicting(Thing) is { } own)
            {
                // At the host's pace for the order (ServerOrders' case 1: the run flag, the walk or the run at her MOVESPEED).
                bool runs = run == true;
                float pace = speed > 0f ? speed : (runs ? RunSpeed : WalkSpeed) * (Thing.Actor?.MoveSpeedScale ?? 1f);
                own.Walked(this, point, node, runs, pace, WalkHere(point, node, runs, pace, clamp, persist, mind));
            }
            return true;
        }
        return WalkHere(point, node, run, speed, clamp, persist, mind);
    }

    /// <summary>
    /// The move order's walk on this machine (<see cref="WalkTo"/> after its hand to the host): the clamp and the walker. A player's
    /// machine that predicts its own character also calls it to walk her again from where the host's word put her
    /// (<see cref="Net.OwnWalk"/>), with no order sent.
    /// </summary>
    internal bool WalkHere(Vector3 point, int node, bool? run, float speed, bool clamp, bool persist, bool mind)
    {
        if (node < 0 || node >= Map.Nodes.Length || Map.Nodes[node].Outline.Length == 0) return false;
        if (Path is null) return false;
        // A move order looks for the ground under a walker that has none before it walks (the ground
        // test with its "don't move me" argument, which is the ground ray). A script's
        // walk — the unclamped one — calls the walker straight and does not, so a thing that has no
        // surface yet walks straight at the point (below, and `WorldThing.Unsurfaced`).
        if (clamp && Node < 0) Node = Map.Beneath(Thing.Position);
        // **The clamp may fail, and a failed clamp is not a refusal.** The clamp pulls the point
        // inside the node by the walker's radius, gives up after four passes, and returns 0 having
        // left the point exactly where it was — and its **only** caller (in the move
        // order) throws that answer away. So a point the engine cannot pull clear is walked at
        // where it is, and the walker's own collision stops it short of the wall.
        //
        // This page's own doc said the opposite until then — *"still outside after the
        // fourth and the move does not happen"* — and this line implemented the doc. It is why
        // Christof could not be clicked up the monastery stairs and why a Cappadocian could not
        // follow him: node 796 there is a landing whose stored half-planes meet at an oblique
        // angle, and the four passes oscillate between the left edge and the diagonal without ever
        // satisfying both. At a radius of 16 it converges; at Christof's 32 it does not.
        //
        // And it is the **move order's** clamp, not the walker's: `simWalker::WalkTo`
        // takes the point as given, so a script's `SendActorToPos` — which calls the
        // walker straight — asks for `clamp: false` and walks to exactly the point it named.
        if (clamp) Path.ClampInside(node, ref point, Radius, Map.Nodes[node].Plane);
        if (!Walk(point, node, run, speed, persist)) return false;
        patience = mind ? Rules.GameRules.Current.Movement.AiWalkGiveUpWaits : GiveUpAfter;                    // ours
        return true;
    }

    /// <summary>
    /// `simWalker::WalkTo` itself: the search from where the walker stands, and the
    /// walk it starts. The order matters, and was Vitomir's own until then:
    /// <list type="bullet">
    /// <item>**the speed is written first**, before anything is known;</item>
    /// <item>a walker with no ground under it walks straight at the point, the waypoint and detour
    /// bits cleared (`&amp; ~0x2a`);</item>
    /// <item>otherwise the search: **no way there answers 0 and touches nothing else**
    /// — the walker goes on along the route it had, at the new speed. Vitomir stopped the
    /// walker before it searched, so every refused order stood the character still: an enemy
    /// re-ordered every half second at a target on ground it cannot reach, a click at a place with
    /// no way to it;</item>
    /// <item>a goal on the ground it stands on is walked at straight, the waypoint and detour bits
    /// cleared (`&amp; ~0xa`);</item>
    /// <item>a route **sets the waypoint bit and leaves the detour bit** — and the goal the
    /// detour holds — so a route ordered while the walker is stepping round
    /// something ends by walking it **back to the goal it had been detouring towards**
    /// (the step's tail, <see cref="Arrive"/>). A straight walk forgets it.</item>
    /// </list>
    /// The detour's own walk (<see cref="Detour"/>) and the return from it (<see cref="Resume"/>)
    /// call this straight, as the engine's do.
    /// </summary>
    bool Walk(Vector3 point, int node, bool? run, float speed, bool persist)
    {
        if (Path is null) return false;
        var result = PathResult.Straight;
        var route = Array.Empty<Waypoint>();
        if (Node >= 0)
        {
            result = Path.Find(Thing.Position, Node, point, node, Radius, isActor: false, out route);
            if (result == PathResult.None)
            {
                if (speed > 0f && Walking) commanded = speed;                  // before the search
                return false;
            }
        }
        // **A new order does not end a wait**. The move order stops nothing when the action is
        // already a walk (action mode 1), and `simWalker::WalkTo` clears
        // only the waypoint and detour bits (`& ~0xa`), so the 300 ms for a person or the 500 for a
        // thing (bit 0x2000 and its time) runs on into the new route. An enemy's approach
        // or manoeuvre re-ordering a walker that is waiting for somebody leaves it waiting.
        float held = leg > 0f ? wait : 0f;
        // A route keeps the detour bit and its held goal; ground straight ahead, or none
        // under the walker, clears them.
        bool keepDetour = result == PathResult.Path && detouring;
        var (heldGoal, heldNode) = (detourGoal, detourNode);
        Clear();
        if (keepDetour) { detouring = true; detourGoal = heldGoal; detourNode = heldNode; }

        Running = run ?? Vector3.Distance(point, Thing.Position) >= Rules.GameRules.Current.Movement.RunBeyond;
        // After the Clear() above, which clears it: a commanded speed belongs to this route only.
        commanded = speed > 0f ? speed : 0f;
        if (commanded > 0f) Running = commanded > WalkSpeed;
        persistent = persist;
        if (persist) order = (point, node, commanded);
        Destination = point;
        GaveUp = false;
        onRoute = true;
        following = false;
        if (Node < 0) return Keep(Aim(point), held);
        // The engine keeps the two cases apart: a goal on the ground the character is
        // already standing on is walked at directly with the waypoint bit clear, and only a real
        // route sets the bit and hands over to NextWaypoint.
        if (result == PathResult.Straight) { Route = []; Step = 0; return Keep(Aim(point), held); }
        Route = route;
        Step = 0;
        following = true;
        return Keep(Next(), held);
    }

    /// <summary>A walk that has started keeps the wait the last one was in (see <see cref="WalkTo"/>).</summary>
    bool Keep(bool started, float held)
    {
        if (started && held > 0f) wait = held;
        return started;
    }

    /// <summary>
    /// A straight leg through the air, which is how the engine sends a flier: <c>pedfly</c>
    /// calls <c>StartWalk</c> itself instead of the move order — so there is
    /// no search, no clamp and no ground. The character goes in a straight line from where it is to
    /// the point, climbing or dropping as the line does, at <paramref name="speed"/> (the flier passes
    /// its bare run speed, with no MOVESPEED on it), turned to face the point as
    /// it sets off (<see cref="Aim"/>), and stops there.
    ///
    /// Measured in 1.1 (Old Town by day): a crow's flights are single straight lines
    /// between perches, some of which are in mid-air, at 250 units a second, with the heading
    /// changing only at take-off — the leg the engine's walker draws, and nothing else.
    ///
    /// What the walker does when something is in the way of a flight is its usual ladder;
    /// the parts of it that need ground — the slide and the detour — have none to work with here,
    /// so a blocked flight waits and sets off again, as the ladder's last rung does.
    /// </summary>
    public bool FlyTo(Vector3 point, float speed)
    {
        Clear();
        Destination = point;
        GaveUp = false;
        commanded = speed > 0f ? speed : RunSpeed;
        Running = commanded > WalkSpeed;
        if (!Aim(point)) return false;
        onRoute = true;
        flying = true;
        Node = -1;                                  // off the ground until it lands
        return true;
    }

    /// <summary>
    /// **Stopped from outside** — by a script, the AI, a conversation, a teleport or the kill: drops
    /// whatever route there was, and a character that was moving stands **at once**. 1.1's stop
    /// reselects the locomotion there and then when the walker was moving.
    /// So a conversation a script starts straight after its `Stop()` puts its talking
    /// animation over a standing character, and nothing puts the stand back over it a frame later.
    /// Found in play: Christof, driven into the smithy's region with
    /// the keys, and the stand chosen on the next frame covered his talking pose for the whole of
    /// `4_1_OutSmithy`. The character keeps whatever it is doing otherwise; an action (a swing, a
    /// gesture) is on its own channel and is not touched.
    /// </summary>
    public void Stop()
    {
        bool moving = Walking || Speed > 0f;
        Clear();
        if (moving) { Speed = 0f; Animate(); }
    }

    /// <summary>
    /// The walker's own ending of a route — a new order replacing the old, an arrival, a give-up, the
    /// keys taking over. Not 1.1's stop: the step it comes from sets the speed and chooses the
    /// locomotion itself, so a character re-ordered in mid-run keeps running rather than standing
    /// for a frame.
    /// </summary>
    void Clear()
    {
        Route = []; Step = 0; leg = 0f; onRoute = false; following = false; stalls = 0; flying = false;
        commanded = 0f; persistent = false; retryIn = 0f;
        // Not `waitedForSomeone`: the engine's stop (`flags & 0xffffdfd0`) and WalkTo
        // both leave bit 0x40, and only a step that goes where it was aimed clears it
        // — so a walker that has waited for somebody once does not wait for them again
        // under a fresh order until it has moved (ours cleared it on every order).
        wait = 0f; detouring = false; detourNode = -1;
        Destination = Thing.Position;
    }

    /// <summary>True when the last route ended by giving up rather than by arriving.</summary>
    public bool GaveUp { get; private set; }

    /// <summary>What was standing on the last refused destination, when that is why it was refused.</summary>
    public WorldThing? Occupant { get; internal set; }

    /// <summary>
    /// Starts the next leg, or the last one to the destination itself. `moveWalk::NextWaypoint`
    /// skips a waypoint it is already standing on rather than stalling on it, which is
    /// what the loop is for, and it does nothing at all once <see cref="following"/> is clear —
    /// there is no next waypoint after the destination, and the caller stops the character.
    /// </summary>
    bool Next()
    {
        if (!following) return false;           // nothing left to cross: the caller stops the walk
        while (Step < Route.Length)
        {
            if (Aim(Route[Step].Point)) return true;
            Step++;
        }
        following = false;
        return Aim(Destination);
    }

    /// <summary>
    /// Points the character at somewhere and works out how far it is. False when it is already
    /// there — the engine's own threshold is a tenth of a unit.
    ///
    /// The distance and the direction are both worked out in three dimensions, as the engine does
    /// (`StartWalk` normalises the full vector), and what is kept is the **flat part** of
    /// that unit direction. That matters on stairs: a leg that climbs 16 units over 8 of ground is
    /// 17.9 long but only has 8 of ground to cover, and a flattened direction would walk the
    /// character straight past the step it was aiming at.
    ///
    /// **It also turns the character to face where it is going, at once** — `StartWalk` calls the
    /// same instant turn every "face" in the engine uses, and turns nothing
    /// afterwards: a walker faces each leg as it starts it and keeps that heading to the end of the
    /// leg. In 1.1 (Old Town by day) every walker and every
    /// crow changes heading at once at the start of a leg and at every corner of a route,
    /// never in between. Only a leg that climbs or drops steeply — the vertical part of the unit
    /// direction 0.8 or more — leaves the heading alone, because facing along it would tip
    /// the character over.
    /// </summary>
    bool Aim(Vector3 target)
    {
        if (!Reaim(target)) return false;
        if (MathF.Abs(legRise) < FaceBelowRise) Thing.Face(target);
        return true;
    }

    /// <summary>
    /// The direction and length of a leg without the turn: what the engine's slide does when it has
    /// stepped the character round something (the slide recomputes the leg's direction and length from
    /// wherever the step ended and calls nothing else), so a walker that slid along a barrel keeps
    /// facing the way it was going.
    /// </summary>
    bool Reaim(Vector3 target)
    {
        var to = target - Thing.Position;
        float distance = to.Length();
        if (distance < Rules.GameRules.Current.Movement.ArrivedDistance) { leg = 0f; return false; }
        legDirection = new Vector2(to.X, to.Y) / distance;
        legRise = to.Z / distance;
        leg = distance;
        stalls = 0;
        return true;
    }

    /// <summary>A leg climbing or dropping at least this much of its length turns nobody.</summary>
    public const float FaceBelowRise = 0.8f;

    /// <summary>How close counts as arrived.</summary>
    public const float Arrived = 0.1f;

    /// <summary>Where the current leg is headed: the waypoint being crossed to, or the destination.</summary>
    public Vector3 Target => Step < Route.Length ? Route[Step].Point : Destination;

    /// <summary>How long the character is standing still for before trying again, in seconds.</summary>
    float wait;
    bool waitedForSomeone, detouring;
    Vector3 detourGoal;
    int detourNode = -1;
    readonly Random detourRoll = Ai.Chance.Game?.Detours ?? new(20260913);   // a game's: the launch's, carried on

    /// <summary>
    /// What the engine does when something is in the way, in the order it does it:
    /// the sideways angle to try, how far out, and nothing else. Nine
    /// cases and a default, rolled at random, up to sixteen times.
    /// </summary>
    static readonly (float Turn, float Distance)[] Detours =
    [
        (91f, 50f), (-91f, 50f), (100f, 100f), (-100f, 100f), (120f, 100f),
        (-120f, 100f), (140f, 100f), (95f, 70f), (-140f, 100f), (-95f, 70f),
    ];

    /// <summary>
    /// How many times this walker has been held up — the engine's wait, called for a
    /// person in the way, for anything else, and for the tenth of a second after a detour's end.
    /// **Each one scuffs**: a wait begun while walking says `stopmove` and the floor's scuff
    /// (then the surface's sound offset by 46), and its end says no
    /// `startmove`, because the walk bit stays set through it (`StartWalk` plays that only when the
    /// bit is clear). <see cref="Simulation"/>'s footfalls hear the count change (a comparison with 1.1;
    /// until then a held-up walker was silent, since <see cref="Walking"/> stays true through a wait).
    /// </summary>
    public int Holds { get; private set; }

    /// <summary>The <see cref="Holds"/> the footfalls have already answered.</summary>
    internal int HoldsHeard;

    /// <summary>The engine's wait: stand for <paramref name="seconds"/>, and count it.</summary>
    void Hold(float seconds) { wait = seconds; Holds++; }

    /// <summary>How long the character waits before trying again: for a person, and for anything
    /// else (the step passes 300 and 500 ms).</summary>
    public const float WaitForPerson = 0.3f, WaitForThing = 0.5f;

    /// <summary>
    /// How close to the end of a leg counts as having got there when something is in the way. The
    /// engine compares what is left of the leg against the walker's own radius, which is
    /// why a walker stopped just short of a doorway still counts as through it.
    /// </summary>
    float CloseEnough => Thing.Collide == CollideShape.None ? 0f : Radius;

    /// <summary>
    /// `CONV_XFLAG_STOPACTORS`: a conversation begun with it in this place stops every
    /// walker ticked before the thing tick ends — after a wait that has run out has restarted the
    /// walk, as the engine's test comes after that (<see cref="Rpg.ConversationStop"/>).
    /// </summary>
    bool StoppedForConversation()
    {
        if (Thing.Place is not { } here || !Rpg.ConversationStop.Up(here)) return false;
        Clear(); Speed = 0;
        return true;
    }

    /// <summary>
    /// One tick of walking a route. A leg is a fixed direction and a distance left to cover, and
    /// the distance comes off only when the step went exactly where it was aimed — the engine's own
    /// rule (the walker's step decrements only on the unblocked path). A step that had to
    /// slide gets the leg re-aimed from wherever it ended up instead, which is what the engine's
    /// own slide does; one that went nowhere at all counts against giving up.
    /// </summary>
    public void Follow(float seconds)
    {
        if (seconds <= 0 || !Walking)
        {
            // A character a player drives from another machine has no route: the push walks it (`GameServer.Steer`, by
            // `Move`) and its walk is the push's to start and stop. Put back to standing here, every frame, before the push
            // walked it again, its walk started over each frame and it slid (ours, seen in play).
            if (Driven) return;
            Speed = 0; Animate(); return;
        }
        if (retryIn > 0f)
        {
            retryIn -= seconds; Speed = 0; Animate();
            if (retryIn <= 0f) TryAgain();
            return;
        }
        if (wait > 0f)
        {
            wait -= seconds; Speed = 0; Animate();
            if (wait > 0f) return;
            // A walker that has waited starts its leg again from where it stands (the step calls
            // StartWalk on the leg's own goal once the wait runs out) — so it faces
            // the leg again, and a leg with nothing left of it is over. The waits already counted
            // towards giving up (ours) still count: the fresh aim would otherwise forget them.
            int waited = stalls;
            if (!Aim(Target)) { if (Arrive() && Thing.Place is { } here) here.Arrived?.Invoke(Thing, 0); return; }
            stalls = waited;
            StoppedForConversation();                                   // after the restart
            return;
        }
        if (StoppedForConversation()) return;

        Speed = commanded > 0f ? commanded : Running ? RunSpeed : WalkSpeed;
        float distance = MathF.Min(Speed * seconds, leg);
        if (flying)
        {
            bool landed = Fly(distance, seconds);
            Animate();
            if (landed && Thing.Place is { } perch) perch.Arrived?.Invoke(Thing, 0);
            return;
        }
        moveDirection = new Vector3(legDirection, legRise);
        float moved = Advance(legDirection, distance, seconds, legRise * distance);
        bool there = false;
        if (!Blocked)
        {
            leg -= distance;
            stalls = 0;
            waitedForSomeone = false;
            if (leg < Rules.GameRules.Current.Movement.ArrivedDistance) there = Arrive();
        }
        else if (moved > 1e-3f)
        {
            stalls = 0;
            if (!Reaim(Target)) there = Arrive();  // slid along something: head at the same place from here
        }
        else Stuck(distance, seconds);
        Animate();
        // **A walk that got where it was going tells the thing's scripts** (the stop, then
        // `arrived(thingGuid, 0, captureId)`, to its own script and to everyone holding
        // a capture on it). Every walk, whoever ordered it: a script's `SendActorToPos`, the AI's,
        // a click. Last, because the script may well send it straight on somewhere else, and the
        // engine calls it as the very last thing its update does too.
        //
        // Only a walk that **arrives** says so. One that stops short — blocked within its own
        // radius of the end, given up, called off by a new order — ends in silence, and
        // so does a detour that runs out: the engine's stop sends nothing.
        if (there && Thing.Place is { } place) place.Arrived?.Invoke(Thing, 0);
    }

    /// <summary>
    /// One tick of a flight: the walker's own step — the position plus the leg's unit
    /// direction times the distance, tested against the things in the way and nothing else — with no
    /// ground to land on. Answers whether that was the end of the flight.
    /// </summary>
    bool Fly(float distance, float seconds)
    {
        Blocked = false;
        BlockedBy = null;
        var from = Thing.Position;
        var to = from + new Vector3(legDirection * distance, legRise * distance);
        moveDirection = new Vector3(legDirection, legRise);
        if (Clear(to))
        {
            Thing.Position = to;
            long was = Thing.Sector;
            Sector = SectorSearch.Of(Solid, to, (int)was);
            Thing.Sector = Sector;
            Props?.Moved(Thing, was, Sector);
            Thing.Place?.Regions?.Moved(Thing, was);
            Achieved = seconds > 0f ? distance / seconds : 0f;
            leg -= distance;
            stalls = 0;
            waitedForSomeone = false;
            return leg < Rules.GameRules.Current.Movement.ArrivedDistance && Arrive();
        }
        Blocked = true;
        Achieved = 0f;
        // Close enough to call it there, as a walker does — which on the last leg stops
        // it without a word.
        if (leg < CloseEnough) { Ground(snap: false); Clear(); Speed = 0; return false; }
        Jammed?.Invoke();
        if (BlockedBy is { Type: ThingType.Actor or ThingType.Player } && !waitedForSomeone)
        {
            waitedForSomeone = true;
            Hold(Rules.GameRules.Current.Movement.WaitBeforeRetry[0]);
            return false;
        }
        Hold(Rules.GameRules.Current.Movement.WaitBeforeRetry[1]);
        if (++stalls >= patience) { GaveUp = true; Ground(snap: false); Clear(); Speed = 0; }
        return false;
    }

    /// <summary>
    /// Nothing moved. The engine works down a ladder rather than giving up, and this is
    /// that ladder: close enough to the end of the leg, so call it reached; slide round whatever it
    /// is; wait for a person to move; end a detour that has run its course; go round the long way;
    /// and failing all of that, wait half a second and try the whole thing again.
    ///
    /// **The engine never gives up.** A walker that cannot get there stands and retries for ever,
    /// which is what an enemy that never reaches you is doing. <see cref="GiveUpAfter"/> (a mind's walk
    /// <see cref="MindGiveUpAfter"/>) is ours, so that a test finishes and a player is not left
    /// standing in the street.
    /// </summary>
    void Stuck(float distance, float seconds)
    {
        // The engine compares what is left against the walker's radius and then goes straight to
        // NextWaypoint — which, on the last leg, stops the character rather than
        // resuming a detour. That asymmetry is the original's, not a slip.
        if (leg < CloseEnough)
        {
            if (following) Step++;
            if (!Next()) { Clear(); Speed = 0; }
            return;
        }
        if (Slide(distance, seconds)) { stalls = 0; return; }
        // The slide could not get round it, so ask. The engine calls straight into the AI here
        // (immediately after the walker's own slide returns false); what
        // happens next is the AI's business and only a party member asks at all.
        Jammed?.Invoke();
        if (BlockedBy is { Type: ThingType.Actor or ThingType.Player } && !waitedForSomeone)
        {
            waitedForSomeone = true;
            Hold(Rules.GameRules.Current.Movement.WaitBeforeRetry[0]);
            return;
        }
        if (detouring)
        {
            detouring = false;
            // The tenth of a second comes after the new route, as the engine orders it
            // (set before it, the route's own Clear() would clear it).
            if (Resume()) Hold(0.1f);
            else if (persistent) StandAndRetry(); else { GaveUp = true; Clear(); Speed = 0; }
            return;
        }
        if (Detour()) { stalls = 0; return; }
        Hold(Rules.GameRules.Current.Movement.WaitBeforeRetry[1]);
        if (++stalls >= patience)
        {
            if (persistent) { stalls = 0; return; }             // 1.1 retries for ever
            Diagnostics.Journal.Current?.Write(Thing.Place?.Milliseconds ?? 0, Diagnostics.JournalKind.Order,
                ("what", "walk given up"), ("who", Diagnostics.Watch.Name(Thing)),
                ("to", $"{Destination.X:0},{Destination.Y:0},{Destination.Z:0}"),
                ("blocked", BlockedBy is { } by ? Diagnostics.Watch.Name(by) : "the world"));
            GaveUp = true; Clear(); Speed = 0;
        }
    }

    /// <summary>
    /// The engine's own slide: take the part of the move that points at whatever is in
    /// the way out of it, bias it a hundredth further round, and try that instead — but only when
    /// the obstacle is not squarely ahead, because there is no way round something you are facing.
    /// </summary>
    bool Slide(float distance, float seconds)
    {
        if (BlockedBy is not { } blocker) return false;
        var away = new Vector2(blocker.Position.X - Thing.Position.X, blocker.Position.Y - Thing.Position.Y);
        if (away.LengthSquared() < 1e-6f) return false;
        var towards = Vector2.Normalize(away);
        float straightOn = Vector2.Dot(towards, legDirection);
        if (straightOn > SlideLimit || straightOn <= 0f) return false;
        var slide = legDirection - (straightOn + SlideBias) * towards;
        if (slide.LengthSquared() < 1e-8f) return false;
        slide = Vector2.Normalize(slide);
        float moved = Advance(slide, distance, seconds, 0f);
        if (moved <= 1e-3f) return false;
        if (!Reaim(Target)) Arrive();
        return true;
    }

    /// <summary>How square on an obstacle has to be before sliding is pointless, and how
    /// much further round than straight past it to aim.</summary>
    public const float SlideLimit = 0.95f, SlideBias = 0.01f;

    /// <summary>
    /// Go round the long way. Pick a sideways direction at random from the engine's own list, check
    /// there is ground there, that nothing is in the way of getting to it, and that the goal is
    /// still reachable from it — then walk there, and pick the real route up again on arrival.
    /// Sixteen tries, which is the engine's own number.
    /// </summary>
    bool Detour()
    {
        if (Path is null || Map is null) return false;
        var goal = Destination;
        int goalNode = Route.Length > 0 ? Route[^1].Node : Node;
        for (int attempt = 0; attempt < Rules.GameRules.Current.Movement.DetourTries; attempt++)
        {
            var (turn, out_) = Detours[detourRoll.Next(Detours.Length)];
            float radians = turn * (MathF.PI / 180f);
            float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
            var sideways = new Vector2(legDirection.X * cos - legDirection.Y * sin,
                                       legDirection.X * sin + legDirection.Y * cos);
            var candidate = Thing.Position + new Vector3(sideways * out_, 0);
            int node = Map.Find(candidate, WalkMap.Reach);
            if (node < 0) continue;
            candidate.Z = Map.Nodes[node].Height(candidate.X, candidate.Y);
            // Nothing in the way one unit out that way, nor at the spot: the walker's own body test
            // twice, with the blocked leg's direction still in
            // direction — so what counts is what the walker counts, ahead of it. Earlier, this asked
            // the move order's "is anybody standing there" (ours here), which a corpse or a dropped
            // sword behind the walker failed as surely as a man in front of it.
            var oneStep = Thing.Position + new Vector3(sideways, legRise);
            if (Ahead(oneStep) is not null || Ahead(candidate) is not null) continue;
            if (Path.Find(candidate, node, goal, goalNode, Radius, isActor: false, out _) == PathResult.None) continue;

            // **The spot is walked to by the walker's own WalkTo**, at the speed
            // the walk already had: a search from where the walker stands, so a spot on
            // another piece of ground is reached by a route through the crossing between them, and a
            // spot with no way to it fails this try and the next is rolled. Earlier, Vitomir aimed
            // at it in a straight line, and since the walker's step tests things and not walls, a
            // spot round a corner was walked at through the corner. The route the detour interrupts
            // is no longer followed (a straight walk clears the waypoint bit, a route to the spot
            // replaces it), so arriving at the spot resumes the real walk rather than crossing a
            // waypoint: until then a detour taken part-way along a route ended the whole walk at the
            // spot — Christof stepping round a townsperson and stopping there, where 1.1's steps
            // round and carries on (Old Town by day).
            bool keep = persistent;
            var was = order;
            if (!Walk(candidate, node, run: Running, speed: commanded, persist: keep)) continue;
            if (keep) { order = was; persistent = true; }
            // The goal the detour holds (written before the tries) and
            // bit 8 once the walk to the spot has started.
            detourGoal = goal; detourNode = goalNode;
            detouring = true;
            return true;
        }
        return false;
    }

    /// <summary>How many sideways directions the engine tries before it stops looking.</summary>
    public const int DetourTries = 16;

    /// <summary>Back onto the real route once a detour has got the character clear of whatever it was.</summary>
    bool Resume()
    {
        if (detourNode < 0) return false;
        var goal = detourGoal; int node = detourNode;
        detourNode = -1;
        // The rest of the order carries across the detour: the pace a script asked for (a patrol
        // walks at 90, not at the template's walk) and whether it may give up. Once the
        // first detour dropped both, so a patroller came round an obstacle at the wrong speed and
        // then could give up on its round.
        bool keep = persistent;
        float pace = commanded;
        var was = order;
        // Straight back to the walker (the step's tail walks to the stored goal), which
        // never asks whether anybody is standing there — so neither does this (earlier, it did) —
        // and is not a move order: no clamp, no hand to the host.
        bool ok = Walk(goal, node, run: null, speed: pace, persist: keep);
        if (keep) { order = was; persistent = true; }
        return ok;
    }

    /// <summary>
    /// Reached the current leg's end. The engine's own order, at the tail of the walker's update:
    /// cross to the next waypoint while there is one; otherwise pick the real route up
    /// again if this was a detour; otherwise the walk is over and the character stops.
    /// </summary>
    /// Answers whether that was the end of the walk — which is when the scripts hear about it.
    bool Arrive()
    {
        // A flight has landed: find out what, if anything, is under it — without moving it, since a
        // perch may be in mid-air (the ground test with its argument set; see Ground).
        if (flying) { flying = false; Ground(snap: false); }
        if (following) { Step++; if (Next()) return false; }
        else if (detouring) { detouring = false; if (Resume()) return false; }
        Clear();
        Speed = 0;
        return true;
    }

    /// <summary>
    /// One tick of walking. <paramref name="direction"/> is where the player is pushing, in world
    /// XY; it is normalised here, so holding two keys is not faster than one. The character turns
    /// towards where it is going at <see cref="TurnRate"/> rather than snapping, and its animation
    /// follows its speed.
    /// </summary>
    public void Move(Vector2 direction, float seconds)
    {
        if (seconds <= 0) return;
        if (Walking) Clear();                        // the keyboard wins over a route
        // At the speed a click would give: the walk or the run times MOVESPEED.
        float want = direction.LengthSquared() > 1e-6f
            ? (Running ? RunSpeed : WalkSpeed) * (Thing.Actor?.MoveSpeedScale ?? 1f) : 0f;
        Speed = want;
        if (want > 0)
        {
            // **In strides no longer than the thinnest ground.** A step lands only on the piece of
            // ground it started on or one linked to it (`WalkMap.Land`), and some doorways are a
            // flight of treads two units deep — the one into the first room of the monastery has
            // six of them in sixteen units before a twelve-unit drop. A run of 250 a second covers
            // four units in a frame at 60 and twelve at 20, which jumps a tread, lands on ground
            // that is not linked to where it started, and is refused, every frame, for good.
            // In driving mode at a sprint, Christof sometimes got stuck
            // on the lip of that doorway. Click-to-move never did, because a route's legs end on
            // the shared edges. Driving is ours, so this is ours too.
            var unit = Vector2.Normalize(direction);
            float left = want * seconds;
            int strides = Math.Max(1, (int)MathF.Ceiling(left / DriveStride));
            float each = left / strides;
            moveDirection = new Vector3(unit, 0f);
            // Driving turns the character gradually, at TurnRate: ours, like driving itself. A route
            // turns it all at once as each leg starts (Aim).
            Turn(MathF.Atan2(-unit.X, unit.Y) * (180f / MathF.PI), seconds);
            for (int i = 0; i < strides; i++)
                if (Advance(unit, each, seconds / strides, 0f) < each * 0.01f) break;   // nowhere to go
            // A companion in the way steps aside (ours: Ai.Minds.MakeWay).
            if (Blocked && BlockedBy is { } inTheWay) Thing.Place?.Minds?.MakeWay(Thing, inTheWay, unit);
        }
        Animate();
    }

    /// <summary>The longest single step the keyboard takes (see <see cref="Move"/>).</summary>
    public const float DriveStride = 1f;

    /// <summary>
    /// The step itself, shared by both ways of driving the character: try to get there, and report
    /// how far it actually went. It turns nobody — the engine's walker update moves the
    /// position and never touches the heading.
    /// </summary>
    float Advance(Vector2 direction, float distance, float seconds, float rise)
    {
        Blocked = false;
        BlockedBy = null;
        var from = Thing.Position;
        var to = from + new Vector3(direction * distance, rise);
        // A route is walked the engine's way — to wherever the leg goes, the ground kept or found
        // underneath by the engine's ray (WalkMap.Stride). Only the keyboard, which is ours, keeps to the ground.
        var step = onRoute ? Map.Stride(Node, from, to, Clear, (int)Thing.Sector) : Map.Move(Node, from, to, Clear);
        // A route step is written whatever the ground (the step sets the position with no surface test
        // before it): a walker with no surface whose ray still finds none moves all the same, on
        // no surface — the rats a generator makes inside their holes (SurfacelessStep.cs).
        bool replan = false;
        if (step.Node >= 0 || SurfacelessStep.Taken(onRoute, step))
        {
            Node = step.Node;
            if (Node >= 0)
            {
                // It has found its feet. 1.1 walks on along the straight leg it was given with no ground under it, through
                // whatever stands between it and the point; with the quality of life spawnwalls it looks for a route from here instead.
                replan = Thing.Unsurfaced && onRoute && !following && (Thing.Place?.Qol ?? Qol.Current).SpawnWalls;
                Thing.Unsurfaced = false;
            }
            Thing.Position = step.Position;
            long was = Thing.Sector;
            Sector = SectorSearch.Of(Solid, step.Position, (int)was);
            Thing.Sector = Sector;
            Props?.Moved(Thing, was, Sector);
            // Every step is a SetPosition in the engine, and SetPosition ends in the region test:
            // this is what makes walking into a room start its conversation.
            Thing.Place?.Regions?.Moved(Thing, was);
        }
        Blocked = step.Blocked;
        if (!Blocked) BlockedBy = null;             // it only went straight there because nothing was
        float moved = Vector3.Distance(from, Thing.Position);
        // The quality of life spawnwalls: the rest of the way by the walk map, or not at all; the stride loop stops here either way.
        if (replan)
        {
            Replanned++;
            var goal = Destination;
            int node = Map.Find(goal);
            if (node < 0 || !Walk(goal, node, Running, commanded, persistent)) Stop();
            return 0f;
        }
        // What it managed, as against what it was told to do. Only the report reads this: the
        // engine animates a walker from the speed it was given, not from the ground it covered, so
        // a character grinding along a wall keeps its walk cycle rather than slowing into a stand.
        Achieved = seconds > 0f ? moved / seconds : 0f;
        return moved;
    }

    /// <summary>Turns towards a heading at <see cref="TurnRate"/>.</summary>
    public void Turn(float heading, float seconds)
    {
        float delta = Wrap(heading - Heading);
        float most = TurnRate * seconds;
        Heading += Math.Clamp(delta, -most, most);
    }

    /// <summary>
    /// Above this commanded speed a character plays its <c>run</c> cycle rather than its <c>walk</c>
    /// — 200 units a second, a double constant. It is an absolute
    /// number, not a comparison with the character's own walk speed, and that has consequences:
    /// **129 of the 662 templates that have both a motion set and speeds run at 200 or less**, so
    /// they play their walking animation while running — every zombie (80), the old men (40), the
    /// half-corpse (15). One template, <c>ahzra</c>, walks at 220 and so plays its run cycle while
    /// walking. Christof walks 115 and runs 250, which is why the number is invisible on him.
    /// </summary>
    public const float RunAnimationBeyond = 200f;

    /// <summary>
    /// The engine's own mode numbers for the four things a character does with its legs — indices
    /// into the 257-mode table, which is also
    /// <c>Codex.java</c>'s <c>MOTION_*</c>.
    /// </summary>
    public const int ModeWalk = 1, ModeRun = 17, ModeStand = 33, ModeLimp = 195;

    /// <summary>The first of the idle flourishes, <c>idle</c>; the weapon's own follow it.</summary>
    public const int ModeIdle = 49;

    /// <summary>
    /// Which mode the character's stance adds to the one below. Each of the locomotion families is
    /// sixteen wide (<c>walk</c>, <c>walk_onehand</c>, <c>walk_axe2</c>…), and the engine takes the
    /// offset from the weapon in the hand — its carry class — **but only for a
    /// player-type character, one with no mind, or one whose mind is fighting** (AI mode 3,
    /// the locomotion's choice). So Christof and his companions (the party are player things) walk with their
    /// swords, and a knight on his rounds walks plain and takes up his halberd stance only once he
    /// is in a fight, exactly as in 1.1 (`stand` and `walk` in
    /// `enemysearch`, `stand_polearm` and `walk_polearm` in `enemy`).
    ///
    /// It is read when the locomotion is chosen and at no other time, which is the engine's order
    /// too: a knight that notices you while standing keeps his plain stand until the walker next
    /// starts, stops or changes speed.
    /// </summary>
    public int Stance
    {
        get
        {
            if (Thing.Actor is not { Weapon: { } weapon }) return 0;
            var mind = Thing.Mind;
            bool armed = Thing.Type == ThingType.Player || mind is null || mind.Mode == Ai.AiMode.Enemy;
            return armed ? weapon.Carry : 0;
        }
    }

    /// <summary>
    /// The carry class the **utility stance** replaces the weapon's with in the locomotion:
    /// no weapon, or one carried as class 0, is `util` (13); class 1 (one-handed) `util_onehand`
    /// (14); class 5 (a gun) `util_gun` (15); any other class stays what <see cref="Stance"/> says —
    /// which is 0 for a character that is not a player, has a mind and is not fighting, even though
    /// the class tested is the weapon's own.
    /// </summary>
    public int UtilityStance => Thing.Actor?.Weapon?.Carry switch
    {
        null or 0 => Rpg.Weapon.UtilCarry,
        1 => Rpg.Weapon.UtilCarryOneHand,
        5 => Rpg.Weapon.UtilCarryGun,
        _ => Stance,
    };

    /// <summary>
    /// The same for the idle flourish, which differs in one place: a weapon of any class
    /// but 1 and 5 idles as plain `util` (13), not as its own class.
    /// </summary>
    public int UtilityIdleStance => Thing.Actor?.Weapon?.Carry switch
    {
        1 => Rpg.Weapon.UtilCarryOneHand,
        5 => Rpg.Weapon.UtilCarryGun,
        _ => Rpg.Weapon.UtilCarry,
    };

    /// <summary>
    /// What the character should be playing for how fast it has been told to go. This is the actor's
    /// own animation update: <c>stand</c> unless the walker is moving, then
    /// <c>run</c> above <see cref="RunAnimationBeyond"/> and otherwise <c>walk</c> — or <c>limp</c>
    /// when the character is <see cref="Hurt"/>.
    ///
    /// The engine then works down a ladder so that a set missing the exact combination still plays
    /// something: the mode with the stance on it, the mode alone, <c>walk</c> with the stance, and
    /// finally plain <c>walk</c>.
    ///
    /// <para>
    /// **It is not run every frame.** Thirteen places call the locomotion's choice, and
    /// every one of them is an event: the walker starting, stopping or changing
    /// speed, a weapon going into the hand, the end of a conversation, a
    /// revive, a mind waking up, and a few more. So whatever else is on the
    /// motion channel — `listen` and `talk` in a conversation, and every motion a script plays with
    /// `PlayActorMotion` — **stays there until the character's locomotion actually changes**.
    /// Vitomir used to call this on every frame, which put `stand` straight back over all of them:
    /// during the conversation, Christof and Wilhem did not animate their
    /// conversation modes. This now plays only when what it would choose changes (which is
    /// every walker event above that matters), and <see cref="Reanimate"/> is the explicit call for
    /// the events that are not a change of speed.
    /// </para>
    /// </summary>
    void Animate(bool force = false)
    {
        if (Thing.Animation is not { } animation || Motions is null) return;
        int gait = Speed <= 0f ? ModeStand : Speed > Rules.GameRules.Current.Movement.RunAnimationBeyond ? ModeRun : ModeWalk;
        // A character is made playing its idle (`ThingAnimation.For`), which is the standing,
        // unarmed locomotion — so the first look at it starts from there rather than from nothing,
        // unless it is holding something it stands differently with: that one moves to its
        // `stand_onehand` (or the like) from the first frame, as everybody in a place that has just
        // loaded in 1.1 starts together.
        int stance = Stance;
        bool fresh = animatedOn is null;
        animatedOn ??= animation;
        if (!force && gait == animatedWant && Speed == animatedSpeed && ReferenceEquals(animation, animatedOn)
            && !(fresh && stance != 0)) return;
        (animatedWant, animatedSpeed, animatedOn) = (gait, Speed, animation);
        // **The limp is looked at only here**, when one of the events above has called for a new
        // choice: a walk turns into a limp, and a run stays a run whatever the bit says.
        // Nothing calls the choice when the bit changes — none of its twelve callers is the effect
        // recompute — so a character wounded in mid-stride keeps walking until the walker next
        // starts, stops or changes speed, and one healed keeps limping as long.
        int want = gait == ModeWalk && Hurt ? ModeLimp : gait;
        // The utility stance first: a torch in the off hand asks for `stand_util`,
        // `walk_util_onehand` and the rest, and only that one mode — a set without it falls through
        // to the ordinary ladder below.
        if (Rpg.Utility.Stance(Thing.Actor) && Motions.Find(animation.Set, want + UtilityStance) is { } util)
        {
            animation.Play(util.Mode, util.Animation, Motions, onMotion: true,
                           rate: RateFor(util.Mode, util.Animation, want, Speed));
            return;
        }
        foreach (int mode in (ReadOnlySpan<int>)[want + stance, want, ModeWalk + stance, ModeWalk])
        {
            // Looked up here rather than inside Play, because a set that lists the same mode twice
            // rolls between the two on every lookup and asking again could land on the other one.
            if (Motions.Find(animation.Set, mode) is not { } found) continue;
            animation.Play(found.Mode, found.Animation, Motions, onMotion: true,
                           rate: RateFor(found.Mode, found.Animation, want, Speed));
            return;
        }
    }

    /// <summary>The gait last chosen (limp counted as walk), and on which animation object.</summary>
    int animatedWant = ModeStand;
    float animatedSpeed;
    Anim.ThingAnimation? animatedOn;

    /// <summary>
    /// The locomotion's choice on demand: put the locomotion back on the motion channel whatever is there now. The
    /// end of a conversation, a revive, a mind waking up, a weapon going into the hand and an idle
    /// flourish running out each call it (see <see cref="Animate"/>).
    /// </summary>
    public void Reanimate() => Animate(force: true);

    /// <summary>
    /// The idle flourish: the character's <c>idle</c> — or its weapon's own,
    /// <c>idle_onehand</c> and the rest, sixteen modes on from <see cref="ModeIdle"/> by the same
    /// <see cref="Stance"/> the locomotion uses — on the **motion** channel at 30 frames a second.
    /// Nothing happens to a character that is moving, or whose set has no idle; 74 of the 214
    /// shipped sets have one, every one <c>noloop fidget</c>, and the fidget flag is what brings
    /// the stand back when it runs out (<see cref="ThingAnimation.FidgetTagType"/>). The utility
    /// stance's own three (<c>idle_util</c>…) wait on the utility stance itself.
    /// Returns how long it runs in milliseconds, or −1 when nothing played.
    /// </summary>
    public int Fidget()
    {
        if (Thing.Animation is not { } animation || Motions is null || Moving) return -1;
        // The utility stance's own idle, `idle_util` and its two kin, and only that one.
        if (Rpg.Utility.Stance(Thing.Actor)
            && Motions.Find(animation.Set, ModeIdle + UtilityIdleStance) is { } util)
            return animation.Play(util.Mode, util.Animation, Motions, onMotion: true);
        int stance = Stance;
        foreach (int mode in (ReadOnlySpan<int>)[ModeIdle + stance, ModeIdle])
        {
            if (Motions.Find(animation.Set, mode) is not { } found) continue;
            return animation.Play(found.Mode, found.Animation, Motions, onMotion: true);
        }
        return -1;
    }

    /// <summary>
    /// How far either gait fix may move a rate: half speed to double. **Ours**, and chosen from the
    /// measurement rather than picked — <c>vitomir --gait</c> puts 97.7% of the game's walk modes
    /// and 82.5% of its run modes inside it. What falls outside is not a bad measurement but a
    /// shipped shortcut: 53 templates run at 250 on one animation drawn for 207, and a handful of
    /// sets have no run animation at all and point <c>run</c> at their walk, which would otherwise
    /// need five or six times speed and look like a cartoon.
    /// </summary>
    public const float GaitFloor = 0.5f, GaitCeiling = 2f;

    /// <summary>
    /// The rate to play a locomotion cycle at. Faithfully <see cref="Anim.ThingAnimation.Rate"/> —
    /// a flat 30 frames a second, whatever the character's speed, which is all the original ever
    /// does. The two <see cref="Fixes"/> change that, and only for the modes the game marks with
    /// <see cref="NmsFlags.MoveSpeed"/>, which is every walk, run and limp in the game and nothing
    /// else:
    /// <list type="bullet">
    /// <item><c>matchedgait</c> plays the cycle at the rate that makes its feet match the ground,
    /// from the stride measured out of the animation's own footfall tags;</item>
    /// <item><c>movespeed</c> scales the rate by how fast the character is moving against the speed
    /// its template gives it — the flag's own intent, which matters only once something (Celerity)
    /// moves a character faster than its template says.</item>
    /// </list>
    /// <c>matchedgait</c> works from the actual speed, so it already covers what <c>movespeed</c>
    /// does and wins when both are on.
    /// </summary>
    /// <param name="switches">
    /// Which fixes are in force. Null takes <see cref="Fixes.Current"/>, which is what the game
    /// does; a **test passes its own**, because installing the global would change what every other
    /// test running beside it sees.
    /// </param>
    public float RateFor(NmsMode mode, NadAnimation animation, int gait, float speed,
                         Fixes? switches = null)
    {
        float rate = Anim.ThingAnimation.Rate;
        if (speed <= 0f || !mode.Has(NmsFlags.MoveSpeed) || Thing.Animation is not { } animation2) return rate;
        var fixes = switches ?? Fixes.Current;

        if (fixes.MatchedGait && Motions is not null &&
            Motions.StrideOf(animation2.Model, animation2.Set, animation) is { } stride)
            return Math.Clamp(Anim.Stride.RateFor(speed, animation.Duration, stride),
                              rate * GaitFloor, rate * GaitCeiling);

        if (fixes.MoveSpeed)
        {
            float template = gait == ModeRun ? RunSpeed : WalkSpeed;
            if (template > 0f) rate = Math.Clamp(rate * speed / template, rate * GaitFloor, rate * GaitCeiling);
        }
        return rate;
    }

    /// <summary>Degrees folded into (−180, 180].</summary>
/// <summary>
    /// A heading in the range the mover keeps them in, (−180, 180]. Public because
    /// <see cref="WorldThing.Face"/> has to produce the same number for the same direction: one
    /// heading arrives from a turn and the other from a look-at, and they are compared.
    /// </summary>
    public static float Wrap(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees <= -180f) degrees += 360f;
        return degrees;
    }
}
