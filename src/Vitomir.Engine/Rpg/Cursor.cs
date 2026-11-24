// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a click on a thing means.
//
// The original does not decide this at the click. It decides it continuously, for whatever is
// under the cursor, and shows the answer on the status bar — which is why `vuiStatusBar` rewrites
// a character's description three ways (talk, neutral, enemy). One routine is the
// classifier, and the click is what the click then does with the answer.
//
// So this is the verb half of the highlight system, written now because without it the player can
// walk at an enemy and nothing else. The *showing* of it is the status bar's job and belongs with
// the HUD.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// What the cursor is offering to do with a thing — the engine's own numbering, returned by
/// by the classifier and switched on by the click.
/// </summary>
public enum CursorVerb
{
    /// <summary>Nothing doing: the player itself, a corpse, a friendly character, anything unknown.</summary>
    None = 0,
    /// <summary>Swing at it. Acted on, as are Take and the drop.</summary>
    Attack = 2,
    /// <summary>Pick it up — an item, a weapon or a suit of armour.</summary>
    Take = 3,
    /// <summary>Use it, or talk to it.</summary>
    Use = 4,
    /// <summary>Pull the stake out of a friendly character. There is a whole verb for it.</summary>
    Unstake = 15,
}

/// <summary>The highlight system's verb half.</summary>
public static class Cursor
{
    /// <summary>
    /// What a click on <paramref name="thing"/> would do, in the engine's own order:
    /// <list type="number">
    /// <item>the player itself is never a target of its own cursor;</item>
    /// <item>a **dead** character offers nothing at all — which is the other half of why you
    /// cannot feed on a corpse: not only does the discipline refuse, the click
    /// never reaches it;</item>
    /// <item>`THING_AF_TALKTO` is <see cref="CursorVerb.Use"/>, and so is `THING_AF_PICKUP` on
    /// anything that is not a party member — that is a body you can carry;</item>
    /// <item>`THING_AF_NEUTRAL` (with `TALKTO`, the same 0x40080 mask every search steps over)
    /// offers nothing;</item>
    /// <item>otherwise it comes down to the **team test** — the same one the searches use: a
    /// friendly staked character is <see cref="CursorVerb.Unstake"/>, a friendly one is nothing,
    /// and anybody else is <see cref="CursorVerb.Attack"/>;</item>
    /// <item>an item, a weapon or armour is <see cref="CursorVerb.Take"/>; a prop is
    /// <see cref="CursorVerb.Use"/>;</item>
    /// <item>and a container is <see cref="CursorVerb.Use"/> unless its flag bit 2 says to break
    /// it open, which makes it <see cref="CursorVerb.Attack"/>. Bit 1 shuts it out entirely.</item>
    /// </list>
    /// </summary>
    public static CursorVerb Verb(WorldThing player, WorldThing thing)
    {
        if (ReferenceEquals(player, thing)) return CursorVerb.None;

        switch (thing.Type)
        {
            case ThingType.Actor:
            case ThingType.Player:
            {
                if (thing.Actor is not { } a) return CursorVerb.None;
                if (a.Has(Actor.Flag.Dead)) return CursorVerb.None;
                if (a.Has(Actor.Flag.TalkTo)) return CursorVerb.Use;
                if (a.Has(Actor.Flag.Pickup) && thing.Type != ThingType.Player) return CursorVerb.Use;
                if ((a.Flags & Minds.LeaveAloneFlags) != 0) return CursorVerb.None;

                bool friendly = Sight.Friendly(player, thing);
                if (friendly && a.Has(Actor.Flag.Staked)) return CursorVerb.Unstake;
                return friendly ? CursorVerb.None : CursorVerb.Attack;
            }

            case ThingType.Item:
            case ThingType.Weapon:
            case ThingType.Armor:
                return CursorVerb.Take;

            case ThingType.Prop:
                return CursorVerb.Use;

            case ThingType.Container:
            {
                // **The container's own word, the thing's state word** — which on a character is ACTOR_FLAGS
                // and on a container is `Rpg.Container`'s state. This used to read `Actor.Flags`,
                // and a container has no actor, so every one of them came back as *use*: a barrel
                // you are meant to smash offered to be opened instead, and an emptied chest went on
                // offering itself.
                uint state = thing.State;
                if ((state & ContainerShut) != 0) return CursorVerb.None;
                return (state & ContainerBreakable) != 0 ? CursorVerb.Attack : CursorVerb.Use;
            }

            default:
                return CursorVerb.None;
        }
    }

    /// <summary>A container the cursor offers nothing on at all: one already opened (bit 0).</summary>
    public const uint ContainerShut = Container.Opened;
    /// <summary>A container you break rather than open (bit 1) — a barrel, with CONTAINER_HEALTH above 0.</summary>
    public const uint ContainerBreakable = Container.Breakable;

    /// <summary>
    /// Whether a click should take the **second** attack. The click handler passes
    /// <c>flags &amp; 1</c> straight through as the attack index, so the modifier the
    /// player holds is what chooses the alternate attack — the same index the AI rolls `attackAlt`
    /// for.
    /// </summary>
    public const uint AltAttack = 0x1;
}
