// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Explosions** (thing type 10) and the **dynamic lights** they flash, end to end against the
/// shipped data: a fireball's `.nex` timeline — what exists at which
/// millisecond, and the light's ramp — a grenade's blow (who is hit, how hard at which distance,
/// and that the kill is the thrower's), the muzzle flash a gun makes, a projectile detonating into
/// its explosion, and the torches' flickering lights laying their blobs on the monastery's walls.
/// The numbers are the original's explosion, its damage, its light and its decals'.
/// </summary>
public class ExplosionTests
{
    static Location Monastery(Vitomir.Engine.Vfs.GameFiles files) =>
        Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");

    /// <summary>Steps the simulation 10 ms at a time until the clock reaches <paramref name="ms"/>.</summary>
    static void RunTo(Simulation sim, int ms)
    {
        while (sim.Milliseconds < ms) sim.Step(0.01f);
    }

    // ---- the light's ramp ------------------------------------------------------------------------

    /// <summary>
    /// **A ramp**: the rate is fixed when it starts —
    /// <c>(target − now) / ms</c> — each frame adds frame-ms × rate while the clock is before the
    /// end, and the frame the clock reaches the end puts the value on the target. The frame it
    /// starts in already steps it, so a ramp of 200 ms in 16 ms frames overshoots by half a frame's
    /// worth before it snaps. A ramp to where it already is starts nothing; a second ramp on the same
    /// field runs beside the first.
    /// </summary>
    [Fact]
    public void A_ramp_steps_by_the_frame_and_snaps_to_its_target_at_the_end()
    {
        var ramps = new ValueRamps();
        var thing = new WorldThing { Index = -1, Guid = 1, Scene = new NsdThing(), Template = "t" };
        var light = DynamicLight.Allocate(blob: false);
        light.Current = 150f;
        ramps.Now = 1000;
        Assert.NotEqual(0, ramps.Start(thing, light, RampField.Intensity, 200f, 200));
        Assert.True((thing.Flags & WorldThing.Flag.Animating) != 0);
        int now = 1000;
        var seen = new List<float>();
        for (int f = 0; f < 14; f++)
        {
            ramps.Step(now, 16);
            seen.Add(light.Current);
            now += 16;
        }
        // 50 over 200 ms is 0.25 a millisecond, 4 a frame: 154, 158, … while 1000 + 16k < 1200.
        Assert.Equal(154f, seen[0], 3);
        Assert.Equal(150f + 4f * 13, seen[12], 3);                     // the frame at 1192, still before the end: 202
        Assert.Equal(200f, seen[13]);                                  // 1208: past the end, on the target
        Assert.Equal(0, ramps.Running);

        Assert.Equal(0, ramps.Start(thing, light, RampField.Intensity, 200f, 500));   // already there
        ramps.Start(thing, light, RampField.Intensity, 100f, 100);
        ramps.Start(thing, light, RampField.Intensity, 300f, 100);
        Assert.Equal(2, ramps.Running);
        ramps.Step(now, 10);                                            // −1 and +1 a ms: they cancel
        Assert.Equal(200f, light.Current, 3);
    }

    /// <summary>
    /// **A colour ramp's "already there" test reads the wrong words**: red against red,
    /// then this frame's intensity against green and the spot direction's Y (always 0) against blue.
    /// A light whose red and intensity happen to match the target's red and green, ramping to a
    /// blue of 0, does not ramp at all.
    /// </summary>
    [Fact]
    public void A_colour_ramp_that_looks_already_there_by_the_wrong_words_is_refused()
    {
        var ramps = new ValueRamps { Now = 0 };
        var thing = new WorldThing { Index = -1, Guid = 1, Scene = new NsdThing(), Template = "t" };
        var light = DynamicLight.Allocate(blob: false);
        light.Color = new Vector3(128f, 50f, 50f);
        light.Intensity = 0f;
        Assert.Equal(0, ramps.StartColor(thing, light, new Vector3(128f, 0f, 0f), 1000));   // "already there"
        Assert.Equal(new Vector3(128f, 50f, 50f), light.Color);
        light.Intensity = 10f;
        Assert.NotEqual(0, ramps.StartColor(thing, light, new Vector3(128f, 0f, 0f), 1000));
    }

    // ---- lights from templates -----------------------------------------------------------------

    /// <summary>
    /// **A template's light**: `torchFlame` — the torch the Old Town
    /// beggar and the knights carry — is 270 strong, range 270 and falloff 1 (no `DYNA_RADIUS`),
    /// torch-coloured, on light style 1 (the low-turbulence flicker); `exp_fireball`'s radius of 100
    /// makes its falloff 150 / 100. Both are on the moment they are made.
    /// </summary>
    [Fact]
    public void A_torch_and_a_fireball_carry_the_lights_their_templates_describe()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var lights = new DynamicLights();
        var torch = new WorldThing { Index = -1, Guid = 1, Scene = NsdThing.Placed("torchFlame", Vector3.Zero, Vector3.Zero, 0), Template = "torchFlame" };
        lights.Apply(torch, templates.Resolve("torchFlame"));
        var t = lights.Of(torch)!;
        Assert.Equal(270f, t.Current);
        Assert.Equal(270f, t.Radius);
        Assert.Equal(1f, t.Falloff);
        Assert.Equal(new Vector3(239f, 176f, 29f), t.Color);
        Assert.Equal(1, t.Style);
        Assert.Equal("", t.Material);
        Assert.True((torch.Flags & WorldThing.Flag.DynaLight) != 0);

        var blast = new WorldThing { Index = -1, Guid = 2, Scene = NsdThing.Placed("exp_fireball", Vector3.Zero, Vector3.Zero, 0), Template = "exp_fireball" };
        lights.Apply(blast, templates.Resolve("exp_fireball"));
        var f = lights.Of(blast)!;
        Assert.Equal(150f, f.Current);
        Assert.Equal(100f, f.Radius);
        Assert.Equal(1.5f, f.Falloff);
        Assert.Equal(new Vector3(224f, 194f, 27f), f.Color);
    }

    /// <summary>
    /// **What a script's `AllocateDynamicLight` makes** (the light's set-up with its argument set): 200 strong,
    /// range 200, white, no bone, the `lightblob` material, and a falloff of **0** — the template path
    /// is the only one that writes the 1. A second allocation is refused; clearing takes the light
    /// and the thing's flag away.
    /// </summary>
    [Fact]
    public void A_scripts_light_is_two_hundred_white_and_does_not_fall_off()
    {
        var lights = new DynamicLights();
        var thing = new WorldThing { Index = -1, Guid = 1, Scene = new NsdThing(), Template = "t" };
        Assert.True(lights.Allocate(thing, blob: true));
        Assert.False(lights.Allocate(thing, blob: true));
        var l = lights.Of(thing)!;
        Assert.Equal((200f, 200f, 200f), (l.Intensity, l.Current, l.Radius));
        Assert.Equal(new Vector3(255f), l.Color);
        Assert.Equal(-1, l.Bone);
        Assert.Equal(0f, l.Falloff);
        Assert.Equal("lightblob", l.Material);
        Assert.True(lights.Clear(thing));
        Assert.Null(lights.Of(thing));
        Assert.True((thing.Flags & WorldThing.Flag.DynaLight) == 0);
    }

    /// <summary>
    /// **The blob's colour is packed without a clamp**: colour × (I − d) / 392 a channel,
    /// red keeping its low byte, green and blue ORed in shifted — so a white 600 light at a wall 10
    /// away (377 a channel) comes out a different colour, not white.
    /// </summary>
    [Fact]
    public void A_blobs_colour_is_the_light_over_392_and_overflows_rather_than_clamps()
    {
        var torch = DynamicLight.Allocate(blob: false);
        torch.Color = new Vector3(239f, 176f, 29f);
        torch.Intensity = 270f;
        var near = new LightBlob(torch, 0, 0, Vector3.Zero, 70f);
        // 200 / 392 × (239, 176, 29) = (121.9, 89.8, 14.8)
        Assert.Equal(0xff7a5a0fu, DynamicLights.BlobColor(near));
        Assert.Equal(MathF.Sqrt(270f * 270f - 70f * 70f), DynamicLights.BlobRadius(near), 3);

        var flash = DynamicLight.Allocate(blob: false);
        flash.Intensity = 600f;
        var hot = new LightBlob(flash, 0, 0, Vector3.Zero, 10f);
        uint c = DynamicLights.BlobColor(hot);                       // 590 × 255 / 392 = 383.8 → 384 = 0x180
        Assert.Equal(0xff800000u | (0x180u << 8) | 0x180u, c);
        Assert.NotEqual(0xffffffffu, c);
    }

    // ---- the explosions ----------------------------------------------------------------------------

    /// <summary>
    /// **The fireball's timeline** (`fireball.nex` through the explosion's step): made at a point,
    /// at 1 ms its light goes on and `incendExplosion` appears 5 units below it (the offset
    /// <c>0 0 −1</c> normalised, times 5) for 750 ms; from 120 ms the light climbs to 200 over 200 ms;
    /// from 430 it sinks to 10 over two seconds while it reddens; at 2450 it goes off and at 2500 the
    /// explosion removes itself. Nothing is hurt: a fireball's harm is its projectile's, and
    /// `fireball.nex` has no SETDAMAGE.
    /// </summary>
    [Fact]
    public void The_fireball_runs_its_script_by_the_millisecond()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        RunTo(sim, 1000);
        var at = place.Player!.Thing.Position + new Vector3(0f, 0f, 60f);
        int start = sim.Milliseconds;
        var blast = place.Place("exp_fireball", at, new Vector3(0f, 0f, 45f), place.Player.Thing.Sector)!;
        Assert.Equal(ThingType.Explosion, blast.Type);
        var e = place.Explosions.Of(blast)!;
        Assert.Equal(start, e.StartMs);
        Assert.Equal(Vector3.Zero, blast.Orientation);               // WORLD ORIENT (0x81)
        Assert.Equal(7, e.Script!.Events.Count);
        var light = place.Lights.Of(blast)!;

        WorldThing? flame = null;
        int flameExpires = 0;
        float peak = 0f;
        int offAt = -1, goneAt = -1;
        while (sim.Milliseconds < start + 2600)
        {
            sim.Step(0.01f);
            int t = sim.Milliseconds - start;
            if (flame is null && place.Things.FirstOrDefault(x => x.Template.Equals("incendExplosion", StringComparison.OrdinalIgnoreCase)) is { } made)
                (flame, flameExpires) = (made, made.ExpireAt);
            if (t < 120) Assert.Equal(150f, light.Current);
            if (t is >= 320 and < 430) peak = MathF.Max(peak, light.Current);
            if (offAt < 0 && t > 1 && (blast.Flags & WorldThing.Flag.DynaLight) == 0) offAt = t;
            if (goneAt < 0 && blast.Removed) goneAt = t;
        }
        Assert.NotNull(flame);
        Assert.Equal(at + new Vector3(0f, 0f, -5f), flame!.Position);
        Assert.InRange(flameExpires - start, 751, 761);            // made at the first tick at or past 1 ms, for 750
        Assert.Equal(200f, peak);
        Assert.InRange(offAt, 2450, 2460);
        Assert.InRange(goneAt, 2500, 2510);
        Assert.Equal(10f, light.Current);
        Assert.Equal(new Vector3(128f, 0f, 0f), light.Color);
        Assert.Equal(0, e.MaxDamage);                                 // never set, so never dealt
    }

    /// <summary>
    /// **A grenade's blow** (`grenadeEXP.nex`: SETDAMAGE 30 1 0 4 128 at 120 ms, APPLYDAMAGE at
    /// 125): everything within 128 is hit with <c>30 + (31 − 30) × (128 − d) / 128</c> fire — the
    /// thrower too, since `exp_grenade` carries no `NO DAMAGE TO SELF` — and nothing further out;
    /// the kill is the thrower's (the damage is handed the owner looked up by GUID).
    /// </summary>
    [Fact]
    public void A_grenade_hits_what_is_in_reach_by_distance_and_the_kill_is_the_throwers()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        RunTo(sim, 500);
        var christof = place.Player!.Thing;
        var others = place.Things.Where(t => t.Type == ThingType.Actor && t.Actor is { Dead: false }
                                             && t.Collide == CollideShape.Cylinder).Take(3).ToList();
        Assert.Equal(3, others.Count);
        var centre = christof.Position + new Vector3(0f, 0f, 20f);
        place.Move(others[0], christof.Position + new Vector3(40f, 0f, 0f));
        place.Move(others[1], christof.Position + new Vector3(0f, 100f, 0f));
        place.Move(others[2], christof.Position + new Vector3(0f, -400f, 0f));
        others[1].Actor!.Health = Combat.DeathThreshold + 0.5f;      // this one will not survive it
        WorldThing? killer = null, killed = null;
        place.Killed = (t, k) => { killed = t; killer = k; return true; };

        var blast = place.Place("exp_grenade", centre, Vector3.Zero, christof.Sector)!;
        blast.SpawnedBy = christof.Guid;
        int start = sim.Milliseconds;
        RunTo(sim, start + 130);
        var hits = ExplosionDamage.Last;
        Assert.Contains(hits, h => ReferenceEquals(h.Target, christof));
        var near = hits.Single(h => ReferenceEquals(h.Target, others[0]));
        var far = hits.Single(h => ReferenceEquals(h.Target, others[1]));
        Assert.DoesNotContain(hits, h => ReferenceEquals(h.Target, others[2]));
        Assert.Equal(40f, near.Distance, 2);
        Assert.Equal(30f + (128f - 40f) / 128f, near.Amount, 3);
        Assert.Equal(30f + (128f - 100f) / 128f, far.Amount, 3);
        Assert.Equal(31f, hits.Single(h => ReferenceEquals(h.Target, christof)).Amount, 3);
        Assert.Same(others[1], killed);
        Assert.Same(christof, killer);
        Assert.Equal(DamageType.Fire, (DamageType)place.Explosions.Of(blast)!.DamageType);
    }

    /// <summary>
    /// **An explosion's line of sight is the engine's sweep** (things and
    /// surfaces, the type mask <c>0xfffffffb</c>). A wall within the length stops it and one beyond
    /// does not; a character standing on the line is not met at all, the mask leaving out all but
    /// props; a prop flagged `CAMERA BLOCK` stops it, and the same prop as the target clears it. In
    /// 1.1 the blow still lands through the wall, by the second ray of no length; the
    /// repair `blastsight` sends that ray to the top of the target, and the wall stops it.
    /// </summary>
    [Fact]
    public void An_explosion_s_line_is_the_sweep_stopped_by_walls_and_blind_to_characters()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var blast = place.Place("exp_grenade", christof.Position + new Vector3(0f, 0f, 20f), Vector3.Zero, christof.Sector)!;
        // The nearest wall straight out from the blast, level, somewhere round it.
        var (toward, wall) = Enumerable.Range(0, 16)
            .Select(k => new Vector3(MathF.Cos(k * MathF.PI / 8f), MathF.Sin(k * MathF.PI / 8f), 0f))
            .Select(d => (d, hit: WorldSweep.FirstSurface(place.Level, (int)blast.Sector, blast.Position, d, 400f)))
            .Where(p => p.hit is { Distance: > 40f })
            .Select(p => (p.d, p.hit!.Value)).First();
        var stranger = place.Things.First(t => t.Type == ThingType.Actor && t.Collide == CollideShape.Cylinder);

        Assert.True(ExplosionDamage.Clear(place, blast, toward, wall.Distance - 1f, stranger));
        Assert.False(ExplosionDamage.Clear(place, blast, toward, wall.Distance + 20f, stranger));

        // A character on the line is passed over: it is never among the sweep's results.
        place.Move(stranger, blast.Position + toward * (wall.Distance * 0.5f) - new Vector3(0f, 0f, 20f));
        Assert.True(ExplosionDamage.Clear(place, blast, toward, wall.Distance - 1f, christof));

        // A prop on the line stops it when it blocks cameras, and is itself a clear target.
        var prop = place.Things.First(t => t.Type == ThingType.Prop && t.Collide == CollideShape.Cylinder && t.Height > 30f
                                          && !t.Has(WorldThing.Flag.DisableCollide) && !t.Has(WorldThing.Flag.CameraBlock));
        place.Move(prop, blast.Position + toward * (wall.Distance * 0.5f) - new Vector3(0f, 0f, 20f));
        Assert.True(ExplosionDamage.Clear(place, blast, toward, wall.Distance - 1f, christof));
        prop.Flags |= WorldThing.Flag.CameraBlock;
        Assert.False(ExplosionDamage.Clear(place, blast, toward, wall.Distance - 1f, christof));
        Assert.True(ExplosionDamage.Clear(place, blast, toward, wall.Distance - 1f, prop));
        prop.Flags &= ~WorldThing.Flag.CameraBlock;

        // Through the wall the first ray fails and 1.1's second, of no length, passes — the repair
        // `blastsight` sends the second to the top of the target, and the wall stops that too.
        place.Move(stranger, blast.Position + toward * (wall.Distance + 20f) - new Vector3(0f, 0f, 20f));
        Assert.True(ExplosionDamage.SeesAsWritten(place, blast, stranger, wall.Distance + 20f));
        Assert.False(ExplosionDamage.Sees(place, blast, stranger, wall.Distance + 20f));
        // And somebody on this side of it is seen as before.
        place.Move(stranger, blast.Position + toward * (wall.Distance * 0.5f) - new Vector3(0f, 0f, 20f));
        Assert.True(ExplosionDamage.Sees(place, blast, stranger, wall.Distance * 0.5f));
    }

    /// <summary>
    /// **A prop's scripts are told**: `damaged` with the prop, the
    /// explosion's owner, the damage by distance and the explosion's damage type.
    /// </summary>
    [Fact]
    public void A_grenade_tells_a_prop_s_scripts_it_was_damaged()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        RunTo(sim, 500);
        var christof = place.Player!.Thing;
        var prop = place.Things.First(t => t.Type == ThingType.Prop && t.Collide != CollideShape.None && !t.Removed);
        place.Move(prop, christof.Position + new Vector3(0f, 60f, 0f));
        var told = new List<(WorldThing Thing, WorldThing? By, float Damage, int Type)>();
        place.ItemScripts = new Ground.Scripts(Damaged: (t, by, d, type) => { told.Add((t, by, d, type)); return d; });

        var blast = place.Place("exp_grenade", christof.Position + new Vector3(0f, 0f, 20f), Vector3.Zero, christof.Sector)!;
        blast.SpawnedBy = christof.Guid;
        RunTo(sim, sim.Milliseconds + 130);
        var hit = ExplosionDamage.Last.SingleOrDefault(h => ReferenceEquals(h.Target, prop));
        Assert.NotNull(hit.Target);
        var said = Assert.Single(told, x => ReferenceEquals(x.Thing, prop));
        Assert.Same(christof, said.By);
        Assert.Equal(hit.Amount, said.Damage, 3);
        Assert.Equal((int)DamageType.Fire, said.Type);
    }

    /// <summary>
    /// **`NO DAMAGE TO SELF` spares the owner, `NO DAMAGE TO TEAM` his friends**
    /// (A holy-water burst (0xf3) thrown by Christof hits neither him nor anyone he counts
    /// as friendly, and does hit the rest.
    /// </summary>
    [Fact]
    public void Holy_water_spares_its_thrower_and_his_friends()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var christof = place.Player!.Thing;
        var stranger = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is { Dead: false }
                                               && !Sight.Friendly(christof, t) && t.Collide == CollideShape.Cylinder);
        Assert.False(ExplosionDamage.Spared(0xf3, christof, null));   // a party member is not spared by 0xf3 without an owner to be friends with
        Assert.True(ExplosionDamage.Spared(0x4, christof, null));     // NO DAMAGE TO PLAYERS
        Assert.False(ExplosionDamage.Spared(0xf3, stranger, christof));
        var friend = place.Things.FirstOrDefault(t => t.Type == ThingType.Actor && t.Actor is { Dead: false } && Sight.Friendly(christof, t));
        if (friend is not null) Assert.True(ExplosionDamage.Spared(0xf3, friend, christof));
        Assert.True(ExplosionDamage.Spared(0x40, new WorldThing { Index = -1, Guid = 5, Scene = new NsdThing(), Template = "p", Type = ThingType.Prop }, null));
        Assert.True(ExplosionDamage.Spared(0, new WorldThing { Index = -1, Guid = 6, Scene = new NsdThing(), Template = "i", Type = ThingType.Item }, null));
    }

    /// <summary>
    /// **A muzzle flash**: the attack's `WEAPON_MUZZLEFLASH` made at the muzzle with the
    /// shooter's orientation, flagged `NO NET SYNCH`; `exp_muzzleMagnum` has no `WORLD ORIENT`, so it
    /// keeps the shooter's facing; its light rises to 150 and is gone with it at 225 ms.
    /// </summary>
    [Fact]
    public void A_muzzle_flash_is_a_quarter_second_explosion_facing_the_shooters_way()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        RunTo(sim, 200);
        var christof = place.Player!.Thing;
        christof.Orientation = new Vector3(0f, 0f, 30f);
        var muzzle = christof.Position + new Vector3(0f, 20f, 60f);
        var flash = ExplosionSystem.MuzzleFlash(place, christof, "exp_muzzleMagnum", muzzle)!;
        Assert.Equal(ThingType.Explosion, flash.Type);
        Assert.Equal(christof.Orientation, flash.Orientation);
        Assert.True((flash.Flags & WorldThing.Flag.NoNetSync) != 0);
        Assert.Equal(0, flash.SpawnedBy);
        int start = sim.Milliseconds;
        float peak = 0f;
        var light = place.Lights.Of(flash)!;
        while (!flash.Removed && sim.Milliseconds < start + 400) { sim.Step(0.01f); peak = MathF.Max(peak, light.Current); }
        Assert.True(flash.Removed);
        Assert.InRange(sim.Milliseconds - start, 225, 235);
        Assert.Equal(150f, peak);
    }

    /// <summary>
    /// **A fireball detonates into its explosion**, owned by the caster: `proj_fireball` fired from
    /// Christof at the wall in front of him bursts on it (`PROJ_EXPLODE_ONWALL`), and `exp_fireball`
    /// begins where it struck with Christof as its owner.
    /// </summary>
    [Fact]
    public void A_fireball_bursts_into_an_explosion_owned_by_its_caster()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        RunTo(sim, 200);
        var christof = place.Player!.Thing;
        var from = Projectiles.Muzzle(christof, new Vector3(0f, 30f, 0f));
        // Any direction with a wall within a few hundred units.
        Vector3 dir = Vector3.UnitY;
        for (int a = 0; a < 360; a += 15)
        {
            var d = new Vector3(-MathF.Sin(a * MathF.PI / 180f), MathF.Cos(a * MathF.PI / 180f), 0f);
            if (Pick.Nearest(place.Level, from, d, 600f) is { } hit && hit.Distance > 60f) { dir = d; break; }
        }
        var shot = Projectiles.Fire(place, christof, "proj_fireball", from, Projectiles.Facing(dir), christof.Guid);
        Assert.NotNull(shot);
        WorldThing? blast = null;
        for (int f = 0; f < 300 && blast is null; f++)
        {
            sim.Step(0.01f);
            blast = place.Things.FirstOrDefault(t => t.Type == ThingType.Explosion);
        }
        Assert.NotNull(blast);
        Assert.Equal("exp_fireball", blast!.Template, ignoreCase: true);
        Assert.Equal(christof.Guid, blast.SpawnedBy);
        Assert.NotNull(place.Explosions.Of(blast));
        Assert.True(shot!.Removed);
    }

    // ---- the walls -----------------------------------------------------------------------------------

    /// <summary>
    /// **A torch lights the walls round it**. No scene in the game places a thing with a
    /// dynamic light — the monastery's wall torches are `flametest` emitters and carry none — so
    /// every one comes in at run time: the torch a `TorchBearer` or the player's torch item makes
    /// (`torchFlame`), a discipline's glow, a fireball, an explosion. One made in the monastery has
    /// its light at the next step; lit and above 50, it lays blobs on the level surfaces within its
    /// intensity less 50 that it is in front of; and on style 1 it flickers — its intensity each
    /// frame is its set 270 times the style's brightness, which dips to 0.95 and moves.
    /// </summary>
    [Fact]
    public void A_torch_flickers_and_lays_its_blobs_on_the_walls_round_it()
    {
        using var files = Install.Open();
        var place = Monastery(files);
        var sim = new Simulation(place);
        sim.Step(0.03f);
        Assert.DoesNotContain(place.Lights.All, p => (p.Key.Flags & WorldThing.Flag.DynaLight) != 0);
        Assert.Empty(place.Lights.Blobs);

        var christof = place.Player!.Thing;
        var torch = place.Place("torchFlame", christof.Position + new Vector3(0f, 0f, 90f), Vector3.Zero, christof.Sector)!;
        sim.Step(0.03f);
        var light = place.Lights.Of(torch)!;
        Assert.Equal(torch.Position, light.Position);
        Assert.NotEmpty(place.Lights.Blobs);
        foreach (var b in place.Lights.Blobs)
        {
            Assert.Same(light, b.Light);
            Assert.InRange(b.Distance, DynamicLights.BehindTolerance, b.Light.Intensity - DynamicLights.BlobFloor);
            Assert.True(DynamicLights.BlobRadius(b) > 0f);
        }
        var seen = new HashSet<float>();
        for (int i = 0; i < 60; i++) { sim.Step(1f / 30f); seen.Add(light.Intensity); }
        Assert.True(seen.Count > 5, "a style-1 light should flicker");
        Assert.All(seen, v => Assert.InRange(v, 270f * 0.94f, 270f * 1.0001f));

        // Switched off (DYNAOFF, or a script clearing the flag), it lays nothing.
        torch.Flags &= ~WorldThing.Flag.DynaLight;
        sim.Step(0.03f);
        Assert.Empty(place.Lights.Blobs);
    }
}
