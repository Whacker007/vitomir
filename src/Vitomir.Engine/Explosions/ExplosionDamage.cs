// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// An explosion's blow (with its line-of-sight test): who is
// in reach, who is spared, how hard each is hit, and what each kind of thing does about it.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Explosions;

/// <summary>
/// The damage an explosion deals, once per APPLYDAMAGE and once per pulse. Everything here is
/// The original's, in its order:
/// <list type="number">
/// <item>**Who is in reach**: the things the sphere query finds within the damage
/// radius of the explosion's position, flooding out of its sector — a sphere against a sphere, a
/// cylinder by its horizontal distance once the heights overlap, a box by its sides — each with
/// the distance the query measured.</item>
/// <item>**Who is spared**: the explosion itself; anything already being destroyed (flag
/// 0x1000000`); with `NO DAMAGE TO SELF`, whoever has the owner's GUID; then by type — an item,
/// region, weapon, armour or another explosion is never hurt, and the `NO DAMAGE TO …` flags spare
/// actors, players, props, projectiles and containers; with `NO DAMAGE TO TEAM`, a character the
/// owner counts as friendly, when the owner is a character.</item>
/// <item>**Whether it can see them**: the engine's sweep from the explosion toward the
/// target's position, as long as the measured distance, meets nothing — or meets the target first
/// — before it meets a wall or a thing flagged `CAMERA BLOCK` (0x4000). Other things are passed
/// through, and its type mask leaves out everything but props, so a character is never met at all.
/// Failing that the engine tries **a second ray, straight up from the explosion by the
/// explosion's own height** (its `HEIGHT` if it is a cylinder, else its `RADIUS`) — it reads its
/// own fields where it meant the target's. No shipped explosion has either, so that ray has no
/// length and meets nothing (unless the explosion is inside a camera-blocking prop's sphere or box),
/// and passes: **in 1.1 a wall does not stop an explosion's damage**. **Repaired**
/// (<see cref="World.Repairs.BlastSight"/>): Vitomir's second ray goes to the top of
/// the target, as meant, so a wall stops the blow.</item>
/// <item>**How hard**: <c>min + (max − min) × (radius − distance) / radius</c> — the full max at the
/// centre, the min at the edge, and less than the min for a body whose edge is in reach but whose
/// middle is not (a cylinder's distance is to its axis).</item>
/// <item>**What it does**: a character or party member takes it as an explosion blow
/// (<see cref="HitKind.Explosion"/>) from **the owner**, looked up by GUID —
/// the kill is the owner's; a container takes it on its health from nobody; a projectile
/// with `PROJ_EXPLODE_ONDAMAGE` detonates, handing its own owner on; a prop's scripts
/// are sent `damaged` (<see cref="Rpg.Ground.Scripts.Damaged"/>).</item>
/// </list>
/// </summary>
public static class ExplosionDamage
{
    /// <summary>The most things one sphere query answers (the query's table).</summary>
    public const int MaxResults = 256;
    /// <summary>`THING_FLAG_CAMERABLOCK`: a thing with it stops an explosion's line of sight (flag <c>0x4000</c>).</summary>
    public const uint Blocks = WorldThing.Flag.CameraBlock;

    /// <summary>One thing in reach and the distance the query measured.</summary>
    public readonly record struct InReach(WorldThing Thing, float Distance);

    /// <summary>What one blow did to one thing, for tests and the journal.</summary>
    public readonly record struct Hit(WorldThing Target, float Distance, float Amount, Harm Harm);

    /// <summary>The hits of the last <see cref="Deal"/>, for tests.</summary>
    [ThreadStatic] static List<Hit>? last;
    /// <summary>What the last blow hit, in the order it hit them.</summary>
    public static IReadOnlyList<Hit> Last => last ?? [];

    [ThreadStatic] static List<WorldThing>? walled;
    /// <summary>Whom the last blow left alone only because a wall stood between (<see cref="Repairs.BlastSight"/>); 1.1 hurts them.</summary>
    public static IReadOnlyList<WorldThing> LastWalled => walled ?? [];

    static readonly ILogger log = Diagnostics.Diag.For("Vitomir.Explosions");

    /// <summary>
    /// Deals the blow. <paramref name="rng"/> is the soak's (the damage rolls it); <paramref name="now"/>
    /// the clock. Answers what it hit.
    /// </summary>
    public static IReadOnlyList<Hit> Deal(Explosion e, Ai.Rng rng, int now)
    {
        var hits = last = [];
        walled = null;
        var blast = e.Thing;
        if (blast.Place is not { } place) return hits;
        float radius = e.DamageRadius;
        if (!(radius > 0f)) return hits;                             // the engine asserts `damageRadius > 0.0`
        var owner = blast.SpawnedBy != 0 ? place.Thing(blast.SpawnedBy) : null;
        foreach (var (target, distance) in Gather(place, blast.Position, radius, blast.Sector))
        {
            if (ReferenceEquals(target, blast) || (target.Flags & WorldThing.Flag.Remove) != 0 || target.Removed) continue;
            if ((e.Flags & Explosion.Flag.NoDamageToSelf) != 0 && blast.SpawnedBy == target.Guid) continue;
            if (Spared(e.Flags, target, owner)) continue;
            if (!Sees(place, blast, target, distance))
            {
                // Said out loud, as a repair with no switch should be: whom 1.1 would have hurt here.
                if (SeesAsWritten(place, blast, target, distance))
                {
                    (walled ??= []).Add(target);
                    log.LogInformation("Explosion: {Blast} does not hurt {Who}, {Distance:0} away behind a wall (the repair blastsight; 1.1 would)",
                        blast.Template, Diagnostics.Watch.Name(target), distance);
                }
                continue;
            }

            float amount = (radius - distance) / radius * (e.MaxDamage - e.MinDamage) + e.MinDamage;
            var harm = default(Harm);
            switch (target.Type)
            {
                case ThingType.Actor:
                case ThingType.Player:
                    harm = Damage.Apply(target, owner, HitKind.Explosion, (DamageType)e.DamageType, amount, rng, now,
                                        motions: place.Motions);
                    break;
                case ThingType.Container:
                    harm = Damage.Apply(target, null, HitKind.Explosion, (DamageType)e.DamageType, amount, rng, now,
                                        motions: place.Motions);
                    break;
                case ThingType.Projectile:
                    // Only a projectile that bursts when damaged, and has something to burst into.
                    if ((target.ProjectileFlags & Projectiles.Flag.ExplodeOnDamage) != 0 && target.ExplosionTemplate.Length > 0)
                        Projectiles.Detonate(target, Projectiles.Flag.ExplodeOnDamage);
                    break;
                case ThingType.Prop:
                    // The prop's scripts are told, and their answer
                    // is thrown away; a prop has no health of its own.
                    place.ItemScripts?.Damaged?.Invoke(target, owner, amount, e.DamageType);
                    break;
            }
            hits.Add(new Hit(target, distance, amount, harm));
        }
        return hits;
    }

    /// <summary>The switch on the target's type and the team test beside it.</summary>
    public static bool Spared(uint flags, WorldThing target, WorldThing? owner)
    {
        switch (target.Type)
        {
            case ThingType.Actor:
                if ((flags & Explosion.Flag.NoDamageToActors) != 0) return true;
                return Team(flags, target, owner);
            case ThingType.Player:
                if ((flags & Explosion.Flag.NoDamageToPlayers) != 0) return true;
                return Team(flags, target, owner);
            case ThingType.Prop: return (flags & Explosion.Flag.NoDamageToProps) != 0;
            case ThingType.Projectile: return (flags & Explosion.Flag.NoDamageToProjectiles) != 0;
            case ThingType.Container: return (flags & Explosion.Flag.NoDamageToContainers) != 0;
            case ThingType.Item or ThingType.Region or ThingType.Weapon or ThingType.Armor
                 or ThingType.None or ThingType.Explosion: return true;
            default: return false;
        }
    }

    /// <summary>`NO DAMAGE TO TEAM`: an owner who is an actor or a player spares whoever it counts as friendly.</summary>
    static bool Team(uint flags, WorldThing target, WorldThing? owner) =>
        (flags & Explosion.Flag.NoDamageToTeam) != 0 && owner is { Type: ThingType.Actor or ThingType.Player }
        && Sight.Friendly(owner, target);

    /// <summary>
    /// The line test twice, as the blow calls it: toward the target's point, and failing that toward
    /// **the top of the target** — its point raised by its `HEIGHT` if it is a cylinder, else its
    /// `RADIUS` — so that somebody whose feet a low thing hides is still caught by the head.
    /// That second ray is the repair <see cref="Repairs.BlastSight"/>: the engine wrote it with the
    /// explosion's own point and height where it meant the target's (<see cref="SeesAsWritten"/>),
    /// which made it a ray of no length that always passes, and a wall stopped nothing.
    /// </summary>
    public static bool Sees(Location place, WorldThing blast, WorldThing target, float distance)
    {
        var toward = target.Position - blast.Position;
        float length = toward.Length();
        if (length != 0f) toward /= length;                         // a zero vector stays zero
        if (Clear(place, blast, toward, distance, target)) return true;
        float up = target.Collide == CollideShape.Cylinder ? target.Height : target.Radius;
        var top = target.Position + new Vector3(0f, 0f, up) - blast.Position;
        float h = top.Length();
        return Clear(place, blast, h != 0f ? top / h : top, h, target);
    }

    /// <summary>
    /// The same as 1.1 has it, kept for the record and the tests: the second ray straight
    /// up from the explosion by the explosion's own height. No shipped explosion has a height or a
    /// radius, so it has no length, meets nothing and passes.
    /// </summary>
    public static bool SeesAsWritten(Location place, WorldThing blast, WorldThing target, float distance)
    {
        var toward = target.Position - blast.Position;
        float length = toward.Length();
        if (length != 0f) toward /= length;
        if (Clear(place, blast, toward, distance, target)) return true;
        float up = blast.Collide == CollideShape.Cylinder ? blast.Height : blast.Radius;
        var rise = new Vector3(0f, 0f, up);
        float h = rise.Length();
        return Clear(place, blast, h != 0f ? rise / h : rise, h, target);
    }

    /// <summary>
    /// The line's type mask (<c>0xfffffffb</c>) — every
    /// thing type left out but a prop, the camera's own mask.
    /// </summary>
    public const uint SightMask = 0xfffffffb;

    /// <summary>
    /// **the engine's sweep** (<see cref="WorldSweep.Line"/>) from the
    /// explosion's position in the explosion's own sector, things and surfaces
    /// (flags 3), <see cref="SightMask"/>, nothing left out — then the results nearest first:
    /// none left, or the target itself, and it is clear; a surface, or a thing flagged
    /// `CAMERA BLOCK`, and it is not; any other thing is passed. Because of the mask **only a prop
    /// is ever met**, so a character target never is, and the line to one is clear unless a wall or
    /// a camera-blocking prop comes first within the length. A line of no length is still cast, as
    /// the engine casts it: it meets only what the explosion's point is already inside.
    /// **Ours:** an explosion whose sector was never recorded is looked for from its position
    /// (<see cref="SectorSearch"/>).
    /// </summary>
    public static bool Clear(Location place, WorldThing blast, Vector3 direction, float length, WorldThing target)
    {
        var level = place.Level;
        int sector = blast.Sector >= 0 && blast.Sector < level.Sectors.Length
            ? (int)blast.Sector : SectorSearch.Find(level, blast.Position, -1);
        var asks = new WorldSweep.Asks(Pick.Tests.Both, TypeMask: SightMask, Filed: place.Props);
        foreach (var met in WorldSweep.Line(level, place.Things, sector, blast.Position, direction, length, asks))
        {
            if (met.Kind == ContactKind.Thing && ReferenceEquals(met.Thing, target)) return true;
            if (met.Kind != ContactKind.Thing || met.Thing is { } t && (t.Flags & Blocks) != 0) return false;
        }
        return true;
    }

    /// <summary>
    /// The sphere query (over the sector flood): what is within <paramref name="radius"/>
    /// of <paramref name="centre"/>, in the order the flood finds them, each with the distance by its
    /// shape — a sphere its centre's, a cylinder its axis's across the ground once the heights
    /// overlap, a box 0 when the sphere touches it. A thing with no shape, or `DISABLE COLLIDE`, is
    /// not found. Past 256, one only gets in by displacing a farther one. The same arithmetic as
    /// `CodexCollision.ThingsInSphere` (`CodexScenes.Powers.cs`), which asks the same query.
    /// </summary>
    public static List<InReach> Gather(Location place, Vector3 centre, float radius, long sector)
    {
        var found = new List<InReach>();
        IEnumerable<WorldThing> candidates = place.Things;
        if (place.Props is { } bodies && place.Solid is { } solid)
        {
            int start = solid.SectorAt(centre, (int)sector);
            if (start >= 0)
            {
                var sectors = bodies.SectorsNear(centre, radius, start);
                var inFlood = new HashSet<int>(sectors);
                candidates = bodies.In(sectors).Where(t => inFlood.Contains((int)t.Sector));
            }
        }
        foreach (var t in candidates)
        {
            if (t.Removed || (t.Flags & WorldThing.Flag.DisableCollide) != 0) continue;
            float? d = t.Collide switch
            {
                CollideShape.Sphere => Vector3.Distance(centre, t.Position) is var s && s <= radius + t.Radius ? s : null,
                CollideShape.Cylinder => centre.Z > t.Position.Z + radius + t.Height || t.Position.Z > centre.Z + radius ? null
                    : Bodies.Distance2D(centre, t.Position) is var h && h <= radius + t.Radius ? h : null,
                CollideShape.Box => BoxHit(t, centre, radius) ? 0f : null,
                _ => null,
            };
            if (d is not { } dist) continue;
            if (found.Count < MaxResults) { found.Add(new InReach(t, dist)); continue; }
            int far = -1;
            for (int i = 0; i < found.Count; i++)
                if (found[i].Distance > dist && (far < 0 || found[i].Distance > found[far].Distance)) far = i;
            if (far >= 0) found[far] = new InReach(t, dist);
        }
        return found;
    }

    /// <summary>A sphere against a thing's turned box, axis by axis.</summary>
    static bool BoxHit(WorldThing t, Vector3 c, float r)
    {
        var m = t.Rotation;
        var rel = c - t.Position;
        Span<Vector3> axes = [new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33)];
        Span<float> lo = [t.BoxMin.X, t.BoxMin.Y, t.BoxMin.Z];
        Span<float> hi = [t.BoxMax.X, t.BoxMax.Y, t.BoxMax.Z];
        for (int i = 0; i < 3; i++)
        {
            float p = Vector3.Dot(rel, axes[i]);
            if (p < lo[i] - r || p > hi[i] + r) return false;
        }
        return true;
    }
}
