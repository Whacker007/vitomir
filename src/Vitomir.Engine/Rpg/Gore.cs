// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The gib spray: one loose chunk of somebody, thrown off a bone.
//
// `rpgSFX::SprayGib` is a small
// routine that three callers lean on, and it is the *launch* half of gore — the half that was
// blocked until the physics step existed. What happens to a chunk afterwards is not here: it is an
// ordinary `MOVETYPE 3` thing and `World.Physics` flies it, bounces it and puts it to sleep.
//
// The three ways in, all of them in the deaths:
//
//   * **an ordinary hit** — one chunk, on a *roll*;
//   * **the killing blow** — one chunk over 5 damage and another
//     over 19, so a big enough blow throws two;
//   * **the gib kill** — sixteen chunks, and those pass **no** bone tag.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// Throwing a chunk of somebody into the world, and the one global switch that refuses to.
/// </summary>
public static class Gore
{
    /// <summary>
    /// The original's own "no gore" setting — bit 2 of its options, tested in five places and by this
    /// function first of all. It is **not** a <see cref="Fixes"/> switch: a Fix turns off something
    /// 1.1 gets wrong, and this is something 1.1 offers.
    ///
    /// It comes from a **registry** value named `NoBlood`, read once while the command line is
    /// parsed (through the registry). A non-zero value sets the bit and
    /// `or ah, 1` over the flags word — bits `0x2` and `0x100`. Bit `0x2` is the one this reads.
    /// Bit `0x100` has **one** reader, found with the Options screen: the Gameplay
    /// screen tests its bit and **hides its blood box** when it is set, so a build
    /// locked by the registry cannot be switched back (`Ui.GameOptions.BloodLocked`). This page
    /// said it was never tested; the byte-wide sweep that concluded so missed the `ah` test.
    /// The Gameplay screen's box is the player's way to set bit `0x2`.
    ///
    /// Default false, because the setting's own default is 0.
    /// </summary>
    public static bool NoBlood { get; set; }

    /// <summary>
    /// `MOTIONTAG_HEAD` — the bone the two damage sprays come off, declared in
    /// `Codex\System\Codex.java` as 19 and passed as the literal `0x13`. So the chunks an ordinary
    /// blow throws come off the victim's **head**, wherever the blow landed.
    /// </summary>
    public const int HeadTag = 19;

    /// <summary>
    /// `MOTIONTAG_INVALID`. The gib kill passes this, and no motion set can declare it — the parser
    /// drops a `tag` line whose name is not in the 44-name table, and index 0 is the empty name. So
    /// the tag lookup always fails for the gib kill and every one of its sixteen chunks comes off a
    /// **random** bone, which is what scatters a body rather than piling it at one joint.
    /// </summary>
    public const int NoTag = 0;

    /// <summary>How fast the random part of the launch is: a unit vector times 20.</summary>
    public const float LaunchSpeed = 20f;

    /// <summary>
    /// Throws one chunk. Answers the thing it made, or null when something refused — the no-gore
    /// setting, a template that is not in the table, or a spawn point in no sector at all.
    ///
    /// The order is the engine's:
    /// <list type="number">
    /// <item>the no-gore setting, before anything else;</item>
    /// <item>the template, by name. A missing one is **not** an error: the gib kill asks for eight
    /// pieces of every class and most classes do not have eight, so this returning null quietly is
    /// load-bearing;</item>
    /// <item>where it starts — <see cref="At"/>;</item>
    /// <item>the maker, when the chunk is a projectile. Every shipped gib template is
    /// `TYPE PROJECTILE`, so this is not the rare case it looks like;</item>
    /// <item>and the launch, when the chunk is a physics thing.</item>
    /// </list>
    /// </summary>
    public static WorldThing? Spray(WorldThing thing, string template, int boneTag, Rng rng)
    {
        if (NoBlood) return null;
        if (thing.Place is not { } place) return null;
        if (place.Templates is null || !place.Templates.Has(template)) return null;

        var (position, orientation) = At(thing, boneTag, rng);

        // The sector is looked up from the spawn point, and a point the collision system
        // places in no sector at all refuses the whole chunk.
        if (place.Solid is { } solid && solid.SectorAt(position, (int)thing.Sector) < 0) return null;

        if (place.Place(template, position, orientation, thing.Sector) is not { } chunk) return null;

        // Only type 4. An explosion gets its maker elsewhere instead and this is not that
        // path, so the test really is the one type.
        if (chunk.Type == ThingType.Projectile) chunk.SpawnedBy = thing.Guid;

        // And only a physics thing is launched. Note the `+=`. By the time this runs the
        // chunk is already in the world, so its template velocity has already been rotated out of
        // the bone's frame (`Location.LaunchVelocity`) — which is what that velocity is *for*: a
        // gib's authored `-80, 0, 250` means up and aside **from the joint it came off**, not up and
        // aside in the world. What this adds is a jitter on top of that, in world space.
        if (chunk.MoveType == WorldThing.MovePhysics) chunk.Velocity += Launch(rng);
        return chunk;
    }

    /// <summary>
    /// The random part of a launch: a direction out of <see cref="Physics.Direction"/>,
    /// times <see cref="LaunchSpeed"/>, with **the Z taken as an absolute value** (a bare
    /// abs, nothing more). So the jitter can push a chunk any way it likes across the floor and only ever
    /// upwards through it — gore never gets a downward kick, only a smaller upward one.
    ///
    /// It is a jitter and not the launch itself: 20 units a second against the 180 to 400 every
    /// shipped gib template already carries in its own `PHYSICS_VELOCITY` — and that one is in the
    /// **bone's** frame, so "upward" there means away from the joint.
    /// </summary>
    public static Vector3 Launch(Rng rng)
    {
        var d = Physics.Direction(rng);
        return new Vector3(d.X, d.Y, MathF.Abs(d.Z)) * Rules.GameRules.Current.Stake.GoreLaunchSpeed;
    }

    /// <summary>
    /// Where a chunk starts, and which way up: the bone's own transform, in world space.
    ///
    /// The engine copies a 3 × 4 matrix wholesale and picks which one in three steps:
    /// <list type="bullet">
    /// <item>**no skeleton** — the thing's own transform, and no bone is involved at all;</item>
    /// <item>**a tag that resolves** — the bone it names (the motion set's tag table is walked,
    /// five dwords a record, and answers the second one);</item>
    /// <item>**otherwise a random bone** — including for any tag that does not resolve, which is
    /// every call the gib kill makes.</item>
    /// </list>
    ///
    /// <b>Ours, not the engine's:</b> the engine keeps a matrix on a thing and Vitomir keeps a
    /// position and Euler degrees, so the bone's rotation is decomposed
    /// (<see cref="WorldThing.OrientationOf"/>) instead of copied. The matrix that comes back out is
    /// the one that went in; what is lost is only the distinction between the Euler triples that
    /// produce it.
    /// </summary>
    public static (Vector3 Position, Vector3 Orientation) At(WorldThing thing, int boneTag, Rng rng)
    {
        if (thing.Animation is not { BoneCount: > 0 } animation) return (thing.Position, thing.Orientation);

        int bone = boneTag < 0 ? -1 : animation.TagBone(boneTag);
        if (bone < 0) bone = RandomBone(animation.BoneCount, rng);

        var m = animation.BoneMatrix(bone) * thing.World;
        return (m.Translation, WorldThing.OrientationOf(m));
    }

    /// <summary>
    /// A bone at random: `(int)(rand01 × count)`, clamped below at 0 and **above at
    /// count** — not at count − 1.
    ///
    /// That upper clamp is the engine's, and it is off by one: `rand01` reaches 1.0 exactly when
    /// `rand()` returns its maximum, so once in 32,768 draws the index is `count` and the engine
    /// reads the matrix one past the end of the posed-bone array. Vitomir cannot reproduce reading
    /// whatever happens to be there, so it takes the last bone instead, and the register records the
    /// difference.
    /// </summary>
    public static int RandomBone(int count, Rng rng)
    {
        int index = (int)(rng.Unit() * count);
        if (index < 0) index = 0;
        else if (index > count) index = count;
        return index >= count ? count - 1 : index;
    }
}
