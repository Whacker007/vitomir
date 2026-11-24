// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Firing something: the last piece the physics step was holding up.
//
// The engine splits it in two, and the split is worth keeping because only one half is combat's:
//
//   * **`simCombat::FireProjectile`** — make the template at a transform, refuse it if it
//     is not a projectile, and hand it its owner. Eleven lines, and no velocity anywhere in it: the
//     projectile flies because its template's `PHYSICS_VELOCITY` is rotated into the world by the
//     finalize (`World.Location.LaunchVelocity`). Every *fired* projectile template says `0, s, 0`
//     — a speed along its own forward axis and nothing else; the gibs and severed heads among the
//     156 use the other axes on purpose, which is why the rotate matters.
//   * **the two natives** (`FireProjectileAtThing`, `FireProjectileAtPos`) — which
//     is all the aiming. They work out a muzzle and a point to aim at, and turn the difference into
//     the transform the first half wants.
//
// Three shipped scripts fire one: `RegionProjectileAtThing`, `TrapRegionProjectile`,
// `TrapRegionProjectile2`. None of them ever calls `SetProjectileFlags`, so a projectile's behaviour
// comes entirely from its template.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>Launching a projectile, and where it launches from.</summary>
public static class Projectiles
{
    /// <summary>
    /// `simCombat::FireProjectile` — the launch proper, once something has decided where
    /// and which way. Answers the new projectile, or null.
    ///
    /// Two of its four lines are worth having:
    /// <list type="bullet">
    /// <item>**a template that is not `TYPE PROJECTILE` is made, unmade on the spot, and the fire
    /// fails.** The engine does not check the template first; it builds the thing, tests
    /// the type is not 4, and calls the thing's own **destructor** — not the ordinary
    /// `Destroy`, so there is no `DROPITEM` and no removal flag to wait for. Asking for a barrel
    /// here leaves nothing behind and answers 0, rather than putting a barrel in the air;</item>
    /// <item>**the owner is written unconditionally**, from the caller's argument rather
    /// than from the reference thing — which is how a trap's projectile can be attributed to whoever
    /// sprang it rather than to the trap.</item>
    /// </list>
    ///
    /// The reference thing supplies only the world and the scene to make the thing in; it does
    /// **not** supply the transform.
    /// </summary>
    public static WorldThing? Fire(Location place, WorldThing reference, string template,
                                   Vector3 position, Vector3 orientation, int ownerGuid,
                                   Rng? rng = null, int nowMs = 0, Anim.MotionSets? motions = null,
                                   Action<Struck>? landed = null)
    {
        if (place.Templates is null || !place.Templates.Has(template)) return null;
        if (place.Place(template, position, orientation, reference.Sector) is not { } shot) return null;

        // Not a projectile, so unmake it and fail.
        if (shot.Type != ThingType.Projectile) { place.Remove(shot, drop: false); return null; }

        shot.SpawnedBy = ownerGuid;

        // A firearm never flies. The shot is resolved here and the thing is already gone
        // by the time this returns, so a caller must not assume what it gets back is in the world.
        if ((shot.ProjectileFlags & Flag.InstantFire) != 0)
        {
            Net.HostSeen.Hitscan(shot);                         // Every player's machine fires it too
            Hitscan(shot, rng ?? Fallback, nowMs, motions, landed);
        }
        return shot;
    }

    /// <summary>The generator a caller with none of its own falls back to, as <see cref="Damage"/> has.</summary>
    static readonly Rng Fallback = new(1);

    /// <summary>
    /// `gameProjectile::OnSurfaceContact` — what a **flying** projectile does when it
    /// meets a wall, and the answer is usually nothing special. The override tests
    /// <see cref="Flag.ExplodeOnWall"/>; if it is clear it **delegates to
    /// <see cref="World.Physics.Contact"/>**, the ordinary bounce every physics thing
    /// gets. That delegation is the whole reason a gib chunk behaves normally despite being a
    /// projectile: every gib template has `PROJECT_FLAGS 0`.
    ///
    /// Before either branch it stamps its impact decal on the surface, at a random rotation out of
    /// 360 degrees and at twice its own radius, when it has one (<see cref="Explosions.Decals.Impact"/>). That field is the template's `WALLMARK`
    /// (244): the template reader gives it to a type-4 thing, a type-10 thing's
    /// going to the explosion's own mark instead. Every bullet names one (`BulletMark1`), the flame
    /// thrower, the flaming gib and the severed heads (`DCLBloodSpatter3`).
    ///
    /// Answers true when the contact blocks the move, as the generic response does.
    /// </summary>
    /// <param name="surfaceSound">The struck material's `sound` number, or −1 when not known.</param>
    /// <param name="at">Where it met the surface (the collision record's point, `+8`); the shot's own position when not given.</param>
    /// <param name="sector">The struck surface's sector, or −1 for the shot's own.</param>
    public static bool OnSurfaceContact(WorldThing shot, Vector3 normal, int surfaceSound = -1,
                                        Vector3? at = null, int sector = -1)
    {
        if (shot.Place is { } here)
            here.Decals.Impact(shot, sector >= 0 ? sector : (int)shot.Sector, normal, at ?? shot.Position, here.Milliseconds);
        if ((shot.ProjectileFlags & Flag.ExplodeOnWall) == 0)
            return World.Physics.Contact(shot, normal, surfaceSound: surfaceSound);     // the base class's
        // Bursting on the wall says the wall's `walk_…` on the shot, between the explosion and the
        // removal — at any speed, unlike the bounce.
        Detonate(shot, Flag.ExplodeOnWall, last: surfaceSound < 0 ? null
            : () => Audio.Noise.Foley(shot, Audio.FoleyEvent.Walk + surfaceSound));
        return true;
    }

    /// <summary>
    /// What the tick does to a projectile whose `TIMER` has run out (in the generic thing
    /// tick's expiry branch): if <see cref="Flag.ExplodeOnTimer"/> is set it **detonates** rather
    /// than simply vanishing. **Seventeen templates set it** — every fireball, firestorm and plague
    /// wind, the five grenades, the molotov, the napalm, the holy water, St George's oil and the
    /// rocket — which is how a thrown grenade goes off in the air when it has not hit anything. Not
    /// one arrow, bolt or bullet does.
    ///
    /// Split in two on purpose. <see cref="BurstsOnTimer"/> only *answers*, so the tick can ask
    /// while it is walking the thing list; <see cref="Expired"/> spawns and removes, which must
    /// happen after that walk — the same reason the tick collects its removals rather than doing
    /// them in place.
    /// </summary>
    public static bool BurstsOnTimer(WorldThing shot) =>
        shot.Type == ThingType.Projectile && (shot.ProjectileFlags & Flag.ExplodeOnTimer) != 0;

    /// <inheritdoc cref="BurstsOnTimer"/>
    /// <summary>Does it.</summary>
    public static void Expired(WorldThing shot) => Detonate(shot, Flag.ExplodeOnTimer);

    /// <summary>
    /// `FireProjectileAtThing` — the aim, and the only place the engine works out where a
    /// shot comes from and where it goes.
    ///
    /// <b>The muzzle</b> is the firer's own position, raised by <see cref="MidHeight"/> of its body,
    /// plus the caller's <paramref name="offset"/> **rotated into the firer's frame** — so a script
    /// gives an offset in body space (forward, left, up) and does not have to know which way the
    /// character is facing.
    ///
    /// <b>The aim point</b> is the target's position raised the same way. So the engine aims at the
    /// middle of a body, not at its feet and not at its head, and it uses the same rule for both
    /// ends of the line.
    ///
    /// <b>Half of *what*</b> is the detail that would be easy to get wrong: it is half the
    /// **height** for a cylinder (`COLLIDETYPE 2`, which is 1,766 of the 2,619 shipped templates and
    /// every character) and half the **radius** for anything else. A sphere or a box is therefore
    /// aimed at rather lower than its middle, and a thing with no body at all is aimed at its
    /// origin, because both fields are zero.
    ///
    /// Answers null when either end is missing, when the template is not in the table, or when the
    /// two ends are in the same place (there is no direction to fire in).
    /// </summary>
    public static WorldThing? FireAt(Location place, WorldThing firer, WorldThing target,
                                     string template, Vector3 offset, Rng? rng = null,
                                     int nowMs = 0, Anim.MotionSets? motions = null)
    {
        var from = Muzzle(firer, offset);
        var to = Aim(target);
        var direction = to - from;
        if (direction.LengthSquared() <= 0f) return null;
        direction = Vector3.Normalize(direction);
        return Fire(place, firer, template, from, Facing(direction), firer.Guid, rng, nowMs, motions);
    }

    /// <summary>
    /// `FireProjectileAtPos` — the same, aimed at a point instead of a thing. The muzzle
    /// rule is identical; only the far end changes.
    /// </summary>
    public static WorldThing? FireAtPoint(Location place, WorldThing firer, string template,
                                          Vector3 offset, Vector3 at, Rng? rng = null,
                                          int nowMs = 0, Anim.MotionSets? motions = null)
    {
        var from = Muzzle(firer, offset);
        var direction = at - from;
        if (direction.LengthSquared() <= 0f) return null;
        direction = Vector3.Normalize(direction);
        return Fire(place, firer, template, from, Facing(direction), firer.Guid, rng, nowMs, motions);
    }

    // ---- a weapon's shot -----------------------------------------------------------

    /// <summary>
    /// **A ranged weapon's `Fire` or `Strike` tag** — which the tag
    /// handler calls in place of the melee roll when the attack is
    /// <see cref="WeaponFlag.Ranged"/>, and its last act `FireWeapon`. Earlier, Vitomir
    /// settled a bow's attack with the sword's roll at the tag; in 1.1 the tag **looses an arrow**,
    /// and whatever the arrow meets is what it hits. In order:
    /// <list type="number">
    /// <item><b>the muzzle</b> (<see cref="WeaponMuzzle"/>), taken before anything turns;</item>
    /// <item><b>the aim</b>: the middle of the target's body (<see cref="Aim"/>), or with no target the
    /// order's own point;</item>
    /// <item><b>the turn</b>: the shooter snaps round to face the aim (on the level);</item>
    /// <item><b>the line</b>, muzzle to aim, <b>scattered</b> by the shooter's aim
    /// (<see cref="Scatter"/>);</item>
    /// <item><b>the launch</b> of the attack's <c>WEAPON_PROJECTILE</c> along it
    /// (<see cref="Fire"/>), owned by the shooter — a gun's shot is a hitscan and is over
    /// before this returns, an arrow flies;</item>
    /// <item>and if anything was launched: the weapon's own <c>use</c> noise on the shooter
    /// (the bow's twang is the <b>weapon's</b> sound, not the arrow's), its muzzle flash
    /// (below), and one shot's ammunition spent (<see cref="Weapon.Spend"/>).</item>
    /// </list>
    /// Answers the projectile, or null when nothing was launched.
    ///
    /// The muzzle flash is the attack's <c>WEAPON_MUZZLEFLASH</c> — only firearms name one, and it
    /// is an explosion — made at the muzzle with the shooter's orientation
    /// (<see cref="Explosions.ExplosionSystem.MuzzleFlash"/>).
    ///
    /// And last, whether or not anything flew: **the arrow goes from the bow**
    /// (hides <see cref="NamedShapes.Arrow"/> on the weapon's sub-model, slot 1), to come back when
    /// the next draw's <c>Project</c> tag nocks another (<see cref="Nock"/>).
    /// </summary>
    public static WorldThing? FireWeapon(Location place, WorldThing firer, Rng rng, int nowMs = 0,
                                         Anim.MotionSets? motions = null, Action<Struck>? landed = null)
    {
        if (firer.Actor is not { Weapon: { } weapon } actor) return null;
        var shot = Loose(place, firer, actor, weapon, rng, nowMs, motions, landed);
        NamedShapes.Hide(firer, NamedShapes.Arrow, HeldModel.WeaponSlot);
        return shot;
    }

    /// <summary>
    /// **The `Project` keyframe tag (8) nocks an arrow** — the tag dispatcher's case 8, for an actor or a
    /// player only, which shows <see cref="NamedShapes.Arrow"/> on the weapon's
    /// sub-model (slot 1) if there is one. Nothing is checked about the weapon: the tag is in the
    /// bow-draw animations, and a model without the shape simply does not answer to the name.
    /// </summary>
    public static bool Nock(WorldThing holder) =>
        holder.Type is ThingType.Actor or ThingType.Player
        && NamedShapes.Show(holder, NamedShapes.Arrow, HeldModel.WeaponSlot);

    /// <summary>The keyframe tag that nocks the arrow (the SDK's `Project`).</summary>
    public const uint ProjectTag = 8;

    /// <summary><see cref="FireWeapon"/> less the arrow's hiding: aim, scatter, launch, noise, spend.</summary>
    static WorldThing? Loose(Location place, WorldThing firer, Actor actor, Weapon weapon, Rng rng, int nowMs,
                             Anim.MotionSets? motions, Action<Struck>? landed)
    {
        var attack = weapon.Current;
        var muzzle = WeaponMuzzle(firer, weapon);
        var aim = actor.Target is { } target ? Aim(target) : actor.ActionPoint;
        firer.Face(aim);
        var line = aim - muzzle;
        if (line.LengthSquared() <= 0f) return null;
        var forward = Scatter(Vector3.Normalize(line), attack.Accuracy,
                              actor.Stat[(int)ActorStat.Dexterity], rng);
        if (attack.Projectile.Length == 0) return null;
        var shot = Fire(place, firer, attack.Projectile, muzzle, Facing(forward), firer.Guid, rng, nowMs,
                        motions, landed);
        if (shot is null) return null;
        Audio.Noise.Weapon(weapon, firer, Audio.FoleyEvent.Use);
        Explosions.ExplosionSystem.MuzzleFlash(place, firer, attack.MuzzleFlash, muzzle);
        weapon.Spend(firer.Type == ThingType.Player, actor.Inventory, firer);
        return shot;
    }

    /// <summary>
    /// Where a weapon's shot leaves its holder: the weapon's
    /// <see cref="Weapon.FirePosition"/> carried out by the holder's <see cref="MuzzleTag"/> bone as
    /// it is posed now, and then by the holder's own matrix. A holder with no model
    /// fires from its origin. A motion set that does not name the tag gives the engine a bone index
    /// of −1, which it does not check; Vitomir takes the holder's own frame there.
    /// </summary>
    public static Vector3 WeaponMuzzle(WorldThing holder, Weapon weapon)
    {
        if (holder.Animation is not { BoneCount: > 0 } animation) return holder.Position;
        var bone = animation.BoneMatrix(animation.TagBone(MuzzleTag));
        return Vector3.Transform(weapon.FirePosition, bone * holder.World);
    }

    /// <summary>
    /// The motion-set tag a shot leaves from (<c>0x1a</c>) — the hand
    /// that holds the weapon.
    /// </summary>
    public const int MuzzleTag = 0x1a;

    /// <summary>
    /// **How far off the line a shot goes**. One roll decides whether it is off
    /// at all, and by how much:
    /// <code>
    ///   spread = 100 - (rand01 x 100 + accuracy + Dexterity)
    ///   if spread &gt; 0:
    ///       yaw   = (rand01 x 30 - 15) x spread / 100      // degrees
    ///       pitch = (rand01 x 30 - 15) x spread / 100
    /// </code>
    /// and the aim is turned by that pitch and yaw **in its own frame** (the rotation
    /// multiplied on the aim's left). So a steady hand — Dexterity plus the attack's
    /// <c>WEAPON_ACCURACY</c> at 100 or more — never scatters at all, and anyone else scatters on a
    /// bad roll by up to 15 degrees either way times how bad it was. This is where a ranged
    /// attack's miss comes from in 1.1: there is **no to-hit roll** when an arrow arrives
    /// (<see cref="Hurt"/>) — an arrow that reaches somebody hits them.
    /// </summary>
    public static Vector3 Scatter(Vector3 forward, float accuracy, float dexterity, Rng rng)
    {
        var s = Rules.GameRules.Current.Ranged.ScatterFormula;     // scale, percent, span, half
        float spread = s[0] - (rng.Unit() * s[0] + accuracy + dexterity);
        if (!(spread > 0f)) return forward;
        spread *= s[1];
        float yaw = (rng.Unit() * s[2] - s[3]) * spread;           // local_3c[2], drawn first
        float pitch = (rng.Unit() * s[2] - s[3]) * spread;         // local_3c[0]
        const float d2r = MathF.PI / 180f;
        var aim = Transform(Facing(forward));
        var turned = Matrix4x4.CreateRotationX(pitch * d2r) * Matrix4x4.CreateRotationZ(yaw * d2r) * aim;
        return Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, turned));
    }

    /// <summary>The scatter's constants: 100, 0.01, 30 and 15.</summary>
    public const float Hundred = 100f, Percent = 0.01f, ScatterSpan = 30f, ScatterHalf = 15f;

    /// <summary>A thing's rotation from its angles, the way <see cref="WorldThing.Rotation"/> builds it.</summary>
    static Matrix4x4 Transform(Vector3 degrees)
    {
        var o = degrees * (MathF.PI / 180f);
        return Matrix4x4.CreateRotationY(o.Y) * Matrix4x4.CreateRotationX(o.X) * Matrix4x4.CreateRotationZ(o.Z);
    }

    /// <summary>
    /// The tag handler's gate before either path: an attack that takes
    /// ammunition and has none left tries to reload from the holder's pack, and if that
    /// fails the weapon says <c>outofammo</c> on the holder and **nothing is fired or swung**.
    /// Answers whether the attack may go on. Only a party member ever runs dry — the spend is
    /// player-only (<see cref="Weapon.Spend"/>) — so an enemy archer never reaches the noise.
    /// </summary>
    public static bool Loaded(WorldThing holder, Weapon weapon)
    {
        var attack = weapon.Current;
        if (attack.MaxAmmo == 0 || attack.Ammo != 0) return true;
        if (holder.Actor is { } actor && weapon.Reload(actor.Inventory, holder)) return true;
        Audio.Noise.Weapon(weapon, holder, Audio.FoleyEvent.OutOfAmmo);
        // And a ranged weapon's arrow goes — nothing left to nock.
        if (attack.Is(WeaponFlag.Ranged)) NamedShapes.Hide(holder, NamedShapes.Arrow, HeldModel.WeaponSlot);
        return false;
    }

    /// <summary>
    /// What a thrown weapon costs: after the throw, an attack with
    /// <see cref="WeaponFlag.Thrown"/> spends <b>one</b> of the item's <c>ITEM_AMOUNT</c>.
    /// At none left an item that is destroyed at zero goes from the pack; any other is left empty
    /// and marked spent. Before the spend, a weapon that was only swapped into the hand for the
    /// moment (second flag word bit 4) gives the hand back to the one it replaced
    /// (<see cref="WeaponOverride.Cancel"/>) — and the charge is still taken
    /// from the weapon that was thrown, which the engine read before the hand went back.
    /// A swapped-in weapon has no item here, so nothing is spent for it. <b>Ours</b>: the item
    /// destroyed while still in the hand leaves it for the holder's own weapon, as taking a weapon
    /// off does; what the engine's destructor does to the hand is not read.
    ///
    /// <para>**A weapon thrown off the pointer** (an Alt-click on the ground, or any attack click
    /// on a thing, with a grenade on the pointer swaps it in) is spent the same
    /// way: the use is called on the weapon thing itself, wherever it is, and the pointer's item is
    /// kept outside the inventory list (<see cref="Ground.Hand"/>) — so it is looked for there too.
    /// A stack stays on the pointer with one fewer; the last one, destroyed, leaves the pointer empty
    /// (<b>ours</b>, as above: the destructor's part is not read).</para>
    /// </summary>
    public static void Thrown(WorldThing holder, Weapon weapon)
    {
        if (!weapon.Current.Is(WeaponFlag.Thrown) || holder.Actor is not { } actor) return;
        if (WeaponOverride.Active(actor)) WeaponOverride.Cancel(holder);
        var item = actor.Inventory.Items.FirstOrDefault(i => ReferenceEquals(i.Weapon, weapon))
                   ?? (actor.Held is { } pointer && ReferenceEquals(pointer.Weapon, weapon) ? pointer : null);
        if (item is null) return;
        item.Amount -= Rules.GameRules.Current.Ranged.ThrownSpendsOne;
        if (item.Amount >= 1) return;
        if ((item.Flags & ItemFlags.DestroyedAtZeroCharges) == 0)
        {
            item.Amount = 0;
            item.Flags |= ItemFlags.Spent;
            return;
        }
        UsedUp.Gone(holder, item);                                                         // The same destroy as the use's
        if (ReferenceEquals(actor.Weapon, weapon))
        {
            actor.Weapon = actor.DefaultWeapon;
            holder.Locomotion?.Reanimate();
        }
    }

    /// <summary>
    /// A shot that reached somebody and counted — what <see cref="Hurt"/> hands whoever is keeping
    /// the tally (the simulation's blows and the journal). <see cref="Margin"/> is the accuracy
    /// roll's (<see cref="Accuracy"/>), <see cref="Damage"/> the dice after any doubling.
    /// </summary>
    public readonly record struct Struck(WorldThing Shot, WorldThing? Owner, WorldThing Victim, int Margin,
                                         float Damage, bool Critical, Harm Harm);

    // ---- what a shot hits, and what happens then ---------------------------------------------

    /// <summary>
    /// `PROJ_*`, the projectile half of the thing's type-specific fields.
    /// Only the six that are read appear here; the three dead ones are in
    /// the register.
    /// </summary>
    public static class Flag
    {
        public const uint ExplodeOnTimer = 0x1, ExplodeOnThing = 0x2, ExplodeOnWall = 0x4,
                          ExplodeOnDamage = 0x10, InstantFire = 0x20, Staking = 0x80, Bullet = 0x200;
    }

    /// <summary>
    /// **The hitscan**, which is how every firearm in the game works — the 17 templates
    /// carrying <see cref="Flag.InstantFire"/> never fly at all. The launch calls this in the frame
    /// the trigger is pulled, and **the projectile does not survive it**: every path out of this
    /// function destroys the thing.
    ///
    /// The engine casts a ray from the projectile's position along its own forward axis, out to its
    /// <see cref="WorldThing.ProjectileRange"/>, asking for things *and* surfaces, with the
    /// projectile itself excluded. Then it walks the results in order and, for each one:
    ///
    /// <list type="bullet">
    /// <item><b>a thing</b> — skip it if it is the projectile's own **owner** (by GUID).
    /// An <see cref="ThingType.Actor"/> or <see cref="ThingType.Player"/> goes to
    /// <see cref="OnThingContact"/>, and the shot **carries on to the next result** unless that
    /// returned true. A <see cref="ThingType.Container"/> is damaged and the shot stops. Everything
    /// else — items, weapons, armour, regions, other projectiles — is passed straight through. The
    /// container takes the shot's raw damage on its health and nothing else;</item>
    /// <item><b>a surface</b> — the shot ends there. The projectile is **moved to the hit point**
    /// first (it computes `position + t x forward` and calls `SetPosition`), so the
    /// wall-mark and the decal land where the bullet struck rather than where it was fired;</item>
    /// <item><b>nothing left</b> — the shot is destroyed having hit nobody.</item>
    /// </list>
    ///
    /// Answers what it hit, or null for a clean miss. Either way the projectile is gone.
    /// </summary>
    public static WorldThing? Hitscan(WorldThing shot, Rng rng, int nowMs = 0, Anim.MotionSets? motions = null,
                                      Action<Struck>? landed = null)
    {
        if (shot.Place is not { } place) return null;
        WorldThing? struck = null;
        // On a network player's machine the shot is destroyed before it looks at anything
        // — no sweep, no mark, no harm; the host's is the one that lands (a comparison with 1.1).
        if (Net.NetOrders.Mode(place) == Net.NetMode.Client) { place.Remove(shot, drop: false); return null; }

        if (place.Level is { } level)
        {
            var forward = Vector3.TransformNormal(new Vector3(0f, 1f, 0f), shot.Rotation);
            // The engine's sweep (`WorldSweep`) from the shot's own sector,
            // along its forward row for its range, with things, surfaces and bit
            // 4 — the dead passed over — and the shot itself left out. Earlier, this was
            // `Pick.Trace`, two-sided and stopped by detail brushes.
            int sector = World.WorldSweep.SectorOf(level, place.Solid, shot.Position, (int)shot.Sector);
            // The Fix `gunreach`: the ray longer by the shooter's radius and a little, so a
            // target the reach test let him fire at is within it.
            float range = shot.ProjectileRange;
            if ((place.Fixes ?? World.Fixes.Current).GunReach && place.Thing(shot.SpawnedBy) is { } shooter)
                range += shooter.Radius + World.Fixes.GunReachSlack;
            foreach (var contact in World.WorldSweep.Line(level, place.Things, sector, shot.Position, forward,
                                                          range,
                                                          new World.WorldSweep.Asks(World.Pick.Tests.Both | World.Pick.Tests.Unknown4,
                                                                                    Ignore: shot)))
            {
                if (contact.Kind == World.ContactKind.Surface)
                {
                    // The shot stops at the first surface, at the point it met it, and is
                    // handed the surface: its impact mark, and — every
                    // bullet sets PROJ_EXPLODE_ONWALL — its burst (`exp_smokepuff`) and the wall's sound.
                    place.Move(shot, contact.Point);
                    int sound = place.Audio is { } audio && contact.Sector >= 0 && contact.Surface >= 0
                        ? audio.SurfaceSound(place, contact.Sector, contact.Surface) : -1;
                    OnSurfaceContact(shot, contact.Normal, sound, contact.Point, contact.Sector);
                    break;
                }
                if (contact.Thing is not { } victim) continue;
                if (victim.Guid == shot.SpawnedBy) continue;         // Never its own maker
                if (victim.Type is ThingType.Actor or ThingType.Player)
                {
                    if (!OnThingContact(shot, victim, rng, nowMs, motions, landed)) continue;  // refused; keep going
                    struck = victim;
                    break;
                }
                // A container takes the shot's raw damage straight onto its health
                // (broken open by nobody at zero) — no roll, no noise, no burst. Until then
                // Vitomir stopped the shot there and did the crate no harm.
                if (victim.Type == ThingType.Container)
                {
                    Container.Damage(victim, shot.ProjectileDamage, motions: motions);
                    struck = victim;
                    break;
                }
                // Everything else is passed through.
            }
        }

        // Every exit destroys it.
        place.Remove(shot, drop: false);
        return struck;
    }

    /// <summary>
    /// `gameProjectile::OnThingContact` — what a projectile does to what it met. Answers
    /// **true when the hit counted**, which is what tells a hitscan to stop and a flying projectile
    /// that it is finished.
    ///
    /// Three refusals come first, and the second is the memorable one:
    /// <list type="bullet">
    /// <item>an actor or player carrying `THING_AF_DEAD` — **you cannot shoot a corpse**, the shot
    /// goes through it;</item>
    /// <item>the projectile's own owner, by GUID;</item>
    /// <item>a thing type above 8, which cannot happen in shipped data.</item>
    /// </list>
    ///
    /// Then a switch on the victim's type (nine entries):
    ///
    /// | type | |
    /// |---|---|
    /// | 0 `ACTOR`, 7 `PLAYER` | <see cref="Hurt"/>, and it detonates **only if** that says the blow counted |
    /// | 2 `PROP` | detonate, no damage at all |
    /// | 8 `CONTAINER` | damage it with the raw <see cref="WorldThing.ProjectileDamage"/> (no dice), play a foley, then detonate |
    /// | 1, 3, 4, 5, 6 | pass through — item, region, another projectile, weapon, armour |
    /// </summary>
    public static bool OnThingContact(WorldThing shot, WorldThing victim, Rng rng,
                                      int nowMs = 0, Anim.MotionSets? motions = null,
                                      Action<Struck>? landed = null)
    {
        if (victim.Type is ThingType.Actor or ThingType.Player
            && victim.Actor is { } dead && dead.Has(Actor.Flag.Dead)) return false;
        if (victim.Guid == shot.SpawnedBy) return false;

        switch (victim.Type)
        {
            case ThingType.Actor:
            case ThingType.Player:
                if (!Hurt(shot, victim, rng, nowMs, motions, landed)) return false;
                break;
            case ThingType.Prop:
                break;                                  // detonate, no damage
            case ThingType.Container:
                // The raw damage, not a roll — a crate does not dodge.
                Damage.Apply(victim, Owner(shot), HitKind.Projectile,
                             (DamageType)shot.ProjectileDamageType, shot.ProjectileDamage,
                             rng, nowMs, motions: motions);
                // The shot's own `hit…` for what the crate is made of.
                Audio.Noise.Foley(shot, Audio.FoleyEvent.HitFlesh + victim.SoundType);
                break;
            default:
                return false;                           // 1, 3, 4, 5, 6 pass through
        }
        Detonate(shot, Flag.ExplodeOnThing);
        return true;
    }

    /// <summary>
    /// The three lines every detonation shares: spawn the
    /// <see cref="WorldThing.ExplosionTemplate"/> **if the matching flag is set**, hand the new
    /// explosion the projectile's own owner so the kill is still attributed, and remove the
    /// projectile.
    ///
    /// A projectile with no explosion template, or without the flag, simply goes — which is the
    /// common case: 114 of the 155 shipped projectiles name no explosion. The explosion placed
    /// here begins as it enters the world and runs its <c>.nex</c> script;
    /// its owner is what credits its damage.
    /// </summary>
    /// <param name="last">Anything to do after the explosion and before the shot goes.</param>
    public static void Detonate(WorldThing shot, uint flag, Action? last = null)
    {
        if (shot.Place is not { } place) return;
        // On a player's machine the burst is the host's, which comes as a thing made (ours).
        if (shot.ExplosionTemplate.Length > 0 && (shot.ProjectileFlags & flag) != 0
            && Net.NetOrders.Mode(place) != Net.NetMode.Client
            && place.Place(shot.ExplosionTemplate, shot.Position, shot.Orientation, shot.Sector)
               is { Type: ThingType.Explosion } blast)
            blast.SpawnedBy = shot.SpawnedBy;
        last?.Invoke();
        place.Remove(shot, drop: false);
    }

    /// <summary>
    /// Hurting a character with a shot. Answers whether the blow counted — and in 1.1
    /// **it always does**, but for one refusal: every path out of the function ends in the shot
    /// being removed and a 1, except a party member's shot at another party member. (Vitomir said
    /// "counted if it did harm" until then, which let a soaked arrow fly on through its target.)
    ///
    /// In the engine's order:
    /// <list type="number">
    /// <item><b>the accuracy roll</b> (<see cref="Accuracy"/>) — first, before the refusal, so it is
    /// drawn even for a shot that is then refused. It is <b>not a to-hit roll</b>: its margin only
    /// decides a critical and a bullet's damage type;</item>
    /// <item><b>the friendly-fire refusal</b> — a shot fired by a party member cannot hit
    /// another party member. Both ends must be `THING_TYPE_PLAYER` carrying `THING_AF_PARTY`; the
    /// owner is looked up by GUID, so a shot outlives its firer and still refuses;</item>
    /// <item><b>the dice pool</b> — <see cref="WorldThing.ProjectileDamage"/> is ten times
    /// a count of d10s (<see cref="Roll"/>): `arrow` is 40, four d10;</item>
    /// <item><b>a critical</b> — a margin over <see cref="Combat.CriticalMargin"/> doubles the dice.
    /// A critical against a <b>vampire</b> from a <see cref="Flag.Staking"/> shot —
    /// every arrow is one — then rolls to stake instead (<c>rand01 x 100</c>, truncated,
    /// against the victim's effect kind 21) — a roll above it stakes the victim and the shot does
    /// no harm at all (<see cref="Stake"/>);</item>
    /// <item><b><see cref="Flag.Bullet"/></b> — against a vampire or a ghoul the shot
    /// rewrites its own damage type: <c>NORMAL</c> when the accuracy margin is under 20, <c>LETHAL</c>
    /// at 20 or more. <b>The margin, not the damage</b> (the hit compares the value that holds
    /// the roll's answer; this page said the rolled damage until then) — a well-placed bullet is
    /// lethal to the undead, a grazing one is bashing;</item>
    /// <item><b>the harm</b>, dealt <b>by the shot</b> as a projectile blow — whoever
    /// counts the kill and whoever the victim's mind turns on look through it to its owner;</item>
    /// <item><b>the noise</b>: the shot's own <c>hit…</c>, chosen the way a swing's is.</item>
    /// </list>
    /// The damage readout, called between the last two, prints the blow's numbers when the developer's damage
    /// display is on (bit 1 of the debug word) and does nothing else.
    /// </summary>
    public static bool Hurt(WorldThing shot, WorldThing victim, Rng rng,
                            int nowMs = 0, Anim.MotionSets? motions = null, Action<Struck>? landed = null)
    {
        // On a network player's machine the shot is only destroyed — no roll, no harm, no
        // noise; the host's shot is the one that hurts (a comparison with 1.1).
        if (Net.NetOrders.Mode(shot.Place) == Net.NetMode.Client) return true;
        int margin = Accuracy(shot, victim, rng);
        var owner = Owner(shot);
        // Party member on party member, refused.
        if (Rules.GameRules.Current.Ranged.PartyFriendlyFireOff
            && victim.Type == ThingType.Player && victim.Actor is { } them && them.Has(Actor.Flag.Party)
            && owner is { Type: ThingType.Player, Actor: { } us } && us.Has(Actor.Flag.Party))
            return false;

        float rolled = Roll(shot.ProjectileDamage, rng);
        bool critical = margin > Rules.GameRules.Current.Combat.CriticalMargin;
        if (critical) rolled += rolled;
        // A staking critical on a vampire stakes instead, with no harm, and is spent.
        if (Stake.Shot(shot, owner, victim, critical, rng)) return true;

        // A bullet is bashing against the undead unless it was well placed.
        if ((shot.ProjectileFlags & Flag.Bullet) != 0
            && victim.Actor is { Type: ActorType.Vampire or ActorType.Ghoul })
            shot.ProjectileDamageType = (uint)(margin < Rules.GameRules.Current.Ranged.BulletLethalMargin ? DamageType.Normal : DamageType.Lethal);
        var kind = (DamageType)shot.ProjectileDamageType;

        var harm = Damage.Apply(victim, shot, HitKind.Projectile, kind, rolled, rng, nowMs,
                                motions: motions);
        CombatMessages.Blow(shot, victim, rolled, harm.Taken, critical ? Rules.GameRules.Current.Combat.CriticalMultiplierStep : 0f);
        // The shot's own `hit…`, chosen the way a swing's is.
        Audio.Noise.Foley(shot, Audio.Noise.Struck(victim, harm.Taken));
        landed?.Invoke(new Struck(shot, owner, victim, margin, rolled, critical, harm));
        return true;
    }

    /// <summary>
    /// **The accuracy roll** — attack against defence, answered as a whole number:
    /// <code>
    ///   attack  = rand01 x Dexterity + effect kind 11    (a character firing)
    ///           = rand01 x 50                            (anything else, or nobody)
    ///   defence = rand01 x Dexterity + effect kind 12    (a character hit)
    ///           = rand01 x 50                            (anything else)
    ///   margin  = (int)(attack - defence)                // truncated toward zero
    /// </code>
    /// The attacker's side also adds the shot's own accuracy, which the projectile's
    /// constructor zeroes and nothing in the single-player game writes, so it is left
    /// out. Each side's effect fold is taken before its roll is drawn. The attacker is the shot's
    /// owner, looked up by GUID; a shot with no owner, or one a trap or a prop fired, rolls 50
    /// (<see cref="Unnamed"/>). Unlike the melee roll, the weapon's accuracy is not in it: that was
    /// spent aiming (<see cref="Scatter"/>).
    /// </summary>
    public static int Accuracy(WorldThing shot, WorldThing victim, Rng rng)
    {
        float attack;
        if (Owner(shot) is { Type: ThingType.Actor or ThingType.Player, Actor: { } a } firer)
        {
            float bonus = firer.Effects.Value(EffectKind.Offense);                 // kind 11
            float share = Rules.GameRules.Current.Ranged.ProjectileAccuracyRoll[0];
            attack = rng.Unit() * (share == 1f ? a.Stat[(int)ActorStat.Dexterity] : a.Stat[(int)ActorStat.Dexterity] * share) + bonus;
        }
        else attack = rng.Unit() * Rules.GameRules.Current.Ranged.ProjectileUnownedRoll;
        float defense;
        if (victim is { Type: ThingType.Actor or ThingType.Player, Actor: { } v })
        {
            float bonus = victim.Effects.Value(EffectKind.Defense);                // kind 12
            float share = Rules.GameRules.Current.Ranged.ProjectileAccuracyRoll[1];
            defense = rng.Unit() * (share == 1f ? v.Stat[(int)ActorStat.Dexterity] : v.Stat[(int)ActorStat.Dexterity] * share) + bonus;
        }
        else defense = rng.Unit() * Rules.GameRules.Current.Ranged.ProjectileUnownedRoll;
        return (int)(attack - defense);
    }

    /// <summary>What stands in for either side of <see cref="Accuracy"/> that is not a character.</summary>
    public const float Unnamed = 50f;

    /// <summary>
    /// The dice pool: `floor(value / 10)` rolls of 1 to 10, plus `rand01 x` whatever is
    /// left over. The engine's arithmetic order is kept — each die is `rand01 x 9 + 1` (9 is
    /// **9**, not 10, and the +1 is why the range starts at 1) and the remainder is added last.
    /// </summary>
    public static float Roll(int damage, Rng rng)
    {
        var d = Rules.GameRules.Current.Ranged.ProjectileDice;     // dice per point, points per die, span, floor
        int dice = (int)(damage * d[0]);
        float total = 0f;
        for (int i = 0; i < dice; i++) total += rng.Unit() * d[2] + d[3];
        return total + rng.Unit() * (damage - dice * d[1]);
    }

    /// <summary>Each die is 1 to 10, written as `rand01 x 9 + 1`.</summary>
    public const float DieSpread = 9f;
    /// <summary>The dice per point of the shot's damage (×0.1), the points per die (10) and each die's floor (1).</summary>
    public const float DicePerPoint = 0.1f, PointsPerDie = 10f, DieFloor = 1f;
    /// <summary>The accuracy margin a bullet needs against the undead before it stops being bashing.</summary>
    public const int BulletLethal = 20;

    /// <summary>
    /// Who fired it, by GUID. Looked up rather than held, so a shot whose firer has died
    /// or left still knows who it belongs to — and answers null when they are gone.
    /// </summary>
    public static WorldThing? Owner(WorldThing shot) =>
        shot.SpawnedBy != 0 ? shot.Place?.Thing(shot.SpawnedBy) : null;

    /// <summary>Where a shot leaves a thing: <see cref="MidHeight"/> up, plus a body-space offset.</summary>
    public static Vector3 Muzzle(WorldThing firer, Vector3 offset) =>
        firer.Position + new Vector3(0f, 0f, MidHeight(firer))
                       + Vector3.TransformNormal(offset, firer.Rotation);

    /// <summary>Where a shot is aimed: the middle of a body.</summary>
    public static Vector3 Aim(WorldThing target) =>
        target.Position + new Vector3(0f, 0f, MidHeight(target));

    /// <summary>
    /// Half a thing's height when it is a cylinder, half its radius otherwise (for the shooter, and again
    /// for the target). Zero for a thing with no body.
    /// </summary>
    public static float MidHeight(WorldThing thing) =>
        0.5f * (thing.Collide == CollideShape.Cylinder ? thing.Height : thing.Radius);

    /// <summary>
    /// The orientation that points a thing's forward axis along a direction — the engine turns the
    /// normalised direction into a matrix and sets its translation to the muzzle,
    /// then hands that matrix to the launch.
    ///
    /// **Ours in shape, theirs in effect.** Vitomir keeps three angles rather than a matrix, so this
    /// builds the angles that produce the same forward axis. **Forward is +Y**: that is what makes
    /// every projectile template's `PHYSICS_VELOCITY 0, s, 0` mean "s units a second that way", and
    /// it is the same axis the walker and the camera treat as forward.
    ///
    /// Roll is left at zero. The engine's matrix has one too — whatever its up-vector convention
    /// produces — and nothing reads it: a projectile is a body of revolution or close enough, its
    /// spin comes from `PHYSICS_ROTVEL`, and the only other consumer of the matrix is the velocity
    /// rotate, which a roll about the forward axis cannot change.
    /// </summary>
    public static Vector3 Facing(Vector3 direction)
    {
        // Yaw about Z from the ground track, pitch about X from the climb. Degrees, as a thing's
        // orientation is stored.
        const float r2d = 180f / MathF.PI;
        float flat = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        float yaw = MathF.Atan2(-direction.X, direction.Y) * r2d;
        float pitch = MathF.Atan2(direction.Z, flat) * r2d;
        return new Vector3(pitch, 0f, yaw);
    }
}
