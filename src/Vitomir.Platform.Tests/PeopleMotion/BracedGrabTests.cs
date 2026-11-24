// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.AirCommitTests;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// <b>grabs that end braced</b> — at a wall under the edge the standing and the falling grab play their braced twins
/// (<c>grab_jump_up_braced</c>, <c>grab_fall_braced</c>: the free ones up to the catch, ending on <c>hang_braced</c>'s first frame), so no brace follows a grab;
/// the free ones stay where nothing is under the edge; the earlier build's straight-into-the-pull-up goes on from the twin into <c>pull_up_braced</c>; the earlier build's (the free grab and
/// The earlier build's brace overlap) behind the switch. Skips without the models repository's files, or.
/// </summary>
public sealed class BracedGrabTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    const float Tick = World.TickSeconds;

    static readonly PersonCommitSettings Commit = new();
    static readonly PersonMovesSettings Moves = new();

    static LedgePaths Paths(CharacterAssets assets, int kind)
    {
        var person = assets.Kind(kind).Person!;
        Assert.SkipUnless(person.Ledge?.Paths.Clips.Any(c => c.Twin) == true, "the models repository's files are too old (no braced twins)");
        return person.Ledge!.Paths;
    }

    /// <summary>A standing tap at the wall of <paramref name="h"/> (or under a slab with nothing beneath its edge) from 0.45 m out; forward held from the tap when asked.</summary>
    static List<Row> Stand(CharacterAssets assets, int kind, PersonMovesSettings? moves, float h, bool slab = false, bool forward = false, int ticks = 400)
    {
        var block = slab ? (new Vector3(1f, -3f, h - 0.2f), new Vector3(3f, 3f, h)) : Wall(h);
        var (w, e) = Make(assets, kind, Commit, new Vector3(0.55f, 0f, 0f), moves, null, blocks: [block]);
        return Drive(w, e, assets, kind, (i, b) => new Ask(forward ? new Vector2(1f, 0f) : Vector2.Zero, Jump: i == 0), ticks);
    }

    /// <summary>A fall past the wall of <paramref name="h"/> (or a slab's edge), use held, put in the air 0.45 m out and 1 m over the falling grab's contact place.</summary>
    static List<Row> Fall(CharacterAssets assets, int kind, PersonMovesSettings? moves, float h, bool slab = false, bool forward = false, int ticks = 400)
    {
        var c = Paths(assets, kind).Of(LedgeRole.GrabFall)!;
        float contactZ = h - (c.EdgeUp - c.At(c.ContactS).Up);
        var block = slab ? (new Vector3(1f, -3f, h - 0.2f), new Vector3(3f, 3f, h)) : Wall(h);
        var (w, e) = Make(assets, kind, Commit, new Vector3(0.55f, 0f, 0f), moves, null, blocks: [block]);
        return Drive(w, e, assets, kind, (i, b) => new Ask(forward ? new Vector2(1f, 0f) : Vector2.Zero, Use: true), ticks,
                     launch: (new Vector3(0.55f, 0f, contactZ + 1f), Vector3.Zero, false));
    }

    static List<string> Ledges(List<Row> rows)
    {
        var all = new List<string>();
        foreach (var r in rows) if (r.Ledge != "-" && (all.Count == 0 || all[^1] != r.Ledge)) all.Add(r.Ledge);
        return all;
    }

    /// <summary>The hands' contact tick (the grab at its <c>contact_s</c>), the knuckles then against the edge (x 1, top h), and the pops from the start to 0.5 s into the hang.</summary>
    (int Contact, Vector2 Knuckles, float Pelvis, float Hands) Measure(string what, List<Row> rows, LedgePaths paths, float h)
    {
        int contact = rows.FindIndex(r => paths.Clips.FirstOrDefault(c => c.Name == r.Ledge) is { } g && g.Role is LedgeRole.GrabStand or LedgeRole.GrabFall
                                          && r.LedgeTime >= g.ContactS - 1e-4f);
        Assert.True(contact > 0, what + ": the hands' contact");
        var k = rows[contact];
        var knuckles = new Vector2(0.5f * (k.KnuckleL.X + k.KnuckleR.X) - 1f, 0.5f * (k.KnuckleL.Z + k.KnuckleR.Z) - h);
        int hang = rows.FindIndex(contact, r => r.Ledge == "hang_braced" || r.Ledge == "hang_free" || r.Move == TraversalKind.PullUp);
        int to = hang > 0 ? hang + (int)(0.5f / Tick) : rows.Count;
        int from = rows.FindIndex(r => r.Move == TraversalKind.Grab);
        float pelvis = Pop(rows, from, to, PelvisOf), hands = Pop(rows, from, to, HandsOf);
        output.WriteLine($"{what}: {string.Join(", ", Ledges(rows))}; the knuckles at the contact {F(knuckles.X * 100f, "0.0")} cm out from the face, {F(knuckles.Y * 100f, "0.0")} cm "
                         + $"over the edge; the pelvis's pop {F(pelvis * 1000f, "0.0")} mm (at {ArgPop(rows, from, to, PelvisOf) - from}), the hands' {F(hands * 1000f, "0.0")} mm, "
                         + $"from the grab to 0.5 s into the hang (the hang {F((hang - from) * Tick)} s after the grab began)");
        return (contact, knuckles, pelvis, hands);
    }

    [Fact]
    public void A_standing_and_a_falling_grab_at_a_wall_play_their_braced_twins_with_no_brace_after()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Paths(assets, kind);
            float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
            var db = Measure($"{body}, a standing grab at {F(h)} m, the moves on", Stand(assets, kind, Moves, h), paths, h);
            var da = Measure($"{body}, a standing grab at {F(h)} m, the moves off", Stand(assets, kind, null, h), paths, h);
            var dbRows = Stand(assets, kind, Moves, h);
            Assert.Equal(["grab_jump_up_braced", "hang_braced"], Ledges(dbRows));
            Assert.Equal(["grab_jump_up", "hang_brace_on", "hang_braced"], Ledges(Stand(assets, kind, null, h)));
            // The twins are their free ones up to the catch: the knuckles at the contact the same.
            Assert.InRange((db.Knuckles - da.Knuckles).Length(), 0f, 0.005f);
            Assert.InRange(db.Pelvis, 0f, da.Pelvis + 0.002f);   // no more than the earlier build's (the jump's own, 16 mm a tick², the largest)
            var fdb = Measure($"{body}, a falling grab at 2.6 m, use held, the moves on", Fall(assets, kind, Moves, 2.6f), paths, 2.6f);
            var fda = Measure($"{body}, a falling grab at 2.6 m, use held, the moves off", Fall(assets, kind, null, 2.6f), paths, 2.6f);
            Assert.Equal(["grab_fall_braced", "hang_braced"], Ledges(Fall(assets, kind, Moves, 2.6f)));
            Assert.InRange((fdb.Knuckles - fda.Knuckles).Length(), 0f, 0.03f);   // the catch laid to the twin's own speed after the contact: within 2 cm
            Assert.InRange(fdb.Pelvis, 0f, fda.Pelvis + 0.002f);
        }
    }

    [Fact]
    public void Under_a_slab_with_nothing_beneath_its_edge_the_free_grabs_stay()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
        var stand = Ledges(Stand(assets, kind, Moves, h, slab: true));
        var fall = Ledges(Fall(assets, kind, Moves, 2.6f, slab: true));
        output.WriteLine($"under a slab: standing {string.Join(", ", stand)}; falling {string.Join(", ", fall)}");
        Assert.Equal("grab_jump_up", stand[0]);
        Assert.Equal("hang_free", stand[^1]);
        Assert.Equal("grab_fall", fall[0]);
        Assert.Equal("hang_free", fall[^1]);
    }

    [Fact]
    public void Held_on_the_braced_twin_goes_straight_into_pull_up_braced()
    {
        var (assets, kind, _) = Player();
        var paths = Paths(assets, kind);
        float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
        foreach (var (what, rows) in new[] { ($"a standing grab at {F(h)} m", Stand(assets, kind, Moves, h, forward: true, ticks: 600)),
                                             ("a falling grab at 2.6 m", Fall(assets, kind, Moves, 2.6f, forward: true, ticks: 600)) })
        {
            var m = Measure(what + ", forward held", rows, paths, what.Contains("2.6") ? 2.6f : h);
            int pull = rows.FindIndex(r => r.Move == TraversalKind.PullUp);
            int stand = pull < 0 ? -1 : rows.FindIndex(pull, r => r.Move == TraversalKind.None && r.State == BodyState.Ground);
            Assert.True(pull > 0 && stand > 0);
            Assert.Equal("pull_up_braced", rows[pull].Ledge);
            float pelvis = Pop(rows, m.Contact, stand + 30, PelvisOf);
            output.WriteLine($"  the pull-up {F((pull - m.Contact) * Tick, "0.000")} s after the contact, standing {F((stand - m.Contact) * Tick)} s after it; the pelvis's pop {F(pelvis * 1000f, "0.0")} mm");
            Assert.InRange((pull - m.Contact) * Tick, 0.1f, 0.2f);
            Assert.InRange(pelvis, 0f, 0.02f);
        }
    }

    [Fact]
    public void The_braced_feet_are_the_clips_own_the_engine_neither_pushes_them_into_the_wall_nor_off_it()
    {
        // The balls of the feet on the wall's face, the toes bent up 40° (soft shoes). The engine's feet's IK plants feet only on the ground
        // (not in a move): the drawn balls in the braced hang are where the clip has them; toeBendLimitDeg under 40 moves them back by the table.
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            var paths = Paths(assets, kind);
            float h = MathF.Round(paths.Of(LedgeRole.GrabStand)!.OwnHeight, 2);
            var skeleton = assets.ModelOf(kind).Skeleton;
            int ballL = skeleton.Find("ball_l"), ballR = skeleton.Find("ball_r");
            var braced = paths.Of(LedgeRole.Hang, braced: true)!;
            float own = paths.EdgeFwd - braced.At(0f).Forward - paths.BracedFeet.Max(f => f.X);   // the clip's own nearer ball off the wall at its first frame
            var least = new List<float>();
            foreach (var (what, moves) in new[] { ("40° (the clips')", Moves), ("20°", Moves with { ToeBendLimitDegrees = 20f }) })
            {
                var (w, e) = Make(assets, kind, Commit, new Vector3(0.55f, 0f, 0f), moves, null, blocks: [Wall(h)]);
                var rows = Drive(w, e, assets, kind, (i, b) => new Ask(Vector2.Zero, Jump: i == 0), 500);
                Assert.Equal("hang_braced", rows[^1].Ledge);
                // Over the hang's loop (its last 4.4 s): each ball's distance from the wall's face (x 1), the least (on the wall) and the most (a foot off it).
                var near = new List<float>();
                for (int i = 0; i < 300; i++)
                {
                    w.Step();
                    near.Add(MathF.Min(1f - Bone(w, e, assets, ballL).X, 1f - Bone(w, e, assets, ballR).X));
                }
                output.WriteLine($"{body}, toes to {what}: the nearer ball's joint {F(near.Min() * 100f, "0.0")} to {F(near.Max() * 100f, "0.0")} cm off the wall's face over 2.5 s of the "
                                 + $"braced hang (the clip's own at its first frame {F(own * 100f, "0.0")} cm: the joint, the toes' pads on the face)");
                least.Add(near.Min());
            }
            Assert.InRange(least[0], own - 0.01f, own + 0.01f);
            Assert.InRange(least[1] - least[0], 0.018f - 0.006f, 0.018f + 0.006f);   // 20°: 18 mm further back, the table's
        }
    }
}
