// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party put down on one of the place's own entrances, without leaving it.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// <c>CodexPlayer.TeleportPartyToEntrance(player, entrance)</c> (the party's start move
    /// with kind **0**). The same party move as <see cref="TeleportPartyToConversation"/> — which is
    /// the same move with kind 1 — over the **player starts** instead of the conversation starts: every
    /// member onto its own start of group <paramref name="group"/> (the entrance), the leader on the
    /// first, turned the way it faces, with its walk and its order dropped. Refused as a whole for an
    /// empty party, and a member with no start stops the moving there, the ones before it moved.
    /// Three shipped things carry the `TeleportPartyToEntrance` script, which calls this when it is
    /// walked into or clicked.
    /// </summary>
    public bool TeleportPartyToEntrance(uint group)
    {
        var party = PartyMembers().ToList();
        if (party.Count == 0) return false;
        for (int i = 0; i < party.Count; i++)
        {
            if (PickStart(AllStarts, group, (uint)(i + 1)) is not { } start) return false;
            var member = party[i];
            member.Position = start.Position;
            float yaw = MathF.Atan2(start.Rotation[1], start.Rotation[0]) * (180f / MathF.PI);
            member.Orientation = new Vector3(0f, 0f, yaw);
            long was = member.Sector;
            member.Sector = start.Sector;
            Props?.Moved(member, was, member.Sector);
            // The ground under it found without moving it; the walk is stopped and
            // the order cancelled — as the conversation's move does.
            if (Player is { } p && ReferenceEquals(p.Thing, member)) p.Ground(snap: false);
            else member.Mind?.Walker.Ground(snap: false);
            member.Mind?.Walker.Stop();
            Rpg.Actions.Cancel(member);                 // unforced: a busy member keeps its order
            // The sector move carries the region word into the new sector and runs no region test: only
            // SetPosition and SetTransform do, so `entered` waits for the member's
            // next move (until then Vitomir tested here and fired it on the spot).
            Regions?.Crossed(member, was);
        }
        return true;
    }
}
