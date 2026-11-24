// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The open hands a player character talks with.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **A player character talks with open hands** — which a conversation
/// calls for each player-type speaker as it begins (beside putting the weapon and the
/// shield away) and again as it ends, unless the conversation's flags say otherwise
/// (<see cref="ConversationFlag.NoWeaponsAway"/>, <see cref="ConversationFlag.NoRestoreWeapons"/>).
///
/// The hands are a model of their own, loaded into **sub-model slot 3**: the character's model
/// name, the armour it wears if any, and <c>F</c> — `christof_F`, `Christof_plate_F`,
/// `serena_leather_F` (the specific-model name with the suffix; 50 such models ship). While they are
/// on, the fists are hidden (<see cref="NamedShapes.Fists"/>) and
/// <see cref="Actor.Flag2.UsingHands"/> is set; taking them off empties the slot and shows the
/// fists again, unless <see cref="Actor.Flag2.WeaponNoHands"/> says the hands are not to come back.
/// A character with no such model — any the `_F` name misses — keeps its fists: the slot load
/// fails (the slot's fill answers 0) and neither the fists nor the bit are touched.
/// </summary>
public static class TalkingHands
{
    /// <summary>The sub-model slot the hands go in (slot 3).</summary>
    public const int Slot = 3;

    /// <summary>What is appended to the model name.</summary>
    public const string Suffix = "F";

    /// <summary>
    /// The hands on. Nothing for a character whose hands are already on or that has no model
    /// instance; otherwise load the hands, and only if they load, hide the fists and set the bit.
    /// Answers whether they went on.
    /// </summary>
    public static bool On(WorldThing thing)
    {
        if (thing.Actor is not { } actor || actor.Has2(Actor.Flag2.UsingHands)) return false;
        if (thing.Shapes is not { Body.Model: { } body }) return false;                    // pad9 == 0
        string name = Name(thing, actor);
        if (thing.Place is not { Files: { } files } place || place.Mesh.Model(name, files) is not { } model
            || model.Bones.Length < body.Bones.Length) return false;
        actor.TalkingHands = name;
        NamedShapes.Fill(thing, Slot, name, model);
        NamedShapes.Fists(thing, shown: false);
        actor.Flags2 |= Actor.Flag2.UsingHands;
        return true;
    }

    /// <summary>
    /// The hands off. Only with the hands on: empty slot 3, show the fists unless
    /// <see cref="Actor.Flag2.WeaponNoHands"/>, clear the bit.
    /// </summary>
    public static void Off(WorldThing thing)
    {
        if (thing.Actor is not { } actor || !actor.Has2(Actor.Flag2.UsingHands)) return;
        if (thing.Shapes is not { Body.Model: not null }) return;
        actor.TalkingHands = "";
        NamedShapes.Fill(thing, Slot, null, null);
        if (!actor.Has2(Actor.Flag2.WeaponNoHands)) NamedShapes.Fists(thing, shown: true);
        actor.Flags2 &= ~Actor.Flag2.UsingHands;
    }

    /// <summary>
    /// The hands' model name: the body's model, `_` and the worn armour's
    /// <c>ARMOR_MODEL</c> when it has one, `_F` — cut at 32 like every name built there.
    /// </summary>
    public static string Name(WorldThing thing, Actor actor) =>
        HeldModel.Composed(thing.Model, actor.Armor?.ArmorModel ?? "", Suffix);
}
