// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What each machine sees of the others, the host's side.
// 1.1 sends a message for each change as it happens — an item worn (0x13 … 0x15), an effect
// on or off (0x19, 0x1a), a blow (0xd), a foley or a named sound (0x4b, 0x4c), a thing's flags
// (message 4), a hitscan shot (0x50) — and tells a player's machine arriving in a place all of it at
// once. Vitomir does the same with two halves: what is *state* (the kit, the effects, the
// flags) is compared with what was last sent at every snapshot and the differences go (ours — 1.1's
// senders are called from the setters themselves, and message 4 from the thing tick's end for a
// thing they marked: the flags go when what they mark differs, `ThingFlags.Trigger`), and what is an *event* (a blow, a sound, a shot)
// is caught where it happens on the host and goes with the next snapshot, after the things it names
// have been announced.
using System.Runtime.CompilerServices;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>An item a character wears, as the kit message carries it: the host's GUID for it, its template and its slot.</summary>
public readonly record struct WornItem(int Guid, string Template, int Slot);

/// <summary>
/// A blow as message 0xd carries it: who took it, who gave it, how much it came to, the
/// hit's kind and the damage's type; and ours, whether it killed and whether the kill cost the
/// attacker Humanity, which the host has decided; and for a death, how the death effects went on the
/// host, which a player's machine then shows itself (<see cref="ClientSeen"/>'s death):
/// <paramref name="Whole"/> false for a character killed in mid-air, whose death effects end at once,
/// and <paramref name="Dusted"/> for one that turned to dust (whose bones and
/// smoke are sent). **Ours**: 1.1's player's machine works both out from its own copy (its own death there); here the
/// action a character died in reaches it only with the next snapshot. Not carried either (ours): 1.1's seed for
/// the shared generator (the host's clock at the blow) — Vitomir's particles keep a
/// generator of their own in each system.
/// </summary>
public readonly record struct SeenBlow(int Victim, int Attacker, float Amount, HitKind Kind, DamageType Type,
                                       bool Killed, bool HumanityLost, bool Whole = true, bool Dusted = false)
{
    public void Write(NetWriter w) =>
        w.I32(Victim).I32(Attacker).F32(Amount).U8((byte)Kind).U8((byte)Type)
         .U8((byte)((Killed ? 1 : 0) | (HumanityLost ? 2 : 0) | (Whole ? 0 : 4) | (Dusted ? 8 : 0)));

    public static SeenBlow Read(NetReader r)
    {
        int victim = r.I32(), attacker = r.I32();
        float amount = r.F32();
        var kind = (HitKind)r.U8();
        var type = (DamageType)r.U8();
        byte bits = r.U8();
        return new(victim, attacker, amount, kind, type, (bits & 1) != 0, (bits & 2) != 0, (bits & 4) == 0, (bits & 8) != 0);
    }
}

/// <summary>
/// An effect put on a character as message 0x19 carries it: the actor, the instance's GUID,
/// the effect's name, its level (−1 by value) and value, how long it has left (0 for one that does not
/// end), its creator, its flags and the script's two parameters.
/// </summary>
public readonly record struct SeenEffect(int Actor, int Guid, string Name, int Level, float Value, int RemainingMs,
                                         int Creator, uint Flags, int IntParam, float FloatParam)
{
    public void Write(NetWriter w) =>
        w.I32(Actor).I32(Guid).Str(Name).I32(Level).F32(Value).I32(RemainingMs).I32(Creator).U32(Flags).I32(IntParam).F32(FloatParam);

    public static SeenEffect Read(NetReader r) =>
        new(r.I32(), r.I32(), r.Str(), r.I32(), r.F32(), r.I32(), r.I32(), r.U32(), r.I32(), r.F32());

    public static SeenEffect Of(WorldThing on, Effect e, int nowMs) =>
        new(on.Guid, e.Guid, e.Name, e.Level, e.Value, e.Until == 0 ? 0 : Math.Max(1, e.Until - nowMs),
            e.CreatorGuid, e.Flags & ~(Effect.FoldedBit | Effect.CountedBit | Effect.SpentBit), e.IntParam, e.FloatParam);
}

/// <summary>The host's side: what has changed since it last looked, what a newcomer needs, and the events caught.</summary>
public sealed partial class HostSeen
{
    static readonly ConditionalWeakTable<Location, HostSeen> places = new();
    [ThreadStatic] static int local;

    readonly GameServer server;
    readonly Location place;
    readonly Dictionary<int, ThingFlags> flags = [];
    readonly Dictionary<int, string> kits = [];
    readonly Dictionary<int, HashSet<int>> effects = [];
    readonly List<byte[]> events = [];

    HostSeen(GameServer server, Location place)
    {
        this.server = server;
        this.place = place;
        // What stands now is what everybody already playing has been sent — nobody, when the host's
        // world has just come up; everybody else is sent it whole on arriving (CatchUp).
        foreach (var t in place.Things)
        {
            if (!Synced(t)) continue;
            flags[t.Guid] = ThingFlags.Of(t);
            if (t.Actor is { } a)
            {
                foreach (var worn in Carried(a)) flags[worn.Guid] = ThingFlags.Of(worn);
                kits[t.Guid] = Signature(a);
                effects[t.Guid] = [.. Live(t).Select(e => e.Guid)];
            }
        }
    }

    /// <summary>Ties a host's place to what it sends of it; the hooks below find it by the place.</summary>
    public static HostSeen Attach(Location place, GameServer server)
    {
        var seen = new HostSeen(server, place);
        places.AddOrUpdate(place, seen);
        return seen;
    }

    public static void Detach(Location place) => places.Remove(place);

    /// <summary>The host's record of what it sends of a place, or null when the place is not a host's (one for each of the host's places).</summary>
    public static HostSeen? For(Location? place) => Of(place);

    static HostSeen? Of(Location? place) =>
        place is not null && places.TryGetValue(place, out var seen) && NetOrders.Mode(place) == NetMode.Server ? seen : null;

    /// <summary>
    /// Whether a thing is sent at all: not a projectile (each machine flies its own, `WorldWatch`) and
    /// not one flagged `NO NET SYNC` (0x100) — ours, as the flag's name says; where 1.1 tests it is not read.
    /// </summary>
    static bool Synced(WorldThing t) => t.Type != ThingType.Projectile && (t.Flags & WorldThing.Flag.NoNetSync) == 0;

    /// <summary>
    /// The effects that go to the players' machines: the live ones, not an instant one already folded
    /// into the base (it has done its work here), and not one whose row is flagged `NOT SYNCED` (0x80),
    /// which the effect's send keeps on the host.
    /// </summary>
    static IEnumerable<Effect> Live(WorldThing t) =>
        t.Effects.All.Where(e => !e.Spent && !e.Folded && !e.Definition.Has(EffectFlags.NotSynced));

    /// <summary>
    /// The things a character wears out of the world (all but a utility item on the hand, which is in it): the
    /// items the kit names to every machine, whose message 4 1.1 sends from their own tick too — a carried thing's
    /// generic tick ends in the same send like any.
    /// A pack's are the owner's alone, which the host's own pack message carries (`HostPack`).
    /// </summary>
    static IEnumerable<WorldThing> Carried(Actor a) =>
        Equipment.Worn(a).Select(i => i.Thing).OfType<WorldThing>().Where(t => !t.InWorld && Synced(t));

    static string Signature(Actor a) =>
        string.Join(';', Worn(a).Select(i => $"{i.Guid}:{i.Template}:{i.Slot}"));

    static IEnumerable<WornItem> Worn(Actor a) =>
        Equipment.Worn(a).Where(i => i.Thing is not null).Select(i => new WornItem(i.Thing!.Guid, i.Thing.Template, i.Location));

    static byte[] Kit(WorldThing t, Actor a)
    {
        var worn = Worn(a).Take(32).ToList();
        var w = new NetWriter(Msg.Kit).I32(t.Guid).U8((byte)worn.Count);
        foreach (var i in worn) w.I32(i.Guid).Str(i.Template).I32(i.Slot);
        return w.ToArray();
    }

    static IEnumerable<byte[]> FlagMessages(IReadOnlyList<ThingFlags> all)
    {
        int per = (NetPeer.MaxMessage - 16) / ThingFlags.Size;
        for (int i = 0; i < all.Count; i += per)
        {
            var chunk = all.Skip(i).Take(per).ToList();
            var w = new NetWriter(Msg.Flags).U16((ushort)chunk.Count);
            foreach (var f in chunk) f.Write(w);
            yield return w.ToArray();
        }
    }

    /// <summary>
    /// Who an effect goes to: an effect whose row is flagged `OWNER ONLY` (0x40) goes only to
    /// the player whose character it is on — the host's own, or nobody's, goes nowhere; any other goes to
    /// everybody in the place (null).
    /// </summary>
    (bool Sent, NetPlayer? Only) Route(WorldThing on, Effect e)
    {
        if (!e.Definition.Has(EffectFlags.OwnerOnly)) return (true, null);
        var owner = server.Players.FirstOrDefault(p => ReferenceEquals(p.Avatar, on));
        return owner is { Link: not null } ? (true, owner) : (false, null);
    }

    /// <summary>
    /// What changed since the last call, in the order it is sent: every thing's flags that differ
    /// (message 4, from the simulation tick), every character's kit that differs
    /// (0x13 … 0x15), every effect put on or gone (0x19, 0x1a), and then the events caught since. Each
    /// with the one player it is for, or null for everybody.
    /// </summary>
    public List<(byte[] Message, NetPlayer? Only)> Changes()
    {
        var sent = new List<(byte[], NetPlayer?)>();
        var changed = new List<ThingFlags>();
        var now = new HashSet<int>();
        foreach (var t in place.Things)
        {
            if (!Synced(t)) continue;
            now.Add(t.Guid);
            var f = ThingFlags.Of(t);
            if (!flags.ContainsKey(t.Guid) && Name(t) is { } named) sent.Add((named, null));     // a thing announced has its name sent
            Mark(t.Guid, f, changed);
            if (t.Actor is not { } a) continue;
            foreach (var worn in Carried(a)) { now.Add(worn.Guid); Mark(worn.Guid, ThingFlags.Of(worn), changed); }
            string sig = Signature(a);
            if (!kits.TryGetValue(t.Guid, out var wore) || wore != sig) { kits[t.Guid] = sig; sent.Add((Kit(t, a), null)); }
            if (!effects.TryGetValue(t.Guid, out var had)) effects[t.Guid] = had = [];
            var live = Live(t).ToList();
            foreach (var e in live)
            {
                if (had.Contains(e.Guid)) continue;
                had.Add(e.Guid);
                if (Route(t, e) is (true, var only)) sent.Add((EffectOn(t, e), only));
            }
            foreach (var gone in had.Where(g => !live.Any(e => e.Guid == g)).ToList())
            {
                had.Remove(gone);
                sent.Add((new NetWriter(Msg.EffectOff).I32(t.Guid).I32(gone).ToArray(), null));
            }
        }
        foreach (var g in flags.Keys.Where(g => !now.Contains(g)).ToList()) { flags.Remove(g); kits.Remove(g); effects.Remove(g); }
        sent.InsertRange(0, FlagMessages(changed).Select(m => (m, (NetPlayer?)null)));
        foreach (var e in events) sent.Add((e, null));
        events.Clear();
        return sent;
    }

    /// <summary>
    /// A thing's flags into the list to send when what makes message 4 go differs from what was last sent (thing flag
    /// 0x20000000, cleared as it is sent), with what rides on it as it stands now (<see cref="ThingFlags.Trigger"/>).
    /// </summary>
    void Mark(int guid, ThingFlags f, List<ThingFlags> changed)
    {
        if (flags.TryGetValue(guid, out var was) && was.Trigger() == f.Trigger()) return;
        changed.Add(f);
        flags[guid] = f;
    }

    byte[] EffectOn(WorldThing t, Effect e)
    {
        var w = new NetWriter(Msg.EffectOn);
        SeenEffect.Of(t, e, place.Milliseconds).Write(w);
        return w.ToArray();
    }

    /// <summary>
    /// What a player's machine arriving in the place is told of it, after the things made and removed:
    /// where every thing the scene placed now stands when it has been moved from its place —
    /// a door opened, a crate pushed, what a keyframe track carried (as a thing made, ours) — what every
    /// thing that is not a character is playing on its model (0x24: as a snapshot of
    /// those things, ours), every thing's flags, every character's kit and every
    /// effect on a character (as 0x19), and every renamed character's name (0x53)
    /// and every attachment (0x25). **Not sent**: revivals (the snapshot carries health and
    /// death); the open teleporters are the arrival's own (`HostPlaces`).
    /// </summary>
    public List<byte[]> CatchUp(NetPlayer p)
    {
        var all = new List<byte[]>();
        foreach (var t in place.Things)
            if (t.Actor is null && Synced(t) && WorldWatch.FromScene(place, t.Guid) && t.Moved)
            {
                var w = new NetWriter(Msg.Made);
                ThingMade.Of(t).Write(w);
                all.Add(w.ToArray());
            }
        var playing = new List<ThingState>();
        foreach (var t in place.Things)
            if (t.Actor is null && Synced(t) && t.Animation is { } anim && (anim.Motion ?? anim.Action) is not null)
                playing.Add(new ThingState(t.Guid, t.Position, t.Orientation,
                    (short)(anim.Motion?.Mode.Index ?? -1), anim.Motion?.Player.Rate ?? 0f,
                    (short)(anim.Action?.Mode.Index ?? -1), 1, anim.Action?.Player.Rate ?? 0f, float.NaN, 0));
        int per = (NetPeer.MaxMessage - 16) / ThingState.Size;
        for (int i = 0; i < playing.Count; i += per)
        {
            var chunk = playing.Skip(i).Take(per).ToList();
            var w = new NetWriter(Msg.Snapshot).I32(server.NowMs).U16((ushort)chunk.Count);
            foreach (var st in chunk) st.Write(w);
            all.Add(w.ToArray());
        }
        var states = new List<ThingFlags>();
        foreach (var t in place.Things.Where(Synced))
        {
            states.Add(ThingFlags.Of(t));
            if (t.Actor is { } a) states.AddRange(Carried(a).Select(ThingFlags.Of));
        }
        all.AddRange(FlagMessages(states));
        foreach (var t in place.Things.Where(Synced))
        {
            if (t.Actor is not { } a) continue;
            if (Equipment.Worn(a).Any()) all.Add(Kit(t, a));
            foreach (var e in Live(t))
                if (Route(t, e) is (true, var only) && (only is null || only == p)) all.Add(EffectOn(t, e));
        }
        all.AddRange(Tail());                                       // names and attachments
        return all;
    }

    /// <summary>
    /// A player asks to end one of their own effects (1.1's 0x1a from a client): only an effect
    /// on their own character, and only one whose row is flagged `CANCELLABLE` (0x200), ended with every
    /// consequence of a removal — as the right click on its icon does on the host's own.
    /// </summary>
    internal void Cancel(NetPlayer from, NetReader r)
    {
        int guid = r.I32();
        if (from.Avatar is not { } me) return;
        if (me.Effects.All.FirstOrDefault(e => e.Guid == guid && !e.Spent) is not { } effect) return;
        if (!effect.Definition.Has(EffectFlags.Cancellable)) return;
        EffectRuntime.Remove(me, effect, notify: true);
        server.Note($"net: {from.Name} cancels {effect.Name}");
    }

    // ---- the hooks: events caught where they happen on the host ---------------------------------

    /// <summary>A character's Humanity before a blow, for <see cref="Struck"/> to tell whether the kill cost any.</summary>
    public static float Humanity(WorldThing? attacker) => attacker?.Actor?.Stat[(int)ActorStat.Humanity] ?? 0f;

    /// <summary>
    /// A blow the host has dealt (message 0xd), or a death (message 0xf — from `Damage.Kill`, every death that gets that far): sent to every machine to be shown
    /// there — the death cries, the message lines — and never dealt again (a player's machine hurts
    /// nobody). Each machine makes the blow's noises, chunk, sparks and blood itself, and a death's chunks
    /// and wound effects, from how the death went here (<see cref="MadeHere.DeathOf"/>).
    /// </summary>
    public static void Struck(WorldThing target, WorldThing? attacker, HitKind kind, DamageType type, float amount,
                              bool killed, float humanityBefore)
    {
        if (Of(target.Place) is not { } seen) return;
        bool lost = killed && attacker?.Actor is { } k && k.Stat[(int)ActorStat.Humanity] < humanityBefore;
        var w = new NetWriter(Msg.Blow);
        var (whole, dusted) = killed ? MadeHere.DeathOf(target) : (true, false);
        new SeenBlow(target.Guid, attacker?.Guid ?? 0, amount, kind, type, killed, lost, whole, dusted).Write(w);
        seen.events.Add(w.ToArray());
    }

    /// <summary>
    /// A foley the host plays (message 0x4b): the thing, the event and where it is
    /// placed. **Ours**: not a projectile's (each machine flies its own), not an animation's keyframe
    /// (each machine plays its own animations, <see cref="Local"/>), not the `moving` loop (the host
    /// stops it by its channel, which does not cross), and nothing for a thing with no foley set.
    /// </summary>
    public static void Foley(WorldThing? thing, int slot, WorldThing? at)
    {
        if (local > 0 || thing is null || thing.Foley.Length == 0 || !Synced(thing)) return;
        if (slot == Audio.FoleyEvent.Moving) return;
        if (Of(thing.Place) is not { } seen) return;
        seen.events.Add(new NetWriter(Msg.Foley).I32(thing.Guid).I32(slot).I32(at?.Guid ?? 0).ToArray());
    }

    /// <summary>
    /// A named sound on a thing (message 0x4c): a discipline's cast and a script's
    /// `CodexSound.CreateAtThing` — the name, the two distances, the volume and the flags.
    /// </summary>
    public static void Sound(WorldThing thing, string name, float min, float max, float volume, uint flags)
    {
        if (name.Length == 0 || Of(thing.Place) is not { } seen) return;
        seen.events.Add(new NetWriter(Msg.Sound).I32(thing.Guid).Str(name).F32(min).F32(max).F32(volume).U32(flags).ToArray());
    }

    /// <summary>
    /// A hitscan shot (message 0x50): resolved and gone within the host's step, so it is caught
    /// as it is fired and every player's machine fires the same shot itself — the
    /// flash, the tracer and the impact — hurting nobody there.
    /// </summary>
    public static void Hitscan(WorldThing shot)
    {
        if (Of(shot.Place) is not { } seen) return;
        var w = new NetWriter(Msg.Shot);
        new ThingFired(shot.Guid, shot.Template, shot.Position, shot.Orientation, shot.Velocity, shot.SpawnedBy).Write(w);
        seen.events.Add(w.ToArray());
    }

    /// <summary>
    /// While one is open, the foley played is this machine's own and is not relayed — an animation's
    /// keyframe noise, which every machine plays from the animation it is showing (ours).
    /// </summary>
    public static LocalNoise Local() { local++; return default; }

    /// <summary>The end of a <see cref="Local"/>.</summary>
    public readonly struct LocalNoise : IDisposable
    {
        public void Dispose() => local--;
    }
}
