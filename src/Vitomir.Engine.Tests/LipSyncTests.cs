// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Lip-sync (<see cref="LipSync"/>): a track read at 30 frames a second with the
/// frame floored (under the round-down rounding mode), the frame held to the track's length
/// less two, each shape turning its bone by twice its `mouth` line's angles (the motion set stores them
/// twice and both are added), and — in the monastery, with a recorded line and its `.nlp` — the
/// jaw moving while the line plays and resting when it ends.
/// </summary>
public class LipSyncTests
{
    static NmsMotionSet Set() => NmsMotionSet.Read(Encoding.Latin1.GetBytes(
        "stand x.nad\r\nmouth a 3 (0.0,0.0,0.0)\r\nmouth b 3 (-1.5,0.0,0.0)\r\nmouth d 3 (-8.0,0.0,0.0)\r\n" +
        "mouth e 2 (0.0,4.0,0.0)\r\nmouth e 3 (-5.5,0.0,0.0)\r\n"), "Test.nms");

    [Fact]
    public void A_track_is_read_at_thirty_frames_a_second_floored_and_blended_to_the_next()
    {
        byte[] track = [1, 2, 3, 4];
        Assert.Equal(new LipSync.Shapes(2, 3, 0.5f), LipSync.At(track, 50));        // 1.5 frames in
        Assert.Equal(new LipSync.Shapes(1, 2, 0f), LipSync.At(track, 0));
        // Past the end the frame is held at length − 2 and the blend runs on from the unheld frame.
        var late = LipSync.At(track, 1010)!.Value;                                   // 30.3 frames in
        Assert.Equal((byte)3, late.First);
        Assert.Equal((byte)4, late.Second);
        Assert.Equal(0.3f, late.Lerp, 3);
        Assert.Null(LipSync.At([], 100));
    }

    [Fact]
    public void Each_shape_turns_its_bone_by_twice_its_angles_and_the_last_line_for_a_letter_wins()
    {
        var set = Set();
        var rotation = new Vector3[5];
        LipSync.Apply(set, new LipSync.Shapes(3, 3, 0f), rotation);                  // d, all of it
        Assert.Equal(new Vector3(-16f, 0f, 0f), rotation[3]);

        rotation = new Vector3[5];
        LipSync.Apply(set, new LipSync.Shapes(1, 3, 0.25f), rotation);               // ¾ b, ¼ d
        Assert.Equal(0.75f * 2 * -1.5f + 0.25f * 2 * -8f, rotation[3].X, 4);

        // `e` is written twice: the second line, on bone 3, is the record.
        rotation = new Vector3[5];
        LipSync.Apply(set, new LipSync.Shapes(4, 4, 0f), rotation);
        Assert.Equal(Vector3.Zero, rotation[2]);
        Assert.Equal(-11f, rotation[3].X, 4);

        // A letter the set has no line for adds nothing (a zeroed record).
        rotation = new Vector3[5];
        LipSync.Apply(set, new LipSync.Shapes(7, 8, 0.5f), rotation);
        Assert.All(rotation, r => Assert.Equal(Vector3.Zero, r));
    }

    [Fact]
    public void A_recorded_line_with_a_track_moves_the_speaker_s_jaw_while_it_plays()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        Assert.SkipUnless(files.TryRead("sounds/dialog/Wilhem_8_2_344.nlp", out _), "Wilhem_8_2_344.nlp is not in this install");
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var place = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof");
        var audio = new GameAudio(files);
        place.Audio = audio;
        audio.Enter(place);
        var sim = new Simulation(place);

        // The line loads with its track beside it.
        var line = audio.Bank.Load("Wilhem_8_2_344", stream: true)!;
        Assert.NotEmpty(line.Lips);
        Assert.True((line.Flags & SoundFlags.LipSync) != 0);

        // Christof's set has the nine mouths; he speaks
        // it, as a conversation's line is spoken.
        var christof = place.Player!.Thing;
        var anim = christof.Animation!;
        Assert.Equal(9, anim.Set.Mouths.Select(m => m.Letter).Distinct().Count());
        int jaw = anim.Set.Mouths[0].Bone;
        int id = audio.Mixer.Play2D(line, 1f, 0.5f, 0);
        christof.MouthBusyUntil = audio.Mixer.GameMs + line.LengthMs;
        christof.MouthChannel = id;

        var moved = new HashSet<float>();
        for (int i = 0; i < 60; i++)
        {
            sim.Step(1f / 30f);
            audio.Frame(1f / 30f, place.Milliseconds, christof.Position, Vector3.UnitY, Vector3.UnitX, christof.Sector);
            Assert.NotNull(anim.Mouth);
            anim.PoseBones();
            moved.Add(MathF.Round(anim.BoneMatrix(jaw).M22, 4));
        }
        Assert.True(moved.Count > 3, "the jaw did not move with the line");

        // The line over, the mouth is the animation's again.
        audio.Mixer.Stop(id);
        christof.MouthBusyUntil = place.Milliseconds;
        sim.Step(1f / 30f);
        Assert.Null(anim.Mouth);
    }
}
