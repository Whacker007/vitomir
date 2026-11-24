// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Every piece of state a saved thing carries has been decided on**. Each settable
/// member of the classes a save writes — a thing, a character, an item, an effect, a discipline, a
/// weapon and its attacks — is on one of two lists here: **kept** by the save (`Save.SaveCapture` /
/// `Save.SaveApply`), or **not kept, and why** — made again on load, 1.1 does not keep it either, or it
/// only lives for a frame. A member added later and on neither list fails this test, so nobody adds
/// state a play-through could lose without deciding what a save does with it.
/// </summary>
public class SaveCoverageTests
{
    static IEnumerable<string> Settable(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
         .Where(p => p.SetMethod is { } set && (set.IsPublic || set.IsAssembly)
                     && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
         .Select(p => p.Name)
         .Concat(t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly).Select(f => f.Name));

    static void Decided(Type t, string[] kept, (string Name, string Why)[] notKept)
    {
        var decided = kept.Concat(notKept.Select(n => n.Name)).ToHashSet(StringComparer.Ordinal);
        var members = Settable(t).ToHashSet(StringComparer.Ordinal);
        var undecided = members.Where(m => !decided.Contains(m)).Order().ToList();
        Assert.True(undecided.Count == 0,
            $"{t.Name} has state nobody has decided a save for: {string.Join(", ", undecided)} — keep it in Save.SaveCapture/SaveApply, or say here why not");
        var stale = decided.Where(d => !members.Contains(d)).Order().ToList();
        Assert.True(stale.Count == 0, $"{t.Name} no longer has {string.Join(", ", stale)}; take them off the lists");
    }

    [Fact]
    public void Every_piece_of_a_thing_s_state_is_kept_or_left_out_on_purpose() => Decided(typeof(WorldThing),
        ["Guid", "Sector", "Flags", "DescriptionId", "Position", "Orientation", "Keyframes", "RenderFlags", "MoveType",
         "PhysicsFlags", "Velocity", "RotVelocity", "Foley", "Model", "Collide", "ContainerHealth", "Owner", "PropOwner",
         "Holder", "Item", "SpawnedBy", "RegionFlags", "SaveId", "Actor", "ModelAlpha", "ModelScale", "ExpireAt",
         "TrackerAngles", "Frame", "State", "AttachedTo", "MadeByEffect"],
        [("Index", "its placement, which is its save id"),
         ("Place", "the place it is loaded into"),
         ("ModelFlags", "the template's; nothing changes it at run time"),
         ("Gravity", "the template's physics; nothing changes it at run time"),
         ("Restitution", "the template's physics; nothing changes it at run time"),
         ("AppliedDeltaV", "one frame's physics"),
         ("LastImpactSpeed", "one frame's physics"),
         ("Audible", "one frame's noise"),
         ("MouthBusyUntil", "a line being spoken; a save is not made in a conversation"),
         ("MouthChannel", "a line being spoken"),
         ("SoundType", "the template's"),
         ("BoxMin", "the model's box, taken at the finalize"),
         ("BoxMax", "the model's box, taken at the finalize"),
         ("MotionSet", "the template's, or a shape's, which PostLoad makes again"),
         ("ProjectileFlags", "a projectile is never saved, in 1.1 too"),
         ("ProjectileDamage", "a projectile is never saved"),
         ("ProjectileDamageType", "a projectile is never saved"),
         ("ProjectileRange", "a projectile is never saved"),
         ("SeeThroughLevel", "the camera's fade, worked out again each frame from where the camera is (ours)"),
         ("InRegions", "1.1's record has none; the next frame's region test finds them"),
         ("Removed", "a removed thing is not saved"),
         ("Animation", "only a held pose is kept (Poses), as 1.1's model block"),
         ("Waiting", "an action waiting for an animation"),
         ("Shapes", "hidden meshes: 1.1's model block keeps none"),
         ("Mind", "made again on load, in 1.1 too"),
         ("Walker", "a walk under way is not saved, in 1.1 either"),
         ("Unsurfaced", "a walk record, found again on the next step"),
         ("MovingSound", "a sound channel"),
         ("Walking", "last frame's footfall state"),
         ("ShownLift", "ours, presentation only: a walker's eased height over a cell level's steps, paid back within a step (CellStepEase)")]);

    [Fact]
    public void Every_piece_of_a_character_s_state_is_kept_or_left_out_on_purpose() => Decided(typeof(Actor),
        ["Name", "Flags", "Flags2", "Type", "Team", "Perception", "Stealth", "OriginalTeam", "Clan", "Aura", "AuraName",
         "Health", "MaxHealth", "Experience", "SpendableExperience", "Cash", "GibClass", "SkeletonId", "MoveSpeedScale",
         "AttackSpeedScale", "Stance", "WalkSpeed", "RunSpeed", "Xp", "Weapon", "Offhand", "Utility", "Armor", "Held",
         "PrevWeapon", "PrevOffhand", "ShapeChange", "HeadModel", "WeaponHidden", "OffhandHidden", "NoItemArmor",
         "IsPlayerThing", "DisciplineTab", "DisciplineTabOffset", "FadeAt", "FadingFrom", "Dusted"],
        [("DefaultWeapon", "the template's fallback weapon"),
         ("BodyModel", "set again from the armour on load; a beheaded body's is not kept, in 1.1 either"),
         ("PortraitModel", "set again from the armour on load"),
         ("TalkingHands", "a conversation's"),
         ("FlinchedAt", "a moment's reaction"),
         ("PendingDiscipline", "an order: 1.1 keeps none"),
         ("Action", "an order: 1.1 keeps none"),
         ("Repeating", "an order"),
         ("Pointing", "an order"),
         ("Target", "an order"),
         ("ActionPoint", "an order"),
         ("OrderMods", "an order"),
         ("BusyUntil", "an order"),
         ("ActionRetryAt", "an order"),
         ("ActionStartedAt", "an order"),
         ("ActionSpeed", "an order"),
         ("HeadMotionSet", "the template's, or a shape's, made again"),
         ("HeadAction", "the portrait head's animation"),
         ("ClanRecord", "looked up again from the clan")]);

    [Fact]
    public void Every_piece_of_an_item_s_state_is_kept_or_left_out_on_purpose() => Decided(typeof(Item),
        ["ItemId", "Amount", "Location", "Cell", "Flags", "BaseCost", "SellFlags", "Effects", "Weapon", "Thing"],
        [("Type", "the template's"),
         ("Destroyed", "a destroyed item is not saved"),
         ("ArmorModel", "the template's")]);

    [Fact]
    public void Every_piece_of_an_effect_s_state_is_kept_or_left_out_on_purpose() => Decided(typeof(Effect),
        ["Until", "Source", "Flags", "Script", "Fade", "NextTick"],
        [("Spent", "a spent effect is not saved"),
         ("Folded", "set again by the post-load recompute")]);

    [Fact]
    public void Every_piece_of_a_discipline_s_state_is_kept_or_left_out_on_purpose() => Decided(typeof(Discipline),
        ["Owner", "Level", "QuickSlot", "Flags", "CastCount", "RecastUntil", "Active", "Script"],
        [("Snapshotted", "the advancement screen's, open only outside play"),
         ("ScrollGuid", "a scroll's one-cast discipline, gone when the cast is")]);

    [Fact]
    public void Every_piece_of_a_weapon_s_state_is_kept_or_left_out_on_purpose()
    {
        Decided(typeof(Weapon), ["CurrentAttack"],
            [("Carry", "the template's"), ("Flags1", "the template's"), ("DefenseType", "the template's"), ("Model", "the template's"),
             ("Foley", "the template's"), ("FirePosition", "the template's"), ("BladeStart", "the template's"), ("BladeEnd", "the template's"),
             ("Trail", "the template's"), ("TrailGuid", "the trail is SAVE NEVER and made again when a blade goes into the hand"),
             ("ThrowType", "the template's"), ("ThrowDistance", "the template's"), ("ThrowChance", "the template's"),
             ("AttackCount", "the template's")]);
        Decided(typeof(WeaponAttack), ["Ammo"],
            [("Flags", "the template's"), ("Damage", "the template's"), ("DamageType", "the template's"), ("StrikeTime", "the template's"),
             ("Accuracy", "the template's"), ("ReloadTime", "the template's"), ("MaxAmmo", "the template's"), ("AmmoType", "the template's"),
             ("AmmoUse", "the template's"), ("MuzzleFlash", "the template's"), ("MaxDistance", "the template's"),
             ("MinDistance", "the template's"), ("Projectile", "the template's"), ("MeleeType", "the template's")]);
    }
}
