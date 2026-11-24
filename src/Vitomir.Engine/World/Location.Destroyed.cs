// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the destroy does to the conversation and the steering (gaps 4 and 5 of a comparison with 1.1).
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// The game's one running conversation, as the destroy reaches it. Set with
    /// <see cref="Simulation.Talk"/>, which keeps it here; null for a place with no conversations.
    /// </summary>
    public Conversations? Talk { get; set; }

    /// <summary>
    /// Two steps of the destroy, between the untargeting and the debris:
    ///
    /// <list type="number">
    /// <item><b>The conversation with it ends</b>: when a conversation is
    /// running and the one it was started with — `ExecuteConversation`'s sixth
    /// argument, <see cref="Conversation.Npc"/> — has this thing's GUID, it is finished as aborted:
    /// the teardown, and `convended(id, aborted = true, code)` to its script when
    /// it asked for callbacks — <see cref="Conversations.Abandon"/>. Only the one talked to: a
    /// speaker who is not the conversation's NPC can be destroyed and the conversation goes on.</item>
    /// <item><b>The steering passes on</b>: when the thing is the steered one,
    /// the party's next-member search runs with the dead passed over (with 1,
    /// <see cref="SteerNext"/>) — counting from the <em>selected</em> member's slot, over the party
    /// as it stands, which still holds the one being destroyed (nothing takes it out of the party
    /// first). So a companion after it in the party is taken first, then the first from the top;
    /// and with nobody else standing the search comes round to the one being destroyed and selects
    /// it again, unless it is dead — only then is it the party's end (the game's end with 2,
    /// `PartyDown`).</item>
    /// </list>
    ///
    /// What 1.1 does next with a destroyed character still selected is not read (its actor is freed
    /// by the deletion, which does not touch the party); Vitomir keeps steering it, as it
    /// did earlier.
    /// </summary>
    void Destroying(WorldThing thing)
    {
        if (Talk is { Current: { } talking } talk && talking.Npc == thing.Guid) talk.Abandon(this);
        if (Minds is not null && ReferenceEquals(thing, Steered)) SteerNext(skipDead: true, going: thing);
    }

    /// <summary>
    /// The party as 1.1's array holds it while <paramref name="going"/> is destroyed: the one going
    /// keeps its slot (<see cref="PartyMembers"/> leaves out anything not in the world).
    /// </summary>
    List<WorldThing> PartyWith(WorldThing going)
    {
        var party = new List<WorldThing>();
        var leader = Player?.Thing;
        if (leader is not null && (leader.InWorld || ReferenceEquals(leader, going))) party.Add(leader);
        foreach (var t in Things)
            if (!ReferenceEquals(t, leader) && (t.InWorld || ReferenceEquals(t, going))
                && t.Actor is { } a && a.Has(Actor.Flag.Party))
                party.Add(t);
        return party;
    }
}
