// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The player's hand on the disciplines: the right button, the number keys, F and H. Each is a few
// lines in the click's handling in front of the cast order (`Cast.Order`), and what they add
// is the order's argument — which thing, which point, which discipline — and one gate: a character
// whose mind has the controls (a frenzy, a mesmerism, a daze, a fear, a pet's or a possessed body)
// does not take orders from the mouse.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>What the player's input does with disciplines.</summary>
public static class CastInput
{
    /// <summary>
    /// The character's mind has the controls — it is frenzied, mesmerised, dazed or afraid
    /// (modes 9 to 12), or it is a pet or possessed (the mind's bits 0x4 and 0x10000000). Every
    /// mouse order to the character being steered is refused while this holds, which is the whole
    /// of "you lose control in a frenzy".
    /// </summary>
    public static bool MindHasControls(WorldThing thing) =>
        thing.Mind is { } m && (m.Mode is AiMode.Frenzied or AiMode.Mesmerized or AiMode.Dazed or AiMode.Afraid
                                || (m.Flags & (Mind.FlagPet | Mind.FlagPossessed)) != 0);

    /// <summary>
    /// The refusal the mouse handlers share: the mind has
    /// the controls **and** it is the party's selected member (`party+4`). A possessed
    /// body has the controls by this test — and is never the selected member, which is why the
    /// player can walk it about and fight with it.
    /// </summary>
    public static bool Refused(WorldThing steered) =>
        MindHasControls(steered) && ReferenceEquals(steered.Place?.Selected, steered);

    /// <summary>
    /// The right button on a thing the pointer offered. Answers what the order answered
    /// (1 refused — the negative cursor —, 2 busy, 4 taken) or 0 for nothing done:
    /// <list type="number">
    /// <item>refused outright while the mind has the controls;</item>
    /// <item>a **scroll** in the hand is cast from, when the character may use it
    /// (<see cref="MayUse"/>) — refused when it may not;</item>
    /// <item>anything else in the hand is put away, and that is all the click does;</item>
    /// <item>otherwise the selected discipline is ordered at the thing — and with none selected,
    /// nothing happens.</item>
    /// </list>
    /// </summary>
    public static int OnThing(WorldThing steered, WorldThing thing, InputModifier mods)
    {
        if (steered.Actor is not { } me) return 0;
        if (Refused(steered)) return 1;
        if (me.Held is { } held)
        {
            if (held.Type == ItemType.Scroll)
                return MayUse(steered, held) ? (int)Cast.Order(steered, thing, null, null, mods, held) : 1;
            if (PutAway(steered, held)) return 0;
        }
        if (me.Disciplines.Current is null) return 0;
        return (int)Cast.Order(steered, thing, null, null, mods);
    }

    /// <summary>
    /// The right button on the ground, with the point under the pointer — which the
    /// engine takes without testing whether the pick found one, so a miss aims at
    /// wherever its buffer last pointed; here a miss is the caster's own feet.
    /// <list type="number">
    /// <item><paramref name="cancelEffect"/> first: the pointer over the icon of an effect that may
    /// be cancelled takes it off, and that is the click;</item>
    /// <item>refused while the mind has the controls;</item>
    /// <item>with an empty hand: an **area** discipline is aimed at the point, anything else is
    /// ordered as it stands — a self discipline casts, one that needs a thing is refused;</item>
    /// <item>a scroll in the hand is cast at the point when it may be used; anything else in the
    /// hand is put away, and refused when it will not go.</item>
    /// </list>
    /// </summary>
    public static int OnGround(WorldThing steered, Vector3? point, InputModifier mods, Func<bool>? cancelEffect = null)
    {
        if (steered.Actor is not { } me) return 0;
        if (cancelEffect?.Invoke() == true) return 4;
        if (Refused(steered)) return 1;
        var at = point ?? steered.Position;
        if (me.Held is not { } held)
        {
            if (me.Disciplines.Current is not { } cur) return 0;
            return cur.Definition.Has(DisciplineFlags.TargetArea)
                ? (int)Cast.Order(steered, null, at, null, mods)
                : (int)Cast.Order(steered, null, null, null, mods);
        }
        if (held.Type == ItemType.Scroll && MayUse(steered, held))
            return (int)Cast.Order(steered, null, at, null, mods, held);
        return PutAway(steered, held) ? 0 : 1;
    }

    /// <summary>
    /// The use gate a scroll passes before it is read — <see cref="Equipment.CanUse"/>
    /// whole: the item's script's `canuse` and the reader's `actorcanuse`, not
    /// while shape-changed, and for a player-type character the item's restrictions. Earlier, the
    /// two script questions were not asked here (no shipped scroll's class declares `canuse`).
    /// </summary>
    public static bool MayUse(WorldThing user, Item item) => Equipment.CanUse(user, item);

    /// <summary>The hand emptied into the pack; false when it will not go.</summary>
    static bool PutAway(WorldThing steered, Item held)
    {
        Ground.ClearHand(steered);
        var put = Stow.Put(steered, held, steering: false);
        if (put is StowResult.Refused) { Ground.Hand(steered, held); return false; }
        // A quest item is destroyed by the cascade; its thing goes too.
        if (put is StowResult.Quest && held.Thing is { } thing) steered.Place?.Remove(thing);
        return true;
    }

    /// <summary>
    /// The keys: 1 to 6 select the discipline in that quick slot — the
    /// <c>DISCIPLINESELECT</c> sound plays whether or not there is one —, F selects Feed, and H
    /// selects Blood Healing or, failing it, Heal; those two sound only when found. Answers whether
    /// anything was selected.
    /// </summary>
    public static bool Key(WorldThing steered, char key)
    {
        if (steered.Actor is not { } me) return false;
        if (key is >= '1' and <= '6')
        {
            Audio.Noise.Ui(steered.Place, Audio.FoleyEvent.DisciplineSelect);
            return DisciplineBook.SelectSlot(steered, key - '1');
        }
        var d = key switch
        {
            'F' or 'f' => me.Disciplines["Feed"],
            'H' or 'h' => me.Disciplines["BloodHealing"] ?? me.Disciplines["Heal"],
            _ => null,
        };
        if (d is null) return false;
        Audio.Noise.Ui(steered.Place, Audio.FoleyEvent.DisciplineSelect);
        DisciplineBook.Select(steered, d);
        return true;
    }
}
