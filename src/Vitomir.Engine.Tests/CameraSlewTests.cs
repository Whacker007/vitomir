// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Auto camera slewing (with the options word's bit 8): once the character has kept its
/// facing for a second the game camera eases round behind it over two, by half a cosine; a turn of
/// the character by more than 10° starts the wait over; a turn of the camera by hand leaves it be
/// until the character turns again; and with the option off nothing moves. And following
/// somebody new starts the slew over from their facing but not its clock, so the next
/// frame swings the camera by whatever share the old wait's age works out to, and is done.
/// </summary>
public class CameraSlewTests
{
    static WorldThing Someone(float yaw) => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", Vector3.Zero, Vector3.Zero, 0), Template = "test",
        Type = ThingType.Player, Actor = new Actor { Health = 100, MaxHealth = 100 }, Orientation = new Vector3(0, 0, yaw),
    };

    static WorldCamera Behind(WorldThing who, float cameraYaw, bool slew)
    {
        var camera = new WorldCamera { HeadOf = null, AutoSlew = slew };
        camera.Start(who);
        camera.Angles.Z = cameraYaw;
        return camera;
    }

    static void Run(WorldCamera camera, int fromMs, int toMs)
    {
        for (int t = fromMs + 50; t <= toMs; t += 50) camera.Tick(0.05f, t);
    }

    [Fact]
    public void After_a_second_of_the_same_facing_the_camera_eases_round_behind_the_character_over_two()
    {
        // The character turns as the camera starts. (`Start` follows the character,
        // which takes the slew's facing from it and leaves the reset's start time of 0 — so without a
        // turn the slew's wait is already over and it eases round from the first frame.)
        var who = Someone(30f);
        var camera = Behind(who, 90f, slew: true);
        who.Orientation = new Vector3(0, 0, 0f);
        Run(camera, 0, 1000);
        Assert.Equal(90f, camera.Angles.Z, 1);                    // still waiting
        Run(camera, 1000, 2050);
        Assert.InRange(camera.Angles.Z, 35f, 55f);                // about half way, by the half cosine
        Run(camera, 2050, 3200);
        Assert.Equal(0f, WorldCamera.Signed(camera.Angles.Z), 1);
    }

    [Fact]
    public void A_turn_by_hand_stays_until_the_character_turns_and_with_the_option_off_nothing_moves()
    {
        var who = Someone(0f);
        var camera = Behind(who, 90f, slew: true);
        Run(camera, 0, 1500);
        camera.TurnYaw(90f);
        camera.Tick(0.05f, 1550);
        camera.StopTurning();
        float put = camera.Angles.Z;
        Run(camera, 1550, 5000);
        Assert.Equal(put, camera.Angles.Z, 1);                    // done: left where the player put it
        who.Orientation = new Vector3(0, 0, 45f);                 // the character turns: the wait starts over
        Run(camera, 5000, 9000);
        Assert.Equal(45f, camera.Angles.Z, 1);

        var off = Behind(Someone(0f), 90f, slew: false);
        Run(off, 0, 5000);
        Assert.Equal(90f, off.Angles.Z, 1);
    }

    [Fact]
    public void Following_somebody_new_takes_their_facing_but_not_a_new_wait()
    {
        // Behind the first, and settled: the level-start swing ran out at two seconds.
        var first = Someone(0f);
        var camera = Behind(first, 0f, slew: true);
        Run(camera, 0, 3000);
        Assert.Equal(0f, WorldCamera.Signed(camera.Angles.Z), 1);

        // Somebody new, facing a quarter turn away, selected 11 seconds in:
        // their facing kept, started and done cleared, the start time still the reset's 0. The next
        // frame finds the facing unchanged and the wait long over, so it swings by the half cosine of
        // 11.05 seconds' age — 46% of the way, about 40.5° — and is done; there it stays.
        var second = Someone(90f);
        camera.SetSubject(second);
        camera.Tick(0.05f, 11_050);
        Assert.Equal(40.51f, camera.Angles.Z, 1);
        Run(camera, 11_050, 16_000);
        Assert.Equal(40.51f, camera.Angles.Z, 1);

        // The same selection 10 seconds in comes out nearly all the way round.
        var again = Behind(Someone(0f), 0f, slew: true);
        Run(again, 0, 3000);
        again.SetSubject(Someone(90f));
        again.Tick(0.05f, 10_050);
        Assert.Equal(89.91f, again.Angles.Z, 1);
    }

    /// <summary>
    /// **1.1's own jumps**: changing character with the
    /// option on, the camera jumps part of the way at once, by the half-cosine share of the old
    /// wait's age — 0.135 of the swing at an age of 3.527 s, and 0.947 at 17.724 s, where the formula
    /// predicts 0.132 and 0.954. Here the same two ages, from straight ahead (0, 1) to a new subject
    /// facing a quarter turn away (−1, 0): the camera points along (−s, 1 − s), so its yaw gives the
    /// share back as tan / (1 + tan), and it is the formula's.
    /// </summary>
    [Theory]
    [InlineData(3527, 0.1318f)]
    [InlineData(17_724, 0.9537f)]
    public void A_new_subject_jumps_the_camera_by_the_share_1_1_showed(int ageMs, float share)
    {
        var camera = Behind(Someone(0f), 0f, slew: true);
        Run(camera, 0, 3000);                                    // the wait began at the reset's 0
        camera.SetSubject(Someone(90f));
        camera.Tick(0.05f, ageMs);
        float tan = MathF.Tan(camera.Angles.Z * (MathF.PI / 180f));
        Assert.Equal(share, tan / (1f + tan), 0.001f);
    }
}
