// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text;
using Vitomir.Engine.Particles;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The particle kinds of the second wave: the spurt, the
/// rain, the shock ring and the pad — each
/// block read the way its own parser reads it, and a few frames worked out by hand from the update
/// loops with every random spread set to nothing, so the numbers depend on the arithmetic alone.
/// The spurt's unsigned backlog test, which turns a drop every quarter second into a stream, is
/// pinned frame by frame.
/// </summary>
public class ParticleKindTests
{
    static NpdFile Npd(string text) => NpdFile.Read(Encoding.Latin1.GetBytes(text));

    static WorldThing Thing(Vector3 at, float yaw = 0f) => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0), Template = "test",
        Position = at, Orientation = new Vector3(0, 0, yaw),
    };

    const string Blocks = """
        spurt jet
        lifespan 1000
        numgroups 1
        maxparticles 100
        startcolor 0xffc00000
        speed 100
        velocity (1.0,0.0,0.0)
        gravity 100
        velocityvar 0
        birthrate 250
        startsize 2
        mingroup 100000
        maxgroup 100000
        end
        spurt lowonly
        lifespan 50
        numgroups 3
        minspeed 20
        maxspeed 80
        minspeed 30
        end
        rain wet
        startsize 100
        maxparticles 2
        velocity (0.0,0.0,-100.0)
        width 3
        startcolor 0xa0505050
        birthrate 100
        end
        shock wave
        lifespan 1000
        startsize 20
        growthrate 300
        startcolor 0xff2c5b95
        endcolor 0x00000000
        velocity (0,0,150)
        rotvel (10,20,30)
        count 1
        birthrate 200
        end
        magic swirl
        lifespan 1000
        birthrate 1000
        maxparticles 4
        startsize 14
        endsize 4
        startcolor 0xffffffff
        endcolor 0x00800000
        minradius 10
        minspeed 90
        mingrowth 20
        velocity (0,0,10)
        end
        ring band
        lifespan 1000
        startsize 50
        growthrate -10
        startcolor 0xff000000
        endcolor 0x00000000
        startcolormax 0xffffffff
        startheight 20
        endheight 200
        texturerate 2.0
        count 1
        birthrate 1000
        numsegs 8
        end
        beam zap
        subtype noise
        lifespan 1000
        birthrate 1000
        updaterate 10
        count 2
        startsize 10
        endsize 10
        startcolor 0xffffffff
        endcolor 0xff000040
        turbulence 0
        texturerate 0
        size 25
        end
        trail blade
        startpos (0,20,0)
        endpos (0,100,0)
        lifespan 150
        birthrate 10
        end
        liquid squirt
        lifespan 1000
        birthrate 100
        startsize 2
        startcolor 0xff6060dc
        endcolor 0x80d0d0dc
        speed 100
        cohesion 1
        groupcohesion 1
        numgroups 2
        mingroup 3
        maxgroup 3
        end
        pad circle
        material ankhs
        startsize 40
        endsize 0.1
        startcolor 0xff870101
        endcolor 0x00000000
        count 1
        rotvel (0,90,0)
        end
        """;

    [Fact]
    public void Each_new_kind_reads_its_own_keys()
    {
        var npd = Npd(Blocks);
        var jet = EmitterSettings.From(npd["jet"]!);
        Assert.Equal((250f, 250f), (jet.MinBirthGap, jet.MaxBirthGap));       // two floats
        Assert.Equal(100u, jet.BirthRate);                                  // the base's, untouched
        Assert.Equal((2f, 2f), (jet.MinStartSize, jet.MaxStartSize));        // startsize writes both
        Assert.Equal((100f, 100f), (jet.MinSpeed, jet.MaxSpeed));            // speed writes both
        Assert.Equal((1u, 100000u, 100000u), (jet.NumGroups, jet.MinGroup, jet.MaxGroup));
        var low = EmitterSettings.From(npd["lowonly"]!);
        Assert.Equal((30f, 80f), (low.MinSpeed, low.MaxSpeed));              // minspeed writes the minimum only
        Assert.Equal(6u, low.MaxParticles);                                 // lifespan ≤ 100: numgroups × 2

        var wet = EmitterSettings.From(npd["wet"]!);
        Assert.Equal((100f, 2u, 3f, 0f), (wet.StartSize, wet.MaxParticles, wet.Width, wet.Length));
        var rain = EmitterSettings.Defaults(EmitterKind.Rain, "r");
        Assert.Equal((490u, 10000f, new Vector3(0, 0, -100)), (rain.MaxParticles, rain.StartSize, rain.Velocity));

        var wave = EmitterSettings.From(npd["wave"]!);
        Assert.Equal(new Vector3(10, 30, 20), wave.RotVel);                  // (x,y,z) stored as x, z, y
        Assert.Equal(20u, wave.MaxParticles);                               // none given: 20
        var circle = EmitterSettings.From(npd["circle"]!);
        Assert.Equal(new Vector3(0, 0, 90), circle.RotVel);                  // the file's (0,90,0) is a yaw
        Assert.Equal((1000u, 10f, 1), (circle.Lifespan, circle.GrowthRate, circle.Count));
    }

    /// <summary>
    /// A spurt with one jet, a drop due every 250 ms and a lifespan of 1000 ms. The first frame aims
    /// the jet; the second sheds the one drop due; the third finds the jet's next drop still in the
    /// future, which the unsigned test reads as a huge backlog — so it is put back a
    /// lifespan and four drops come at once, the first of them dead on arrival. Then one again.
    /// </summary>
    [Fact]
    public void A_spurt_sheds_a_lifespans_worth_nearly_every_frame()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var thing = Thing(new Vector3(10, 20, 30));
        var e = Assert.IsType<SpurtEmitter>(ps.Attach(thing, "jet"));
        ps.Touch(e);
        ps.Step(0.1f, 1000);
        var g = Assert.Single(e.Groups!);
        Assert.Equal(Vector3.UnitX, g.Direction);
        Assert.Equal(Vector3.Zero, g.Position);                             // the emitter's own point, in its frame
        Assert.InRange(g.End, 1400u, 101000u);                              // rand × 100000 on
        Assert.Empty(ps.Sprites);                                           // its clock is now: nothing due

        ps.Step(0.1f, 1100);
        ref var d0 = ref e.Drops[0];
        Assert.Equal(2000u, d0.Death);                                      // due at 1000, + lifespan
        Assert.Equal(new Vector3(100, 0, -10), d0.Velocity);                // velocity, then the fall
        Assert.Equal(new Vector3(20, 20, 29), d0.Position);                 // then the move with it
        Assert.Equal(1250u, e.Groups![0].NextBirth);

        ps.Step(0.1f, 1200);
        // 1200 − 1250 underflows: the next is put back to 200, and 200, 450, 700 and 950 all come now.
        Assert.Equal(new uint[] { 1200, 1450, 1700, 1950 }, e.Drops[1..5].Select(d => d.Death));
        Assert.Equal(1200u, e.Groups![0].NextBirth);
        Assert.Equal(new Vector3(10, 20, 30), e.Drops[1].Position);         // dead on arrival: never moved
        Assert.Equal(new Vector3(20, 20, 29), e.Drops[2].Position);
        Assert.Equal(4, ps.Sprites.Count);                                  // the first drop and three new
        Assert.All(ps.Sprites, s => Assert.Equal((0xffc00000u, 2f), (s.Color, s.Size)));

        ps.Step(0.1f, 1300);                                                // 100 behind: no reset, one drop
        Assert.Equal(2200u, e.Drops[5].Death);
        Assert.Equal(0u, e.Drops[6].Death);
        Assert.Equal(1450u, e.Groups![0].NextBirth);
    }

    /// <summary>
    /// A jet is kept in its thing's frame: turn the thing a quarter and the next drop leaves along
    /// the thing's new heading from the jet's point carried round with it.
    /// </summary>
    [Fact]
    public void A_spurts_jet_turns_with_its_thing()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var thing = Thing(new Vector3(0, 0, 0));
        var e = (SpurtEmitter)ps.Attach(thing, "jet")!;
        ps.Touch(e);
        ps.Step(0.1f, 1000);
        thing.Orientation = new Vector3(0, 0, 90);
        thing.Position = new Vector3(5, 0, 0);
        ps.Step(0.1f, 1100);
        Assert.Equal(0f, e.Drops[0].Velocity.X, 4);
        Assert.Equal(100f, e.Drops[0].Velocity.Y, 4);
        Assert.Equal(5f, e.Drops[0].Position.X, 4);
        Assert.Equal(10f, e.Drops[0].Position.Y, 4);
    }

    /// <summary>
    /// Rain keeps its drops in a box <c>startsize</c> across around the camera: a drop that falls out
    /// of the bottom comes back in at the top by the remainder, and moving the camera
    /// folds every drop into the new box. Squares at <c>width</c> with no <c>length</c>; streaks
    /// <c>length</c> seconds of fall long with one.
    /// </summary>
    [Fact]
    public void Rain_falls_through_a_box_that_follows_the_camera()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var e = Assert.IsType<RainEmitter>(ps.Attach(Thing(Vector3.Zero), "wet"));
        Assert.True(e.Registered);                                          // live the moment it is made
        e.Drops[0].Position = new Vector3(0, 0, -45);
        e.Drops[1].Position = new Vector3(0, 0, 40);
        ps.Step(0.1f, 100);
        Assert.Equal(45f, e.Drops[0].Position.Z);                           // −55: 50 − fmod(105, 100)
        Assert.Equal(30f, e.Drops[1].Position.Z);
        Assert.Equal(2, ps.Sprites.Count);
        Assert.Equal((0xa0505050u, 3f), (ps.Sprites[0].Color, ps.Sprites[0].Size));

        ps.Eye = new Vector3(1000, 0, 0);
        ps.Step(0f, 100);
        Assert.Equal(1000f, e.Drops[0].Position.X);                         // 1050 − fmod(1050, 100)
        Assert.InRange(e.Drops[1].Position.X, 950f, 1050f);

        e.Settings.Length = 0.1f;
        ps.Step(0f, 100);
        Assert.Empty(ps.Sprites);
        var streak = ps.Streaks[0];
        Assert.Equal(e.Drops[0].Position + new Vector3(0, 0, -10), streak.Tip);
        Assert.Equal(3f, streak.Width);
        Assert.Equal(ParticleRun.Streaks, ps.Batches.Single().Run);
    }

    /// <summary>
    /// The rain's thing gone: one drop fewer every <c>birthrate</c> milliseconds, and the emitter is
    /// finished with none left.
    /// </summary>
    [Fact]
    public void Rain_thins_out_when_its_thing_goes()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var thing = Thing(Vector3.Zero);
        var e = (RainEmitter)ps.Attach(thing, "wet")!;
        ps.Step(0.1f, 1000);
        ps.Detach(thing);                                                   // next "birth" at now
        ps.Step(0.1f, 1050);
        Assert.Equal(1u, e.Settings.MaxParticles);                          // 1000 < 1050: one fewer
        Assert.Single(ps.Sprites);
        ps.Step(0.1f, 1150);
        Assert.DoesNotContain(e, ps.Live);
    }

    /// <summary>
    /// A shock ring: laid at the thing a unit up, at full life, when due; then it loses a tenth of
    /// its life in a tenth of a second, moves 15 along its own Z (velocity 150), grows by 30
    /// (growthrate 300) and is drawn as a square that size, coloured nine tenths of the way.
    /// </summary>
    [Fact]
    public void A_shock_ring_rises_grows_and_fades()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var e = Assert.IsType<ShockEmitter>(ps.Attach(Thing(new Vector3(0, 0, 100)), "wave"));
        e.Settings.RotVel = Vector3.Zero;
        ps.Touch(e);
        ps.Step(0.1f, 100);
        ref var r = ref e.Rings[0];
        Assert.Equal(0.9f, r.Life, 5);
        Assert.Equal(50f, r.Size);                                          // 20 + 0.1 × 300
        Assert.Equal(new Vector3(0, 0, 116), r.Frame.Translation);          // 100, one up, 15 along
        Assert.Equal(200u, e.NextBirth);
        Assert.Equal(0, e.Settings.Count);                                  // its one ring spent
        var q = ps.Quads.Single();
        Assert.Equal(new Vector3(-50, 50, 116), q.P0);
        Assert.Equal(new Vector3(50, -50, 116), q.P2);
        Assert.Equal(Emitter.Blend(0xff2c5b95, 0, 0.9f), q.Color01);
        for (int t = 200; t <= 1100; t += 100) ps.Step(0.1f, t);
        Assert.Equal(0f, e.Rings[0].Life);                                  // below 0: put out
        Assert.False(e.Registered);                                         // nothing alive: out of the list
    }

    /// <summary>
    /// The turn a ring or pad is given each frame: roll, pitch and yaw in degrees, the
    /// yaw turning the X axis towards Y.
    /// </summary>
    [Fact]
    public void A_turn_is_roll_pitch_and_yaw()
    {
        var m = RingBase.Turn(new Vector3(0, 0, 90), new Vector3(1, 2, 3));
        Assert.Equal(0f, m.M11, 6);
        Assert.Equal(1f, m.M12, 6);
        Assert.Equal(-1f, m.M21, 6);
        Assert.Equal(new Vector3(1, 2, 3), m.Translation);
        var p = RingBase.Turn(new Vector3(0, 30, 0), Vector3.Zero);
        Assert.Equal(-0.5f, p.M13, 6);                                      // −sin pitch
    }

    /// <summary>
    /// A pad: its one ring laid at once with no life, then opening out over a second — sized from
    /// <c>endsize</c> to <c>startsize</c> by its life — turning 90° a second about the vertical and
    /// kept on the thing a unit up; when the thing goes it closes again and the emitter goes.
    /// </summary>
    [Fact]
    public void A_pad_opens_under_its_thing_and_closes_when_it_goes()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var caster = Thing(new Vector3(5, 5, 0));
        var e = Assert.IsType<PadEmitter>(ps.Attach(caster, "circle"));
        ps.Touch(e);
        ps.Step(0.25f, 250);
        ref var r = ref e.Rings[0];
        Assert.Equal(0.25f, r.Life, 5);
        Assert.Equal((40f - 0.1f) * 0.25f + 0.1f, r.Size, 4);
        Assert.Equal(new Vector3(5, 5, 1), r.Frame.Translation);
        Assert.Equal(MathF.Cos(22.5f * MathF.PI / 180f), r.Frame.M11, 5);   // a quarter second at 90°/s
        Assert.Equal(MathF.Sin(22.5f * MathF.PI / 180f), r.Frame.M12, 5);
        caster.Position = new Vector3(50, 0, 10);
        for (int t = 500; t <= 1500; t += 250) ps.Step(0.25f, t);
        Assert.Equal(1f, e.Rings[0].Life);                                  // held at 1
        Assert.Equal(40f, e.Rings[0].Size, 4);
        Assert.Equal(new Vector3(50, 0, 11), e.Rings[0].Frame.Translation);
        Assert.Single(ps.Quads);
        ps.Detach(caster);
        ps.Step(0.25f, 1750);
        Assert.Equal(0.75f, e.Rings[0].Life, 5);
        for (int t = 2000; t <= 2750; t += 250) ps.Step(0.25f, t);
        Assert.DoesNotContain(e, ps.Live);
    }

    /// <summary>
    /// A magic mote circles a rising centre: born where the thing is,
    /// 10 out at a random angle, turning 90° a second, widening 20 a second, rising 10 a second, and
    /// drawn from its end size and colour towards its start ones by its life.
    /// </summary>
    [Fact]
    public void A_magic_mote_circles_a_rising_centre()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var caster = Thing(new Vector3(100, 0, 0));
        var e = Assert.IsType<MagicEmitter>(ps.Attach(caster, "swirl"));
        ps.Touch(e);
        ps.Step(0.1f, 100);                                                 // due at 0, 100 late
        ref var m = ref e.Motes[0];
        Assert.Equal(new Vector3(100, 0, 2), m.Centre);                     // 1 up for lateness, 1 for the frame
        Assert.Equal(12f, m.Radius, 4);                                    // 10, then 20 × 0.1
        Assert.Equal(0.9f, m.Life, 5);
        Assert.Equal(1000u, e.NextBirth);
        float yaw = m.Angles.Z;
        var s0 = ps.Sprites.Single();
        Assert.Equal(12f, Vector3.Distance(s0.Position, m.Centre), 3);      // on its circle
        Assert.Equal(13f, s0.Size, 4);                                      // 4 + 10 × 0.9
        Assert.Equal(Emitter.Blend(0xffffffff, 0x00800000, 0.9f), s0.Color);
        ps.Step(0.5f, 600);
        Assert.Equal(yaw + 45f, e.Motes[0].Angles.Z, 3);
        var s1 = ps.Sprites.Single();
        var turned = new Vector3(MathF.Cos((yaw + 45f) * MathF.PI / 180f), MathF.Sin((yaw + 45f) * MathF.PI / 180f), 0);
        Assert.True(Vector3.Distance(e.Motes[0].Centre + 22f * turned, s1.Position) < 0.01f);
    }

    /// <summary>
    /// A ring's band: eight spokes round its circle, each joined to the next at the bottom
    /// and at a height from 200 towards 20 by its life; the texture twice round; the bottom edge in
    /// the start and end colours, the top in <c>startcolormax</c> and <c>endcolor</c>. And the ring
    /// kind never finishes (its finished test answers 1).
    /// </summary>
    [Fact]
    public void A_ring_draws_a_band_and_never_finishes()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var thing = Thing(new Vector3(0, 0, 0));
        var e = Assert.IsType<RingEmitter>(ps.Attach(thing, "band"));
        Assert.Equal(8, e.Spokes.Length);
        ps.Touch(e);
        ps.Step(0.5f, 500);
        ref var r = ref e.Rings[0];
        Assert.Equal(0.5f, r.Life, 5);
        Assert.Equal(45f, r.Size, 4);                                       // 50 − 10 × 0.5
        var quads = ps.Quads;
        Assert.Equal(8, quads.Count);                                       // n + 1 records, n quads
        var q = quads[0];
        Assert.Equal(new Vector3(45, 0, 1), q.P0);                          // spoke 0 × size, one up
        Assert.Equal(new Vector3(45, 0, 111), q.P1);                        // + (20 − 200) × 0.5 + 200
        Assert.Equal(new Vector2(0.25f, 1), q.T2);                          // 2 / 8 round
        Assert.Equal(Emitter.Blend(0xff000000, 0, 0.5f), q.C0);
        Assert.Equal(Emitter.Blend(0xffffffff, 0, 0.5f), q.C1);
        Assert.Equal(new Vector2(2f, 0), quads[7].T3);                     // the closing record at the full rate
        Assert.True(Vector3.Distance(quads[7].P3, q.P0) < 0.001f);
        ps.Detach(thing);
        for (int t = 600; t <= 3000; t += 100) ps.Step(0.1f, t);
        Assert.Contains(e, ps.Live);                                        // dying, dark, and still there
        Assert.Empty(ps.Quads);
    }

    /// <summary>
    /// A beam: from its thing to the thing's frame 1, cut into
    /// <c>ceil(100 / 25)</c> = 4 points, drawn as a ribbon 10 wide laid square to the line of sight
    /// — seen from above, flat along the world's Y — and struck only <c>count</c> times.
    /// </summary>
    [Fact]
    public void A_beam_runs_to_its_things_second_frame_and_faces_the_camera()
    {
        var ps = new ParticleSystem(Npd(Blocks)) { Eye = new Vector3(50, 0, 100) };
        var thing = Thing(Vector3.Zero);
        thing.Keyframes = [new NsdKeyframe(Vector3.Zero, Vector3.Zero), new NsdKeyframe(Vector3.Zero, new Vector3(100, 0, 0))];
        var e = Assert.IsType<BeamEmitter>(ps.Attach(thing, "zap"));
        Assert.Equal(2, e.Remaining);
        ps.Touch(e);
        ps.Step(0.1f, 100);
        ref var b = ref e.Bolts[0];
        Assert.Equal(4, b.Points);
        Assert.Equal(0.9f, b.Life, 5);
        Assert.Equal(1, e.Remaining);
        Assert.Equal(1100u, e.NextBirth);                                  // birthrate after now
        var quads = ps.Quads;
        Assert.Equal(3, quads.Count);
        foreach (var q in quads)
            foreach (var c in new[] { q.P0, q.P1, q.P2, q.P3 })
            {
                Assert.Equal(5f, MathF.Abs(c.Y), 3);                        // half the width, across
                Assert.Equal(0f, c.Z, 3);
            }
        Assert.Equal(0f, quads[0].P0.X, 3);
        Assert.Equal(100f, quads[2].P2.X, 3);
        Assert.Equal(100f / 3f, quads[1].V01, 3);                          // along ÷ the texture's height (1 here)
        Assert.Equal(Emitter.Blend(0xffffffff, 0xff000040, 0.9f), quads[0].Color01);
        for (int t = 200; t <= 2500; t += 100) ps.Step(0.1f, t);
        Assert.Equal(0, e.Remaining);                                       // two struck, no more
        Assert.Equal(2200u, e.NextBirth);
        ps.Detach(thing);
        ps.Step(0.1f, 2600);
        Assert.DoesNotContain(e, ps.Live);                                  // no thing: gone at once
    }

    /// <summary>A beam's frame: Y along it, X = Y × up; a vertical beam takes the world's X instead.</summary>
    [Fact]
    public void A_beams_frame_has_the_beam_as_its_Y()
    {
        var m = BeamEmitter.Basis(Vector3.UnitX);
        Assert.Equal(new Vector3(0, -1, 0), new Vector3(m.M11, m.M12, m.M13));
        Assert.Equal(Vector3.UnitX, new Vector3(m.M21, m.M22, m.M23));
        Assert.Equal(new Vector3(0, 0, 1), new Vector3(m.M31, m.M32, m.M33));
        var v = BeamEmitter.Basis(Vector3.UnitZ);
        Assert.Equal(new Vector3(0, -1, 0), new Vector3(v.M31, v.M32, v.M33));
        Assert.Equal(new Vector3(1, 0, 0), new Vector3(v.M11, v.M12, v.M13));   // Y × Z
    }

    /// <summary>
    /// A trail without the always bit lays only while its thing hangs on a character swinging a
    /// <c>STRIKETRAIL</c> attack and busy with it: not before the swing, not with a plain
    /// attack, not once the swing's time is up.
    /// </summary>
    [Fact]
    public void A_blade_trail_lays_only_in_a_striketrail_swing()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var hand = Thing(Vector3.Zero);
        var holder = new WorldThing
        {
            Index = 1, Guid = 2, Scene = NsdThing.Placed("guard", Vector3.Zero, Vector3.Zero, 0), Template = "guard",
            Type = ThingType.Actor, Actor = new Actor(),
        };
        var sword = new Vitomir.Engine.Rpg.Weapon { Name = "sword" };
        holder.Actor!.Weapon = sword;
        hand.AttachedTo = holder;
        var e = Assert.IsType<TrailEmitter>(ps.Attach(hand, "blade"));
        ps.Touch(e);
        ps.Step(0.02f, 20);
        Assert.DoesNotContain(e.Segments, s => s.Laid);                     // standing: nothing
        holder.Actor.Action = Vitomir.Engine.Rpg.ActionMode.Swing;
        holder.Actor.BusyUntil = 500;
        ps.Step(0.02f, 40);
        Assert.DoesNotContain(e.Segments, s => s.Laid);                     // a swing, but no STRIKETRAIL
        sword.Attacks[0].Flags = Vitomir.Engine.Rpg.WeaponFlag.StrikeTrail;
        ps.Step(0.02f, 60);
        Assert.Single(e.Segments, s => s.Laid);
        Assert.True(TrailEmitter.Swinging(hand, 499));
        Assert.False(TrailEmitter.Swinging(hand, 500));                     // the swing's time is up
        holder.Actor.Flags |= Actor.Flag.Dead;
        Assert.True(TrailEmitter.Swinging(hand, 500));                      // …unless the swinger is dead
    }

    /// <summary>
    /// An object emitter makes things: the Fire Storm's <c>firestorm</c> drops a
    /// <c>proj_firestorm</c> every 125 ms somewhere in a disc of 70 round itself, each to live two
    /// seconds.
    /// </summary>
    [Fact]
    public void A_fire_storm_rains_projectiles()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_HAVN", sky: false);
        var ps = place.Particles;
        var storm = place.Place("firestorm", place.StartPosition + new Vector3(0, 0, 100), Vector3.Zero, place.Things[0].Sector)!;
        var sim = new Simulation(place);
        for (int i = 0; i < 16; i++) sim.Step(1f / 32f);                   // half a second
        var e = Assert.IsType<ObjectEmitter>(ps.Of(storm));
        Assert.Equal(("proj_firestorm", 125u, 2000u), (e.Settings.Template, e.Settings.BirthRate, e.Settings.Lifespan));
        var drops = place.Things.Where(t => t.Template.Equals("proj_firestorm", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.InRange(drops.Length, 3, 5);
        Assert.All(drops, d => Assert.InRange(d.ExpireAt, 1, 2600));
        Assert.All(drops, d => Assert.InRange(new Vector2(d.Position.X - storm.Position.X, d.Position.Y - storm.Position.Y).Length(), 0f, 70.01f + 300f * 0.6f));
    }

    /// <summary>
    /// A liquid: with a cohesion of 1 its clumps go exactly along the
    /// thing's Y axis; with a group cohesion of 1 every clump sheds when its turn comes; the turns
    /// go round the clumps, and a clump of three is thrown afresh when empty. Drawn in its start
    /// colour, unfaded.
    /// </summary>
    [Fact]
    public void A_liquid_sheds_its_clumps_in_turn()
    {
        var ps = new ParticleSystem(Npd(Blocks));
        var e = Assert.IsType<LiquidEmitter>(ps.Attach(Thing(Vector3.Zero), "squirt"));
        ps.Touch(e);
        ps.Step(0.1f, 100);
        Assert.Equal(2, e.Clumps!.Length);
        Assert.All(e.Clumps, c => Assert.Equal(Vector3.UnitY, c.Direction));
        Assert.Equal((2, 3), (e.Clumps[0].Left, e.Clumps[1].Left));          // one shed from the first
        Assert.Equal(new Vector3(0, 10, 0), e.Drops[0].Position);
        Assert.InRange(e.NextBirth, 100u, 119u);                            // 100 + step % 20
        Assert.InRange(e.Drops[0].Death, 1000u, 1999u);                     // due + extra + lifespan
        Assert.Equal((0xff6060dcu, 2f), (ps.Sprites[0].Color, ps.Sprites[0].Size));
        for (int t = 200; t <= 700; t += 100) ps.Step(0.1f, t);
        Assert.True(e.Clumps.All(c => c.Left is >= 0 and <= 3));
        Assert.Equal(7, e.Drops.Count(d => d.Death != 0));                   // one a turn, every turn
    }

    /// <summary>
    /// The shipped blocks: every spurt, rain, shock and pad is made as its own kind, and a
    /// beheading's two spurts and Vienna's snow read as the file says.
    /// </summary>
    [Fact]
    public void The_shipped_blood_snow_and_rings_are_their_own_kinds()
    {
        using var files = Install.Open();
        Assert.True(files.TryRead("misc/game.npd", out var data));
        var ps = new ParticleSystem(NpdFile.Read(data, "misc/game.npd"));
        var blood = ps.Prototype("blood0")!;
        Assert.Equal((1u, 500f, 3000u, 6000u), (blood.NumGroups, blood.MinBirthGap, blood.Lifespan, blood.MaxParticles));
        Assert.Equal((500u, 1500u), (blood.MinGroup, blood.MaxGroup));
        Assert.IsType<SpurtEmitter>(ps.Make(ps.Prototype("headBlood")!));
        var snow = ps.Prototype("snow")!;
        Assert.Equal((3000f, 0f, 2.5f, 1000u), (snow.StartSize, snow.Length, snow.Width, snow.MaxParticles));
        Assert.Equal((120f, 120f), (snow.Turbulence, snow.VelocityVariance));
        Assert.Equal(0x80303030u, ps.Prototype("drizzle")!.StartColor);    // nine digits: the low 32 bits
        Assert.IsType<RainEmitter>(ps.Make(snow));
        Assert.IsType<ShockEmitter>(ps.Make(ps.Prototype("shockwave")!));
        Assert.IsType<PadEmitter>(ps.Make(ps.Prototype("flamePad")!));
        Assert.IsType<RingEmitter>(ps.Make(ps.Prototype("ringTower")!));
        Assert.IsType<BeamEmitter>(ps.Make(ps.Prototype("blueLightning")!));
        var bolt = ps.Prototype("blueLightning")!;
        Assert.Equal((false, 16f, 24f, 10u, 2u), (bolt.Sinus, bolt.SegmentLength, bolt.Turbulence, bolt.UpdateRate, bolt.MaxParticles));
        Assert.True(ps.Prototype("redWavyBeam")!.Sinus);
        Assert.IsType<MagicEmitter>(ps.Make(ps.Prototype("redMagic")!));
        var tower = ps.Prototype("ringTower")!;
        Assert.Equal((20f, 800f, 32u, 1f), (tower.StartHeight, tower.EndHeight, tower.NumSegs, tower.TextureRate));
        var red = ps.Prototype("redMagic")!;
        Assert.Equal((10f, 20f, -240f, 240f, 40f), (red.MinRadius, red.MaxRadius, red.MinSpeed, red.MaxSpeed, red.MinBirthGap));
    }
}
