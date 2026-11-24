// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text;
using Vitomir.Engine.Particles;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The particle system: a `game.npd` block turned into numbers the way each
/// kind's constructor and parser turn it, a particle's position, size, colour and life after a few
/// frames worked out by hand from the update loops (gas, fire, flame), the
/// live list's rules (128 slots, 60 frames unseen, dying), and the emitters the monastery's torches
/// and candles are given as the level loads. The hand-worked cases zero every random spread, so the
/// numbers depend on the arithmetic alone; the fire's one random direction is drawn from the
/// engine's own <c>rand()</c> in the engine's order.
/// </summary>
public class ParticleTests
{
    static NpdFile Npd(string text) => NpdFile.Read(Encoding.Latin1.GetBytes(text));

    /// <summary>A gas that grows and sinks, one particle a second, no spread anywhere.</summary>
    static EmitterSettings Sinking() => new()
    {
        Kind = EmitterKind.Gas, Name = "test", StartSize = 10f, GrowthRate = 5f, Gravity = 100f,
        Lifespan = 1000, BirthRate = 1000, MaxParticles = 2, StartColor = 0xff804020, EndColor = 0,
    };

    [Fact]
    public void A_block_becomes_the_kinds_defaults_then_its_lines()
    {
        var npd = Npd("""
            fire candle
            material fireSprite
            lifespan 150
            birthrate 20
            startsize 2.0
            end
            gas smoke
            lifespan 2000
            birthrate 100
            maxparticles 7
            maxspeed 30
            minspeed 10
            drag 10
            end
            flame jet
            lifespan 1000
            birthrate 10
            velocity (0.0,400.0,-10.0)
            endsize 50
            end
            gas quick
            lifespan 50
            birthrate 100
            end
            """);
        var candle = EmitterSettings.From(npd["candle"]!);
        Assert.Equal(14u, candle.MaxParticles);                  // 150 / 20 × 2
        Assert.Equal(1000u, candle.LifeJitter);
        Assert.Equal(20u, candle.BirthJitter);
        Assert.Equal(-80f, candle.Gravity);
        Assert.Equal(200f, candle.Drag);
        Assert.Equal(0.25f, candle.SizeVariance);
        Assert.Equal(0u, candle.EndColor);                       // the gas constructor's
        Assert.Equal("fireSprite", candle.Material);

        var smoke = EmitterSettings.From(npd["smoke"]!);
        Assert.Equal(40u, smoke.MaxParticles);                   // a gas has no maxparticles case
        Assert.Equal((10f, 10f), (smoke.MinSpeed, smoke.MaxSpeed));   // minspeed writes both, last
        Assert.Equal(0f, smoke.Gravity);

        var jet = EmitterSettings.From(npd["jet"]!);
        Assert.Equal(new Vector3(0, 400, -10), jet.Velocity);
        Assert.Equal(0u, jet.LifeJitter);                        // the constructor zeroes it
        Assert.Equal(200u, jet.MaxParticles);
        Assert.Equal(50f, jet.EndSize);

        Assert.Equal(2u, EmitterSettings.From(npd["quick"]!).MaxParticles);   // the gas parser's floor

        var sparks = EmitterSettings.From(Npd("sparks s\nmaxparticles 150\nminbirthrate 50\nmaxbirthrate 500\nstartsize 1.0\ncount 15\nstartcolormin 0x80808000\nvelocity (0.0,0.0,200.0)\nvelocityvar 120.0\ngravity 280.0\nend\n")["s"]!);
        Assert.Equal((150u, 50u, 500u, 15), (sparks.MaxParticles, sparks.MinBirthRate, sparks.MaxBirthRate, sparks.Count));
        Assert.Equal((1f, 1f), (sparks.MinStartSize, sparks.MaxStartSize));
        Assert.Equal((new Vector3(0, 0, 200), 120f, 280f), (sparks.Velocity, sparks.VelocityVariance, sparks.Gravity));
        Assert.Equal(0x80808000u, sparks.StartColorMin);
    }

    /// <summary>
    /// One gas particle over three frames of 0.1 s. It is due at 0 ms and born in the first frame,
    /// 100 ms late, so it starts 0.1 s old (life 0.9); the same frame's update then ages it again.
    /// Each frame gravity adds −10 to its fall, it moves by its new velocity, grows by 0.5 and
    /// loses 0.1 of its life.
    /// </summary>
    [Fact]
    public void A_gas_particle_falls_grows_and_fades_by_the_numbers()
    {
        var ps = new ParticleSystem(Npd(""));
        var e = ps.Make(Sinking());
        ps.Touch(e);
        ps.Step(0.1f, 100);
        ref var p = ref e.Particles[0];
        Assert.Equal(new Vector3(0, 0, -1f), p.Position);
        Assert.Equal(10.5f, p.Size);
        Assert.Equal(0.8f, p.Life, 5);
        ps.Step(0.1f, 200);
        ps.Step(0.1f, 300);
        Assert.Equal(new Vector3(0, 0, -30f), p.Velocity);
        Assert.Equal(-6f, p.Position.Z, 4);
        Assert.Equal(11.5f, p.Size);
        Assert.Equal(0.6f, p.Life, 5);
        Assert.Equal(1000u, e.NextBirth);                        // one a second: none since
        Assert.False(e.Particles[1].Life > 0f);
        // Drawn at its size, in its colour: each byte 0.6 of the way from end to start, rounded.
        var sprite = Assert.Single(ps.Sprites);
        Assert.Equal(11.5f, sprite.Size);
        Assert.Equal(0x994D2613u, sprite.Color);
    }

    /// <summary>
    /// Drag scales the velocity by 1 − dt × drag × 0.001 for a gas: with drag 100 and dt 0.1 that
    /// is 0.99 a frame, applied after gravity's push.
    /// </summary>
    [Fact]
    public void Gas_drag_is_a_thousandth_of_the_drag_a_second()
    {
        var ps = new ParticleSystem(Npd(""));
        var s = Sinking();
        s.Drag = 100f;
        var e = ps.Make(s);
        ps.Touch(e);
        ps.Step(0.1f, 100);
        Assert.Equal(-10f * 0.99f, e.Particles[0].Velocity.Z, 5);
        ps.Step(0.1f, 200);
        Assert.Equal((-9.9f - 10f) * 0.99f, e.Particles[0].Velocity.Z, 4);
    }

    /// <summary>
    /// A fire particle two units out from where it was born, standing still: each frame its
    /// sideways velocity loses (position − home) × dt — the pull back into the column, worked out
    /// from the position it has just moved to — and its vertical velocity is left alone. It is
    /// drawn at its size × its life.
    /// </summary>
    [Fact]
    public void A_fire_particle_is_pulled_back_to_its_column()
    {
        var ps = new ParticleSystem(Npd(""));
        var s = EmitterSettings.Defaults(EmitterKind.Fire, "column");
        (s.Gravity, s.Drag, s.MaxParticles) = (0f, 0f, 2);
        var f = (FireEmitter)ps.Make(s);
        f.Particles[0] = new Particle { Position = new Vector3(2, -1, 5), Home = Vector2.Zero, Size = 4f, Life = 1f, LifeRate = 1f };
        f.NextBirth = uint.MaxValue;                              // no births: only the update runs
        ps.Touch(f);
        ps.Step(0.1f, 100);
        ref var q = ref f.Particles[0];
        Assert.Equal(new Vector3(2, -1, 5), q.Position);
        Assert.Equal(-0.2f, q.Velocity.X, 6);
        Assert.Equal(0.1f, q.Velocity.Y, 6);
        Assert.Equal(0f, q.Velocity.Z);
        ps.Step(0.1f, 200);
        Assert.Equal(1.98f, q.Position.X, 5);
        Assert.Equal(-0.99f, q.Position.Y, 5);
        Assert.Equal(5f, q.Position.Z);
        Assert.Equal(-0.2f - 1.98f * 0.1f, q.Velocity.X, 5);
        Assert.Equal(0.8f, q.Life, 5);
        Assert.Equal(4f * q.Life, ps.Sprites[0].Size, 5);

        // Fire's own defaults: rising at 80 and damped by 1 − dt × 200 × 0.01 — a third of the
        // velocity gone in a sixtieth of a second.
        var d = EmitterSettings.Defaults(EmitterKind.Fire, "hot");
        Assert.Equal((-80f, 200f), (d.Gravity, d.Drag));
    }

    /// <summary>
    /// A flame launches along its block's velocity turned into the thing's frame and
    /// grows from its start size to its end size as it dies — the particle's own size
    /// is drawn and never used.
    /// </summary>
    [Fact]
    public void A_flame_launches_along_its_things_heading_and_grows_as_it_dies()
    {
        var ps = new ParticleSystem(Npd(""));
        var s = EmitterSettings.Defaults(EmitterKind.Flame, "jet");
        (s.Velocity, s.VelocityVariance, s.Gravity, s.Drag) = (new Vector3(0, 100, 0), 0f, 0f, 0f);
        (s.StartSize, s.EndSize, s.Lifespan, s.BirthRate, s.BirthJitter, s.MaxParticles) = (2f, 10f, 1000, 1000, 0, 2);
        var e = (FlameEmitter)ps.Make(s);
        // Turned a quarter to the left: its +Y (forward) is the world's −X.
        (e.AxisX, e.AxisY, e.AxisZ) = (Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ);
        e.Particles[0] = new Particle { Life = 1f, LifeRate = 1f };
        var v = e.Settings.Velocity;
        var turned = new Vector3(e.AxisZ.X * v.Z + e.AxisY.X * v.Y + e.AxisX.X * v.X,
                                 e.AxisZ.Y * v.Z + e.AxisY.Y * v.Y + e.AxisX.Y * v.X,
                                 e.AxisZ.Z * v.Z + e.AxisY.Z * v.Y + e.AxisX.Z * v.X);
        Assert.Equal(new Vector3(-100, 0, 0), turned);
        e.Particles[0].Velocity = turned;
        e.NextBirth = uint.MaxValue;
        ps.Touch(e);
        ps.Step(0.25f, 250);
        Assert.Equal(new Vector3(-25, 0, 0), e.Particles[0].Position);
        Assert.Equal(0.75f, e.Particles[0].Life);
        Assert.Equal((2f - 10f) * 0.75f + 10f, ps.Sprites[0].Size);
    }

    /// <summary>
    /// Sparks come in bursts (here one spark a burst, a burst every 100 ms), flung at their velocity,
    /// falling by gravity, and drawn in their own colour and size until their time is up — no fade.
    /// A burst 100 ms late is aged 100 ms on the spot, as every kind's births are.
    /// </summary>
    [Fact]
    public void Sparks_burst_fall_and_stop_without_fading()
    {
        var ps = new ParticleSystem(Npd(""));
        var s = EmitterSettings.Defaults(EmitterKind.Sparks, "spark");
        (s.Count, s.MinBirthRate, s.MaxBirthRate, s.MaxParticles, s.Lifespan) = (0, 100, 101, 20, 1000);
        (s.Velocity, s.VelocityVariance, s.Gravity, s.MinStartSize, s.MaxStartSize) = (new Vector3(0, 0, 100), 0f, 100f, 2f, 2f);
        (s.StartColorMin, s.StartColorMax) = (0x80ff8000, 0x80ff8000);
        var e = Assert.IsType<SparksEmitter>(ps.Make(s));
        ps.Touch(e);
        ps.Step(0.1f, 100);
        Assert.Equal(1100u, e.Sparks[0].Death);
        Assert.Equal(new Vector3(0, 0, 90), e.Sparks[0].Velocity);
        Assert.Equal(19f, e.Sparks[0].Position.Z, 4);
        ps.Step(0.1f, 200);
        Assert.Equal(27f, e.Sparks[0].Position.Z, 4);
        Assert.Equal(19f, e.Sparks[1].Position.Z, 4);
        Assert.Equal(2, ps.Sprites.Count);
        Assert.All(ps.Sprites, sp => Assert.Equal((0x80ff8000u, 2f), (sp.Color, sp.Size)));
        // A long frame: the ten bursts due from 200 to 1100 all come now, and each spark's time runs
        // from **now**, not from when its burst was due (lifespan + the clock).
        ps.Step(0.1f, 1150);
        Assert.Equal(2150u, e.Sparks[2].Death);
        Assert.Equal(2150u, e.Sparks[11].Death);
        Assert.Equal(11, ps.Sprites.Count);                        // the first died at 1100
    }

    [Fact]
    public void A_colour_is_rounded_half_to_even_byte_by_byte()
    {
        Assert.Equal(0x78804000u, Emitter.Blend(0xc0ff8000, 0x30000000, 0.5f));
        Assert.Equal(0xc0ff8000u, Emitter.Blend(0xc0ff8000, 0x30000000, 1f));
        Assert.Equal(0x30000000u, Emitter.Blend(0xc0ff8000, 0x30000000, 0f));
    }

    /// <summary>
    /// The frame an emitter goes live in has no time since its last update, so its first births —
    /// the backlog of one lifespan — are moved back by −∞ × a movement of zero and come out at NaN.
    /// 1.1 does the same arithmetic in the same frame, so it is kept.
    /// </summary>
    [Fact]
    public void The_first_births_of_a_new_emitter_are_never_seen()
    {
        var ps = new ParticleSystem(Npd(""));
        ps.Step(0.1f, 5000);
        var s = Sinking();
        (s.Lifespan, s.MaxParticles) = (2000, 4);
        var e = ps.Make(s);
        ps.Touch(e);                                               // live at 5000 ms
        ps.Step(0f, 5000);
        // The backlog is 2000 ms: births due at 3000 (dead on arrival) and 4000 (half its life left).
        Assert.Equal(0f, e.Particles[0].Life, 5);
        Assert.Equal(0.5f, e.Particles[1].Life, 5);
        Assert.True(float.IsNaN(e.Particles[1].Position.X));
    }

    [Fact]
    public void The_live_list_holds_128_and_forgets_what_goes_unseen_for_60_frames()
    {
        var ps = new ParticleSystem(Npd(""));
        var all = Enumerable.Range(0, 130).Select(_ => ps.Make(Sinking())).ToArray();
        foreach (var e in all) ps.Touch(e);
        Assert.Equal(128, all.Count(e => e.Registered));
        Assert.False(all[128].Registered);
        for (int f = 0; f < 60; f++) ps.Step(0.01f, 10 * (f + 1));
        Assert.True(all[0].Registered);                            // seen at frame 0; 60 frames on
        ps.Step(0.01f, 610);
        Assert.False(all[0].Registered);
        Assert.All(ps.Live, slot => Assert.Null(slot));
    }

    /// <summary>
    /// A destroyed thing's emitter: no more births, what is alive lives out its life, and
    /// then the emitter leaves the list for good.
    /// </summary>
    [Fact]
    public void A_dying_emitter_lets_its_particles_finish_then_goes()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_HAVN", sky: false);
        var thing = place.Place("gas", place.StartPosition, Vector3.Zero, place.Things[0].Sector)!;
        var ps = place.Particles;
        for (int i = 0; i < 30; i++) ps.Step(0.05f, 50 * (i + 1));
        var e = ps.Of(thing)!;                                     // the template's own PARTICLENAME
        Assert.Equal("gas", e.Name);
        Assert.True(e.Registered);
        Assert.Contains(e.Particles, p => p.Life > 0f && float.IsFinite(p.Position.X));
        place.Remove(thing);
        ps.Step(0.05f, 1550);
        Assert.True(e.Dying);
        Assert.Null(e.Thing);
        Assert.Null(ps.Of(thing));
        uint next = e.NextBirth;
        for (int i = 0; i < 200 && ps.Live.Contains(e); i++) ps.Step(0.05f, 1600 + 50 * i);
        Assert.DoesNotContain(e, ps.Live);
        Assert.Equal(next, e.NextBirth);                           // not one birth since
    }

    /// <summary>
    /// An arrow's trail: one segment a frame at most, across the arrow 30 units behind it
    /// (<c>startpos (0,-30,-5)</c>, <c>endpos (0,-30,5)</c>), each fading over 350 ms, and each two
    /// consecutive live segments one quad of the ribbon, oldest first.
    /// </summary>
    [Fact]
    public void An_arrow_lays_a_ribbon_behind_it()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_HAVN", sky: false);
        var ps = place.Particles;
        var arrow = place.Things.First(t => ps.Of(t) is null && t.InWorld);
        var e = Assert.IsType<TrailEmitter>(ps.Attach(arrow, "arrowTrail"));
        Assert.Equal(70u, e.Settings.MaxParticles);               // 350 / 10 × 2
        Assert.Equal(TrailEmitter.FlagAlways, e.Flags);
        arrow.Orientation = Vector3.Zero;
        var start = arrow.Position;
        for (int f = 1; f <= 5; f++)
        {
            arrow.Position = start + new Vector3(0, 100 * f, 0);
            ps.Step(0.05f, 1000 + 50 * f);
        }
        var laid = e.Segments.Where(s => s.Laid).ToArray();
        Assert.Equal(5, laid.Length);                             // one a frame, though ten were due
        Assert.Equal(start + new Vector3(0, 500 - 30, -5), e.Segments[4].Start);
        Assert.Equal(start + new Vector3(0, 500 - 30, 5), e.Segments[4].End);
        // Each fades by dt / 0.35 s a frame, and the newest has had one frame of it.
        Assert.Equal(1f - 0.05f / 0.35f, e.Segments[4].Life, 5);
        var ribbon = ps.Batches.Single(b => b.Emitter == e);
        Assert.True(ribbon.Ribbon);
        Assert.Equal(4, ribbon.Count);
        var q = ps.Quads[ribbon.First + 3];                      // the newest quad
        Assert.Equal(e.Segments[3].Start, q.P0);
        Assert.Equal(e.Segments[4].End, q.P2);
        Assert.Equal(e.Segments[4].Life, q.V23);
    }

    /// <summary>
    /// The monastery as it loads: every torch, candle and brazier is an EMITTER-flagged thing whose
    /// template names a fire block, and every one of them has its emitter, made from the block, drawn
    /// in the fire sprite's own mode — additive.
    /// </summary>
    [Fact]
    public void The_monasterys_torches_and_candles_get_their_flames()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2");
        var ps = place.Particles;
        var byName = ps.All.GroupBy(e => e.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(65, byName["candleflame"]);
        Assert.Equal(34, byName["flametest"]);
        Assert.Equal(19, byName["flamemedium"]);
        Assert.Equal(8, byName["flametest2"]);
        var torch = ps.All.First(e => e.Name == "flametest");
        Assert.IsType<FireEmitter>(torch);
        Assert.Equal("flametest", torch.Thing!.Template, ignoreCase: true);
        Assert.Equal(500, torch.Particles.Length);
        Assert.Equal(-200f, torch.Settings.Gravity);
        var sprite = place.Materials.Items[torch.Material];
        Assert.EndsWith("fireSprite", sprite.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RenderKind.AddParticle, DrawPlan.ForParticle(sprite.Traits));

        var sim = new Simulation(place);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        // The 126 flames, and the blade trails hung on the three swords in the scene
        // — which lay nothing while nobody swings, so draw nothing. With every emitter
        // in the place counted as seen (no portal visibility yet) that is more than the 128 slots,
        // and the last trail gets none.
        int trails = place.Things.Count(x => x.Template.Equals("weapon_trail", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, trails);
        Assert.Equal(128, ps.Live.Count(e => e is not null));
        Assert.Equal(126, ps.Batches.Count);
        // Every torch's flames are where the torch is, rising.
        var t = ps.Batches.First(b => b.Emitter == torch);
        foreach (var s in ps.Sprites.Skip(t.First).Take(t.Count).Where(s => float.IsFinite(s.Position.X)))
        {
            Assert.InRange(Vector3.Distance(new Vector3(s.Position.X, s.Position.Y, 0), new Vector3(torch.Origin.X, torch.Origin.Y, 0)), 0f, 30f);
            Assert.InRange(s.Position.Z - torch.Origin.Z, -10f, 250f);
        }
    }
}
