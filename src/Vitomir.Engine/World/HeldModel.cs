// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a character has in its hands, and how the engine names the model for it.
namespace Vitomir.Engine.World;

/// <summary>
/// The name of the model a character carries in one of its hands.
///
/// **A held weapon is not an attachment.** The obvious guess — and the one this project carried in
/// its backlog for a long time — is that the engine hangs a sword off a `Weapon` attachment point
/// on the skeleton, the way the NMS's <c>tag</c> lines and the attachment
/// subsystem would let it. It does not. `SetWeapon` builds a **file
/// name** and hands it to the model's sub-model slot 1:
/// the held model is a second `.nod` loaded onto the character's own model, carrying **the same
/// skeleton**, with its geometry weighted to the hand bone. Nothing is parented to anything; the
/// weapon is skinned by the character's bones like the character's own coat is.
///
/// That is why every weapon ships five times. `scythe_0.nod` has 40 bones, `scythe_1.nod` 35,
/// `scythe_3.nod` 48 — one copy per `SKELETONID`, each rigged to that body's skeleton, and all 86
/// vertices of every copy weighted 1.0 to that skeleton's hand bone (25, 26 and 34 respectively).
/// The five files hold the same scythe in five different hands.
///
/// Two recipes, and a weapon flag chooses between them (bit 14 of the first attack
/// record's flags — see <see cref="Rpg.WeaponFlag.SpecificModels"/>):
/// <list type="bullet">
/// <item><b>By skeleton</b>, the ordinary one: <c>WEAPON_MODEL</c> then <c>_</c> then
/// <c>SKELETONID</c>. `scythe` on a Cappadocian is `scythe_0`; on the female Cappadocian,
/// `scythe_3`.</item>
/// <item><b>By character</b>, when the weapon carries `SPECIFIC MODELS`: the
/// character's own model name, then the armour it is wearing if any, then <c>WEAPON_MODEL</c> —
/// `christof_claws`, `Christof_plate_claws`, `christof_Leather_claws`. All six templates that use
/// it are claws, which have to grow out of a particular pair of hands.</item>
/// </list>
///
/// The off-hand (slot 2) is named from `ARMOR_MODEL` the first way — a shield is
/// `normanshield_0` — and body armour replaces the character's *own* model
/// with `<model>_<ARMOR_MODEL>` in slot 0, `christof_plate` (<see cref="BodyModels"/>).
///
/// **Of the 448 actor templates that name an `ACTOR_WEAPON`, 159 resolve to a held model and 158
/// of those files ship** (the one miss is a multiplayer milkmaid's `bucket_0`). The other 289 hold
/// a weapon with no `WEAPON_MODEL` at all — `Punch`, `Bite`, `enemy_Claw` and the rest — and the
/// engine draws nothing for those, because a fist is already part of the body.
/// </summary>
public static class HeldModel
{
    /// <summary>
    /// The engine composes these into a 32-byte buffer (0x20 is passed to every write), so a
    /// longer name is cut off rather than overflowing. No shipped name comes close.
    /// </summary>
    public const int NameLength = 32;

    /// <summary>
    /// Which sub-model slot each thing goes in (the sub-model set's third argument). A model instance has
    /// <see cref="Slots"/> of them and the engine refuses an index outside that.
    /// </summary>
    public const int WeaponSlot = 1, OffhandSlot = 2;

    /// <summary>How many sub-model slots a model instance has (`slot &lt; 4`).</summary>
    public const int Slots = 4;

    /// <summary>
    /// The model a character holds, or an empty string for a character that holds nothing you can
    /// see. <paramref name="actorModel"/> is the character's own model name and
    /// <paramref name="armourModel"/> its `ARMOR_MODEL`, both of which only matter to the
    /// `SPECIFIC MODELS` recipe.
    /// </summary>
    public static string Weapon(Rpg.Weapon? weapon, int skeletonId, string actorModel, string armourModel = "")
    {
        // No weapon, or a weapon whose WEAPON_MODEL string is empty, clears the slot.
        if (weapon is null || weapon.Model.Length == 0) return "";
        return weapon.Attacks[0].Is(Rpg.WeaponFlag.SpecificModels)
            ? Composed(actorModel, armourModel, weapon.Model)
            : Numbered(weapon.Model, skeletonId);
    }

    /// <summary>
    /// The model on the off arm — a shield, in practice. `SetOffhand` asserts the item
    /// is armour and builds `ARMOR_MODEL_SKELETONID` the same way a weapon builds its name, with no
    /// per-character alternative: `normanshield_0`, `sunshield_3`, `woodbuckler_0`. An item with an
    /// empty `ARMOR_MODEL` clears the slot.
    /// </summary>
    public static string Offhand(Rpg.Item? item, int skeletonId) =>
        item is null || item.ArmorModel.Length == 0 ? "" : Numbered(item.ArmorModel, skeletonId);

    /// <summary>`"%s_%d"` — the model name and the body it is rigged for.</summary>
    public static string Numbered(string name, int skeletonId) =>
        name.Length == 0 ? "" : Fit($"{Stem(name)}_{skeletonId}");

    /// <summary>
    /// The per-character name: the character's own model, then `_` and the armour it is
    /// wearing if it is wearing any, then `_` and the weapon's. Each piece is appended into the
    /// same 32-byte buffer, so the whole thing is cut at 32.
    /// </summary>
    public static string Composed(string actorModel, string armourModel, string name)
    {
        string s = Fit(Stem(actorModel));
        if (armourModel.Length > 0) s = Fit(s + "_" + Stem(armourModel));
        if (name.Length > 0) s = Fit(s + "_" + Stem(name));
        return s;
    }

    /// <summary>A name without its extension: templates write `christof.nod`, models are `christof`.</summary>
    public static string Stem(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    static string Fit(string s) => s.Length <= NameLength - 1 ? s : s[..(NameLength - 1)];
}
