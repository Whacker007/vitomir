// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A melee blow that takes a head off: the roll, the head, the headless body and the bleed-out.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Decapitation** — the roll in the hit reaction (after the flinch) and the beheading
/// itself.
///
/// It happens only on a blow the victim **survives** (the hit reaction is not reached by a killing
/// blow), and what then kills it is the `ef_beheaded` it is given — at once, or over a few seconds.
/// </summary>
public static class Beheading
{
    /// <summary>The roll's scale and what Strength is worth in it.</summary>
    public const float Hundred = 100f, StrengthShare = 0.1f;

    /// <summary>The motion-set tags the head and the blood come off: 19 `Head` (0x13), 18 `Neck` (0x12).</summary>
    public const int HeadTag = 0x13, NeckTag = 0x12;

    /// <summary>The effect (two copies of the same string in the original) and how long it runs (100 000 ms).</summary>
    public const string Effect = "ef_beheaded";
    /// <inheritdoc cref="Effect"/>
    public const int EffectMs = 100_000;

    /// <summary>
    /// The even chance that picks the bleed-out (0.5) and its two values as fractions of
    /// the victim's maximum health: ×10, which is death at the first tick, or ×0.2 a tick,
    /// five ticks of it.
    /// </summary>
    public const float Even = 0.5f, Quick = 10f, Slow = 0.2f;

    /// <summary>The spurt left on the neck, and how it rides there (0x8001).</summary>
    public const string Blood = "blood0";
    /// <inheritdoc cref="Blood"/>
    public const uint BloodAttach = 0x8001;

    /// <summary>
    /// The roll: the victim `THING_AF_CANBEHEAD`, the attacker a character
    /// (actor or player), a **melee** blow (hit kind 1), the attacker's current attack
    /// <see cref="WeaponFlag.CanBehead"/> (the SDK's `BLADED`) — and only then a <c>rand()</c>:
    /// <c>rand01 × 100 &lt; Strength × 0.1 + damage</c>. Strength is worth a tenth of a point.
    /// </summary>
    public static bool Roll(WorldThing victim, WorldThing? attacker, HitKind kind, float damage, Rng rng, int nowMs)
    {
        if (victim.Actor is not { } actor || !actor.Has(Actor.Flag.CanBehead)) return false;
        if (attacker is not { Type: ThingType.Actor or ThingType.Player, Actor: { } by }) return false;
        if (kind != HitKind.Melee) return false;
        if (by.Weapon is not { } weapon || !weapon.Current.Is(WeaponFlag.CanBehead)) return false;
        var chance = Rules.GameRules.Current.Stake.BeheadChance;
        if (!(rng.Unit() * chance[0] < by.Stat[(int)ActorStat.Strength] * chance[1] + damage)) return false;
        return Behead(victim, attacker, rng, nowMs);
    }

    /// <summary>
    /// **The beheading.** For a victim that can lose its head and has not:
    /// <list type="number">
    /// <item>with the no-gore setting (<see cref="Gore.NoBlood"/>), it is simply killed
    /// (melee) and nothing below happens;</item>
    /// <item>the head: the template <c>&lt;victim's template&gt;_head</c> (`%s_head`) — `Skel_Bow_head`,
    /// `zombu_head`; **a victim without one keeps its head** and nothing else happens either (7 of
    /// the 71 beheadable templates: `testEnemy`, `darkTeutonicKnight_conv` and five Setites). It is
    /// made at the victim's `Head` bone (tag 19), or at the victim itself with no skeleton or no
    /// such tag, and a physics head gets the same jitter as a gib (×20, z made
    /// positive);</item>
    /// <item>the body becomes `&lt;model&gt;_nohead` (<see cref="BodyModels"/>);</item>
    /// <item>an even chance: <c>rand01 ≥ 0.5</c> gives `ef_beheaded` at ten times the victim's
    /// maximum health — dead at the first tick — and otherwise at a fifth of it, and a victim with a
    /// mind is **frightened of the attacker** (forced) and made unaffectable by anything
    /// mind-altering (mind flag 1). Either way for 100 s, credited to the attacker;</item>
    /// <item>`THING_AF2_ISBEHEADED`;</item>
    /// <item>and unless it is `NOBLEED`, a `blood0` made at its `Neck` bone and attached there.</item>
    /// </list>
    /// </summary>
    /// <para>
    /// **On a network host** the head and the neck's blood are each machine's own (<see cref="Net.MadeHere"/>), and
    /// once the head is made and the body headless every player's machine is told (`(victim,
    /// 0x19, attacker)`), which strikes its own off — <paramref name="told"/>: the same, from its own rolls and
    /// under its own no-gore setting, except that there the effect is refused (at
    /// its head: an effect not from the network on a player's machine) and the kill a no-gore machine would give
    /// does nothing (the death unforced there); and it has no minds to frighten.
    /// </para>
    /// </summary>
    /// <returns>Whether the head came off.</returns>
    public static bool Behead(WorldThing victim, WorldThing? attacker, Rng rng, int nowMs, bool told = false)
    {
        if (victim.Actor is not { } actor || !actor.Has(Actor.Flag.CanBehead) || actor.Has2(Actor.Flag2.IsBeheaded))
            return false;
        if (Gore.NoBlood)
        {
            if (!told) Damage.Kill(victim, attacker, HitKind.Melee, DamageType.Normal, nowMs, 0f, victim.Place?.Motions, rng);
            return false;
        }
        if (victim.Place is not { } place || place.Templates is not { } templates) return false;
        string head = victim.Template + "_head";
        if (!templates.Has(head)) return false;

        var (position, orientation, _) = At(victim, HeadTag);
        if (place.Solid is { } solid && solid.SectorAt(position, (int)victim.Sector) < 0) return false;
        WorldThing? severed;
        using (Net.MadeHere.Everywhere(victim)) severed = place.Place(head, position, orientation, victim.Sector);
        if (severed is null) return false;
        if (severed.MoveType == WorldThing.MovePhysics) severed.Velocity += Gore.Launch(rng);
        // The head is a `TYPE PROJECTILE` thing made inside its own body, and the beheading gives it **no
        // maker** (a gib gets one) — nor does Vitomir. The flight is the engine's sweep
        // (World.WorldSweep), whose cylinder test does not report a body the line starts on the axis of
        // and moves out of, so the head does not strike its own neck (BeheadingTests; earlier, Vitomir
        // named the body as the maker, from when its flight tested rays against standing cylinders).

        BodyModels.Set(victim, BodyModels.NoHead);
        Net.MadeHere.Beheaded(victim, attacker);                                      // on a host

        var rules = Rules.GameRules.Current.Stake;
        float value;
        if (rng.Unit() >= rules.BeheadBleedChoice) value = actor.MaxHealth * rules.BeheadBleedValues[0];
        else
        {
            if (!told && victim.Mind is { } mind && attacker is { Type: ThingType.Actor or ThingType.Player }
                && place.Minds is { } minds)
            {
                minds.Frighten(mind, attacker, force: true);                          // afraid of the attacker, forced
                mind.Flags |= Mind.FlagUnaffected;
            }
            value = actor.MaxHealth * rules.BeheadBleedValues[1];
        }
        // Refused on a player's machine (not from the network); the host's reaches it as EffectOn.
        if (!told) EffectRuntime.Apply(victim, Effect, -1, durationMs: rules.BeheadEffectMs, creator: attacker, value: value);
        actor.Flags2 |= Actor.Flag2.IsBeheaded;

        if (actor.Has(Actor.Flag.NoBleed) || !templates.Has(Blood)) return true;
        var (at, turn, bone) = At(victim, NeckTag);
        if (place.Solid is { } s2 && s2.SectorAt(at, (int)victim.Sector) < 0) return true;
        WorldThing? blood;
        using (Net.MadeHere.Everywhere(victim)) blood = place.Place(Blood, at, turn, victim.Sector);
        if (blood is not null)
            Attachments.Attach(victim, blood, bone, Vector3.Zero, Matrix4x4.Identity, BloodAttach);
        return true;
    }

    /// <summary>
    /// Where a tag's bone is in the world, or the thing itself when it has no skeleton or the set
    /// does not name the tag — unlike a gib, never a random bone. Answers the
    /// bone too, −1 for none.
    /// </summary>
    static (Vector3 Position, Vector3 Orientation, int Bone) At(WorldThing thing, int tag)
    {
        if (thing.Animation is not { BoneCount: > 0 } animation) return (thing.Position, thing.Orientation, -1);
        int bone = animation.TagBone(tag);
        if (bone < 0) return (thing.Position, thing.Orientation, -1);
        var m = animation.BoneMatrix(bone) * thing.World;
        return (m.Translation, WorldThing.OrientationOf(m), bone);
    }
}
