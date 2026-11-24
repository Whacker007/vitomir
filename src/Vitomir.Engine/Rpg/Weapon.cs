// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a character fights with.
//
// The engine's gameWeapon is a thing (THING_TYPE_WEAPON) carrying **two attack records** of 0x40
// bytes each, with the index of the one in use and how many are real
// in the two bytes after them. The array ends exactly where those two bytes begin, which is
// why there can only ever be two. Every template tag whose name ends in a 2 writes the second
// record, and writing any of them is also what tells the weapon it has two: the setter falls
// through to `count = 2`. The defaults below are the
// constructor's.
//
// The defaults and the units are 1.1's own, not the SDK's:
// RELOADTIME is seconds on disk and milliseconds in memory (x1000, truncated), STRIKETIME
// stays seconds, DAMAGE is a whole number and the distances and accuracies are floats.
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// One of a weapon's two attacks. `WEAPON_*1` tags fill the first, `WEAPON_*2` tags the second.
/// </summary>
public sealed class WeaponAttack
{
    /// <summary>What the engine's constructor leaves in a record it is never told about.</summary>
    public const int DefaultAmmo = 20, DefaultAmmoUse = 1;
    public const float DefaultMaxDistance = 50f, DefaultMinDistance = 20f;

    /// <summary>`WEAPON_FLAGS`, or `WEAPON_FLAGS1`/`2` where the template gives them separately.</summary>
    public uint Flags { get; set; }
    /// <summary>`WEAPON_DAMAGE`. A whole number in every shipped template, 1..250.</summary>
    public int Damage { get; set; }
    /// <summary>`WEAPON_DAMAGETYPE` — which soak the target gets to use.</summary>
    public World.DamageType DamageType { get; set; }
    /// <summary>`WEAPON_STRIKETIME`, in seconds: how long the swing animation is stretched to take.</summary>
    public float StrikeTime { get; set; }
    /// <summary>`WEAPON_ACCURACY`, added to the attacker's Dexterity before the offence roll.</summary>
    public float Accuracy { get; set; }
    /// <summary>`WEAPON_RELOADTIME` in **milliseconds**: how long a ranged attack leaves the character busy.</summary>
    public int ReloadTime { get; set; }
    public int Ammo { get; set; } = Rules.GameRules.Current.Items.AmmoDefaultCapacity;
    public int MaxAmmo { get; set; } = Rules.GameRules.Current.Items.AmmoDefaultCapacity;
    public int AmmoType { get; set; }
    public int AmmoUse { get; set; } = (int)Rules.GameRules.Current.Movement.WeaponDefaults[0];
    public string MuzzleFlash { get; set; } = "";
    /// <summary>`WEAPON_MAXDISTANCE`: the furthest this attack reaches.</summary>
    public float MaxDistance { get; set; } = Rules.GameRules.Current.Movement.WeaponDefaults[1];
    /// <summary>`WEAPON_MINDISTANCE`: how close a *moving* attacker has to get. See <see cref="Reach"/>.</summary>
    public float MinDistance { get; set; } = Rules.GameRules.Current.Movement.WeaponDefaults[2];
    public string Projectile { get; set; } = "";
    /// <summary>
    /// Which attack animation this record plays, as an offset into the engine's block of attack
    /// modes: <see cref="Weapon.AttackModeBase"/> plus this is the body mode. It is the first field of
    /// the record — `WEAPON_TYPE2` (tag 0x29) writes the first record's and
    /// `WEAPON_MELEETYPE2` (tag 0xe2) the second's. 116 templates set the
    /// second, which is how one weapon gets two different swings: `Claw_vozhd` is `claw` then
    /// `clawspecial`, `Claw_szlachta` is `punch` then `claw`.
    /// </summary>
    public int MeleeType { get; set; }

    /// <summary>
    /// How far away this attack can be made from. A ranged attack, and a standing attacker, use
    /// the full <see cref="MaxDistance"/>; a *moving* melee attacker has to close to the midpoint
    /// of min and max first (0.5).
    /// </summary>
    public float Reach(bool attackerMoving) =>
        !attackerMoving || Is(WeaponFlag.Ranged)
            ? MaxDistance
            : MinDistance + (MaxDistance - MinDistance) * Rules.GameRules.Current.Movement.MeleeMidReach;

    /// <summary>The fraction of the min..max span a moving melee attacker must close to.</summary>
    public const float MidReach = 0.5f;

    public bool Is(uint flag) => (Flags & flag) != 0;
}

/// <summary>
/// The bits of `WEAPON_FLAGS`. **Every one of the seventeen is named**, by
/// `FLAG_WEAPON_FLAGS` in the SDK's own `NodSDK/noteditordata.txt` — the table the level editor
/// showed its checkboxes from, which ships inside the player's install. The names below are the
/// SDK's wherever the SDK has one; where this project had already found what a bit does in 1.1
/// and given it a behavioural name, that name is kept and the SDK's is recorded beside it, because
/// the SDK says what the bit was *for* and 1.1 shows what it *does*. They agree
/// everywhere they both speak.
///
/// Fourteen of the seventeen are used by the 275 shipped weapon templates; `DISTANCESCALE`,
/// `CAN PARRY` and `CAN DODGE` are set by nothing at all.
/// </summary>
public static class WeaponFlag
{
    /// <summary>
    /// The attack throws a projectile rather than swinging. It decides three things at once: the
    /// `Strike`/`Fire` animation tag fires the projectile path instead of the melee one,
    /// the character is left busy for <see cref="WeaponAttack.ReloadTime"/> rather than for the
    /// length of the animation, and the approach always uses the full reach.
    /// 94 templates.
    /// </summary>
    public const uint Ranged = 0x1;
    /// <summary>
    /// `DISTANCESCALE`. No shipped template sets it and nothing in 1.1 uses it.
    /// </summary>
    public const uint DistanceScale = 0x2;
    /// <summary>
    /// `REPEATABLE`: hold the button and the attack keeps coming. The 26 templates that carry it
    /// are the automatic weapons and nothing else — `assaultRifle`, `chaingun`, `flamethrower`,
    /// `chainsaw`, the pistols — so the name and the data agree. Read by the `Repeat` tag's handler
    /// (<see cref="HeldFire"/>), which loops the attack animation back to its `RepeatTo`
    /// tag while the button is down, and by its multiplayer server copy.
    /// </summary>
    public const uint Repeatable = 0x4;
    /// <summary>A critical hit with this can stake a vampire. 11 templates.</summary>
    public const uint CanStake = 0x8;
    /// <summary>Doubles the damage against a target with THING_AF_HURTBYSILVER. 2 templates.</summary>
    public const uint Silver = 0x10;
    /// <summary>
    /// The weapon leaves the hand when it strikes (0x43be f.). 17 templates.
    ///
    /// **The SDK calls this bit `QUICKUSE`**, and both readings are the same set: every template
    /// that carries it is something you throw and then no longer have — greek fire, holy water,
    /// shuriken, throwing knives, the grenades, the molotov — which is exactly what belongs on the
    /// quick-use strip. The name here stays the behaviour 1.1 gives it.
    /// </summary>
    public const uint Thrown = 0x20;
    /// <summary>
    /// This attack can take a head off, if the target has THING_AF_CANBEHEAD. 99
    /// templates. The SDK calls the bit `BLADED`, which is the same thing said from the other side.
    /// </summary>
    public const uint CanBehead = 0x40;
    /// <summary>Play the motion set's `special` variant of the attack mode. 41 templates.</summary>
    public const uint SpecialAnimation = 0x80;
    /// <summary>
    /// Sets <see cref="SpecialAnimation"/> on the **second** attack. The engine does this when it
    /// applies WEAPON_FLAGS to both records: record 1 gets `flags | 0x80`. 49 templates.
    /// </summary>
    public const uint SecondAttackIsSpecial = 0x100;
    /// <summary>
    /// `BLADE TRAIL`: the smear a swung blade leaves behind it. 38 templates, every one of them a
    /// sword or an axe, and the same 38 that carry <see cref="StrikeTrail"/>. Read on the first
    /// attack's word when the weapon goes into a hand: `WEAPON_TRAIL` (tag 242)
    /// names the particle **thing** made and hung on the hand (<see cref="Weapon.Trail"/>,
    /// <c>Particles.BladeTrails</c>).
    /// </summary>
    public const uint BladeTrail = 0x200;
    /// <summary>`CAN PARRY`. No shipped template sets it.</summary>
    public const uint CanParry = 0x400;
    /// <summary>`CAN DODGE`. No shipped template sets it.</summary>
    public const uint CanDodge = 0x800;
    /// <summary>
    /// The attack may be replaced by the weapon's special move — a throw
    /// (<see cref="SpecialMove"/>). 8 templates. The SDK calls the bit `THROW`, which is what the
    /// special move turned out to be.
    /// </summary>
    public const uint CanSpecialMove = 0x1000;
    /// <summary>
    /// `STRIKETRAIL`: the trail is drawn only across the striking part of the swing rather than the
    /// whole of it — `WEAPON_BLADESTART` and `WEAPON_BLADEEND` (tags 240, 241) are the two ends of
    /// the blade it is drawn between. The same 38 templates as <see cref="BladeTrail"/>.
    /// </summary>
    public const uint StrikeTrail = 0x2000;
    /// <summary>
    /// `SPECIFIC MODELS`: the model this weapon puts in the character's hand is **named after the
    /// character** rather than after the weapon. Without the bit the held model is
    /// `WEAPON_MODEL_SKELETONID` — `scythe_0` — and one file serves every character on that
    /// skeleton; with it the engine composes `character[_armour]_WEAPON_MODEL` instead, so Christof
    /// in plate gets `Christof_plate_claws` (see
    /// <see cref="World.HeldModel"/>).
    ///
    /// Six templates carry it and all six are the claw a vampire fights with, which is why: claws
    /// grow out of a specific pair of hands and have to be modelled per character, where a scythe
    /// is a scythe.
    /// </summary>
    public const uint SpecificModels = 0x4000;
    /// <summary>
    /// `(TWO HANDED -- OBSOLETE)` — the SDK's own name, parentheses and all. 75 templates still set
    /// it and nothing in 1.1 uses it: the live two-handed bit is `ITEM_FLAGS` 0x8000.
    /// Left here so a template that sets it is not read as setting something else.
    /// </summary>
    public const uint TwoHandedObsolete = 0x8000;
    /// <summary>
    /// The damage roll does **not** add the attacker's Strength. Exactly two templates
    /// set it, `taser` and `weap_taser` — so in 1.1 a gun's damage scales with Strength like a
    /// sword's, which reads as an oversight and is kept.
    /// </summary>
    public const uint NoStrength = 0x10000;
}

/// <summary>
/// A weapon: what its template says, plus which of its attacks is in use. Modelled on the engine's
/// gameWeapon rather than on the SDK's tag list, so the two-record structure and its quirks survive.
/// </summary>
public sealed class Weapon
{
    /// <summary>The engine's array is two long and its count byte starts at 1.</summary>
    public const int MaxAttacks = 2;

    public string Name { get; init; } = "";

    /// <summary>
    /// `WEAPON_TYPE1` (tag 0x28): how the weapon is **carried**, not how it is swung.
    /// The engine adds it to whichever locomotion block the character is in — walk 1, run
    /// <see cref="RunBase"/>, stand <see cref="StandBase"/>, limp <see cref="LimpBase"/> — to get the
    /// body mode, so a halberd (4) stands as `stand_polearm` (33 + 4 = 37) and walks as
    /// `walk_polearm` (1 + 4 = 5). The original's locomotion choice.
    /// </summary>
    public int Carry { get; set; }
    /// <summary>
    /// The template's own `WEAPON_FLAGS1` (tag 0xee), 0 when it has none — **not** the first attack's
    /// flags, which `WEAPON_FLAGS` (0x27) sets as well. A treasure record keeps this tag and no other
    /// flags, and the shop shelf's rollover reads its ranged
    /// and silvered bits there — so a bow whose ranged bit comes from
    /// `WEAPON_FLAGS`, under `_weapon`'s `WEAPON_FLAGS1 0`, has no ammo line on the shelf.
    /// </summary>
    public uint Flags1 { get; set; }
    /// <summary>`WEAPON_DEFENSETYPE`. No shipped template sets one.</summary>
    public int DefenseType { get; set; }
    public string Model { get; set; } = "";
    /// <summary>
    /// The weapon's own `.nag` (tag 6). A weapon is a thing in the engine, and its swing and the
    /// sound of what it hits come from its set, not from the character holding it.
    ///
    /// </summary>
    public string Foley { get; set; } = "";

    /// <summary>
    /// `WEAPON_FIREPOS` (tag 0x5f): where a shot leaves from, **in the frame of the
    /// hand that holds it** — the holder's motion-set tag 26 bone. Every firearm sets one
    /// (a pistol's is 16 forward and 8 up the hand, a rifle's 40 and 15); **no bow or crossbow
    /// does**, so an arrow leaves from the hand itself. The constructor zeroes it.
    /// </summary>
    public System.Numerics.Vector3 FirePosition { get; set; }

    /// <summary>`WEAPON_BLADESTART` (240) and `WEAPON_BLADEEND` (241): the blade's two ends in the hand's frame — a trail's segment runs between them.</summary>
    public System.Numerics.Vector3 BladeStart { get; set; }
    /// <inheritdoc cref="BladeStart"/>
    public System.Numerics.Vector3 BladeEnd { get; set; }
    /// <summary>
    /// `WEAPON_TRAIL` (242, 31 characters): the template of the particle thing hung on
    /// the hand as its blade trail — `weapon_trail` (whose emitter is `bastard`) on 34
    /// swords and axes, `flameTrail`, `bloodTrail`, `flameLine` and `holyLine` on four.
    /// </summary>
    public string Trail { get; set; } = "";
    /// <summary>The GUID of the trail it last hung on a hand, which taking it out of the hand looks up.</summary>
    public int TrailGuid { get; set; }

    /// <summary>The blade trail's tags: `WEAPON_BLADESTART`, `WEAPON_BLADEEND`, `WEAPON_TRAIL`.</summary>
    public const uint TagBladeStart = 240, TagBladeEnd = 241, TagTrail = 242;

    /// <summary>
    /// `WEAPON_THROWTYPE` (clamped 0..3; which of `throw1`…`throw4` and `thrown1`…`thrown4` the
    /// special move plays), `_THROWDIST` (where the victim is put, in front of the thrower) and
    /// `_THROWCHANCE` (clamped 0..1; the roll a `THROW` strike must come under) — the reader's cases
    /// 0xf5, 0xf7, 0xf8, read by <see cref="SpecialMove"/>.
    /// </summary>
    public int ThrowType { get; set; }
    public float ThrowDistance { get; set; }
    public float ThrowChance { get; set; } = Rules.GameRules.Current.Movement.WeaponDefaults[4];
    public const float DefaultThrowDistance = 200f, DefaultThrowChance = 0.5f;

    public WeaponAttack[] Attacks { get; } = [new(), new()];
    /// <summary>How many of the two records are real. 1 unless a `*2` tag was set.</summary>
    public int AttackCount { get; set; } = 1;
    /// <summary>Which record is in use.</summary>
    public int CurrentAttack { get; set; }

    public WeaponAttack Current => Attacks[CurrentAttack];

    // ---- the body modes a weapon selects (the attack and the locomotion choices) ----

    /// <summary>
    /// The first of the engine's attack modes (`claw`). An attack record's
    /// <see cref="WeaponAttack.MeleeType"/> is an offset from here, which is all there is to it: the
    /// halberd's 19 is `polethrust` (65 + 19 = 84), the crossbow's 8 is `crossbow`, the taser's 12 is
    /// `pistol`. All 90 armed templates come out right.
    /// </summary>
    public const int AttackModeBase = 65;

    /// <summary>The blocks <see cref="Carry"/> is added to. Walking is the first block, at 1.</summary>
    public const int WalkBase = 1, RunBase = 17, StandBase = 33, LimpBase = 195;

    /// <summary>
    /// What the engine plays when the motion set has nothing for the mode it asked for
    /// `swordslash`). The `special` flag is kept for the retry.
    /// </summary>
    public const int FallbackAttackMode = 66;

    /// <summary>
    /// The **utility** stance's substitutes. When it is on, thirteen of the attack modes are swapped
    /// for a `util_` variant and the carry class is forced to `util` (13), `util_onehand` (14) or
    /// `util_gun` (15). What turns it on is the off-hand utility item: the utility item
    /// (asserted `ITEM_TYPE_UTILITY`) and the flag is its own `0x800`,
    /// read by the attack and locomotion choices — in the shipped game, the torch; see
    /// <see cref="Utility"/>. A swapped mode the set lacks falls back to `swordslash` as any other.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int> UtilAttackModes = new Dictionary<int, int>
    {
        [1] = 95, [3] = 96, [5] = 97, [9] = 98, [10] = 99, [11] = 100, [12] = 101,
        [17] = 102, [18] = 103, [21] = 104, [24] = 105, [26] = 106, [27] = 107,
    };

    /// <summary>The carry classes the utility stance collapses to, by ordinary class.</summary>
    public const int UtilCarry = 13, UtilCarryOneHand = 14, UtilCarryGun = 15;

    /// <summary>The body mode an attack record swings with.</summary>
    public int AttackMode(int attack, bool utility = false)
    {
        int melee = Attacks[attack].MeleeType;
        if (utility && UtilAttackModes.TryGetValue(melee, out var swapped)) return swapped;
        return AttackModeBase + melee;
    }

    /// <summary>
    /// The body mode a character carrying this walks, runs, stands or limps in. The engine's own
    /// fallbacks are the caller's business: base + class, then the base alone, then the walk block's
    /// entry for the class, then plain `walk`.
    /// </summary>
    public int CarryMode(int block, bool utility = false) => block + (utility switch
    {
        false => Carry,
        true => Carry switch { 0 => UtilCarry, 1 => UtilCarryOneHand, 5 => UtilCarryGun, _ => Carry },
    });

    /// <summary>Picks an attack, the way the engine does: out-of-range asks are ignored.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < AttackCount) CurrentAttack = index;
    }

    /// <summary>Reads a weapon out of a resolved template, or null when the template is not one.</summary>
    public static Weapon? From(string name, IReadOnlyDictionary<uint, NodTag> tags)
    {
        if (!tags.TryGetValue(World.Tags.Type, out var type) || type.AsUInt() != (uint)World.ThingType.Weapon)
            return null;
        var w = new Weapon { Name = name };
        var (a, b) = (w.Attacks[0], w.Attacks[1]);

        // WEAPON_FLAGS fills both records; the second also gets SpecialAnimation when the first
        // asked for it. WEAPON_FLAGS1/2 override either outright — **in the order the template's
        // modifiers come in**, which is the engine's: the template chain is flattened base first, a
        // derived tag replacing its base's in place and a new one going on the end, and then
        // applies the list in that order. `_weapon` sets `WEAPON_FLAGS1 0` for everybody, so for the
        // 23 templates that set `WEAPON_FLAGS` below it — the claws, the fists, the bites, the
        // stakes, the thrown flasks — the flags win. Earlier, this applied FLAGS1 last whatever
        // came first, and those weapons' first attack lost its flags (a claw its SPECIFIC MODELS,
        // so it was never drawn). The resolved dictionary keeps that order: an id added again keeps
        // its first place.
        foreach (var (id, tag) in tags)
        {
            if (id == World.Tags.WeaponFlags)
            {
                a.Flags = b.Flags = tag.AsUInt();
                if ((a.Flags & WeaponFlag.SecondAttackIsSpecial) != 0) b.Flags |= WeaponFlag.SpecialAnimation;
            }
            else if (id == World.Tags.WeaponFlags1) a.Flags = w.Flags1 = tag.AsUInt();
            else if (id == World.Tags.WeaponFlags2) { b.Flags = tag.AsUInt(); w.Second(); }
        }

        w.Carry = Int(tags, World.Tags.WeaponType1);
        w.DefenseType = Int(tags, World.Tags.WeaponDefenseType);
        w.Model = Str(tags, World.Tags.WeaponModel);
        w.Foley = Str(tags, World.Tags.Foley);
        if (tags.TryGetValue(World.Tags.WeaponFirePos, out var fp)) w.FirePosition = fp.AsVector3();
        if (tags.TryGetValue(TagBladeStart, out var bs)) w.BladeStart = bs.AsVector3();
        if (tags.TryGetValue(TagBladeEnd, out var be)) w.BladeEnd = be.AsVector3();
        if (tags.TryGetValue(TagTrail, out var tr)) { string t = tr.AsString(); w.Trail = t.Length > 31 ? t[..31] : t; }   // strncpy 0x20, byte 31 zeroed

        Fill(w, a, tags, first: true);
        Fill(w, b, tags, first: false);

        // The melee type is the first field of each record: `WEAPON_TYPE2` writes the first one's and
        // `WEAPON_MELEETYPE2` the second's, and the second — like every other `*2` tag — is also what
        // says the weapon has two attacks.
        a.MeleeType = Int(tags, World.Tags.WeaponMeleeType);
        if (tags.TryGetValue(World.Tags.WeaponMeleeType2, out var mt)) { b.MeleeType = (int)mt.AsUInt(); w.Second(); }

        if (tags.TryGetValue(World.Tags.WeaponThrowType, out var tt))
            w.ThrowType = Math.Clamp((int)tt.AsUInt(), 0, 3);
        w.ThrowDistance = tags.TryGetValue(World.Tags.WeaponThrowDist, out var td)
            ? Math.Max(td.AsFloat(), 0f) : Rules.GameRules.Current.Movement.WeaponDefaults[3];
        w.ThrowChance = tags.TryGetValue(World.Tags.WeaponThrowChance, out var tc)
            ? Math.Clamp(tc.AsFloat(), 0f, 1f) : Rules.GameRules.Current.Movement.WeaponDefaults[4];
        return w;
    }

    /// <summary>Any `*2` tag makes the weapon two-attacked, whatever the tag was.</summary>
    void Second() { if (AttackCount < 3) AttackCount = 2; }

    static void Fill(Weapon w, WeaponAttack at, IReadOnlyDictionary<uint, NodTag> tags, bool first)
    {
        var t = first ? World.Tags.WeaponFirst : World.Tags.WeaponSecond;
        if (Set(tags, t.Damage, v => at.Damage = (int)v.AsUInt()) && !first) w.Second();
        if (Set(tags, t.DamageType, v => at.DamageType = (World.DamageType)v.AsUInt()) && !first) w.Second();
        if (Set(tags, t.StrikeTime, v => at.StrikeTime = v.AsFloat()) && !first) w.Second();
        if (Set(tags, t.Accuracy, v => at.Accuracy = v.AsFloat()) && !first) w.Second();
        // Seconds on disk, milliseconds in memory, truncated toward zero (× 1000).
        if (Set(tags, t.ReloadTime, v => at.ReloadTime = (int)(v.AsFloat() * MillisecondsPerSecond)) && !first) w.Second();
        if (Set(tags, t.MaxAmmo, v => at.Ammo = at.MaxAmmo = (int)v.AsUInt()) && !first) w.Second();
        if (Set(tags, t.AmmoType, v => at.AmmoType = (int)v.AsUInt()) && !first) w.Second();
        if (Set(tags, t.AmmoUse, v => at.AmmoUse = (int)v.AsUInt()) && !first) w.Second();
        if (Set(tags, t.MuzzleFlash, v => at.MuzzleFlash = v.AsString()) && !first) w.Second();
        if (Set(tags, t.MaxDistance, v => at.MaxDistance = v.AsFloat()) && !first) w.Second();
        if (Set(tags, t.MinDistance, v => at.MinDistance = v.AsFloat()) && !first) w.Second();
        if (Set(tags, t.Projectile, v => at.Projectile = v.AsString()) && !first) w.Second();
    }

    public const float MillisecondsPerSecond = 1000f;

    // ---- ammo (the spend and the reload) ----

    /// <summary>
    /// What the AI's combat gate asks: **this attack uses ammo and has
    /// none**. A weapon with no <see cref="WeaponAttack.MaxAmmo"/> never runs out, which is why a
    /// sword is not constantly out of ammo despite the constructor leaving 20 in every record.
    /// </summary>
    public bool OutOfAmmo => Current.MaxAmmo != 0 && Current.Ammo == 0;

    /// <summary>
    /// Load the current attack from <paramref name="inventory"/>, returning whether
    /// anything was loaded. Three details of the original come with it, and none of them is obvious:
    /// <list type="bullet">
    /// <item>the **smallest** matching stack is taken, not the first or the largest;</item>
    /// <item>**every** record sharing the ammo type is filled, not just the one in use — the engine
    /// walks both of its two records unconditionally;</item>
    /// <item>the stack is consumed **whole**, and each magazine clamps at its own capacity, so
    /// reloading a part-full magazine throws the remainder of the stack away. Taking the smallest
    /// stack first is what keeps that rare rather than routine.</item>
    /// </list>
    /// </summary>
    /// <param name="holder">
    /// Who is reloading, for the noise the reload makes: the spent stack's `remove` as it leaves the
    /// pack (the release with its sound on), from the stack's own set, on its owner. The
    /// stack is also told `use` just before, but on **itself** — no place given — and a
    /// stack in a pack is in no world, so the foley's world test drops it: the reload's `use` is never
    /// heard, and Vitomir does not start it.
    /// </param>
    public bool Reload(Inventory inventory, World.WorldThing? holder = null)
    {
        var current = Current;
        if (current.MaxAmmo == 0 || current.Ammo >= current.MaxAmmo) return false;

        var stack = inventory.SmallestAmmo(current.AmmoType);
        if (stack is null) return false;

        foreach (var attack in Attacks)
            if (attack.AmmoType == current.AmmoType)
                attack.Ammo = Math.Min(attack.Ammo + stack.Amount, attack.MaxAmmo);

        if (holder is not null) Items.Heard(holder, stack, Audio.FoleyEvent.Remove);
        inventory.Remove(stack);
        return true;
    }

    /// <summary>
    /// The console's <c>freeammo</c> (and <c>idkfa</c>): bit 2 of the debug word,
    /// which the spend tests first — nobody runs out. Off unless somebody turns it on.
    /// </summary>
    public static bool FreeAmmo { get; set; }

    /// <summary>
    /// Spend a shot's worth of ammo, and reload straight away if that emptied the
    /// magazine. <paramref name="holderIsPlayer"/> is the whole of the original's own condition and
    /// it is a large one: the engine checks the holder's **thing type is
    /// <see cref="World.ThingType.Player"/>** before touching a single round, so **only the playable
    /// cast ever runs out of ammo**. An enemy crossbowman fires for ever. Like the spend itself,
    /// the ammo-use amount comes from the record in use while *every* record sharing the type is
    /// decremented.
    /// </summary>
    public void Spend(bool holderIsPlayer, Inventory? inventory, World.WorldThing? holder = null)
    {
        if (!holderIsPlayer || FreeAmmo) return;

        var current = Current;
        int use = current.AmmoUse;
        foreach (var attack in Attacks)
            if (attack.AmmoType == current.AmmoType)
                attack.Ammo = attack.Ammo > use ? attack.Ammo - use : 0;

        if (Current.Ammo == 0 && inventory is not null) Reload(inventory, holder);
    }

    static bool Set(IReadOnlyDictionary<uint, NodTag> tags, uint id, Action<NodTag> apply)
    {
        if (!tags.TryGetValue(id, out var t)) return false;
        apply(t);
        return true;
    }

    static int Int(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? (int)t.AsUInt() : 0;
    static string Str(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? t.AsString() : "";
}
