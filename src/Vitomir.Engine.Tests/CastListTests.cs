// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The cast lists a cast name is looked up in: the party's first, then the
/// place's, which skips anybody carrying `THING_AF_PARTY` and holds at most 64. This is
/// what a conversation's speakers and `GuidFromCastID` both go through. The first tests are a name
/// lookup and need no level; the last ones load the monastery to pin that **the place's list is
/// made on arrival and never grows** (nothing rebuilds it), and that a party rebuild takes the
/// members' names out of it for good — as 1.1 has it: a companion that has left the
/// party is not animated by a conversation.
/// </summary>
public class CastListTests
{
    static WorldThing Character(string castId, int guid, bool party = false)
    {
        var t = new WorldThing
        {
            Index = guid, Guid = guid, Scene = new NsdThing(), Template = "t", Type = ThingType.Actor,
            Actor = new Actor { CastId = castId, Health = 100f, MaxHealth = 100f },
        };
        if (party) t.Actor.Flags |= Actor.Flag.Party;
        return t;
    }

    static WorldThing? Find(IEnumerable<WorldThing> party, IEnumerable<WorldThing> things, string cast) =>
        CastList.Of(party, things).FirstOrDefault(t => t.Actor!.CastId.Equals(cast, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_party_member_answers_before_anybody_else_with_the_same_cast_id()
    {
        var stranger = Character("Wilhem", 1);
        var wilhem = Character("Wilhem", 2, party: true);
        Assert.Same(wilhem, Find([wilhem], [stranger, wilhem], "Wilhem"));
        // The party's rebuild takes the name out of the place's list, so the stranger
        // who shares it is in neither.
        Assert.Equal([wilhem], CastList.Of([wilhem], [stranger, wilhem]));
    }

    [Fact]
    public void A_fallen_party_member_still_answers_from_the_party_s_list()
    {
        var wilhem = Character("Wilhem", 2, party: true);
        wilhem.Actor!.Flags |= Actor.Flag.Dead;
        Assert.Same(wilhem, Find([wilhem], [wilhem], "Wilhem"));   // the roster, dead or not (FallenListener)
    }

    [Fact]
    public void The_place_s_list_skips_the_party_flag_and_the_unnamed()
    {
        var flagged = Character("Ghost", 1, party: true);        // flagged, but not in the roster
        var unnamed = Character("", 2);
        Assert.Empty(CastList.Of([], [flagged, unnamed]));
    }

    [Fact]
    public void The_place_s_list_stops_at_64_and_the_first_of_a_name_wins()
    {
        var things = Enumerable.Range(0, CastList.PlaceCapacity + 6).Select(i => Character($"cast{i}", i + 1)).ToList();
        var twin = Character("cast0", 999);
        things.Insert(1, twin);
        var list = CastList.Of([], things).ToList();
        Assert.Equal(CastList.PlaceCapacity, list.Count);
        Assert.DoesNotContain(twin, list);
        Assert.Same(things[0], Find([], things, "cast0"));
        Assert.NotNull(Find([], things, $"cast{CastList.PlaceCapacity - 1}"));
        Assert.Null(Find([], things, $"cast{CastList.PlaceCapacity}"));    // the 65th is not in it
    }

    // ---- on arrival, and never after (nothing rebuilds it) -------------

    static Location Monastery()
    {
        var dir = Vitomir.Engine.Vfs.GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        var place = Location.Load(new Vitomir.Engine.Vfs.GameFiles(dir!), "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        CastList.Arrive(place);
        return place;
    }

    const string KaziSpeaks = """
        CONVERSATION: Test 0
        EVENT: ev_0
        	LINE:         Kazi_1_1_1                   Kazi
        """;

    [Fact]
    public void A_character_spawned_after_arrival_answers_to_no_cast_id_until_the_place_is_entered_again()
    {
        var place = Monastery();
        var lead = place.Player!.Thing;
        var kazi = place.Place("kazi", lead.Position + new System.Numerics.Vector3(64f, 0f, 0f), lead.Orientation, lead.Sector);
        Assert.SkipWhen(kazi?.Actor is null, "no kazi template");
        Assert.Equal("Kazi", kazi!.Actor!.CastId);

        Assert.Null(CastList.Find(place, "Kazi"));                      // GuidFromCastID answers 0
        var talk = Conversation.Resolve(
            NcoFile.Read(System.Text.Encoding.Latin1.GetBytes(KaziSpeaks.ReplaceLineEndings("\r\n")), "Test.nco").Conversations[0],
            CastList.Of(place), lead);
        Assert.Empty(talk.Actors);                                      // no speaker: no talk, listen or lip sync
        Assert.Equal(["Kazi"], talk.Missing);

        CastList.Arrive(place);                                         // the place entered again, or a save made here loaded
        Assert.Same(kazi, CastList.Find(place, "Kazi"));
    }

    [Fact]
    public void A_companion_who_joins_and_then_leaves_answers_to_nobody_until_the_place_is_entered_again()
    {
        var place = Monastery();
        var stranger = CastList.Of(place).FirstOrDefault(t => !t.Actor!.Has(Actor.Flag.Party));
        Assert.SkipWhen(stranger is null, "nobody in the monastery with a cast id");
        string cast = stranger!.Actor!.CastId;
        Assert.Same(stranger, CastList.Find(place, cast));              // in the place's list since arrival

        stranger.Actor.Flags |= Actor.Flag.Party;                       // AddToParty
        CastList.PartyRebuilt(place);
        Assert.Same(stranger, CastList.Find(place, cast));              // now from the party's list

        stranger.Actor.Flags &= ~Actor.Flag.Party;                      // RemoveFromParty
        CastList.PartyRebuilt(place);
        Assert.Null(CastList.Find(place, cast));                        // its name went out of the place's list when it joined

        CastList.Arrive(place);
        Assert.Same(stranger, CastList.Find(place, cast));
    }

    [Fact]
    public void A_join_and_a_leave_between_two_lookups_still_take_the_name_out()
    {
        var place = Monastery();
        var stranger = CastList.Of(place).First(t => !t.Actor!.Has(Actor.Flag.Party));
        stranger.Actor!.Flags |= Actor.Flag.Party;
        CastList.PartyRebuilt(place);
        stranger.Actor.Flags &= ~Actor.Flag.Party;
        CastList.PartyRebuilt(place);
        Assert.Null(CastList.Find(place, stranger.Actor.CastId));
    }
}
