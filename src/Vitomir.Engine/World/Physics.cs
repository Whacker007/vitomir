// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Things that move under their own weight.
//
// `MOVETYPE 3` is the engine's physics mover, and until now Vitomir stored its state and integrated
// none of it: the velocities and the physics flags have been faithful from the start and nothing
// read them. Four separate pieces of work were queued behind that one gap — projectiles, the gib
// spray, dropped loot that falls, and anything a script pushes with `SetVelocity`.
//
// The whole of it is two engine functions:
//
//   * **`simThing::MovePhysics`** — the integrator, dispatched from the generic thing
//     tick's move-kind switch (move kind 1 → the walker, 3 → here).
//     Session 7's plan expected it among the thing code's four uncited functions; it is in none of
//     them.
//   * **`simThing::OnSurfaceContact`** — the bounce, reached as a virtual from the
//     collide sweep. It is the *default*, which is the important part: a gib, a dropped item and a
//     pushed prop all get it, because only `gameProjectile` overrides it.
//
// And the **query** that decides whether the move is blocked, and by what, is the engine's own since
// The move test casts the collision sweep (`WorldSweep`) along the move with things and
// surfaces, the mover left out, and offers every result to the mover nearest first — a surface to
// `OnSurfaceContact`, a thing to `OnThingContact` — until one says the move is over. Earlier, this
// was Vitomir's own: a march along the segment against the collision trees, and a ray against each
// character's standing cylinder for the projectile.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>The physics step: `simThing::MovePhysics` and the contact it runs into.</summary>
public static class Physics
{
    // ---- 1.1's own constants ------------------------------------------

    /// <summary>
    /// Which way down is (three f32). It is a **unit vector**, hard-coded, and
    /// there is no global gravity magnitude anywhere — the strength is per thing
    /// (<see cref="WorldThing.Gravity"/>). Nothing in any data file sets the direction.
    /// </summary>
    public static readonly Vector3 Down = new(0f, 0f, -1f);

    /// <summary>
    /// Physics bit 1 — gravity applies to this thing. Cleared when a thing is switched to
    /// <see cref="WorldThing.MovePhysics"/>, so a physics thing does **not** fall until
    /// something turns it on.
    /// </summary>
    public const uint Gravity = 0x1;

    /// <summary>
    /// Physics bit 8 — the thing has come to rest. The integrator tests it first and returns
    /// immediately, and <see cref="Contact"/> is what sets it. Not a script's "freeze" flag: it is
    /// the sleep the bounce puts a settled object into.
    /// </summary>
    public const uint Asleep = 0x8;

    /// <summary>The speed along the surface normal above which a contact is audible.</summary>
    public const float ImpactSoundSpeed = -10f;

    /// <summary>
    /// The floor the normal-direction speed is clamped to before the impulse is computed
    /// (−1, applied when `vn >= −1`). It means a *grazing* touch is given the same kick
    /// as a 1-unit-per-second one — the oddest single line in the function, and the reason
    /// the bounce floor exists.
    /// </summary>
    public const float MinimumNormalSpeed = -1f;

    /// <summary>A bare constant added to the impulse. Not part of any restitution formula.</summary>
    public const float ImpulseBoost = 0.1f;

    /// <summary>What every component of the velocity is multiplied by after a bounce.</summary>
    public const float Damping = 0.8f;

    /// <summary>
    /// Under what all three axes must be for the thing to fall asleep — and the Z threshold is a
    /// different constant from the other two (1 for X and Y, 10 for Z), which
    /// is deliberate in the original and easy to miss. **Signed**, not speeds: see <see cref="Contact"/>.
    /// </summary>
    public const float RestSpeedXY = 1f, RestSpeedZ = 10f;

    /// <summary>The contact is ignored entirely when the thing is already moving away this fast.</summary>
    public const float SeparatingSpeed = 1f;

    // ---- the step -------------------------------------------------------------------------------

    /// <summary>
    /// One thing, one tick (`simThing::MovePhysics`). The order is the engine's and it is
    /// observable, so it matters: **velocity is updated by gravity first, and the position then
    /// integrates the new velocity** — semi-implicit Euler, not the explicit kind.
    ///
    /// On a blocked move the engine writes **nothing**: no slide along the surface, no partial step
    /// up to the contact point, no clamp. The position simply stays where it was and the thing
    /// retries the identical move next frame. The only thing a contact changes is the velocity, and
    /// that happens inside <see cref="Contact"/>.
    /// </summary>
    public static void Step(Location location, WorldThing thing, float seconds, Shots? shots = null)
    {
        if (!Integrate(thing, seconds, out var target)) return;
        if (Blocked(location, thing, target, shots)) return;
        if (thing.Removed) return;                     // a thing it met finished it

        // The engine re-finds the sector rather than trusting the old one, and relinks
        // the thing when it has changed. **A point in no sector at all is not refused**: the finder
        // answers the sector it was given when no sector holds the point, so the guard
        // never fires and the thing flies on, still filed under its last sector. This
        // said the opposite until then read the sector placing.
        location.Move(thing, target);
    }

    /// <summary>
    /// The half of the step that touches nothing but the thing: the sleep gate, gravity, and the
    /// two integrations. Answers false when this thing does not move at all this tick, and
    /// otherwise hands back where it is *trying* to go — which the caller may refuse.
    ///
    /// Split out from <see cref="Step"/> so it can be exercised with no level around it; the engine
    /// has it all in one function, and nothing observable depends on which.
    /// </summary>
    public static bool Integrate(WorldThing thing, float seconds, out Vector3 target)
    {
        target = thing.Position;
        if (thing.MoveType != WorldThing.MovePhysics) return false;
        if ((thing.PhysicsFlags & Asleep) != 0) return false;

        if ((thing.PhysicsFlags & Gravity) != 0)
        {
            // Dt × the thing's own gravity, times the unit down vector. What is stored at
            // The stored gravity is this *delta-v*, not an acceleration — the collide sweep subtracts it back
            // off again, so it has to be the same quantity.
            var dv = Down * (thing.Gravity * seconds);
            thing.Velocity += dv;
            thing.AppliedDeltaV = dv;
        }
        else thing.AppliedDeltaV = Vector3.Zero;    // Cleared, not left stale

        // The position integrates the velocity gravity has *already* changed — semi-implicit Euler.
        target = thing.Position + thing.Velocity * seconds;

        // The rotation integrates whether or not the move is blocked, and it is applied
        // to the transform before the collide call rather than after it.
        var spin = thing.RotVelocity * seconds;
        if (spin != Vector3.Zero) thing.Orientation += spin;
        return true;
    }

    /// <summary>
    /// Every physics thing in a location, one tick. Removed things are skipped; the list is copied
    /// because a contact can in principle spawn (an explosion) and the engine's own tick walks a
    /// linked list it is allowed to append to.
    /// </summary>
    public static void StepAll(Location location, float seconds, Shots? shots = null)
    {
        foreach (var thing in location.Snapshot)     // a contact can spawn or destroy
            if (!thing.Removed && thing.MoveType == WorldThing.MovePhysics)
                Step(location, thing, seconds, shots);
    }

    /// <summary>
    /// What a projectile needs when it reaches somebody: the dice to roll with, the clock, the
    /// motion sets a death is played from, and whoever keeps the tally of blows. The simulation
    /// hands its own; a test with none gets a fixed generator and no tally.
    /// </summary>
    public sealed record Shots(Ai.Rng Rng, int NowMs, Anim.MotionSets? Motions, Action<Rpg.Projectiles.Struck>? Landed);

    // ---- the collide, and what it does on the way through -----------------------------------------

    /// <summary>
    /// The move test — whether the move is refused. The sweep with things and surfaces (flags
    /// 3), from where the thing is, in its own sector, along the move, with the
    /// thing itself left out and no type mask; then every result, nearest first,
    /// offered to the mover until one finishes the move:
    ///
    /// * a **surface** — first the frame's gravity delta-v is rewound by the part of the move not
    ///   travelled, *before* the response is asked and whether or not it blocks; then the
    ///   `OnSurfaceContact` virtual: <see cref="Contact"/>, or the projectile's own;
    /// * a **thing** — the `OnThingContact` virtual. Only `gameProjectile` overrides it;
    /// the ten other classes keep the base's answer, 0, "not
    ///   finished" — so a dropped item passes through every body, barrel and table in its way and
    ///   stops only at a surface. A gib chunk or a severed head is a projectile (`TYPE 4`), so a body,
    ///   prop or container it meets spends it.
    ///
    /// Returns true when the position must not be committed.
    /// </summary>
    static bool Blocked(Location location, WorldThing thing, Vector3 target, Shots? shots)
    {
        var from = thing.Position;
        var delta = target - from;
        float length = delta.Length();
        if (length == 0f) return false;                         // A zero move never collides
        var direction = delta * (1f / length);                  // the normalize multiplies by the reciprocal

        int sector = WorldSweep.SectorOf(location.Level, location.Solid, from, (int)thing.Sector);
        var met = WorldSweep.Line(location.Level, location.Things, sector, from, direction, length,
                             new WorldSweep.Asks(Pick.Tests.Both, Ignore: thing));
        var rng = shots?.Rng ?? Fallback;
        foreach (var contact in met)
        {
            if (contact.Kind == ContactKind.Surface)
            {
                thing.Velocity += -(1f - contact.Distance / length) * thing.AppliedDeltaV;
                int sound = SurfaceSound(location, contact.Sector, contact.Surface);
                // A projectile mostly gets the default too: its override delegates whenever
                // PROJ_EXPLODE_ONWALL is clear (`Rpg.Projectiles.OnSurfaceContact`).
                bool stop = thing.Type == ThingType.Projectile
                    ? Rpg.Projectiles.OnSurfaceContact(thing, contact.Normal, sound,
                                                       from + direction * contact.Distance, contact.Sector)
                    : Contact(thing, contact.Normal, surfaceSound: sound);
                if (stop) return true;
            }
            else if (contact.Thing is { } other && thing.Type == ThingType.Projectile
                     && Rpg.Projectiles.OnThingContact(thing, other, rng, shots?.NowMs ?? location.Milliseconds,
                                                       shots?.Motions ?? location.Motions, shots?.Landed))
                return true;
            if (thing.Removed) return true;                     // ours: something it met finished it
        }
        return false;
    }

    /// <summary>The generator a step with none falls back to, as the launch's does.</summary>
    static readonly Ai.Rng Fallback = new(1);

    /// <summary>
    /// The `sound` of the material a contact struck, or −1 with no audio — the engine counts a
    /// surface with no material as <see cref="Audio.KeyframeSounds.NoMaterial"/>, which
    /// the audio's own lookup answers.
    /// </summary>
    static int SurfaceSound(Location location, int sector, int surface)
    {
        if (location.Audio is null || sector < 0 || sector >= location.Level.Sectors.Length) return -1;
        if (surface < 0 || surface >= location.Level.Sectors[sector].Surfaces.Length) return Audio.KeyframeSounds.NoMaterial;
        return location.Audio.SurfaceSound(location, sector, surface);
    }

    /// <summary>
    /// `simThing::OnSurfaceContact` — the default response, and the one every physics
    /// thing that is not a projectile gets. It is a textbook restitution impulse with **two
    /// untextbook additions**, both of which this applies unconditionally because they are what the
    /// original does: the normal speed is floored at −1 before the impulse is computed (so a thing
    /// touching a floor at a hundredth of a unit is bounced as hard as one arriving at a full unit),
    /// and a bare **0.1** is added to the impulse, belonging to no term of any
    /// restitution formula. Both were switchable until then (the bounce Fixes, gone with their parameter).
    ///
    /// Returns true when the contact blocks the move.
    /// </summary>
    /// <param name="surfaceSound">
    /// The struck material's `sound` number, for the noise a hard enough contact makes; −1 for none.
    /// </param>
    public static bool Contact(WorldThing thing, Vector3 normal, int surfaceSound = -1)
    {
        float vn = Vector3.Dot(thing.Velocity, normal);

        // 0x4b13d?: a thing already separating from the plane this fast is not in contact with it.
        if (vn > SeparatingSpeed) return false;

        thing.LastImpactSpeed = vn;
        thing.Audible = vn < ImpactSoundSpeed;
        // A contact faster than 10 units a second is heard — the thing's own `walk_…` for
        // the struck material, the same event a footstep plays.
        if (thing.Audible && surfaceSound >= 0) Audio.Noise.Foley(thing, Audio.FoleyEvent.Walk + surfaceSound);

        // Both of the untextbook additions, applied unconditionally — this is what 1.1 does.
        // They used to sit behind `bounceminimum` and `bounceboost`; both switches were removed, by design:
        // the base game's own janky physics until a real physics engine goes in.
        float used = vn;
        if (used >= MinimumNormalSpeed) used = MinimumNormalSpeed;
        float impulse = -((thing.Restitution + 1f) * used) + ImpulseBoost;

        thing.Velocity += impulse * normal;
        thing.Velocity *= Damping;

        // **The rest test is signed**: each axis is compared, the comparison's
        // 0 or 1 loaded as an integer, and *that* made absolute and tested against zero — in effect
        // `abs(v.x < 1.0f)`, the bracket in the wrong place. So a thing moving towards −x
        // and −y at any speed, and not rising faster than 10, is put to sleep where it touched, while
        // the same speed towards +x keeps it going. Kept; until then
        // Vitomir took the magnitudes (a comparison with 1.1).
        var v = thing.Velocity;
        if (v.X < RestSpeedXY && v.Y < RestSpeedXY && v.Z < RestSpeedZ)
            thing.PhysicsFlags |= Asleep;

        return true;
    }

    // ---- the launch ---------------------------------------------------------------------------------

    /// <summary>
    /// A direction at random — what the gib spray scatters a chunk with, and the only
    /// place in the engine a uniform direction is wanted.
    ///
    /// It is **rejection sampling**, and the rejection is the whole point: a point is drawn from the
    /// cube [−1, 1]³ and thrown away if it fell outside the unit sphere, so the directions come out
    /// even instead of bunching towards the cube's eight corners. Otherwise it is normalised and
    /// returned.
    ///
    /// Two details are the engine's own. A draw of **exactly zero length** is not normalised — the
    /// divide is skipped and the raw (0, 0, 0) is what the caller gets, because the length test then
    /// passes. And the arithmetic is kept in the engine's order (x² added to y² + z², and a
    /// reciprocal multiplied rather than a divide) so the same seed gives the same chunk.
    /// </summary>
    public static Vector3 Direction(Ai.Rng rng)
    {
        while (true)
        {
            // Three draws, each doubled and shifted into [−1, 1].
            float x = rng.Unit() * 2f - 1f, y = rng.Unit() * 2f - 1f, z = rng.Unit() * 2f - 1f;
            float length = MathF.Sqrt(x * x + (z * z + y * y));
            var v = new Vector3(x, y, z);
            if (length != 0f) v *= 1f / length;
            if (length <= 1f) return v;   // Redrawn while the length is over 1
        }
    }
}
