// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Whether the pointer lands on a thing at all — the gate the UI update runs on
// every hit as it walks the pick. It was missing, and the symptom was a peaceful square that
// looked like a fight: the pointer over Old Town's civilians as a human Christof and got a
// **sword** over every one of them.
//
// The shape of the mistake is worth keeping. The cursor choice really does have no neutral
// test — it tests TALKTO, then PICKUP, then the team — so a copy of that choice alone is faithful
// to the choice and wrong about the game, because by the time 1.1 reaches it every thing
// the verb classifier had nothing to say about has already been refused. **A faithful copy of one
// step is not a faithful copy of what the player sees.**
using Vitomir.Engine.Rpg;
using Vitomir.Formats;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

public class PointerTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    /// <summary>
    /// **What play showed, pinned against the shipped scene.** Every character Old Town places is
    /// `THING_AF_NEUTRAL` — the four `civ*_pe` stand-ins, the `stranger`, the two `convknight`s,
    /// the horse and the crow — so the pointer lands on none of them and the cursor stays the
    /// plain one. The second half is what makes it a regression test rather than a tautology: for
    /// every one of them the cursor switch on its own would have said <c>Attack</c>.
    /// </summary>
    [Fact]
    public void Nobody_in_old_town_offers_the_pointer_anything()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        var people = place.Things.Where(t => t.Type == ThingType.Actor).ToList();
        Assert.True(people.Count >= 9, $"only {people.Count} characters in the scene");

        foreach (var who in people)
        {
            Assert.False(Highlight.Offers(me, who),
                         $"{who.Template} offers the pointer {Cursor.Verb(me, who)}");
            // And the switch behind the gate is exactly what would have drawn the blade.
            Assert.Equal(CursorId.Attack, Highlight.Cursor(who, me));
        }
    }

    /// <summary>
    /// The gate must not mute a real fight. Clear the neutral bit on one of them and the pointer
    /// finds them again, with the blade — which is the same civilian, the same team and the same
    /// distance as the ones above.
    /// </summary>
    [Fact]
    public void A_character_who_is_not_neutral_is_pointed_at_with_a_blade()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        var who = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { } a
                                          && a.Has(Actor.Flag.Neutral) && !t.Has(WorldThing.Flag.NoHighlight));

        who.Actor!.Flags &= ~Actor.Flag.Neutral;

        Assert.True(Highlight.Offers(me, who));
        Assert.Equal(CursorVerb.Attack, Cursor.Verb(me, who));
        Assert.Equal(CursorId.Attack, Highlight.Cursor(who, me));
    }

    /// <summary>
    /// `THING_FLAG_NOHIGHLIGHT` is its own refusal, ahead of everything else — the horse and the
    /// crow carry it as well as being neutral, so they would stay out of it even if somebody
    /// cleared their neutral bit. That is the level author's own switch and nothing about the
    /// character.
    /// </summary>
    [Fact]
    public void A_thing_the_level_marked_nohighlight_is_never_pointed_at()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        var beast = place.Things.First(t => t.Has(WorldThing.Flag.NoHighlight) && t.Type == ThingType.Actor);

        beast.Actor!.Flags &= ~Actor.Flag.Neutral;      // the other reason it would be refused

        Assert.False(Highlight.Offers(me, beast));
        Assert.Contains(beast.Template, new[] { "horse", "crow" });
    }

    /// <summary>
    /// **A secret thing needs a perceptive character to point at it.** `THING_FLAG_SECRET` wants
    /// any perception at all and `THING_FLAG_TOPSECRET` wants four or more, both read off the
    /// player's own perception — the same byte the notice test compares a target's stealth
    /// against. Nothing in this tree set either bit before, so the mechanic existed in the data
    /// and did nothing.
    /// </summary>
    [Fact]
    public void A_secret_thing_wants_perception_and_a_top_secret_one_wants_four()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        // Not just any prop: the scene marks a fair number of them `NOHIGHLIGHT` — the hanging
        // smithy sign, the banners — because they are scenery rather than furniture.
        var prop = place.Things.First(t => t.Type == ThingType.Prop && Highlight.Offers(me, t));
        Assert.Contains(place.Things, t => t.Type == ThingType.Prop && t.Has(WorldThing.Flag.NoHighlight));

        me.Actor!.Perception = 0;
        prop.Flags |= WorldThing.Flag.Secret;
        Assert.False(Highlight.Offers(me, prop));
        me.Actor.Perception = 1;
        Assert.True(Highlight.Offers(me, prop));

        prop.Flags = (prop.Flags & ~WorldThing.Flag.Secret) | WorldThing.Flag.TopSecret;
        Assert.False(Highlight.Offers(me, prop));
        me.Actor.Perception = 3;
        Assert.False(Highlight.Offers(me, prop));
        me.Actor.Perception = 4;
        Assert.True(Highlight.Offers(me, prop));
    }

    /// <summary>
    /// A character the player cannot notice is not pointed at either — the gate runs the sight test
    /// before it asks the verb anything, so a hidden enemy does not give itself away by turning
    /// the cursor.
    /// </summary>
    [Fact]
    public void Somebody_the_player_cannot_notice_is_not_pointed_at()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        var who = place.Things.First(t => t.Type == ThingType.Actor && !t.Has(WorldThing.Flag.NoHighlight));
        who.Actor!.Flags &= ~Actor.Flag.Neutral;
        Assert.True(Highlight.Offers(me, who));

        who.Actor.Stealth = 3;
        me.Actor!.Perception = 2;
        Assert.False(Highlight.Offers(me, who));
        me.Actor.Perception = 3;
        Assert.True(Highlight.Offers(me, who));
    }

    /// <summary>
    /// An item lying on the floor is pointed at and offers the pickup cursor — which is the case
    /// an earlier change made reachable, and the one that would have been quietly broken if the gate had
    /// been written as "actors only".
    /// </summary>
    [Fact]
    public void An_item_on_the_floor_is_pointed_at()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        var dropped = place.Place("broadsword", me.Position, System.Numerics.Vector3.Zero, me.Sector)!;

        Assert.True(Highlight.Offers(me, dropped));
        Assert.Equal(CursorVerb.Take, Cursor.Verb(me, dropped));
        Assert.Equal(CursorId.Pickup, Highlight.Cursor(dropped, me));

        // And once it is out of the world the pick never reaches it — **not** because the gate
        // refuses it. The gate has no world test in it and neither does this; the engine's ray
        // walks a sector's own thing list, which a withdrawn thing has been unlinked from, and
        // ours walks `Location.Things`, which it has left. Adding a world test here would be a
        // guard the engine does not have, in the one place where its absence cannot bite.
        Assert.True(place.Withdraw(dropped));
        Assert.DoesNotContain(dropped, place.Things);
    }

    /// <summary>
    /// **A caster points further than the verb does.** With a discipline selected, anything that
    /// discipline could be cast at is pointable — so a neutral civilian, who offers the pointer
    /// nothing at all otherwise, becomes a target the moment a discipline that can take one is up.
    /// That is how you cast on somebody you cannot attack, and it is the second of the gate's two
    /// routes past the verb.
    /// </summary>
    [Fact]
    public void A_selected_discipline_reaches_past_the_verb()
    {
        using var files = Install.Open();
        var place = Open(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers.File is null, "misc/game.ndd not in the install");
        var me = place.Player!.Thing;
        var who = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { } a
                                          && a.Has(Actor.Flag.Neutral) && !t.Has(WorldThing.Flag.NoHighlight));
        who.Position = me.Position;
        Assert.False(Highlight.Offers(me, who), "a neutral civilian offers nothing to begin with");

        // Not an offensive one: that is the discipline a neutral target refuses.
        var gentle = powers.File!.All.First(d => !d.Has(DisciplineFlags.Offensive)
                                                 && d.Has(DisciplineFlags.TargetEnemy) && d.MaxDistance > 0);
        me.Actor!.Disciplines.Current = me.Actor.Disciplines.Add(gentle);

        Assert.True(Highlight.Offers(me, who));
        // And putting it away puts the pointer back.
        me.Actor.Disciplines.Current = null;
        Assert.False(Highlight.Offers(me, who));
    }

    /// <summary>The player never points at their own character (the gate's first test).</summary>
    [Fact]
    public void The_player_is_never_pointed_at()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        Assert.False(Highlight.Offers(me, me));
    }

    /// <summary>
    /// **The other half of the Old Town test, and the correction that earned it.** In the base game,
    /// against an earlier claim that the civilians are refused for neutrality, those monks can be killed and fed
    /// off, despite being neutral; and when Christof is human,
    /// all of the characters are special versions that cannot be interacted with, with a `_PE`
    /// suffix.
    ///
    /// That is right, and the reason is stronger: **no monk template is neutral at
    /// all.** `Civ09monk1` and `Civ10monk2` carry the ordinary civilian `0x9004040` — humanity
    /// loss and nothing that stops a blow — and only the `_PE` variants carry `0x40000`. So the
    /// rule that keeps a mortal Christof's hands off Prague is **which people the scene places**,
    /// not a property of the people: `OTDY_4_1` places `civ09monk1_pe`, `MON1_8_2` places
    /// `civ09monk1`, and the pointer treats them differently because they *are* different.
    ///
    /// Pinned here against the scene a screenshot came from, so the earlier misreading cannot
    /// come back: the monastery's monks offer the pointer a blade.
    /// </summary>
    [Fact]
    public void The_monastery_monks_are_pointed_at_with_a_blade()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false,
                                  scene: "MON1_8_2", player: "Christof");
        var me = place.Player!.Thing;
        var monks = place.Things.Where(t => t.Type == ThingType.Actor
                                            && t.Template.StartsWith("civ", StringComparison.OrdinalIgnoreCase)
                                            && t.Template.Contains("monk", StringComparison.OrdinalIgnoreCase))
                                .ToList();
        Assert.True(monks.Count >= 3, $"expected the monastery's monks, found {monks.Count}");

        foreach (var monk in monks)
        {
            Assert.False(monk.Actor!.Has(Actor.Flag.Neutral), $"{monk.Template} should not be neutral");
            Assert.False(monk.Has(WorldThing.Flag.NoHighlight), $"{monk.Template} should not be nohighlight");
            monk.Position = me.Position;                     // the gate's notice test wants them in reach
            Assert.True(Highlight.Offers(me, monk), $"{monk.Template} offers the pointer nothing");
            Assert.Equal(CursorVerb.Attack, Cursor.Verb(me, monk));
        }
    }

    /// <summary>
    /// **`NOHIGHLIGHT` and neutral are two different exclusions, and the nun proves it.** `Nun`
    /// carries the ordinary civilian `ACTOR_FLAGS 0x9004040` — she can be killed and fed on — and
    /// `THINGFLAGS 0x40`, so the pointer never lands on her. Twenty-five of the shipped actor
    /// templates carry the author's flag; seventy carry the neutral bit; neither set contains the
    /// other.
    /// </summary>
    [Fact]
    public void A_nun_can_be_killed_and_still_never_highlights()
    {
        using var files = Install.Open();
        var place = Open(files);
        var me = place.Player!.Thing;
        var nun = place.Place("Nun", me.Position, System.Numerics.Vector3.Zero, me.Sector);
        Assert.SkipWhen(nun?.Actor is null, "the Nun template is not in this install");

        Assert.False(nun!.Actor!.Has(Actor.Flag.Neutral), "the plain Nun is not neutral");
        Assert.True(nun.Has(WorldThing.Flag.NoHighlight), "the plain Nun is marked nohighlight");
        Assert.False(Highlight.Offers(me, nun));
        // The verb would have had plenty to say; the author's flag is what stops it.
        Assert.Equal(CursorVerb.Attack, Cursor.Verb(me, nun));
    }
}
