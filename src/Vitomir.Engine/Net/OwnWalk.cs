// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's own character predicted on her machine. Ours: 1.1's player's machine
// waits for the host's walk message before its copy moves at all.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Her own walk, at once, on her machine** (*"Yes to all, let's do it"*; part of the
/// network layer, as the sealed session is, outside the faithful, Fixes and QoL settings; on by default, no player's option,
/// <c>--no-predict</c> a developer's switch to compare). The host stays the judge of everything: it walks her by the same
/// order, finds its own path and sends where she is; this machine only stops waiting to show what her own click certainly causes.
///
/// <list type="bullet">
/// <item>**A click to walk** (<see cref="Player.WalkTo"/> on her machine) sends the move order with a number
/// (<see cref="NetOrder.Seq"/>) and starts the same walk here, along the path this machine finds from the same place on the same
/// walk map (<see cref="Walked"/>). A stop she makes (the cancel, Space) is numbered the same way and stands her here at once.</item>
/// <item>**Her orders not yet answered are kept**: the host answers each numbered order with <see cref="Msg.Taken"/> (its number,
/// what came of it, and the host's time it was carried out). Until then the host's snapshots of her are of before it, and are not
/// used for her place (her health, her death and her action channel are, as for anybody).</item>
/// <item>**When the host's snapshot says where she is** (a time <c>T</c> at or after the order's <c>A</c>), it is compared with
/// where her walk here was at the same time into it (<c>T - A</c> after the click, from the walk's history): the host's place,
/// her own steps since laid on top (the history holds her places with the corrections taken out, so a correction is counted
/// once). A difference under <see cref="Noise"/> is nothing; under <see cref="SnapBeyond"/> it is **blended** over a few frames
/// (<see cref="BlendRate"/>); a larger one **snaps** her there at once and walks her on from there to the order's point (a new
/// search, no order sent).</item>
/// <item>**A refused order** (the host's answer not <see cref="Rpg.OrderResult.Started"/>: busy, paused, possessed, nobody's way
/// there) stops her walk here and gives her back to the snapshots; so does the end of the host's walk while hers here goes on
/// (blocked there, arrived early here), and an order whose movement is the host's (an attack, a use, a pick-up, a feed: the
/// approach is the host's to find).</item>
/// <item>**The end**: once both walks are over she is the snapshots' again (<see cref="WorldMirror"/>, eased the last few units).</item>
/// <item>**Her keyboard walk** (<c>[qol] driving</c>, <see cref="Drive"/>): the push moves her here at once with the
/// single-player <see cref="Player.Move"/> as the host's will, while the push goes to the host as before (<see cref="GameClient.Drive(System.Numerics.Vector2, bool)"/>,
/// unchanged on the wire). The push has no number and no answer, so the host's time is tied to hers by her place: the first snapshot
/// that has her moved from where the host had her standing is matched with the moment her walk here had gone as far
/// (<see cref="AlignFrom"/>); from then on each snapshot is compared as a click's walk is, blended or snapped the same. Letting go stands
/// her here at once; once the host has her standing too, past her release, she is the snapshots' again.</item>
/// </list>
/// Everybody else is as before, eased between the host's snapshots. Deterministic under a tape: every time read is the place's
/// clock or the host's in a message, so a player's tape replays the same (the tape says whether it was on: <c>netpredict</c>).
/// </summary>
public sealed class OwnWalk(Func<WorldThing?> me)
{
    /// <summary>On for every player's machine made from now on unless the developer's <c>--no-predict</c> (or a tape made without it) says not.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Whether this machine predicts her (<see cref="Enabled"/> when it was made).</summary>
    public bool On { get; set; } = Enabled;

    /// <summary>Her keyboard walk predicted too, unless the developer's <c>--no-predict</c> or a tape made earlier says not (<c>netpredict ... drives</c>).</summary>
    public static bool DrivesEnabled { get; set; } = true;

    /// <summary>Whether this machine predicts her keyboard walk (<see cref="DrivesEnabled"/> when it was made; nothing without <see cref="On"/>).</summary>
    public bool Drives { get => drives && On; set => drives = value; }
    bool drives = DrivesEnabled;

    /// <summary>
    /// A difference under this many units is nothing: a frame of her run at 30 frames a second (250 units a second), the most the
    /// two walks can stand apart by starting a frame apart. Measured: they agree within about a unit (<c>NetPredictionTests</c>).
    /// </summary>
    public const float Noise = 8f;

    /// <summary>A difference of this many units or more snaps her to the host's place (two of her radii): a smaller one is blended.</summary>
    public const float SnapBeyond = 64f;

    /// <summary>How fast a blend closes, a second: the rest falls by 1 - e^(-rate x seconds) a frame, most of it in four frames at 30 a second.</summary>
    public const float BlendRate = 20f;

    /// <summary>How much of her walk's history is kept (her places, newest last), in the place's milliseconds.</summary>
    public const int HistoryMs = 4000;

    /// <summary>How long an order's answer may take before her walk here is given back to the snapshots (ours; a reliable answer lost only with the connection).</summary>
    public const int AnswerWaitMs = 5000;

    /// <summary>
    /// How far the host must have moved her from where it had her standing (units, flat) before her keyboard walk's start is matched
    /// with the host's: half a frame of her walk at 30 frames a second, so the first snapshot after the host's first step does.
    /// </summary>
    public const float AlignFrom = 2f;

    /// <summary>
    /// How long the host's snapshots must have had her on one spot (its milliseconds) for her keyboard walk to start from there:
    /// a snapshot's interval and a little, two snapshots the same.
    /// </summary>
    public const int HostRestMs = 90;

    /// <summary>
    /// How long past her release (her time, on the host's matched to it) the host's snapshot of her standing ends her keyboard walk
    /// here: the release's way out may be a few frames later than her push's was, by the jitter.
    /// </summary>
    public const int ReleaseSettleMs = 100;

    /// <summary>
    /// How long the host may have her stand still while she walks here (its milliseconds) before her keyboard walk is given back to the
    /// snapshots: a refusal of the host's that her machine could not see (busy there, the Storyteller's body, a body in her way
    /// there and not here). Two and a half snapshots.
    /// </summary>
    public const int HostStoppedMs = 250;

    /// <summary>The least her keyboard walk waits for the host to move her before it counts as refused: see <see cref="DriveWaitMs"/>.</summary>
    public const int DriveWaitFloorMs = 500;

    WorldMirror? mirror;
    ushort numbered;
    ushort pending;
    OrderKind pendingKind;
    bool acked, active;
    int ackMs, startMs;
    readonly List<(int Ms, Vector3 Raw)> history = [];
    Vector3 decided, applied;
    ThingState? lastHost;
    Player? walker;
    Vector3 point;
    int pointNode;
    bool? run;
    float speed;
    // Her keyboard walk (Drive).
    bool driving, aligned, pushing, pressRefused;
    int offsetMs, releasedMs = -1, movedHereMs = -1, pressSnaps, waitMs = DriveWaitFloorMs;
    int? learnedOffset;
    Player? driver;
    Vector3 hostRest;
    // The host's words on her place, whoever walks her (Seen).
    bool hostSeen;
    Vector3 hostLast;
    int hostLastMs, hostStillSinceMs;

    /// <summary>The console's account of a keyboard walk given back (rare: each says why).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>What it did (for the measurement and the console's account).</summary>
    public Counts Done { get; } = new();

    /// <summary>The tally of her walks and their corrections.</summary>
    public sealed class Counts
    {
        /// <summary>Walks and stops started here at once.</summary>
        public int Predicted;
        /// <summary>Host's places compared with hers (snapshots after the answer).</summary>
        public int Compared;
        /// <summary>Differences blended in, and snapped.</summary>
        public int Blended, Snapped;
        /// <summary>Orders the host refused, and walks the host ended before hers here.</summary>
        public int Refused, EndedThere;
        /// <summary>The largest difference seen, and the sum of all compared (units).</summary>
        public float Largest, Total;
        /// <summary>Every difference compared, in order (units).</summary>
        public readonly List<float> Errors = [];
        /// <summary>Keyboard walks started here at once; of them, the ones the host never moved her for (<see cref="DriveWaitMs"/>), stopped her in (<see cref="HostStoppedMs"/>) or shoved her twice in (two snaps).</summary>
        public int Drives, DriveRefused, DriveStopped, DriveShoved;
    }

    /// <summary>
    /// The console's account of it (said as her machine leaves the game): <c>net: own walk predicted: 3 clicks, 5 keyboard walks;
    /// 120 compared, 2 blended, 0 snapped, 0 refused, 0 keyboard walks given back; mean 0.4 u, largest 11.7 u</c>.
    /// </summary>
    public string Line()
    {
        var d = Done;
        return $"net: own walk predicted: {d.Predicted} clicks, {d.Drives} keyboard walks; {d.Compared} compared, {d.Blended} blended, {d.Snapped} snapped, " +
               $"{d.Refused} refused, {d.DriveRefused + d.DriveStopped + d.DriveShoved} keyboard walks given back ({d.DriveRefused} not moved there, {d.DriveStopped} stood there, {d.DriveShoved} moved there); " +
               $"mean {(d.Compared == 0 ? 0f : d.Total / d.Compared):0.0} u, largest {d.Largest:0.0} u";
    }

    /// <summary>Whether this machine is walking her itself now (the snapshots leave her place, her facing and her gait alone).</summary>
    public bool Active => active;

    /// <summary>Whether <paramref name="thing"/> is her and is being walked here (<see cref="Active"/>).</summary>
    public bool Owns(WorldThing thing) => active && ReferenceEquals(me(), thing);

    /// <summary>Whether <paramref name="thing"/> is her own character, whose walks this predicts.</summary>
    public bool Is(WorldThing thing) => On && ReferenceEquals(me(), thing);

    /// <summary>Whether the walk predicted here now is her keyboard's (<see cref="Drive"/>).</summary>
    public bool Driving => active && driving;

    /// <summary>How long her keyboard walk waits here for the host to move her (this machine's time): two round trips and 300 ms, at least <see cref="DriveWaitFloorMs"/>.</summary>
    public int DriveWaitMs => waitMs;

    /// <summary>The world this machine mirrors (each place's, at <see cref="GameClient.Enter"/>); null forgets it all (a journey).</summary>
    public void Attach(WorldMirror? world)
    {
        mirror = world;
        active = false;
        pending = 0;
        history.Clear();
        decided = applied = Vector3.Zero;
        lastHost = null;
        walker = null;
        driving = aligned = hostSeen = pressRefused = false;
        driver = null;
        learnedOffset = null;
        releasedMs = -1;
    }

    /// <summary>
    /// An order of hers going to the host (<see cref="GameClient.Send(NetOrder)"/>): its number. A move or a stop is predicted (the
    /// walk is started by <see cref="Walked"/>; a stop has stood her already, by the cancel that sent it); any other order's
    /// movement is the host's, so her walk here ends and the snapshots have her.
    /// </summary>
    public ushort Numbered(OrderKind kind)
    {
        numbered = (ushort)(numbered == ushort.MaxValue ? 1 : numbered + 1);
        // A stop while her keyboard walks her changes nothing on the host (its next turn walks her by the push again), so none here.
        if (kind == OrderKind.Stop && Driving) return numbered;
        if (kind == OrderKind.Stop && me() is { } her) Begin(numbered, kind, her);
        else if (kind != OrderKind.Move) End(stopHere: true);
        return numbered;
    }

    /// <summary>
    /// The number a cast of hers goes with (<see cref="OwnCast"/>; <see cref="NetCast.Seq"/>): the same count as her orders', so
    /// the host's one answer (<see cref="Msg.Taken"/>) names either. Her walk here is left as it is: a cast not guessed stops her on
    /// the host, and the snapshots end her walk here as before; a guessed one stands her (<see cref="Stand"/>).
    /// </summary>
    public ushort NumberedCast() => numbered = (ushort)(numbered == ushort.MaxValue ? 1 : numbered + 1);

    /// <summary>The number the last order of hers went with (<see cref="Numbered"/>): her swing's and her cast's, for <see cref="OwnStrike"/> and <see cref="OwnCast"/>.</summary>
    public ushort Last => numbered;

    /// <summary>
    /// She stands where she is now, as for a stop (<see cref="OwnStrike"/>): her attack's order, just numbered, will swing at
    /// once on the host, which stands her there as the cancel at her click stood her here; her place is compared with the host's
    /// once it answers, instead of being given to the snapshots, a round trip old, which would ease her back along her walk.
    /// </summary>
    public void Stand(WorldThing her)
    {
        if (!On || !ReferenceEquals(me(), her) || numbered == 0) return;
        walker = null;
        Begin(numbered, OrderKind.Stop, her);
    }

    /// <summary>
    /// The walk her click asked for, started here as the move order went (<see cref="Player.WalkTo"/>): <paramref name="started"/>
    /// says whether this machine found a way; with none, the host's walk is shown as before.
    /// </summary>
    public void Walked(Player by, Vector3 to, int node, bool? running, float pace, bool started)
    {
        // A click while her keys push her is the host's walk for no longer than its next turn, whose push cancels the route (as
        // here: the frame's push clears it); her keyboard walk goes on.
        if (Driving && pushing) return;
        if (!started) { End(stopHere: false); return; }
        walker = by;
        point = to;
        pointNode = node;
        run = running;
        speed = pace;
        Begin(numbered, OrderKind.Move, by.Thing);
    }

    void Begin(ushort number, OrderKind kind, WorldThing her, bool drive = false)
    {
        driving = drive;
        aligned = false;
        releasedMs = movedHereMs = -1;
        if (!drive) driver = null;
        pending = number;
        pendingKind = kind;
        acked = false;
        active = true;
        startMs = her.Place?.Milliseconds ?? 0;
        history.Clear();
        decided -= applied;                   // a blend under way goes on from here
        applied = Vector3.Zero;
        history.Add((startMs, her.Position));
        mirror?.Release(her);
        if (drive) Done.Drives++;
        else Done.Predicted++;
    }

    /// <summary>The host's answer to a numbered order (<see cref="Msg.Taken"/>): when it was carried out, or that it was not.</summary>
    public void Taken(ushort number, Rpg.OrderResult result, int hostMs)
    {
        if (!active || number != pending) return;
        acked = true;
        ackMs = hostMs;
        if (pendingKind == OrderKind.Move && result != Rpg.OrderResult.Started)
        {
            Done.Refused++;
            End(stopHere: true);
        }
    }

    /// <summary>
    /// Every snapshot's word on her place, whoever walks her (<see cref="WorldMirror.Apply"/>): where the host had her last, and
    /// since when on that spot — for a keyboard walk's start (<see cref="Drive"/>) and its end.
    /// </summary>
    public void Seen(int hostMs, ThingState s)
    {
        if (!hostSeen || Flat(s.Position - hostLast) >= 0.5f) hostStillSinceMs = hostMs;
        hostSeen = true;
        hostLast = s.Position;
        hostLastMs = hostMs;
        lastHost = s;
    }

    /// <summary>A snapshot's word on her (<see cref="WorldMirror.Apply"/>, while <see cref="Owns"/>): compared with her walk here.</summary>
    public void Host(int hostMs, ThingState s)
    {
        lastHost = s;
        if (driving) { Drove(hostMs, s); return; }
        if (!active || !acked || hostMs < ackMs || me() is not { } her) return;
        int into = hostMs - ackMs;
        if (Compare(her, s.Position, RawAt(startMs + into))) return;
        // The host's walk is over: hers here too, or she is given back to the snapshots.
        bool hostWalks = (s.Flags & ThingState.Moving) != 0;
        bool walksHere = walker is { Walking: true } && ReferenceEquals(walker.Thing, her);
        if (!hostWalks && walksHere && pendingKind == OrderKind.Move) { Done.EndedThere++; End(stopHere: true); }
        else if (!hostWalks && !walksHere) End(stopHere: false);
    }

    /// <summary>
    /// The host's place for her against her walk's here at <paramref name="raw"/> (the same time into it, the corrections taken out):
    /// nothing, blended or snapped. Answers whether the walk here ended for it (a keyboard walk shoved twice).
    /// </summary>
    bool Compare(WorldThing her, Vector3 host, Vector3 raw)
    {
        var d = host - (raw + decided);
        float err = d.Length();
        Done.Compared++;
        Done.Errors.Add(err);
        Done.Total += err;
        if (err > Done.Largest) Done.Largest = err;
        if (err >= SnapBeyond)
        {
            decided += d;
            Snap(her);
            Done.Snapped++;
            // A second large difference in one press is somebody else moving her there (the Storyteller with her body, a
            // script): the snapshots have her for the rest of it.
            if (driving && pushing && ++pressSnaps >= 2)
            {
                Log?.Invoke($"net: own keyboard walk given back: the host has her {err:0} u from her walk here, a second time in one press");
                Done.DriveShoved++;
                pressRefused = true;
                End(stopHere: true);
                return true;
            }
        }
        else if (err >= Noise)
        {
            decided += d;
            Done.Blended++;
        }
        return false;
    }

    /// <summary>
    /// A snapshot's word on her while her keyboard walks her here: its time matched with hers at the first that has her moved
    /// on the host, then compared; the host standing her while she walks here gives her back to the snapshots; the host standing her
    /// past her release ends the walk here.
    /// </summary>
    void Drove(int hostMs, ThingState s)
    {
        if (!active || me() is not { } her) return;
        if (!aligned)
        {
            float moved = Flat(s.Position - hostRest);
            if (moved < AlignFrom) return;
            // She went less far here than the host has her gone (held here by a wall or a body the host's copy of her slides past,
            // or let go before the host's walk ran out): matched with her newest place, the difference corrected from there.
            int at = TimeAt(moved) ?? history[^1].Ms;
            offsetMs = at - hostMs;
            learnedOffset = offsetMs;
            aligned = true;
        }
        int local = hostMs + offsetMs;
        var raw = RawAt(local);
        if (Compare(her, s.Position, raw)) return;
        bool hostStill = hostMs > hostStillSinceMs;
        bool walkingHere = releasedMs < 0 || local < releasedMs;
        // The host has her stand while she walks here: a refusal her machine did not see. The snapshots have her for the rest of the press.
        if (walkingHere && hostMs - hostStillSinceMs >= HostStoppedMs && Flat(raw - RawAt(hostStillSinceMs + offsetMs)) is var gone && gone > 2 * Noise)
        {
            Log?.Invoke($"net: own keyboard walk given back: the host has stood her {hostMs - hostStillSinceMs} ms at {s.Position.X:0},{s.Position.Y:0} while she walked {gone:0} u here");
            Done.DriveStopped++;
            Done.EndedThere++;
            pressRefused = pushing;
            End(stopHere: true);
        }
        else if (releasedMs >= 0 && local >= releasedMs + ReleaseSettleMs && hostStill && driver is not { Walking: true }) End(stopHere: false);
    }

    /// <summary>When her walk here had first gone <paramref name="far"/> units (flat) from its start, between the history's samples; null when it has not yet.</summary>
    int? TimeAt(float far)
    {
        if (history.Count == 0) return null;
        var from = history[0].Raw;
        float was = 0f;
        for (int i = 1; i < history.Count; i++)
        {
            float d = Flat(history[i].Raw - from);
            if (d >= far)
            {
                float t = d - was < 1e-4f ? 1f : (far - was) / (d - was);
                return history[i - 1].Ms + (int)MathF.Round(t * (history[i].Ms - history[i - 1].Ms));
            }
            was = MathF.Max(was, d);
        }
        return null;
    }

    /// <summary>The farthest her walk here has gone from its start (units, flat).</summary>
    float Farthest() => history.Count == 0 ? 0f : history.Max(h => Flat(h.Raw - history[0].Raw));

    static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>
    /// **Her keyboard walk, at once** (*"yes, predict it"*; the frame's push each frame, <c>[qol] driving</c>, while
    /// the push itself goes to the host as before). Moves her here with the single-player <see cref="Player.Move"/>, the host's own walk
    /// for her push, and answers whether it did (false: the snapshots show her, as earlier).
    ///
    /// The host's refusals, on her machine: **paused** (the host's pause, as it has said: not started, and one under way ends there),
    /// **dead, held or busy** (her copy's, as <see cref="Rpg.Actions.ReadyToMove"/> asks, and doing nothing but standing or walking with
    /// nothing on her action channel, as <see cref="OwnCast"/>'s rule is: what the host's cancel would end is the host's to end), **an
    /// order, swing or cast of hers unanswered** (<paramref name="waiting"/>: it may keep her busy there), and **<c>driving</c> off
    /// on the host** (the frame makes no push then). What her machine cannot see — the Storyteller with her body, a body in her way
    /// there and not here, a business of the host's — is corrected: the host not moving her within <see cref="DriveWaitMs"/>, or
    /// standing her for <see cref="HostStoppedMs"/> while she walks here, or a second snap, gives her back to the snapshots (eased to
    /// where the host has her) for the rest of the press. A push gone stale on the host (300 ms unrenewed, a few datagrams lost) stands
    /// her there a moment and the difference is blended in.
    /// </summary>
    public bool Drive(Player by, Vector2 push, bool run, float seconds, bool paused, bool waiting, float roundTripMs)
    {
        var her = by.Thing;
        bool pushed = push.LengthSquared() > 1e-6f;
        if (!pushed) { pressRefused = false; pressSnaps = 0; }
        pushing = pushed;
        if (!Drives || !ReferenceEquals(me(), her) || !her.InWorld || her.Place is not { } place) return false;
        int now = place.Milliseconds;
        int stepMs = (int)MathF.Round(seconds * 1000f);
        waitMs = Math.Max(DriveWaitFloorMs, (int)(2f * roundTripMs) + 300);
        bool free = !paused && !waiting && Free(her, now);
        if (Driving)
        {
            if (paused) { End(stopHere: true); return false; }
            if (pushed && free)
            {
                releasedMs = -1;
                by.Running = run;
                by.Move(push, seconds);
            }
            else
            {
                if (releasedMs < 0) releasedMs = now;
                if (!by.Walking) by.Stop();                  // stood here at once, as the single-player keyboard's let-go
            }
            Record(her, now + stepMs);
            if (movedHereMs < 0 && Flat(history[^1].Raw - history[0].Raw) >= AlignFrom) movedHereMs = now;
            return true;
        }
        if (!pushed || pressRefused || !free || seconds <= 0f) return false;
        // Where the host's time is tied to hers: her start's, when the host has her standing; or the last found, or her click walk's
        // answer, when the host walks her now (a route the keyboard takes over); with neither, the snapshots show her (as earlier).
        bool fromRest = hostSeen && hostLastMs - hostStillSinceMs >= HostRestMs;
        int offset;
        if (fromRest) offset = 0;
        else if (learnedOffset is { } learned) offset = learned;
        else if (active && acked && !driving) offset = startMs - ackMs;
        else return false;
        Begin(0, OrderKind.Move, her, drive: true);
        driver = by;
        walker = null;
        aligned = !fromRest;
        offsetMs = offset;
        hostRest = hostLast;
        if (!by.Map.IsGround(by.Node) || !by.Map.Over(by.Node, her.Position)) by.Ground(snap: false);   // the snapshots moved her
        by.Running = run;
        by.Move(push, seconds);
        Record(her, now + stepMs);
        if (Flat(history[^1].Raw - history[0].Raw) >= AlignFrom) movedHereMs = now;
        return true;
    }

    /// <summary>Her copy free to walk at once, as the host's <see cref="Rpg.Actions.ReadyToMove"/> will find it, with nothing for the host's cancel to end.</summary>
    static bool Free(WorldThing her, int now)
    {
        if (her.Actor is not { } a) return true;
        if (now < a.BusyUntil || a.Dead || a.Has2(Actor.Flag2.Restrained)) return false;
        if (a.Action is not (Rpg.ActionMode.None or Rpg.ActionMode.Move)) return false;
        return her.Animation?.Action is not { } playing || Anim.ChatLips.Local(playing);
    }

    /// <summary>Her place after this frame's step, in the history at the step's end (her keyboard walk's own, as the host's snapshot is after its turn's).</summary>
    void Record(WorldThing her, int ms)
    {
        var raw = her.Position - applied;
        if (history.Count > 0 && history[^1].Ms >= ms) history[^1] = (history[^1].Ms, raw);
        else history.Add((ms, raw));
        int old = 0;
        while (old < history.Count - 2 && history[old + 1].Ms < ms - HistoryMs) old++;
        if (old > 0) history.RemoveRange(0, old);
    }

    /// <summary>One frame (<see cref="GameClient.Poll"/>): her place kept in the history, and the blend moved on.</summary>
    public void Step(float seconds)
    {
        if (!active || me() is not { InWorld: true, Place: { } place } her) return;
        int now = place.Milliseconds;
        if (driving)
        {
            // The host has not moved her for her push in two round trips: refused there (busy, the Storyteller's) — given back.
            // Not while she has not moved here either (pushed into a wall: the host stands her too); nothing to give back once let go.
            if (!aligned && movedHereMs < 0 && releasedMs >= 0) { End(stopHere: false); return; }
            if (!aligned && movedHereMs >= 0 && now - movedHereMs > waitMs)
            {
                Log?.Invoke($"net: own keyboard walk given back: the host has not moved her in {now - movedHereMs} ms (its last word {Flat(hostLast - hostRest):0.0} u from where it had her, " +
                            $"{hostLastMs - hostStillSinceMs} ms on that spot; she went {Farthest():0} u here)");
                Done.DriveRefused++;
                pressRefused = pushing;
                End(stopHere: true);
                return;
            }
            if (releasedMs >= 0 && now - releasedMs > AnswerWaitMs) { End(stopHere: false); return; }
        }
        else if (!acked && now - startMs > AnswerWaitMs) { End(stopHere: false); return; }
        var rest = decided - applied;
        if (rest.LengthSquared() > 1e-6f)
        {
            var step = rest.Length() < 0.5f ? rest : rest * (1f - MathF.Exp(-seconds * BlendRate));
            place.Move(her, her.Position + step);
            applied += step;
            if (driver is { } d && !d.Map.Over(d.Node, her.Position)) d.Ground(snap: false);   // the walk map's piece under her
        }
        if (driving) return;                                   // her keyboard walk keeps its own history (Drive)
        // Her move order here follows her walk here while it is hers (the snapshots' word on it is the host's walk, a round trip
        // behind): so a cancel on this machine finds her walking, stops her here and tells the host (ForwardedOrders.Cancelled).
        if (her.Actor is { } a && walker is not null && ReferenceEquals(walker.Thing, her))
        {
            if (walker.Walking && a.Action == Rpg.ActionMode.None) a.Action = Rpg.ActionMode.Move;
            else if (!walker.Walking && a.Action == Rpg.ActionMode.Move) a.Action = Rpg.ActionMode.None;
        }
        var raw = her.Position - applied;
        if (history.Count > 0 && history[^1].Ms == now) history[^1] = (now, raw);
        else history.Add((now, raw));
        int old = 0;
        while (old < history.Count - 2 && history[old + 1].Ms < now - HistoryMs) old++;
        if (old > 0) history.RemoveRange(0, old);
    }

    /// <summary>Her place here at <paramref name="ms"/>, the corrections taken out: the history's, between its samples.</summary>
    Vector3 RawAt(int ms)
    {
        if (history.Count == 0) return Vector3.Zero;
        if (ms <= history[0].Ms) return history[0].Raw;
        for (int i = 1; i < history.Count; i++)
            if (history[i].Ms >= ms)
            {
                var (a, b) = (history[i - 1], history[i]);
                float t = b.Ms == a.Ms ? 1f : (ms - a.Ms) / (float)(b.Ms - a.Ms);
                return Vector3.Lerp(a.Raw, b.Raw, t);
            }
        return history[^1].Raw;
    }

    /// <summary>A large difference: put where the host has her, her steps since on top, and walked on from there.</summary>
    void Snap(WorldThing her)
    {
        if (her.Place is not { } place) return;
        place.Move(her, her.Position + (decided - applied));
        applied = decided;
        if (driver is { } d && !d.Map.Over(d.Node, her.Position)) d.Ground(snap: false);
        if (driving) return;
        if (pendingKind == OrderKind.Move && walker is { Walking: true } w && pointNode >= 0)
            w.WalkHere(point, pointNode, run, speed, clamp: true, persist: false, mind: false);
    }

    /// <summary>Her walk here ends (<paramref name="stopHere"/>: stood where she is) and the snapshots have her again, eased from here.</summary>
    void End(bool stopHere)
    {
        if (!active) return;
        active = false;
        pending = 0;
        if (stopHere && walker is { Walking: true } w && me() is { } her && ReferenceEquals(w.Thing, her)) w.Stop();
        if (stopHere && driving && driver is { } d && ReferenceEquals(d.Thing, me())) d.Stop();   // her keyboard walk stood here
        driving = aligned = false;
        driver = null;
        releasedMs = -1;
        if (me() is { } mine && lastHost is { } s) mirror?.Follow(mine, s);
        decided = applied = Vector3.Zero;
        history.Clear();
    }
}
