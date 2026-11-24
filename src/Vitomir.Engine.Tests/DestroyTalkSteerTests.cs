// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Two steps of the destroy that a comparison with 1.1 found unbuilt,
/// built (`World/Location.Destroyed.cs`):
/// destroying the one a conversation was started with ends it as aborted and tells its script,
/// and destroying the steered character runs the party's next-member
/// search with the dead passed over — over the party as it stands,
/// which still holds the one being destroyed. Earlier, a script's remove touched neither, so the
/// conversation went on with nobody to talk to and the steering stayed on a thing that was gone.
/// </summary>
public class DestroyTalkSteerTests
{
    /// <summary>The University with Christof and Wilhem both in the party (as `SteeringTests` has it).</summary>
    static (Location Place, WorldThing Christof, WorldThing Wilhem) Party(GameFiles files)
    {
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.First(m => ReferenceEquals(m.Thing, place.Player!.Thing));
        var wilhem = minds.All.First(m => !ReferenceEquals(m, christof) && m.Thing.Type == ThingType.Player);
        foreach (var m in new[] { christof, wilhem })
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Enter(AiMode.Party);
        }
        return (place, christof.Thing, wilhem.Thing);
    }

    static NcoConversation Lines() => NcoFile.Read(Encoding.Latin1.GetBytes(
        "CONVERSATION: T 0\r\nEVENT: ev_0\r\n\tLINE: a Christof\r\n\tLINE: b Christof\r\n"), "Test.nco").Conversations[0];

    /// <summary>A conversation started with <paramref name="npc"/> as the one talked to, its ending recorded.</summary>
    static (Conversations Talk, Conversation Made, Func<(int, bool, int)?> Ended) Talking(Location place, WorldThing npc)
    {
        var talk = new Conversations();
        new Simulation(place) { Talk = talk };                  // the game hands the place its conversation this way
        var made = talk.Run(place, Lines(), ConversationFlag.Callbacks, 77, place.Milliseconds);
        made.Npc = npc.Guid;                                    // ExecuteConversation's sixth argument
        (int, bool, int)? ended = null;
        made.Ended += (id, aborted, code) => ended = (id, aborted, code);
        return (talk, made, () => ended);
    }

    /// <summary>
    /// **Destroying the one being talked to ends the conversation** (the conversation's partner against the
    /// GUID, then the abort): the teardown, and `convended(id, aborted = true, code)` to a
    /// script that asked for callbacks.
    /// </summary>
    [Fact]
    public void Destroying_the_one_being_talked_to_ends_the_conversation_and_tells_its_script()
    {
        using var files = Install.Open();
        var (place, _, wilhem) = Party(files);
        var (talk, made, ended) = Talking(place, wilhem);
        Assert.True(made.Running);

        Assert.True(place.Remove(wilhem));

        Assert.Null(talk.Current);
        Assert.False(made.Running);
        Assert.Equal<(int, bool, int)?>((77, true, made.ReturnCode), ended());
    }

    /// <summary>Only the conversation's own NPC ends it: anybody else can be destroyed mid-conversation.</summary>
    [Fact]
    public void Destroying_somebody_else_leaves_the_conversation_running()
    {
        using var files = Install.Open();
        var (place, christof, wilhem) = Party(files);
        var (talk, _, ended) = Talking(place, christof);

        Assert.True(place.Remove(wilhem));

        Assert.True(talk.Current!.Running);
        Assert.Null(ended());
    }

    /// <summary>
    /// **Destroying the steered one passes control on**: to the next
    /// member standing — and destroying a member nobody is steering changes nothing.
    /// </summary>
    [Fact]
    public void Destroying_the_steered_one_passes_control_to_the_next_standing()
    {
        using var files = Install.Open();
        var (place, christof, wilhem) = Party(files);
        var extra = place.Things.First(t => t.InWorld && t.Type == ThingType.Actor && t.Actor is not null);
        Assert.Same(christof, place.Steered);

        Assert.True(place.Remove(extra));
        Assert.Same(christof, place.Steered);

        Assert.True(place.Remove(christof));
        Assert.Same(wilhem, place.Steered);
        Assert.Same(wilhem, place.Selected);
    }

    /// <summary>
    /// The search runs over the party with the one being destroyed still in it (nothing takes it out
    /// first), so with every other member down it comes round to that one and selects it again —
    /// the party's end is asked only when it is dead too.
    /// </summary>
    [Fact]
    public void With_nobody_else_standing_the_search_keeps_the_one_destroyed_unless_it_is_dead()
    {
        using var files = Install.Open();
        var (place, christof, wilhem) = Party(files);
        var chronicle = ChronicleSet.Load(files).ForLevel("P1_UNIV")!.Value.Chronicle;
        place.Session = new Session { Here = place, Chronicle = chronicle };
        Damage.Kill(wilhem, null, HitKind.Melee, DamageType.Lethal, nowMs: place.Milliseconds);
        Assert.Same(christof, place.Steered);
        Assert.Null(chronicle.Pending);

        Assert.True(place.Remove(christof));
        Assert.Same(christof, place.Steered);
        Assert.Null(chronicle.Pending);

        // Dead, the one being destroyed is passed over as well: nobody is left, and the party is down.
        var (again, first, second) = Party(files);
        again.Session = new Session { Here = again, Chronicle = chronicle };
        Damage.Kill(second, null, HitKind.Melee, DamageType.Lethal, nowMs: again.Milliseconds);
        Damage.Kill(first, null, HitKind.Melee, DamageType.Lethal, nowMs: again.Milliseconds);
        Assert.NotNull(chronicle.Pending);                      // the death's own search
        chronicle.Take();
        Assert.True(again.Remove(first));
        Assert.Equal(new SequenceRequest(SequenceKind.EndGame, Reason: Chronicle.EndPartyDead), chronicle.Pending);
    }
}
