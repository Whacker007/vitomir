// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A character in another's shape, and given its own back: the change and its end,
// the record between them.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The shape-change record** — the 0x40 bytes allocated by the change and
/// freed by the end. It keeps what the new shape replaced, so that the end can give each piece
/// back, and the two things a save writes of it: the template's name and the flags.
/// <code>
///   the render model the character had
///   a player's head skin
///   a player's head model
///   the motion set
///   a player's head motion set
///   the foley
///   the weapon in the hand
///   the template's name, 31 characters and a zero
///   the flags the change was asked with
/// </code>
/// Each kept field is set only when the shape replaced that piece, and the end gives back only the
/// fields that are set — a null here is "left alone", exactly as the engine's zero is.
///
/// The engine keeps the old **model object**, and with it everything that lives on a model: its
/// four sub-model slots (the armour that stands in for the body, the shield on the arm, the talking
/// hands), the hide bits of its named shapes, its alpha, scale, shell and freeze. Vitomir keeps those
/// on the thing and the actor rather than on a model object, so the record keeps them too
/// (<see cref="Shapes"/>, <see cref="Fx"/>, <see cref="BodyModel"/>, <see cref="PortraitModel"/>) and
/// the new shape starts with none of them — which is what a freshly loaded model is
/// (clears the whole allocation). That half is ours in its form and the engine's in its effect.
/// </summary>
public sealed class ShapeChange
{
    /// <summary>The template the character took the shape of, as it was asked for.</summary>
    public required string Template { get; init; }
    /// <summary>The flags it was asked with: <see cref="ShapeShift.NoWeapon"/> and the rest.</summary>
    public uint Flags { get; init; }

    /// <summary>The model it had, or null when the shape did not change it.</summary>
    public string? Model { get; internal set; }
    /// <summary>The motion set it had, or null.</summary>
    public string? MotionSet { get; internal set; }
    /// <summary>The foley it had, or null.</summary>
    public string? Foley { get; internal set; }
    /// <summary>
    /// The weapon it had in the hand, or null — kept both when the shape put its own
    /// weapon in the hand and when <see cref="ShapeShift.Weaponless"/> emptied it.
    /// </summary>
    public Weapon? Weapon { get; internal set; }
    /// <summary>A player's portrait head model (with its skin), or null.</summary>
    public string? HeadModel { get; internal set; }
    /// <summary>A player's head motion set, or null.</summary>
    public string? HeadMotionSet { get; internal set; }

    /// <summary>The old model's own instance state — its slots and hidden shapes (ours: part of the model object in 1.1).</summary>
    public NamedShapes.State? Shapes { get; internal set; }
    /// <summary>The old model's alpha, scale, shell and freeze (ours: part of the model object in 1.1).</summary>
    public ModelFx? Fx { get; internal set; }
    /// <summary>What stood in the old model's sub-model slot 0 — its armour (ours: the model object's slot in 1.1).</summary>
    public string? BodyModel { get; internal set; }
    /// <summary>What stood in the old head's sub-model slot 0 — the armour's head (ours, likewise).</summary>
    public string? PortraitModel { get; internal set; }

    /// <summary>
    /// The weapon and off-hand item that were in the hands when the shape went on and were **not**
    /// put onto the new model — nothing fills a fresh model's slots but a later `SetWeapon` or
    /// `SetOffhand` with something different (the same weapon is left alone).
    /// Ours: what 1.1 expresses by the new model's slots simply being empty.
    /// </summary>
    internal Weapon? UndrawnWeapon { get; set; }
    /// <inheritdoc cref="UndrawnWeapon"/>
    internal Item? UndrawnOffhand { get; set; }
    /// <summary>Whether the shape was built on this character, rather than read back from a save with only its name and flags.</summary>
    public bool Built { get; internal set; }

    /// <summary>
    /// Fix `shapetorch` (ours): the utility item taken out of the hand, still in its slot, while
    /// the shape is on, and given back when it ends (<see cref="ShapeTorchFix"/>). Null without the fix.
    /// </summary>
    public Item? Pocketed { get; internal set; }
}

/// <summary>
/// **Shapeshifting**: Protean's Shape of the Beast turns a character into its
/// clan's beast (`game.ncd`'s shape column — `wolf_Grey` for a Brujah), and Obfuscate's Mask of
/// 1,000 Faces into another character altogether. Both are scripts (`EffectDiscShapeOfTheBeast`,
/// `EffectDiscMask1000Faces`) that call `CodexActor.ActorShapeShift` when their crouch or
/// their second is up, and `ActorEndShapeShift` when the effect ends or the character is
/// killed. 1.1's side is two steps and the record between them:
/// <see cref="Shift"/> and <see cref="End"/>.
///
/// **What a shape changes** is the template's model, motion set and foley, the weapon in the hand,
/// and for a player-type character the portrait head — and **nothing else**: not the stats, the
/// health, the speeds, the size, the name, the team or the skeleton id. A wolf-shaped Christof is
/// Christof with a wolf's body, a wolf's walk and a wolf's bite; Shape of the Beast's other two
/// effects (`ef_disc_shapeofthebeast2`, a heal over time, and `…3`, `MOVESPEED` × 1.2 to 2.5) are
/// what make him quicker and tougher, not the template. While a shape is on, no discipline can be
/// cast, no item used or worn, taken off or let go.
/// </summary>
public static class ShapeShift
{
    /// <summary>
    /// The flags (`Codex.java`'s `ACTOR_SHAPESHIFT_*`): keep the weapon in the hand; keep the
    /// portrait head; keep the foley; and an empty hand that stays empty. Shape of the Beast passes
    /// none; Mask of 1,000 Faces passes <see cref="NoFoley"/> + <see cref="Weaponless"/>.
    /// </summary>
    public const uint NoWeapon = 0x01, NoHead = 0x02, NoFoley = 0x04, Weaponless = 0x08;

    /// <summary>The longest template name the record and the native keep (<c>strncpy</c> 0x20 and a zero at 31).</summary>
    public const int NameLength = 31;

    /// <summary>Whether a shape is on this character (`IsActorShapeShifted`).</summary>
    public static bool IsShifted(Actor actor) => actor.ShapeChange is not null;

    /// <summary>
    /// **The change** — the character into <paramref name="template"/>'s shape. In the engine's order:
    /// <list type="number">
    /// <item>a shape already on refuses (answers false) unless <paramref name="forced"/> — the
    /// post-load step is the one caller that forces it;</item>
    /// <item>a template that is not there refuses;</item>
    /// <item>a weapon swap in force is ended (the claws go);</item>
    /// <item>a record already there is freed **without being undone** — so a forced shape over a
    /// shape that was built keeps the second's originals and loses the first's;</item>
    /// <item>`MODEL` (tag 4): the old model kept, the template's loaded in its place;</item>
    /// <item>`MOTIONSET` (tag 9): likewise;</item>
    /// <item>`FOLEY` (tag 6), unless <see cref="NoFoley"/>: likewise;</item>
    /// <item>with <see cref="Weaponless"/> the weapon in the hand is kept and the hand left **empty**
    /// (the hand emptied directly, no `SetWeapon`, so the fallback fists do not come back); otherwise
    /// `ACTOR_WEAPON` (tag 93), unless <see cref="NoWeapon"/>: a weapon made from that template,
    /// the old one kept, the new one given its owner and put in the hand
    /// (`SetWeapon`);</item>
    /// <item>a player-type character only (type 7), unless <see cref="NoHead"/>: `ACTOR_HEADMODEL`
    /// (tag 57) — the portrait's head and its skin — and `ACTOR_HEADMOTION` (tag 58);</item>
    /// <item>the locomotion chosen again on the new motion set.</item>
    /// </list>
    /// The template's tags are the resolved ones, parents and all. **Ours**: a model that
    /// does not load is not put on (1.1 would take the null and fail on the mixer), and an
    /// `ACTOR_WEAPON` that makes no weapon is not put in the hand (1.1 makes whatever it names).
    /// </summary>
    /// <returns>Whether the shape went on.</returns>
    public static bool Shift(WorldThing thing, string template, uint flags, bool forced = false, Fixes? fixes = null)
    {
        if (thing.Actor is not { } actor || thing.Type is not (ThingType.Actor or ThingType.Player)) return false;
        if (actor.ShapeChange is not null && !forced) return false;
        if (thing.Place?.Templates is not { } templates) return false;
        if (template.Length > NameLength) template = template[..NameLength];
        if (template.Length == 0 || !templates.Has(template)) return false;
        var tags = templates.Resolve(template);

        // Fix `shapetorch` (ours, off by default): the off hand's utility item out of the hand and
        // out of the world, still in its slot, kept on the record until the shape ends. A forced
        // shape over one that had it keeps it (the record freed, what it kept carried over).
        var pocketed = ShapeTorchFix.LetGo(thing, (fixes ?? thing.Place?.Fixes ?? Fixes.Current).ShapeTorch, loading: forced)
                       ?? actor.ShapeChange?.Pocketed;

        WeaponOverride.Cancel(thing);
        var record = new ShapeChange { Template = template, Flags = flags, Built = true };  // the old one freed
        record.Pocketed = pocketed;
        actor.ShapeChange = record;

        // The body: tags 4 and 9. The old model object goes into the record
        // whole — its slots, its hidden shapes and its alpha, scale and shell with it (ours).
        string? model = Str(tags, Tags.Model);
        if (model is not null && thing.Place is { } place && !place.CanLoadModel(model)) model = null;
        string? motionSet = Str(tags, Tags.MotionSet);
        if (model is not null)
        {
            record.Model = thing.Model;
            record.Shapes = thing.Shapes;
            record.Fx = thing.Fx;
            record.BodyModel = actor.BodyModel;
            thing.Model = model;
            thing.Shapes = null;
            thing.ResetFx();
            actor.BodyModel = "";
            record.UndrawnOffhand = actor.Offhand;
        }
        if (motionSet is not null)
        {
            record.MotionSet = thing.MotionSet;
            thing.MotionSet = motionSet;
        }
        // The foley, tag 6, unless NOFOLEY.
        if ((flags & NoFoley) == 0 && Str(tags, Tags.Foley) is { } foley)
        {
            record.Foley = thing.Foley;
            thing.Foley = foley;
        }

        // The hand. WEAPONLESS empties it without `SetWeapon` — the one path that leaves
        // a character holding nothing at all, which the attack order and every mind refuse.
        Weapon? put = null;
        if ((flags & Weaponless) != 0)
        {
            record.Weapon = actor.Weapon;
            actor.Weapon = null;
        }
        else if ((flags & NoWeapon) == 0 && Str(tags, Tags.ActorWeapon) is { } weaponName
                 && templates.Has(weaponName) && Weapon.From(weaponName, templates.Resolve(weaponName)) is { } made)
        {
            record.Weapon = actor.Weapon;
            put = made;
        }
        else if (model is not null) record.UndrawnWeapon = actor.Weapon;

        // A player's portrait head: type 7 only, tags 57 and 58, unless NOHEAD.
        if (thing.Type == ThingType.Player && (flags & NoHead) == 0)
        {
            if (Str(tags, Tags.ActorHeadModel) is { } head)
            {
                record.HeadModel = actor.HeadModel;
                record.PortraitModel = actor.PortraitModel;
                actor.HeadModel = head;
                actor.PortraitModel = "";
            }
            if (Str(tags, Tags.ActorHeadMotion) is { } headMotion)
            {
                record.HeadMotionSet = actor.HeadMotionSet;
                actor.HeadMotionSet = headMotion;
            }
        }

        // The hand before the new body is drawn, so the body is drawn holding what it holds.
        if (put is not null) actor.Weapon = put;                                            // SetWeapon
        if (model is not null || motionSet is not null) thing.Place?.Reshape(thing);
        thing.Locomotion?.Reanimate();
        return true;
    }

    /// <summary>
    /// **The end** — the shape undone. Nothing on is false. Otherwise each piece the record kept is
    /// given back, in the engine's order — the model, the foley, the motion set, then the weapon
    /// through `SetWeapon` (which chooses the locomotion again) with the shape's own weapon thrown
    /// away, and a player's portrait head and its motion set — and the record is freed.
    ///
    /// **A piece the record did not keep stays as the shape left it.** The one shipped way to see it
    /// is a hand that was already empty when the shape went on: the shape's weapon then stays in it.
    /// The kept model object comes back as it was shelved — the armour still in its slot 0, a named
    /// shape still hidden — which Vitomir does by giving back the pieces the record holds for it.
    /// **Ours**: the character's model starts its animation afresh where 1.1's shelved model object
    /// carries on from what it was playing when it was put away, and the locomotion is chosen again
    /// even when no weapon came back.
    /// </summary>
    /// <returns>Whether a shape was on.</returns>
    public static bool End(WorldThing thing)
    {
        if (thing.Actor is not { } actor || actor.ShapeChange is not { } record) return false;
        bool reshape = false;
        if (record.Model is { } model)
        {
            thing.Model = model;
            thing.Shapes = record.Shapes;
            thing.ResetFx(record.Fx);
            actor.BodyModel = record.BodyModel ?? "";
            reshape = true;
        }
        if (record.Foley is { } foley) thing.Foley = foley;
        if (record.MotionSet is { } motionSet)
        {
            thing.MotionSet = motionSet;
            reshape = true;
        }
        if (record.Weapon is { } weapon) actor.Weapon = weapon;                             // SetWeapon; the shape's own destroyed
        if (thing.Type == ThingType.Player)
        {
            if (record.HeadModel is { } head)
            {
                actor.HeadModel = head;
                actor.PortraitModel = record.PortraitModel ?? "";
            }
            if (record.HeadMotionSet is { } headMotion) actor.HeadMotionSet = headMotion;
        }
        actor.ShapeChange = null;
        // Drawn last, with the record gone, so the old body is drawn with all it holds.
        if (reshape) thing.Place?.Reshape(thing);
        // Fix `shapetorch`: the item back in the hand, on the tag of the body it has now, and lit.
        ShapeTorchFix.GiveBack(thing, record.Pocketed);
        thing.Locomotion?.Reanimate();
        return true;
    }

    /// <summary>
    /// The weapon the character's model shows in its hand: the one it holds, except the one that was
    /// in the hand when a shape went on without putting a weapon of its own there — the fresh
    /// model's slot is empty until something else is put in the hand (ours, <see cref="ShapeChange.UndrawnWeapon"/>).
    /// </summary>
    public static Weapon? DrawnWeapon(Actor actor, Weapon? held) =>
        actor.ShapeChange is { UndrawnWeapon: { } kept } && ReferenceEquals(kept, held) ? null : held;

    /// <summary>The off hand's twin of <see cref="DrawnWeapon"/>: a shield is not on a wolf's arm.</summary>
    public static Item? DrawnOffhand(Actor actor, Item? held) =>
        actor.ShapeChange is { UndrawnOffhand: { } kept } && ReferenceEquals(kept, held) ? null : held;

    // ---- saves (the actor writer's tail) ------------------------------------------

    /// <summary>
    /// What a save writes of a character in a changed shape: **its own** model, foley and head, not
    /// the shape's — the writers ask the record first (the model, the foley,
    /// the head, the weapon's GUID) — and the record's name and flags at the end
    /// of the actor (tag 0x29, a word 0 or 1, and then 32 bytes of name and the flags).
    /// The motion set is not written; the template gives it back. Called once the thing's record is
    /// filled (<c>Save.SaveCapture</c>).
    /// </summary>
    public static void Capture(WorldThing thing, Save.ThingSave save)
    {
        if (thing.Actor?.ShapeChange is not { } record || save.Actor is not { } actor) return;
        if (record.Model is { } model) save.Model = model;
        if (record.Foley is { } foley) save.Foley = foley.Length > 0 ? foley : null;
        if (record.HeadModel is { } head) actor.Head = head.Length > 0 ? head : null;
        actor.Shape = new Save.ShapeSave { Template = record.Template, Flags = record.Flags };
    }

    /// <summary>
    /// The record read back — a fresh one holding only the name and the flags, everything
    /// it would keep still zero; the post-load step makes the shape again from it (<see cref="Remake"/>).
    /// </summary>
    public static ShapeChange? Loaded(Save.ShapeSave? save) =>
        save is { Template.Length: > 0 } s ? new ShapeChange { Template = s.Template, Flags = s.Flags } : null;

    /// <summary>
    /// A record there after a load is made again — the change with its own name and flags,
    /// **forced**, over the character as the load put it back (its own model, weapon and head).
    /// A utility item the fix `shapetorch` had out of the hand when the save was taken comes out
    /// of it again here, whatever the fix says now (<see cref="ShapeTorchFix"/>).
    /// </summary>
    public static void Remake(WorldThing thing)
    {
        if (thing.Actor?.ShapeChange is { } record) Shift(thing, record.Template, record.Flags, forced: true);
    }

    /// <summary>
    /// A character carried to another place (<c>Campaign.Carry</c>) is the same actor on a new thing:
    /// 1.1 moves the thing itself, record and all, so the shape goes with it. The model and the foley
    /// are carried already; this carries the motion set and puts it on the new body (ours: the
    /// carry is Vitomir's).
    /// </summary>
    public static void Carried(WorldThing was, WorldThing now, Location into)
    {
        if (now.Actor?.ShapeChange is null || string.Equals(was.MotionSet, now.MotionSet, StringComparison.OrdinalIgnoreCase)) return;
        now.MotionSet = was.MotionSet;
        into.Reshape(now);
        now.Locomotion?.Reanimate();
    }

    static string? Str(IReadOnlyDictionary<uint, Formats.NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) && t.AsString() is { Length: > 0 } s ? s : null;
}
