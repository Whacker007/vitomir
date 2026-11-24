// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a wound shows by its kind of damage: the tail of the hit reaction.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The wound sparks** — the last part of the hit reaction (from the damage on a
/// blow that does not kill, after the noises and the flinch, the beheading roll and the loose chunk):
/// a switch on the kind of damage that hangs effect things on the victim's skeleton. Each is made at
/// the victim and attached to a bone with no offset and the flags <c>0x8001</c>:
/// <list type="bullet">
/// <item><b>Electric</b> (3): on every bone but the root, with a chance of <b>one in four</b> each
/// (<c>rand × 1/32767 &lt; 0.25</c>), a <c>syssfx_electricdamage</c> and a
/// <c>syssfx_electricsparks</c> — the sparks crawling over a man struck by lightning. They live as
/// their templates say.</item>
/// <item><b>Fire</b> and <b>sun</b> (4, 5): on every bone but the root, one in four, a
/// <c>syssfx_firedamage</c>; then a <c>syssfx_firedynalight</c> on the root bone — each thrown away
/// <b>80 ms a point of damage</b> later (<c>now − trunc(amount × −80)</c>).</item>
/// <item><b>Cold</b> (7): <c>syssfx_colddamage</c> the same way, one in four, 80 ms a point.</item>
/// <item><b>Faith, poison, disease</b> (6, 8, 9): nothing.</item>
/// <item><b>Anything else</b> — normal, lethal, aggravated: unless blood is off (the gore
/// setting, bit 2) or the victim is <c>NOBLEED</c> (flag <c>0x10</c>), a
/// <c>blood0</c> at the victim, thrown away 300 ms later, turned to face the attacker across the
/// floor (a look at the attacker's x and y at its own height) and hung on a bone chosen by
/// what the attacker is: a character or an explosion, the root; <b>a projectile, the bone
/// nearest it</b>; anything else, none — it stays where it was made.</item>
/// </list>
/// The skeleton is the render model's (its bone count at <c>+6</c>): a thing with
/// no model shows nothing (the blood is still made, and stays unhung).
/// </summary>
public static class WoundEffects
{
    /// <summary>The chance a bone catches one.</summary>
    public const float BoneChance = 0.25f;
    /// <summary>How long a fire or cold effect lasts, a point of damage (−80, subtracted).</summary>
    public const float MsPerPoint = 80f;
    /// <summary>How long a wound's blood lasts (<c>now + 300</c>).</summary>
    public const int BloodMs = 300;
    /// <summary>The attach flags every one is hung with (<c>0x8001</c>).</summary>
    public const uint AttachFlags = 0x8001;

    public const string ElectricDamage = "syssfx_electricdamage", ElectricSparks = "syssfx_electricsparks",
                        FireDamage = "syssfx_firedamage", FireLight = "syssfx_firedynalight",
                        ColdDamage = "syssfx_colddamage", Blood = "blood0";

    /// <summary>The killing blow's copy: a bone's chance and how long each lives (<c>now + 8000</c>).</summary>
    public const float DeathBoneChance = 0.33f;
    /// <inheritdoc cref="DeathBoneChance"/>
    public const int DeathMs = 8000;

    /// <summary>
    /// The hit reaction's damage switch for a blow of <paramref name="amount"/> of <paramref name="type"/> on
    /// <paramref name="victim"/> by <paramref name="attacker"/> (the weapon's owner, or the
    /// projectile). Answers the things it made.
    ///
    /// <paramref name="death"/> is the **killing blow's** copy of the switch, at the tail of the death
    /// effects (read in a comparison with 1.1) for a body left whole — not dust, not
    /// gibbed, not killed in mid-air. The same templates and the same blood, three differences: a bone
    /// catches one on **0.33** (<see cref="DeathBoneChance"/>), every effect lives **8 s**
    /// (<see cref="DeathMs"/>, the sparks included, which a hit leaves to their templates), and the
    /// frost's loop starts at the **root** bone (the counter's first value is tested too).
    /// </summary>
    public static List<WorldThing> Show(WorldThing victim, WorldThing? attacker, DamageType type, float amount, Rng rng, int nowMs,
                                        bool death = false)
    {
        var made = new List<WorldThing>();
        if (!victim.InWorld || victim.Place is not { } place) return made;
        int bones = victim.Animation?.BoneCount ?? 0;
        var show = Rules.GameRules.Current.Stake.WoundShowMsPerPoint;
        int expiry = death ? nowMs + (int)show[4]
                           : nowMs + (int)(amount * show[0]);             // now − trunc(amount × −80)
        float chance = death ? show[2] : show[1];
        int sparks = death ? expiry : 0;
        switch (type)
        {
            case DamageType.Electric:
                if (bones == 0 || !Has(place, ElectricDamage) || !Has(place, ElectricSparks)) break;
                for (int bone = 1; bone < bones; bone++)
                {
                    if (!(rng.Unit() < chance)) continue;
                    Hang(place, victim, ElectricDamage, bone, sparks, made);
                    Hang(place, victim, ElectricSparks, bone, sparks, made);
                }
                break;
            case DamageType.Fire:
            case DamageType.Sun:
                if (bones > 0 && Has(place, FireDamage))
                    for (int bone = 1; bone < bones; bone++)
                        if (rng.Unit() < chance) Hang(place, victim, FireDamage, bone, expiry, made);
                if (bones > 0 && Has(place, FireLight)) Hang(place, victim, FireLight, 0, expiry, made);
                break;
            case DamageType.Cold:
                if (bones == 0 || !Has(place, ColdDamage)) break;
                for (int bone = death ? 0 : 1; bone < bones; bone++)
                    if (rng.Unit() < chance) Hang(place, victim, ColdDamage, bone, expiry, made);
                break;
            case DamageType.Faith:
            case DamageType.Poison:
            case DamageType.Disease:
                break;
            default:
                Bleed(place, victim, attacker, nowMs, made);
                break;
        }
        return made;
    }

    static bool Has(Location place, string template) => place.Templates?.Has(template) == true;

    static void Hang(Location place, WorldThing victim, string template, int bone, int expiry, List<WorldThing> made)
    {
        if (place.Spawn(template, victim) is not { } fx) return;
        if (expiry != 0) fx.ExpireAt = expiry;
        Attachments.Attach(victim, fx, bone, Vector3.Zero, Matrix4x4.Identity, AttachFlags);
        made.Add(fx);
    }

    /// <summary>A wound's blood.</summary>
    static void Bleed(Location place, WorldThing victim, WorldThing? attacker, int nowMs, List<WorldThing> made)
    {
        if (Gore.NoBlood || victim.Actor?.Has(Actor.Flag.NoBleed) == true || !Has(place, Blood)) return;
        if (place.Spawn(Blood, victim) is not { } blood) return;
        blood.ExpireAt = nowMs + (int)Rules.GameRules.Current.Stake.WoundShowMsPerPoint[3];
        made.Add(blood);
        if (attacker is not null)
        {
            // Turned to look at the attacker's x and y from its own height.
            var to = new Vector3(attacker.Position.X, attacker.Position.Y, blood.Position.Z) - blood.Position;
            if (to.LengthSquared() > 0f)
                blood.Orientation = blood.Orientation with { Z = MathF.Atan2(to.Y, to.X) * 180f / MathF.PI };
        }
        if (BoneFor(victim, attacker) is { } bone)
            Attachments.Attach(victim, blood, bone, Vector3.Zero, Matrix4x4.Identity, AttachFlags);
    }

    /// <summary>
    /// The bone a wound's blood hangs on, by what struck — a character or an explosion,
    /// the root; a projectile, the bone nearest where it is; anything else (or no model), none.
    /// </summary>
    public static int? BoneFor(WorldThing victim, WorldThing? attacker)
    {
        if (attacker is null || victim.Animation is not { BoneCount: > 0 } anim) return null;
        switch (attacker.Type)
        {
            case ThingType.Actor or ThingType.Player or ThingType.Explosion:
                return 0;
            case ThingType.Projectile:
            {
                int best = -1;
                float nearest = 99999f;
                for (int b = 0; b < anim.BoneCount; b++)
                {
                    float d = Vector3.Distance((anim.BoneMatrix(b) * victim.World).Translation, attacker.Position);
                    if (d < nearest) { nearest = d; best = b; }
                }
                return best >= 0 ? best : null;
            }
            default:
                return null;
        }
    }
}
