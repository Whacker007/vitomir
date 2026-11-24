// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Tests;

/// <summary>The ladder's pose: the hang's slot, the hands on the ladder's face and the right foot on it by IK.</summary>
public sealed partial class TraversalPoseTests
{
    [Fact]
    public void On_a_ladder_the_hands_hold_its_face_and_the_right_foot_stands_on_it_as_the_body_climbs()
    {
        var (assets, kind) = PersonKind();
        // The ladder's face is the wall's at y = 0.6, looking out along −Y; the body on it 0.35 m out, facing it (+Y).
        var level = Wall(top: 4f);
        var foot = new Vector3(0, 0.25f, 0f);
        var (world, e) = Make(assets, kind, level, foot);
        for (int t = 0; t <= 120; t++)
        {
            var feet = foot + new Vector3(0, 0, 0.5f + t / 120f);   // a metre's climb in a second
            var lip = new Vector3(0, 0.6f, feet.Z + BodyMotor.LadderHands);
            Step(world, e, feet, false, new Traversal
            {
                Kind = TraversalKind.Ladder, Progress = feet.Z / 3f, Seconds = 3f, From = foot, To = foot + new Vector3(0, 0, 3f),
                Lip = lip, LipNormal = new Vector3(0, -1, 0), Height = 4f,
            }, new Vector3(0, 0, 1f));
            if (t < 30) continue;   // the pose eases in
            Assert.InRange(Vector3.Distance(Bone(world, e, assets, LeftKnuckles), lip + new Vector3(-0.2f, 0, 0)), 0f, 0.03f);
            Assert.InRange(Vector3.Distance(Bone(world, e, assets, RightKnuckles), lip + new Vector3(0.2f, 0, 0)), 0f, 0.03f);
            var right = Bone(world, e, assets, RightAnkle);
            Assert.True(right.Y > 0.45f && right.Y < 0.6f, $"the right ankle at {right}: on the ladder's face, in front of it");
        }
    }
}
