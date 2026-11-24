// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// The waist that came loose (in play the upper torso looked disconnected from the lower body, at
/// the waist) and the leash. The turn chain's chest over the hips is spread over the spine mostly in the chest
/// (<see cref="TurnChain.SpineShares"/>), the spine left on the pelvis (an earlier build slid it at the belt to keep its top in place), the pelvis
/// never turned or moved by the upper chain; the eye is turned back to the view about the root; the hips never more than 90° behind
/// the view however fast it spins. On the made-up person always, on the models repository's player when its files are here.
/// </summary>
public sealed class TurnWaistTests(ITestOutputHelper output)
{
    const float Deg = DetMath.DegToRad;
    static readonly LegTurnSettings S = new();

    /// <summary>The most two neighbouring trunk bones are turned apart by the chain at its limits: the largest share of the spine's turn.</summary>
    static float Bound => 0.45f * S.SpineMost;

    /// <summary>The pose of <paramref name="assets"/>' kind <paramref name="kind"/> standing, with the legs' turn and the chain given (degrees from the facing).</summary>
    static Matrix4x4[] Pose(CharacterAssets assets, int kind, float legs, float chest, float head) => Posed(assets, kind, legs, chest, head).Model;

    /// <summary>As <see cref="Pose"/>, with the skin matrices.</summary>
    static (Matrix4x4[] Model, Matrix4x4[] Skin) Posed(CharacterAssets assets, int kind, float legs, float chest, float head)
    {
        var anim = CharacterAnimation.For(assets, kind);
        int n = assets.ModelOf(kind).Skeleton.Count;
        var (model, skin) = (new Matrix4x4[n], new Matrix4x4[n]);
        new PoseEvaluator().Evaluate(assets, anim, new PoseRig { LegYaw = legs * Deg }, model, skin, default, Turn(chest, head));
        return (model, skin);
    }

    static PersonTurn Turn(float chest, float head) => new() { Chest = chest * Deg, Head = head * Deg, On = true };

    /// <summary>The chain at its limits, each way: the legs' turn, the chest 30° past it, the head 25° past that (and back).</summary>
    static IEnumerable<(float Legs, float Chest, float Head)> Limits()
    {
        foreach (float legs in new[] { 0f, 45f, -90f, 90f, 150f })
            foreach (float way in new[] { 1f, -1f })
            {
                yield return (legs, legs + way * 30f, legs + way * 55f);
                yield return (legs, legs + way * 30f, legs + way * 5f);
            }
    }

    void Check(CharacterAssets assets, int kind, PersonRig rig, string who)
    {
        var skeleton = assets.ModelOf(kind).Skeleton;
        var still = TrunkTurns.Of(skeleton, rig, Pose(assets, kind, 0f, 0f, 0f));   // the stand's own
        float waist = 0f, neck = 0f, shift = 0f;
        foreach (var (legs, chest, head) in Limits())
        {
            var turned = Pose(assets, kind, legs, chest, head);
            var t = TrunkTurns.Of(skeleton, rig, turned);
            (waist, neck, shift) = (MathF.Max(waist, t.Waist), MathF.Max(neck, t.Neck), MathF.Max(shift, t.Shift));
            // The pelvis and the legs as the legs' turn alone has them: the upper chain neither turns nor moves them.
            var hips = Pose(assets, kind, legs, legs, legs);
            foreach (int b in new[] { rig.Pelvis, rig.LeftLeg.Upper, rig.RightLeg.Upper, rig.LeftLeg.End, rig.RightLeg.End })
                for (int c = 0; c < 16; c++) Assert.Equal(hips[b][c / 4, c % 4], turned[b][c / 4, c % 4], 4);
            // The chest and the head where the chain has them (across the shoulders and the eyes, from the stand's).
            Assert.True(TurnChain.Lines(turned, rig, out float drawnChest, out float drawnHead));
            TurnChain.Lines(Pose(assets, kind, 0f, 0f, 0f), rig, out float chest0, out float head0);
            Assert.Equal(chest * Deg, DetMath.WrapAngle(drawnChest - chest0) + MathF.Round((chest * Deg - DetMath.WrapAngle(drawnChest - chest0)) / DetMath.TwoPi) * DetMath.TwoPi, 0.2f * Deg);
            Assert.Equal(head * Deg, DetMath.WrapAngle(drawnHead - head0) + MathF.Round((head * Deg - DetMath.WrapAngle(drawnHead - head0)) / DetMath.TwoPi) * DetMath.TwoPi, 0.3f * Deg);
        }
        output.WriteLine($"{who}: the chain at its limits — neighbouring trunk bones at most {waist / Deg:0.0}° apart (the stand's own {still.Waist / Deg:0.0}°; "
                         + $"bound {Bound / Deg:0.0}° over it), neck and head {neck / Deg:0.0}°, spine_01 off the pelvis {shift * 1000f:0.000} mm");
        Assert.InRange(waist, 0f, Bound + still.Waist + 0.5f * Deg);
        Assert.InRange(neck, 0f, 0.5f * S.NeckMost + still.Neck + 0.5f * Deg);
        Assert.InRange(shift, 0f, 1e-4f);
    }

    [Fact]
    public void The_chests_turn_is_spread_over_the_spine_and_the_pelvis_is_left_to_the_legs_on_the_made_up_person()
    {
        var (assets, kind, person) = PeopleDoubles.Kind();
        Check(assets, kind, person.Rig, "the made-up person");
        Assert.Equal(1f, TurnChain.SpineShares[0] + TurnChain.SpineShares[1] + TurnChain.SpineShares[2], 5);
        Assert.True(TurnChain.SpineShares[0] < TurnChain.SpineShares[1] && TurnChain.SpineShares[1] <= TurnChain.SpineShares[2], "the lumbar least, the chest most");
    }

    [Fact]
    public void The_chests_turn_is_spread_over_the_spine_and_the_pelvis_is_left_to_the_legs_on_the_player()
    {
        var (assets, kind, person) = Player();
        Check(assets, kind, person.Rig, "the player");
    }

    [Fact]
    public void The_even_waist_slid_the_spine_off_the_pelvis_and_its_switch_brings_it_back()
    {
        // The player's spine leans forward of the root, as a body's does: the earlier build's kept the chest's top where it was and slid spine_01 off
        // the pelvis to do it (on a play-test's tape up to 21 cm, the waist looking disconnected); the earlier build's leaves it on the pelvis.
        var (assets, kind, rig) = Fresh(player: true);
        var skeleton = assets.ModelOf(kind).Skeleton;
        float now = TrunkTurns.Of(skeleton, rig, Pose(assets, kind, 90f, 120f, 145f)).Shift;
        assets.PersonWaistEven = true;
        var old = TrunkTurns.Of(skeleton, rig, Pose(assets, kind, 90f, 120f, 145f));
        float oldEven = TrunkTurns.Of(skeleton, rig, Pose(assets, kind, 0f, 30f, 30f)).Waist;
        output.WriteLine($"the player, hips 90° behind, the chest 30° ahead of them: spine_01 off the pelvis {now * 100f:0.00} cm (the even waist's {old.Shift * 100f:0.0} cm)");
        Assert.InRange(now, 0f, 1e-4f);
        Assert.True(old.Shift > 0.01f, "the even waist slid the spine on the pelvis");
        Assert.Equal(10f * Deg, oldEven, 0.6f * Deg);   // the 30° evenly over three bones
    }

    [Fact]
    public void The_eye_is_turned_back_onto_the_view()
    {
        // The simulation's eye (the first-person camera, the aim, use) moves with no part of the chain: the eyes the pose has, each turn
        // undone about its joint, are where the eyes of the body all on the view are; the earlier build's way with the earlier build's waist, for the record.
        foreach (bool player in new[] { false, true })
        {
            if (player && Tests.People.PeopleLibraryTests.Library.Value is null) continue;
            var (assets, kind, rig) = Fresh(player);
            var on = Eyes(Pose(assets, kind, 0f, 0f, 0f), rig);
            float most = 0f, mostOld = 0f;
            foreach (var (legs, chest, head) in Limits())
            {
                assets.PersonWaistEven = false;
                var (pose, skin) = Posed(assets, kind, legs, chest, head);
                var back = TurnChain.EyesOnTheView(assets.ModelOf(kind).Skeleton, assets, CharacterAnimation.For(assets, kind), assets.Kind(kind).Person!, legs * Deg, Turn(chest, head), skin, Eyes(pose, rig));
                most = MathF.Max(most, Vector3.Distance(back, on));
                assets.PersonWaistEven = true;
                pose = Pose(assets, kind, legs, chest, head);
                var neck = pose[rig.Neck].Translation;
                var (sin, cos) = DetMath.SinCos(-head * Deg);   // The earlier build's camera: about the neck by the head's turn
                var eyes = Eyes(pose, rig);
                var oldBack = new Vector3(neck.X + (eyes.X - neck.X) * cos - (eyes.Y - neck.Y) * sin, neck.Y + (eyes.X - neck.X) * sin + (eyes.Y - neck.Y) * cos, eyes.Z);
                mostOld = MathF.Max(mostOld, Vector3.Distance(oldBack, on));
            }
            output.WriteLine($"{(player ? "the player" : "the made-up person")}: the eye turned back onto the view at most {most * 100f:0.00} cm from the eye "
                             + $"of the body on the view (the even waist's {mostOld * 100f:0.00} cm)");
            Assert.InRange(most, 0f, 0.001f);
        }

        static Vector3 Eyes(Matrix4x4[] pose, PersonRig rig) => (pose[rig.EyeL].Translation + pose[rig.EyeR].Translation) * 0.5f;
    }

    /// <summary>A kind of its own (its switches may be set): the made-up person, or the player (skips without the files).</summary>
    static (CharacterAssets Assets, int Kind, PersonRig Rig) Fresh(bool player)
    {
        if (!player)
        {
            var (a, k, p) = PeopleDoubles.Kind();
            return (a, k, p.Rig);
        }
        var (_, _, person) = Player();
        var assets = new CharacterAssets();
        int kind = PeopleKinds.Register(assets, person, Tests.People.PeopleLibraryTests.Library.Value!.Motions(PersonRecipe.Player.Sex), "player");
        return (assets, kind, person.Rig);
    }

    /// <summary>The made-up person spun by its view at <paramref name="speed"/> degrees a second for <paramref name="seconds"/>: the legs', chest's and head's turns each tick.</summary>
    static List<(float Legs, float Chest, float Head)> Spin(float speed, float seconds, LegTurnSettings legs)
    {
        var (assets, k, _) = PeopleDoubles.Kind();
        var (world, e) = Make(assets, k, Floor(), Vector3.Zero, legs: legs);
        for (int t = 0; t < World.TicksPerSecond / 2; t++) Step(world, e, Vector3.Zero, DetMath.HalfPi, true);
        var seen = new List<(float, float, float)>();
        for (int t = 0; t < seconds * World.TicksPerSecond; t++)
        {
            Step(world, e, Vector3.Zero, DetMath.WrapAngle(DetMath.HalfPi + speed * Deg * t * Tick), true);
            var chain = world.Get<PersonTurn>(e);
            seen.Add((world.Get<PoseRig>(e).LegYaw, chain.Chest, chain.Head));
            Assert.True(Finite(world, e));
        }
        return seen;
    }

    [Fact]
    public void The_leash_holds_the_hips_at_90_degrees_under_a_1000_degree_a_second_spin()
    {
        foreach (float speed in new[] { 1000f, -1000f })
        {
            var run = Spin(speed, 3f, S);
            float most = run.Max(r => MathF.Abs(r.Legs));
            foreach (var r in run)
            {
                Assert.InRange(MathF.Abs(r.Legs), 0f, S.Leash + 1e-5f);
                Assert.InRange(r.Chest - r.Legs, -S.SpineMost - 1e-4f, S.SpineMost + 1e-4f);
                Assert.InRange(r.Head - r.Chest, -S.NeckMost - 1e-4f, S.NeckMost + 1e-4f);
            }
            int held = run.Count(r => MathF.Abs(r.Legs) >= S.Leash - 1e-4f);
            var off = Spin(speed, 3f, S with { Leash = 0f });
            float offMost = off.Max(r => MathF.Abs(r.Legs));
            output.WriteLine($"spun at {speed:0}°/s for 3 s: the hips at most {most / Deg:0.0}° behind the view, held at the leash {held} ticks of {run.Count}; "
                             + $"without it (the even waist) {offMost / Deg:0.0}°");
            Assert.Equal(S.Leash, most, 4);
            Assert.True(held > run.Count / 2, "a spin that long is carried by the leash most of the time");
            Assert.True(offMost > 120f * Deg, "without the leash the hips fall further behind");
            // Every way they are behind, it is the way the view went: carried round, never flipped to the other side.
            Assert.All(run.Skip(30), r => Assert.True(r.Legs * speed <= 1e-4f));
        }
        // A turn that never reaches it is as before to the bit.
        Assert.Equal(Spin(200f, 0.4f, S), Spin(200f, 0.4f, S with { Leash = 0f }));
    }

    [Fact]
    public void The_turns_note_carries_the_leash_and_the_waist_and_a_note_without_them_is_the_even_waist()
    {
        Assert.EndsWith(" leash=90 waist=1", S.Note());
        var given = S with { Leash = 60f * Deg, WaistEven = true };
        var back = new LegTurnSettings().WithNote(given.Note());
        Assert.Equal(given.Note(), back.Note());
        Assert.Equal(60f * Deg, back.Leash, 5);
        Assert.True(back.WaistEven);
        Assert.False(S with { Leash = 180f * Deg } is { Leashed: true });
        Assert.Contains(" leash=0 ", (S with { Leash = DetMath.Pi }).Note());
        // A tape's note (no leash, no waist): the leash off and the earlier build's waist, whatever this build's settings say.
        string cm = "chain head=300 headLag=0.03 neck=25 chest=200 chestLag=0.06 spine=30 from=30 on=20 speedup=2 pivotFrom=75 pivotRate=240";
        var old = S.WithNote(cm);
        Assert.True(old.Chain);
        Assert.False(old.Leashed);
        Assert.True(old.WaistEven);
        Assert.Equal(cm + " leash=0 waist=0", old.Note());
        Assert.False(S.WithNote(null).Leashed);
    }
}
