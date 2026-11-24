// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the thing under the pointer does to the screen: which cursor it puts up, and what colour it
// glows — and, first, **whether the pointer lands on it at all**.
//
// That last one is the game library's `Offers`, and leaving it out is how a quiet street ended up
// drawing a blade over every passer-by. The UI update runs it on each hit as it walks the pick and
// takes the first thing that says yes; a thing the verb classifier has nothing to offer on — every
// `THING_AF_NEUTRAL` civilian in the game — never becomes the hovered thing, so the cursor switch
// below never sees it. Port the switch without the gate and you have a faithful copy of one
// function and the wrong game.
//
// **The cursor's switch is not the verb's switch.** `Rpg.Cursor.Verb` is the action classifier,
// which answers what a click *does*; the cursor's choice answers what the pointer *looks
// like*, and it is a shorter switch over the same thing. They agree about actors and items and
// disagree about props and containers: the verb gives a prop `Use` and a container `Use` or
// `Attack`, while the cursor switch has no case for either, so both keep the plain pointer. A
// barrel you can smash shows an ordinary cursor right up until you click it. That is shipped
// behaviour and is why the two functions are written out separately here rather than folded.
//
// The glow is a third thing again: the interface's hover writes a colour into the hovered thing's own
// model (`thing+300`) when the hover changes, and never writes it back — so an
// object keeps its tint until something else sets one. **The cursor itself is never tinted**;
// The cursor draw passes 255 for all four vertex colours with no path that varies them.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>The pointer's and the world's answer to what is under the mouse.</summary>
public static class Highlight
{
    /// <summary>Flag `0x80` — can be talked to.</summary>
    public const uint TalkTo = 0x80;
    /// <summary>Flag `0x100` — can be picked up. On an actor this is a body you can carry.</summary>
    public const uint Liftable = 0x100;

    /// <summary>
    /// **Whether the pointer lands on this thing at all** — the game library's `Offers`, which the UI
    /// update runs on every hit as it walks the pick (the hover's ray loop). The loop takes the
    /// first thing that answers yes; when nothing does, the hovered thing stays null and the
    /// cursor, the hover tint and the description bar all get nothing to say.
    ///
    /// **This gate is what makes a peaceful street peaceful**, and it was missing. Every civilian
    /// in Old Town carries `THING_AF_NEUTRAL`; the verb classifier
    /// (<see cref="Rpg.Cursor.Verb"/>) answers <see cref="CursorVerb.None"/> for a neutral
    /// character; and **a thing with no verb is not pointed at**. Without the gate the cursor went
    /// straight on to <see cref="Cursor"/>'s friendly test, which knows nothing about neutrality,
    /// and drew a blade over every passer-by the player is not on a team with. That showed in
    /// play: the cursor turned to a sword over them, which is wrong.
    ///
    /// The engine's own order:
    /// <list type="number">
    /// <item>never the player's own character;</item>
    /// <item>never a thing on its way out (<see cref="WorldThing.Flag.Remove"/>) or one the level
    /// author marked <see cref="WorldThing.Flag.NoHighlight"/> — which is how the horse and the
    /// crow in Old Town stay out of it, independently of being neutral;</item>
    /// <item><see cref="WorldThing.Flag.Secret"/> needs **any** perception at all and
    /// <see cref="WorldThing.Flag.TopSecret"/> needs **4 or more**, both read off the player's own
    /// perception — the same byte the notice test compares stealth against. A perceptive character
    /// can point at things an oblivious one cannot see;</item>
    /// <item>a character that <see cref="Actor.Flag.StartHidden"/> is out;</item>
    /// <item>the player has to be able to **notice** it (its stealth against their
    /// perception);</item>
    /// <item>and then the verb decides: anything <see cref="Rpg.Cursor.Verb"/> offers something on
    /// is pointable, and anything it does not is not.</item>
    /// </list>
    ///
    /// **Two routes past the verb**, for a caster. With a discipline **selected**,
    /// the player may point at anything that discipline could be cast at —
    /// <see cref="Rpg.DisciplineTarget.MayTarget"/>. So a neutral
    /// civilian the pointer would not otherwise touch **is** pointable while a discipline that can
    /// take them is up, which is how you cast on somebody you cannot attack.
    ///
    /// The other route is the same test through a **scroll** held in the hand (item type 11,
    /// the scroll's own discipline), which is asked first.
    ///
    /// Storyteller mode replaces the whole function with "anything but my own
    /// thing"; there is no Storyteller here to model.
    /// </summary>
    public static bool Offers(WorldThing player, WorldThing thing)
    {
        if (ReferenceEquals(player, thing)) return false;
        if ((thing.Flags & (WorldThing.Flag.Remove | WorldThing.Flag.NoHighlight)) != 0) return false;

        int perception = player.Actor?.Perception ?? 0;
        if (thing.Has(WorldThing.Flag.Secret) && perception == 0) return false;
        if (thing.Has(WorldThing.Flag.TopSecret) && perception < 4) return false;

        if (thing.Type is ThingType.Actor or ThingType.Player
            && thing.Actor is { } who && who.Has(Actor.Flag.StartHidden)) return false;

        // Stealth is zero for everybody until a HIDE effect raises it, so the moving half of the
        // notice test only ever matters for a character that is already hiding.
        if (!Sight.Notices(player, thing, targetMoving: false)) return false;

        if (Rpg.Cursor.Verb(player, thing) != CursorVerb.None) return true;

        // A scroll in the hand, and then the selected discipline, reach further than the verb does.
        if (player.Actor?.Held is { Type: Rpg.ItemType.Scroll, ScrollDiscipline.Length: > 0 } scroll
            && player.Place?.Powers?[scroll.ScrollDiscipline] is { } read
            && Rpg.DisciplineTarget.MayTarget(read, player, thing)) return true;
        return player.Actor?.Disciplines.Current is { } casting
               && Rpg.DisciplineTarget.MayTarget(casting.Definition, player, thing);
    }

    /// <summary>
    /// The cursor choice's switch. <paramref name="hovered"/> null — nothing under the pointer — is the
    /// plain cursor, which is also what the engine falls back to when the pick misses, and what
    /// <see cref="Offers"/> leaves it at for most of a street.
    ///
    /// **Null is the switch's one case that sets nothing**: a character that is neither talkable nor
    /// liftable and is on the player's side (the side test answers yes), or with no player to
    /// ask, jumps straight to the return and **the cursor stays as it was** —
    /// <see cref="CursorState.Update"/> keeps it. The only friends the pointer lands on are a staked
    /// one (the verb is unstake) and one a selected discipline or a scroll may target, and the
    /// pointer nearly always reaches them from the ground, so it is the plain pointer that stays:
    /// **nothing on screen says a staked friend can be freed** (1.1: the cursor
    /// did not change over staked Wilhem, but the click unstaked him — "which is why I never knew it
    /// could be done"). Swept straight off an enemy onto a friend, the blade stays.
    ///
    /// **It has no neutral test and does not need one**, which is worth knowing before anybody
    /// adds one: by the time this runs, <see cref="Offers"/> has already refused every thing the
    /// verb classifier had nothing to say about.
    /// </summary>
    public static CursorId? Cursor(WorldThing? hovered, WorldThing? player)
    {
        if (hovered is null || ReferenceEquals(hovered, player)) return CursorId.Default;
        switch (hovered.Type)
        {
            case ThingType.Actor:
            case ThingType.Player:
            {
                uint flags = hovered.Actor?.Flags ?? 0u;
                if ((flags & TalkTo) != 0) return CursorId.Talk;
                if ((flags & Liftable) != 0) return CursorId.Pickup;
                // The side test compares the team byte on both and the cursor only
                // turns to a blade when it answers **no** — the same test `Sight.Friendly` is. A
                // yes, or no player at all, sets nothing.
                return player is null || Sight.Friendly(player, hovered) ? null : CursorId.Attack;
            }

            case ThingType.Item:
            case ThingType.Weapon:
            case ThingType.Armor:
                return CursorId.Pickup;

            // No case for Prop or Container: the switch's `default` catches them.
            default:
                return CursorId.Default;
        }
    }

    /// <summary>
    /// The tint the hover writes into the hovered thing's model, as `0xAARRGGBB`. The verb is
    /// <see cref="Rpg.Cursor.Verb"/>'s, and the one branch that also looks at the type is
    /// <see cref="CursorVerb.Use"/> — a person you can talk to glows a different colour from a
    /// door you can open.
    /// </summary>
    public static uint Colour(CursorVerb verb, ThingType type) => verb switch
    {
        CursorVerb.Attack => 0xFF600000,
        CursorVerb.Take => 0xFF005050,
        CursorVerb.Use => type is ThingType.Actor or ThingType.Player ? 0xFF000050u : 0xFF505000u,
        _ => 0xFF104040,
    };
}
