// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Keeping a client's world the host's. **Ours**: 1.1 sends a message for each thing that changes
// (a walk, a position, a strike, a death), most of
// whose bodies are unread; Vitomir sends what the things *are* instead — ten times a second, where
// every moving thing is, which way it faces and what it is playing — and reliably, which things
// came into the world and which left it. This also folds in what 1.1 sends as its own events: a
// tracked thing (a lift, a door, a train) starting a new track section (message 0x21) and its keyframe transform (message 0x22) are just `Position`/`Orientation` below;
// an animation mode played on a thing (message 0x23: mode, flags, rate — from casting,
// `PlayMotionSetMode` and the AI) is `MotionMode`/`ActionMode` and their rates, both channels together.
//
// Both ends load the same level and scene from their own install, so the things the scene placed
// are already on both, with the same GUIDs: the loader numbers them 1, 2, 3 … in the scene's order
// (`Location.BuildThings`), and a player the harness adds comes after all of them. Anything else — a
// script's spawn, a dropped sword, another player — the host announces with its template.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>What the host sends about one thing in a snapshot.</summary>
public readonly record struct ThingState(int Guid, Vector3 Position, Vector3 Orientation,
                                         short MotionMode, float MotionRate,
                                         short ActionMode, byte ActionSerial, float ActionRate,
                                         float Health, byte Flags)
{
    public const byte Dead = 0x1;
    /// <summary>
    /// A character (type 0 or 7) under a move order (action 1) — the flag 1.1's walk message carries (its
    /// last byte), with which a player's machine puts its copy in the move order too, so that a cancel there
    /// has something to cancel and tells the host (<see cref="ForwardedOrders.Cancelled"/>).
    /// </summary>
    public const byte Moving = 0x2;
    /// <summary>How many bytes one takes on the wire.</summary>
    public const int Size = 4 + 12 + 12 + 2 + 4 + 2 + 1 + 4 + 4 + 1;

    public void Write(NetWriter w) =>
        w.I32(Guid).Vec(Position).Vec(Orientation).I16(MotionMode).F32(MotionRate)
         .I16(ActionMode).U8(ActionSerial).F32(ActionRate).F32(Health).U8(Flags);

    public static ThingState Read(NetReader r) =>
        new(r.I32(), r.Vec(), r.Vec(), r.I16(), r.F32(), r.I16(), r.U8(), r.F32(), r.F32(), r.U8());
}

/// <summary>
/// A thing the host announces: its GUID, template and where it stands, and a prop's owner, which
/// 1.1's thing-made message carries for a prop (protocol 19), 0 for anything else.
/// </summary>
public readonly record struct ThingMade(int Guid, string Template, Vector3 Position, Vector3 Orientation, int Owner = 0)
{
    public void Write(NetWriter w) => w.I32(Guid).Str(Template).Vec(Position).Vec(Orientation).I32(Owner);
    public static ThingMade Read(NetReader r) => new(r.I32(), r.Str(), r.Vec(), r.Vec(), r.I32());

    /// <summary>A thing's announcement, with its owner when it is a prop (for type 2 only).</summary>
    public static ThingMade Of(WorldThing t) =>
        new(t.Guid, t.Template, t.Position, t.Orientation, t.Type == ThingType.Prop ? t.PropOwner : 0);
}

/// <summary>
/// A projectile the host has fired, for the players' machines to fly as a picture (1.1's message 0x50,
/// which has each machine fire it through the same firing code and give it the
/// host's GUID): its GUID, template, where it is, which way it faces, how fast it goes, and whose it is.
/// </summary>
public readonly record struct ThingFired(int Guid, string Template, Vector3 Position, Vector3 Orientation, Vector3 Velocity, int Owner)
{
    public void Write(NetWriter w) => w.I32(Guid).Str(Template).Vec(Position).Vec(Orientation).Vec(Velocity).I32(Owner);
    public static ThingFired Read(NetReader r) => new(r.I32(), r.Str(), r.Vec(), r.Vec(), r.Vec(), r.I32());
}

/// <summary>The host's side: which things came and went since it last looked, and the snapshot.</summary>
public sealed class WorldWatch
{
    readonly Location place;
    readonly HashSet<int> present = [];
    readonly Dictionary<int, (Vector3 Position, Vector3 Orientation, int ChangedMs)> moved = [];
    readonly Dictionary<int, (object? Channel, byte Serial)> actions = [];

    /// <summary>How long a thing that has stopped moving goes on being sent, to cover a lost packet (ours).</summary>
    public const int SettleMs = 2000;

    public WorldWatch(Location place)
    {
        this.place = place;
        foreach (var t in place.Things) if (!NetSynch.Kept(t)) present.Add(t.Guid);
    }

    public Location Place => place;

    /// <summary>How many things the scene placed, which are on every machine already.</summary>
    public static int SceneCount(Location place) =>
        (place.WorldScene?.Things.Length ?? 0) + (place.ChapterScene?.Things.Length ?? 0);

    /// <summary>
    /// Whether a GUID is one the scene numbered as it was read — the range <see cref="Location.SceneGuids"/>,
    /// which every machine has on the host's numbers (GUIDs are game-wide, no longer 1 to the count).
    /// </summary>
    public static bool FromScene(Location place, int guid) => place.FromScene(guid);

    /// <summary>
    /// What a newcomer needs to be told to have this world: every thing in it the scene did not
    /// place, and every thing the scene placed that is no longer in it.
    /// </summary>
    public (List<ThingMade> Made, List<int> Removed) CatchUp()
    {
        var made = new List<ThingMade>();
        var removed = new List<int>();
        foreach (var t in place.Things)
            if (!FromScene(place, t.Guid) && Replicated(t)) made.Add(Announce(t));
        // A number in the range that answers nothing is a fallback weapon's (LocationGuids.cs) or a thing
        // gone; either way the other machine has nothing by it to take out.
        for (int guid = place.SceneGuids.First; guid < place.SceneGuids.End; guid++)
            if (place.Thing(guid) is not { InWorld: true }) removed.Add(guid);
        return (made, removed);
    }

    /// <summary>The projectiles that came into the world at the last <see cref="Changes"/>.</summary>
    public List<ThingFired> Fired { get; } = [];

    /// <summary>What came into the world and what left it since the last call.</summary>
    public (List<ThingMade> Made, List<int> Removed) Changes()
    {
        var made = new List<ThingMade>();
        var now = new HashSet<int>();
        Fired.Clear();
        foreach (var t in place.Things)
        {
            // A kept thing is never told of, coming or going.
            if (NetSynch.Kept(t)) continue;
            now.Add(t.Guid);
            if (present.Contains(t.Guid)) continue;
            if (Replicated(t)) made.Add(Announce(t));
            else if (t.Type == ThingType.Projectile)
                Fired.Add(new ThingFired(t.Guid, t.Template, t.Position, t.Orientation, t.Velocity, t.SpawnedBy));
        }
        var removed = present.Where(g => !now.Contains(g)).ToList();
        present.Clear();
        present.UnionWith(now);
        foreach (var g in removed) { moved.Remove(g); actions.Remove(g); }
        return (made, removed);
    }

    /// <summary>
    /// Whether a thing is announced and kept in the snapshots. A projectile is not: it is announced
    /// once as fired (<see cref="Fired"/>) and each machine flies its own picture of it, which hurts
    /// nobody there (`Damage.Apply` on a player's machine) and goes when the host's goes. A character
    /// flagged `NO NET SYNCH` — the Storyteller's ghost — is never announced, walked or caught up either
    /// (<see cref="NetSynch.Kept"/>).
    /// </summary>
    static bool Replicated(WorldThing t) => t.Type != ThingType.Projectile && !NetSynch.Kept(t);

    static ThingMade Announce(WorldThing t) => ThingMade.Of(t);

    /// <summary>
    /// The snapshot: every character, and every other thing that has moved in the last
    /// <see cref="SettleMs"/> or is playing something new, in as few messages as fit.
    /// </summary>
    public List<byte[]> Snapshot(int nowMs)
    {
        var states = new List<ThingState>();
        foreach (var t in place.Things)
        {
            if (!Replicated(t)) continue;
            bool actor = t.Actor is not null;
            bool changed = !moved.TryGetValue(t.Guid, out var was)
                ? false
                : was.Position != t.Position || was.Orientation != t.Orientation;
            if (!moved.ContainsKey(t.Guid)) moved[t.Guid] = (t.Position, t.Orientation, int.MinValue / 2);
            if (changed) moved[t.Guid] = (t.Position, t.Orientation, nowMs);
            bool recent = nowMs - moved[t.Guid].ChangedMs < SettleMs;
            var state = State(t);
            bool acting = state.ActionMode >= 0 && actions.TryGetValue(t.Guid, out var a) && a.Serial != 0;
            if (!actor && !recent && !acting) continue;
            states.Add(state);
        }
        var messages = new List<byte[]>();
        int per = (NetPeer.MaxMessage - 16) / ThingState.Size;
        for (int i = 0; i < states.Count; i += per)
        {
            var w = new NetWriter(Msg.Snapshot).I32(nowMs);
            var chunk = states.Skip(i).Take(per).ToList();
            w.U16((ushort)chunk.Count);
            foreach (var s in chunk) s.Write(w);
            messages.Add(w.ToArray());
        }
        if (messages.Count == 0) messages.Add(new NetWriter(Msg.Snapshot).I32(nowMs).U16(0).ToArray());
        return messages;
    }

    ThingState State(WorldThing t)
    {
        var anim = t.Animation;
        var motion = anim?.Motion;
        var action = anim?.Action;
        if (Anim.ChatLips.Local(action)) action = null;             // a chat line's talk is each machine's own
        byte serial = 0;
        if (action is not null)
        {
            actions.TryGetValue(t.Guid, out var track);
            serial = track.Serial;
            if (!ReferenceEquals(track.Channel, action)) serial = (byte)(serial == 255 ? 1 : serial + 1);
            actions[t.Guid] = (action, serial);
        }
        return new ThingState(t.Guid, t.Position, t.Orientation,
            (short)(motion?.Mode.Index ?? -1), motion?.Player.Rate ?? 0f,
            (short)(action?.Mode.Index ?? -1), serial, action?.Player.Rate ?? 0f,
            t.Actor?.Health ?? float.NaN,
            (byte)((t.Actor?.Dead == true ? ThingState.Dead : 0)
                   | (t.Type is ThingType.Actor or ThingType.Player && t.Actor?.Action == Rpg.ActionMode.Move ? ThingState.Moving : 0)));
    }
}

/// <summary>
/// The client's side: the host's GUIDs mapped to this machine's things, what the host says applied
/// to them, and the positions eased between snapshots so a walk is a walk and not a hop every tenth
/// of a second.
/// </summary>
public sealed class WorldMirror
{
    readonly Location place;
    readonly Dictionary<int, WorldThing> byHost = [];
    readonly Dictionary<WorldThing, (Vector3 Position, Vector3 Orientation)> targets = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<WorldThing, byte> serials = new(ReferenceEqualityComparer.Instance);
    int lastSnapshot = int.MinValue;

    /// <summary>A jump longer than this is a teleport and is not eased (ours).</summary>
    public const float SnapDistance = 250f;
    /// <summary>How quickly a thing closes on where the host says it is: about a snapshot's worth (ours).</summary>
    public const float Ease = 15f;

    public WorldMirror(Location place)
    {
        this.place = place;
        for (int guid = place.SceneGuids.First; guid < place.SceneGuids.End; guid++)
            if (place.Thing(guid) is { } t) byHost[guid] = t;
    }

    public Location Place => place;

    /// <summary>
    /// Her own character's walk predicted on this machine (<see cref="OwnWalk"/>): while it walks her, a snapshot's word on her
    /// place, facing, gait and move order goes to it and not to her, and she is not eased; everything else of hers is applied.
    /// </summary>
    public OwnWalk? Own { get; set; }

    /// <summary>A thing left to this machine for now (<see cref="OwnWalk"/>): not eased toward the host's last word.</summary>
    public void Release(WorldThing thing) => targets.Remove(thing);

    // Where each thing the snapshots carry stood in the last one, and the host's time it came there (OwnStrike).
    readonly Dictionary<WorldThing, (Vector3 Position, int SinceMs)> still = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Whether the host's snapshots have had <paramref name="thing"/> standing where it is for at least <paramref name="ms"/> of the
    /// host's time, and this machine shows it there (<see cref="OwnStrike"/>: a target the host will find where she sees it). A
    /// thing no snapshot has carried has not moved since the scene placed it.
    /// </summary>
    public bool Still(WorldThing thing, int ms)
    {
        if (!still.TryGetValue(thing, out var s)) return !targets.ContainsKey(thing);
        return (long)lastSnapshot - s.SinceMs >= ms && Vector3.Distance(thing.Position, s.Position) < 1f;
    }

    /// <summary>A mark of <see cref="Replayed"/> let go (<see cref="OwnStrike"/>'s guess taken back): the snapshots' word on its action holds again.</summary>
    public void Forget(WorldThing thing) => replayed.Remove(thing);

    /// <summary>
    /// A thing given back to the snapshots (<see cref="OwnWalk"/>'s end): eased from where it is toward the host's last word on it,
    /// taken there at once when that is past <see cref="SnapDistance"/>, and in the host's move order or out of it as that said.
    /// </summary>
    public void Follow(WorldThing thing, ThingState s)
    {
        if (!thing.InWorld || thing.AttachedTo is not null) return;
        if (Vector3.Distance(thing.Position, s.Position) > SnapDistance) { place.Move(thing, s.Position); thing.Orientation = s.Orientation; }
        targets[thing] = (s.Position, s.Orientation);
        if (thing.Actor is { } a && thing.Type is ThingType.Actor or ThingType.Player)
        {
            bool moving = (s.Flags & ThingState.Moving) != 0;
            if (moving && a.Action == Rpg.ActionMode.None) a.Action = Rpg.ActionMode.Move;
            else if (!moving && a.Action == Rpg.ActionMode.Move) a.Action = Rpg.ActionMode.None;
        }
    }

    /// <summary>This machine's thing for the host's GUID, or null.</summary>
    public WorldThing? Find(int hostGuid) => byHost.GetValueOrDefault(hostGuid);

    /// <summary>The host's GUID for this machine's thing, or 0 — what an order names its target by.</summary>
    public int HostGuid(WorldThing thing)
    {
        foreach (var (g, t) in byHost) if (ReferenceEquals(t, thing)) return g;
        return 0;
    }

    /// <summary>Says that a thing of this machine's stands for the host's <paramref name="hostGuid"/> — the local player, for its character.</summary>
    public void Bind(int hostGuid, WorldThing local)
    {
        foreach (var key in byHost.Where(kv => ReferenceEquals(kv.Value, local)).Select(kv => kv.Key).ToList())
            byHost.Remove(key);
        byHost[hostGuid] = local;
    }

    /// <summary>A thing the host made: brought back if this machine had it, built from its template otherwise.</summary>
    public WorldThing? Made(ThingMade m)
    {
        if (!byHost.TryGetValue(m.Guid, out var t))
        {
            t = place.Restore(m.Template, m.Guid, null);
            if (t is null) return null;
            byHost[m.Guid] = t;
        }
        if (!t.InWorld)
        {
            t.Position = m.Position;
            t.Orientation = m.Orientation;
            t.Sector = place.At(m.Position, t.Sector);
            place.Enter(t);
        }
        else place.Move(t, m.Position);
        // A prop's owner (for type 2), in this machine's numbers where it has its own copy (ours:
        // its own character may carry a number of its own).
        if (t.Type == ThingType.Prop)
            t.PropOwner = m.Owner != 0 && byHost.TryGetValue(m.Owner, out var owner) ? owner.Guid : m.Owner;
        targets[t] = (m.Position, m.Orientation);
        return t;
    }

    /// <summary>
    /// A projectile the host fired: made here from its template where the
    /// host's is, facing and moving as it does, its owner this machine's copy of the host's, and known
    /// by the host's GUID so that it goes when the host's goes. It flies with this
    /// machine's physics; it hurts nobody here.
    /// </summary>
    public WorldThing? Fired(ThingFired f)
    {
        if (byHost.ContainsKey(f.Guid)) return null;
        var owner = Find(f.Owner);
        var shot = place.Place(f.Template, f.Position, f.Orientation, owner?.Sector ?? place.At(f.Position, 0));
        if (shot is null) return null;
        if (shot.Type != ThingType.Projectile) { place.Remove(shot, drop: false); return null; }
        shot.Velocity = f.Velocity;
        shot.SpawnedBy = owner?.Guid ?? 0;
        byHost[f.Guid] = shot;
        return shot;
    }

    /// <summary>A thing the host took out of the world: taken out here too, kept in case it comes back.</summary>
    public void Removed(int hostGuid)
    {
        if (!byHost.TryGetValue(hostGuid, out var t)) return;
        targets.Remove(t);
        still.Remove(t);
        if (t.InWorld) place.Withdraw(t);
    }

    /// <summary>
    /// A snapshot: each thing's target set, its health and death, and its animations played when the
    /// host's differ. A snapshot older than the last one applied is dropped (the unreliable channel
    /// may reorder). 1.1 sends a revival as its own message (0x20: the actor and the health and blood
    /// fractions, from the revive, each machine reviving its copy); here it rides the snapshot's
    /// health and death, carried continuously instead of as an event.
    /// </summary>
    readonly Dictionary<WorldThing, (int Mode, int AtMs)> replayed = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// How long, in the host's time past the last snapshot applied when a motion was replayed here, a snapshot saying the thing
    /// does nothing is taken for one the host sent before its strike: the unreliable channel may bring an older
    /// snapshot after the reliable strike, and it stopped the swing, and then the host's own snapshot of the swing, the
    /// replayed one, was not started again, so her machine showed no swing at all.
    /// </summary>
    public const int ReplayedStaleMs = 500;

    /// <summary>
    /// This machine has just started a thing's action motion itself — a strike or a throw of the host's
    /// replayed here (message 0xc, `ClientArms`) — so the host's next snapshot, whose new action is the
    /// same motion, does not start it a second time (**ours**: 1.1 sends no action motions in its
    /// snapshots; each machine plays what its own strike starts). The first new action the snapshot
    /// brings for the thing clears the mark either way. Until then, for <see cref="ReplayedStaleMs"/> of the host's time, a
    /// snapshot that says it does nothing does not stop the motion (one sent before the strike, overtaken by it).
    /// </summary>
    public void Replayed(WorldThing thing)
    {
        if (thing.Animation?.Action?.Mode.Index is int mode) replayed[thing] = (mode, lastSnapshot);
        else replayed.Remove(thing);
    }

    public void Apply(int hostMs, IEnumerable<ThingState> states)
    {
        if (hostMs < lastSnapshot) return;
        lastSnapshot = hostMs;
        foreach (var s in states)
        {
            if (!byHost.TryGetValue(s.Guid, out var t) || !t.InWorld) continue;
            // A thing riding another follows it here (message 0x25, `Attachments.Update`), not the host's word for where it is.
            if (t.AttachedTo is not null) { targets.Remove(t); continue; }
            if (!still.TryGetValue(t, out var was) || Vector3.Distance(was.Position, s.Position) >= 0.5f) still[t] = (s.Position, hostMs);
            // Her own walk predicted here: the host's place for her is compared with it, not put on her. Every word on her
            // place is kept by it whoever walks her (where her keyboard walk starts from).
            if (Own?.Is(t) == true) Own.Seen(hostMs, s);
            bool own = Own?.Owns(t) == true;
            if (own) Own!.Host(hostMs, s);
            else
            {
                if (!targets.ContainsKey(t) || Vector3.Distance(t.Position, s.Position) > SnapDistance)
                {
                    place.Move(t, s.Position);
                    t.Orientation = s.Orientation;
                }
                targets[t] = (s.Position, s.Orientation);
            }
            if (t.Actor is { } a && !float.IsNaN(s.Health))
            {
                a.Health = s.Health;
                if ((s.Flags & ThingState.Dead) != 0) a.Flags |= Actor.Flag.Dead;
                else a.Flags &= ~Actor.Flag.Dead;
                // The host's move order (mode 1 when the walk says so). **Ours**: the copy does not walk
                // itself here, so the host's word that it no longer is stands for its own arrival.
                if (!own && t.Type is ThingType.Actor or ThingType.Player)
                {
                    bool moving = (s.Flags & ThingState.Moving) != 0;
                    if (moving && a.Action == Rpg.ActionMode.None) a.Action = Rpg.ActionMode.Move;
                    else if (!moving && a.Action == Rpg.ActionMode.Move) a.Action = Rpg.ActionMode.None;
                }
            }
            // A character in a conversation running on this machine plays that conversation's
            // `listen` and `talk` (on this machine), not what the host has it doing.
            if (t.Animation is { } anim && place.Motions is { } motions && !Rpg.Conversation.Talking(t))
            {
                if (!own && s.MotionMode >= 0 && anim.Motion?.Mode.Index != s.MotionMode)
                    anim.Play(s.MotionMode, motions, onMotion: true, rate: s.MotionRate > 0 ? s.MotionRate : Anim.ThingAnimation.Rate);
                if (s.ActionMode < 0)
                {
                    // A snapshot older than a strike replayed here, overtaken by it, does not stop it.
                    if (replayed.TryGetValue(t, out var mark) && (long)hostMs - mark.AtMs < ReplayedStaleMs) continue;
                    if (anim.Action is not null && !Anim.ChatLips.Local(anim.Action)) anim.StopAction();
                    serials.Remove(t);
                }
                else if (!serials.TryGetValue(t, out var seen) || seen != s.ActionSerial)
                {
                    serials[t] = s.ActionSerial;
                    // A motion this machine has just started itself, replaying the host's strike or throw,
                    // is the one this new action is: it is not started a second time.
                    if (!(replayed.Remove(t, out var mine) && mine.Mode == s.ActionMode))
                        anim.Play(s.ActionMode, motions, onMotion: false, rate: s.ActionRate > 0 ? s.ActionRate : Anim.ThingAnimation.Rate);
                }
            }
        }
    }

    /// <summary>One frame of easing: every thing a little closer to where the host last said it was.</summary>
    public void Step(float seconds)
    {
        float k = 1f - MathF.Exp(-seconds * Ease);
        foreach (var (t, (pos, rot)) in targets)
        {
            if (!t.InWorld || Own?.Owns(t) == true) continue;
            if (t.Position != pos) place.Move(t, Vector3.Distance(t.Position, pos) < 0.5f ? pos : Vector3.Lerp(t.Position, pos, k));
            if (t.Orientation != rot) t.Orientation = new Vector3(Angle(t.Orientation.X, rot.X, k), Angle(t.Orientation.Y, rot.Y, k), Angle(t.Orientation.Z, rot.Z, k));
        }
    }

    /// <summary>Degrees eased the short way round.</summary>
    static float Angle(float from, float to, float k)
    {
        float d = ((to - from) % 360f + 540f) % 360f - 180f;
        return MathF.Abs(d) < 0.05f ? to : from + d * k;
    }
}
