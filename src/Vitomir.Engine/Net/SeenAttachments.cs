// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Attachments and names on the players' machines.
// 1.1's message 0x25 carries a thing attached to
// another — sent by `AttachThing` on a host and for every attachment in a newcomer's
// catch-up — and its message 0x53 a character's own name,
// sent with every character announced and in the catch-up.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed partial class HostSeen
{
    /// <summary>
    /// The second-flags bit a renamed character carries (second flag 0x800): the Storyteller's
    /// `rename` sets it with the name, and a save keeps the name only when it is set.
    /// </summary>
    public const uint Renamed = 0x800;

    /// <summary>
    /// A script has attached one thing to another on the host (`CodexThing.AttachThing`, which on
    /// a host sends message 0x25 once the attach has said yes): the players in the place are told, after the
    /// things it names have been announced. The engine's own attaches — a torch on the hand, a wound's
    /// blood — send nothing, as 1.1's do not.
    /// </summary>
    public static void Attached(WorldThing parent, WorldThing child)
    {
        if (!Synced(child) || !Synced(parent) || Of(parent.Place) is not { } seen) return;
        if (parent.Attachments.FirstOrDefault(a => ReferenceEquals(a.Child, child)) is not { } made) return;
        seen.events.Add(AttachMessage(parent, made));
    }

    /// <summary>
    /// Message 0x25: the child's GUID, the parent's, the bone, the flags and — **ours** — the relative
    /// matrix the attach made, where 1.1 sends the offset and the Euler angles the attach then makes it
    /// from (the catch-up turns the relative matrix back into them). The same attachment
    /// either way.
    /// </summary>
    static byte[] AttachMessage(WorldThing parent, Attachment a)
    {
        var m = a.Relative;
        return new NetWriter(Msg.Attached).I32(a.Child.Guid).I32(parent.Guid).I32(a.Bone).U32(a.Flags)
            .F32(m.M11).F32(m.M12).F32(m.M13).F32(m.M21).F32(m.M22).F32(m.M23).F32(m.M31).F32(m.M32).F32(m.M33)
            .Vec(m.Translation).ToArray();
    }

    /// <summary>
    /// Message 0x53 for a character (type 0 or 7) whose name is its own — renamed by the Storyteller
    /// (<see cref="Renamed"/>). **Ours**: a character that is not renamed has its template's name on every
    /// machine (Vitomir reads it from the description each time), so nothing goes; 1.1 sends whatever
    /// the character's name holds. Null when nothing is sent.
    /// </summary>
    static byte[]? Name(WorldThing t) =>
        t.Type is ThingType.Actor or ThingType.Player && t.Actor is { } a && (a.Flags2 & Renamed) != 0 && t.DescriptionId.Length > 0
            ? new NetWriter(Msg.Named).I32(t.Guid).Str(t.DescriptionId.Length > 79 ? t.DescriptionId[..79] : t.DescriptionId).ToArray()
            : null;

    /// <summary>What the catch-up adds: every renamed character's name and every attachment of every thing.</summary>
    IEnumerable<byte[]> Tail()
    {
        foreach (var t in place.Things.Where(Synced))
            if (Name(t) is { } named) yield return named;
        foreach (var t in place.Things.Where(Synced))
            foreach (var a in t.Attachments.ToList())
                if (Synced(a.Child) && a.Child.InWorld) yield return AttachMessage(t, a);
    }
}

public sealed partial class ClientSeen
{
    /// <summary>
    /// 1.1's 0x25 read: both things must be here; the child is attached to the parent at the
    /// bone, which refuses one already riding something. **Ours**: the host's relative matrix is
    /// put back exactly — its translation as the offset and its rotation as the Euler matrix, with the
    /// four skip bits (12 to 15) cleared so neither is skipped; the flags' own bit 0 (let go destroys it)
    /// is kept. From then on the child follows its parent here (`Attachments.Update`), and the host's
    /// snapshots leave it alone (`WorldMirror`).
    /// </summary>
    void Attached(NetReader r)
    {
        int child = r.I32(), parent = r.I32(), bone = r.I32();
        uint flags = r.U32();
        var m = new Matrix4x4(r.F32(), r.F32(), r.F32(), 0f, r.F32(), r.F32(), r.F32(), 0f, r.F32(), r.F32(), r.F32(), 0f, 0f, 0f, 0f, 1f);
        var at = r.Vec();
        if (mirror!.Find(child) is not { } rider || mirror.Find(parent) is not { } holder || rider.AttachedTo is not null) return;
        const uint skips = Attachments.KeepPlace | Attachments.KeepRotation | Attachments.NoOffset | Attachments.NoEuler;
        if (Attachments.Attach(holder, rider, bone, at, m, flags & ~skips))
            Log?.Invoke($"net: thing {child} ({rider.Template}) rides {holder.Template}{(bone >= 0 ? $" at bone {bone}" : "")}");
    }

    /// <summary>
    /// 1.1's 0x53 read: a character here (type 0 or 7) takes the name — at most 79 characters —
    /// as its own (here its description, which the rollover and the message lines read), and
    /// is marked renamed.
    /// </summary>
    void Named(NetReader r)
    {
        int guid = r.I32();
        string name = r.Str();
        if (name.Length > 79) name = name[..79];
        if (mirror!.Find(guid) is not { Type: ThingType.Actor or ThingType.Player, Actor: { } a } body) return;
        body.DescriptionId = name;
        a.Flags2 |= HostSeen.Renamed;
        Log?.Invoke($"net: thing {guid} is named {name}");
    }
}
