// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// (His play: <i>"When I run at it, the player just one leg hops over it"</i>): the run's and the sprint's own vault
/// on the clips (<c>BodyMotor.OverPress</c>,
/// <see cref="PersonOverSettings.Auto"/>), the player driven by its keys as in the game: running or sprinting at a thin wall in the vault clips' bands
/// it vaults by itself on <c>vault_low</c>/<c>vault_high</c>, entered on the run's left footfall, the clip played at the speed met; not at a walk, not
/// passing along a wall or meeting it at a slant, not at a wall above the bands or a deep block; off the switch the sprint vault at a sprint and
/// a stop at a run, as before. Skips without the models repository's files, or.
/// </summary>
public sealed class AutoVaultTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static PersonOverClips Over(CharacterAssets assets, int kind)
    {
        var over = assets.Kind(kind).Person!.Over;
        Assert.SkipUnless(over?.Paths?.Any(TraversalKind.Vault) is not null && assets.Kind(kind).Person!.Air is not null,
                          "the models repository's files are too old (no vault clips)");
        return over!;
    }

    /// <summary>The player's body driven by its keys (the control, the spring, the animation with the air's and the moves' clips) at <paramref name="at"/> facing <paramref name="yaw"/>; the floor at 0 and <paramref name="blocks"/>.</summary>
    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonOverSettings over, Vector3 at, float yaw, params (Vector3 Min, Vector3 Max)[] blocks)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        foreach (var (min, max) in blocks) t.Box(min, max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: over);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), yaw, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        return (w, e);
    }

    /// <summary>A tick as it went: the clip, its time and fade, the move and its length, the body.</summary>
    sealed record Row(string Clip, float Time, string Previous, TraversalKind Move, float Seconds, bool Ground, Vector3 Feet, Vector3 Velocity, int Pending, float Phase,
                      Vector3 Pelvis = default, Vector3 HandL = default, Vector3 HandR = default);

    /// <summary><paramref name="ticks"/> ticks of <paramref name="frame"/> after half a second standing; every tick's row.</summary>
    static List<Row> Drive(World w, EntityId e, CharacterAssets assets, ActionFrame frame, int ticks)
    {
        for (int i = 0; i < 60; i++) MovementServices.Step(w, ActionFrame.Idle);
        var rows = new List<Row>(ticks);
        var rig = assets.Kind(w.Get<CharacterAnimation>(e).Kind).Person!.Rig;
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, frame);
            var body = w.Get<Body>(e);
            var a = w.Get<CharacterAnimation>(e);
            var over = w.TryStore<PersonOver>(out var overs) && overs.Has(e) ? overs.Get(e).Move : default;
            float d = a.Clip >= 0 ? assets.Clip(a.Clip).Duration : 0f;
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, a.PreviousClip >= 0 ? assets.Clip(a.PreviousClip).Name : "-", w.Get<Traversal>(e).Kind,
                             w.Get<Traversal>(e).Seconds, body.State == BodyState.Ground, body.Feet, body.Velocity, over.Pending, d > 0f ? a.Time / d - MathF.Floor(a.Time / d) : 0f,
                             Bone(w, e, assets, rig.Pelvis), Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip)));
        }
        return rows;
    }

    void Say(string what, List<Row> rows, int from, int to)
    {
        output.WriteLine(what);
        for (int i = Math.Max(0, from); i < Math.Min(rows.Count, to); i++)
        {
            var r = rows[i];
            output.WriteLine($"  {i,4} {r.Clip,-16} {F(r.Time, "0.000")} ({F(r.Phase, "0.00")}) {(r.Previous != "-" ? "<" + r.Previous : ""),-12} {r.Move,-8} {(r.Ground ? "G" : "A")} "
                             + $"held {r.Pending} x {F(r.Feet.X, "0.000")} y {F(r.Feet.Y, "0.000")} z {F(r.Feet.Z, "0.000")} v {F(new Vector2(r.Velocity.X, r.Velocity.Y).Length())}");
        }
    }

    /// <summary>The largest change a tick of a point's change a tick (m: a pop) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static float Pop(List<Row> rows, Func<Row, Vector3> of, int from, int to)
    {
        float pop = 0f;
        for (int i = Math.Max(2, from); i < Math.Min(rows.Count, to); i++)
            pop = MathF.Max(pop, ((of(rows[i]) - of(rows[i - 1])) - (of(rows[i - 1]) - of(rows[i - 2]))).Length());
        return pop;
    }

    static ActionFrame Ahead(Buttons held = Buttons.None, float side = 0f) => new(new Vector2(side, 1f), default, held, default);

    /// <summary>A thin wall (0.25 m deep, 6 m wide) across +X at x <paramref name="x"/>, <paramref name="h"/> high.</summary>
    static (Vector3, Vector3) Wall(float x, float h) => (new Vector3(x, -3f, 0f), new Vector3(x + 0.25f, 3f, h));

    [Fact]
    public void A_sprint_and_a_run_vault_a_thin_wall_by_themselves_on_the_clips_from_the_left_footfall_at_the_speed_met()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Over(assets, kind).Paths!;
            foreach (var (gait, held) in new[] { ("sprint", Buttons.Sprint), ("run", Buttons.None) })
                foreach (float h in new[] { 0.62f, 0.8f, 1.0f, 1.18f })
                {
                    var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, Wall(12f, h));
                    var rows = Drive(w, e, assets, Ahead(held), 800);
                    int start = rows.FindIndex(r => r.Move != TraversalKind.None);
                    if (start < 0) Say("no move", rows, 0, rows.Count);
                    Assert.True(start > 0, $"{body} {gait} at {F(h)} m: a move began");
                    var clip = paths.For(TraversalKind.Vault, h)!;
                    int held0 = rows.FindIndex(r => r.Pending == 2);
                    Say($"{body} {gait} at {F(h)} m", rows, start - 3, start + 3);
                    Assert.Equal(TraversalKind.Vault, rows[start].Move);
                    Assert.Equal(clip.Name, rows[start].Clip);
                    Assert.True(held0 >= 0 && held0 < start, "held for the footfall by itself, no press");
                    int end = rows.FindIndex(start, r => r.Move == TraversalKind.None);
                    if (end < 0 || rows[end].Feet.X <= 12.25f + 0.3f || !rows[end].Ground) Say("its end", rows, (end < 0 ? rows.Count : end) - 6, (end < 0 ? rows.Count : end) + 3);
                    Assert.True(end > start && rows[end].Feet.X > 12.25f + 0.3f && rows[end].Ground, "over it, on the ground beyond");
                    // The rate: the clip's length over the move's (its own at a run, faster at a sprint).
                    float rate = clip.Duration / rows[start].Seconds;
                    float speedIn = rows[start - 1].Velocity.X;
                    float expect = BodyMotor.VaultRate(clip, speedIn);
                    float phase = rows[start - 1].Phase;
                    output.WriteLine($"{body} {gait} at {F(h)} m: {clip.Name} by itself, held {F((start - held0) * Tick)} s from {F(12f - rows[held0].Feet.X)} m short of the face, "
                                     + $"begun {F(12f - rows[start].Feet.X)} m short (its own {F(clip.FaceM)}) on the {rows[start - 1].Clip}'s phase {F(phase)} (its enter_phase {F(clip.EnterPhase)}), "
                                     + $"met at {F(speedIn)} m/s: rate {F(rate, "0.000")}, {F((end - start) * Tick)} s, out at {F(rows[end].Velocity.X)} m/s; "
                                     + $"pops mm/tick²: in pelvis {F(Pop(rows, r => r.Pelvis, start - 3, start + 4) * 1000f, "0.0")} hands "
                                     + $"{F(MathF.Max(Pop(rows, r => r.HandL, start - 3, start + 4), Pop(rows, r => r.HandR, start - 3, start + 4)) * 1000f, "0.0")}, out pelvis "
                                     + $"{F(Pop(rows, r => r.Pelvis, end - 3, end + 4) * 1000f, "0.0")} hands {F(MathF.Max(Pop(rows, r => r.HandL, end - 3, end + 4), Pop(rows, r => r.HandR, end - 3, end + 4)) * 1000f, "0.0")}");
                    Assert.InRange(Pop(rows, r => r.Pelvis, start - 3, start + 4), 0f, 0.03f);
                    Assert.InRange(Pop(rows, r => r.Pelvis, end - 3, end + 4), 0f, 0.03f);
                    Assert.InRange(rate, expect - 0.01f, expect + 0.01f);
                    if (gait == "run") Assert.InRange(rate, 1f, 1.05f);
                    else Assert.InRange(rate, 1.6f, BodyMotor.MostVaultRate);
                    // On the run's (or the sprint's) left footfall: its phase at the clip's enter_phase.
                    float off = phase - clip.EnterPhase;
                    off -= MathF.Round(off);
                    Assert.InRange(MathF.Abs(off), 0f, 0.06f);
                    // Not the sprint vault.
                    Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Hurdle);
                }
        }
    }

    [Fact]
    public void A_walk_does_not_vault_by_itself_nor_a_wall_passed_along_or_met_at_a_slant_nor_one_above_the_bands_nor_a_deep_block()
    {
        var (assets, kind, _) = Player();
        Over(assets, kind);
        // At a walk: stopped at the wall (a tap still vaults it).
        {
            var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, Wall(6f, 0.8f));
            var rows = Drive(w, e, assets, Ahead(Buttons.Walk), 600);
            Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None || r.Pending != 0);
            Assert.InRange(rows[^1].Feet.X, 5f, 6f);
        }
        foreach (var (what, held) in new[] { ("running", Buttons.None), ("sprinting", Buttons.Sprint) })
        {
            // Along a wall 0.6 m to the side, the way it goes: passed.
            {
                var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, (new Vector3(2f, 0.6f, 0f), new Vector3(30f, 0.85f, 0.8f)));
                var rows = Drive(w, e, assets, Ahead(held), 480);
                Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None || r.Pending != 0);
                output.WriteLine($"{what} along a wall: passed it ({F(rows[^1].Feet.X)} m on)");
            }
            // At a slant: going 55° off square to a long wall (from (0, 0) toward +X+Y; the wall across x 8): not vaulted.
            {
                float yaw = 55f * DetMath.DegToRad;
                var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, yaw, (new Vector3(8f, -10f, 0f), new Vector3(8.25f, 30f, 0.8f)));
                var rows = Drive(w, e, assets, Ahead(held), 480);
                Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None);
                output.WriteLine($"{what} at 55°: not vaulted, ended at {F(rows[^1].Feet.X)},{F(rows[^1].Feet.Y)}");
            }
            // Within the cone (25°): vaulted.
            {
                float yaw = 25f * DetMath.DegToRad;
                var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, yaw, (new Vector3(8f, -10f, 0f), new Vector3(8.25f, 30f, 0.8f)));
                var rows = Drive(w, e, assets, Ahead(held), 480);
                Assert.Contains(rows, r => r.Move == TraversalKind.Vault);
                output.WriteLine($"{what} at 25°: vaulted");
            }
            // Above the bands (1.3 m): stopped at it.
            {
                var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, Wall(8f, 1.3f));
                var rows = Drive(w, e, assets, Ahead(held), 480);
                Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None || r.Pending != 0);
                Assert.InRange(rows[^1].Feet.X, 7f, 8f);
            }
            // A deep block in the climb's band (0.9 m, 2 m deep): climbed onto only with a tap; a run or a sprint stops at it.
            {
                var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, (new Vector3(8f, -3f, 0f), new Vector3(10f, 3f, 0.9f)));
                var rows = Drive(w, e, assets, Ahead(held), 480);
                Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None || r.Pending != 0);
            }
        }
        // A thin wall of 0.5 m (below the vault clips' 0.6): the run's step-over as before (not the clip vault).
        {
            var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, Wall(8f, 0.45f));
            var rows = Drive(w, e, assets, Ahead(), 480);
            Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Vault || r.Pending != 0);
            Assert.Contains(rows, r => r.Move == TraversalKind.StepOver);
        }
    }

    [Fact]
    public void The_reach_highlight_shows_a_thing_the_clips_vault_in_the_run_vault_s_colour_not_the_sprint_vault_s()
    {
        var (assets, kind, _) = Player();
        var paths = Over(assets, kind).Paths!;
        var level = new TestLevel().Floor(-20, -20, 20, 20).Box(new Vector3(2f, -3f, 0f), new Vector3(2.25f, 3f, 0.8f)).Build().Collision;
        var marks = new List<Vitomir.Platform.Presentation.ReachMark>();
        Vitomir.Platform.Presentation.ReachHighlight.Find(level, new Vector3(0f, 0f, 0.002f), 0f, Locomotion.Sandbox, marks);
        Assert.Contains(marks, m => (m.Moves & Vitomir.Platform.Presentation.ReachMoves.Hurdle) != 0);   // the sprint vault's orange
        Vitomir.Platform.Presentation.ReachHighlight.Find(level, new Vector3(0f, 0f, 0.002f), 0f, Locomotion.Sandbox, marks, runVaults: paths);
        Assert.NotEmpty(marks);
        Assert.All(marks, m => Assert.Equal(Vitomir.Platform.Presentation.ReachMoves.RunVault | Vitomir.Platform.Presentation.ReachMoves.Vault, m.Moves));
    }

    [Fact]
    public void Turning_away_while_it_is_held_gives_it_up_with_no_jump()
    {
        var (assets, kind, _) = Player();
        Over(assets, kind);
        var (w, e) = Make(assets, kind, new PersonOverSettings(), Vector3.Zero, 0f, Wall(10f, 0.8f));
        for (int i = 0; i < 60; i++) MovementServices.Step(w, ActionFrame.Idle);
        int held = -1;
        for (int i = 0; i < 400 && held < 0; i++)
        {
            MovementServices.Step(w, Ahead());
            if (w.Get<PersonOver>(e).Move.Pending == 2) held = i;
        }
        Assert.True(held >= 0, "held by itself");
        // Off to the left, square to the way it was going.
        bool jumped = false, moved = false;
        for (int i = 0; i < 120; i++)
        {
            MovementServices.Step(w, new ActionFrame(new Vector2(-1f, 0f), default, Buttons.None, default));
            jumped |= w.Get<Body>(e).State == BodyState.Air;
            moved |= w.Get<Traversal>(e).Kind != TraversalKind.None;
        }
        Assert.Equal(0, w.Get<PersonOver>(e).Move.Pending);
        Assert.False(jumped);
        Assert.False(moved);
    }

    [Fact]
    public void Off_the_switch_a_sprint_takes_the_sprint_vault_s_stand_in_and_a_run_stops_at_the_thing()
    {
        var (assets, kind, _) = Player();
        Over(assets, kind);
        var off = new PersonOverSettings { Auto = false };
        {
            var (w, e) = Make(assets, kind, off, Vector3.Zero, 0f, Wall(12f, 0.8f));
            var rows = Drive(w, e, assets, Ahead(Buttons.Sprint), 600);
            Assert.Contains(rows, r => r.Move == TraversalKind.Hurdle);
            Assert.DoesNotContain(rows, r => r.Move == TraversalKind.Vault || r.Pending != 0);
        }
        {
            var (w, e) = Make(assets, kind, off, Vector3.Zero, 0f, Wall(8f, 0.8f));
            var rows = Drive(w, e, assets, Ahead(), 480);
            Assert.DoesNotContain(rows, r => r.Move != TraversalKind.None || r.Pending != 0);
        }
        // The note: auto=1 carried, and a note without it has none.
        var on = new PersonOverSettings();
        Assert.Contains("auto=1", on.Note(), StringComparison.Ordinal);
        Assert.True(new PersonOverSettings().WithNote(on.Note()).Auto);
        Assert.False(new PersonOverSettings().WithNote("clips=1 enter=0.12 cut=0.08 off=0.3").Auto);
        Assert.Equal("clips=1 enter=0.12 cut=0.08 off=0.3", off.Note());
    }
}
