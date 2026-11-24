// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The player's two camera settings — <c>firstPersonActionSway</c> (cm) and <c>thirdPersonFollow</c> (s)
/// — saved and loaded, kept within their ends, the camera's numbers made from them (none when they are the defaults), and
/// carried on a tape (they move the simulation's view). Needs no game files.
/// </summary>
public sealed class CameraSettingsTests
{
    static string TempPath(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-camera-settings-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    [Fact]
    public void The_defaults_are_the_cameras_and_a_saved_file_loads_the_same()
    {
        var s = new PlatformSettings();
        Assert.Equal(4f, s.FirstPersonActionSway);
        Assert.Equal(0.08f, s.ThirdPersonFollow);
        Assert.Null(CameraSettings.Of(s.FirstPersonActionSway, s.ThirdPersonFollow, s.ThirdPersonMeleeFraming));     // the defaults: no numbers of the player's
        Assert.False(s.ThirdPersonMeleeFraming);                                                                  // the melee framing off by default
        Assert.True(CameraSettings.Of(4f, 0.08f, meleeFraming: true)!.MeleeFraming);
        var d = new CameraSettings();
        Assert.Equal(0.04f, d.ActionAllowance);
        Assert.Equal(0.08f, d.ThirdPersonFollowSeconds);

        s.FirstPersonActionSway = 12f;
        s.ThirdPersonFollow = 0.15f;
        var path = TempPath("camera.json");
        s.Save(path);
        Assert.Contains("\"firstPersonActionSway\"", File.ReadAllText(path), StringComparison.Ordinal);
        var back = PlatformSettings.Load(path);
        Assert.Equal(12f, back.FirstPersonActionSway);
        Assert.Equal(0.15f, back.ThirdPersonFollow);
        var c = CameraSettings.Of(back.FirstPersonActionSway, back.ThirdPersonFollow)!;
        Assert.Equal(0.12f, c.ActionAllowance, 6);
        Assert.Equal(0.15f, c.ThirdPersonFollowSeconds, 6);
        Assert.Equal(d.LookDownAhead, c.LookDownAhead);     // the rest stay the camera's own
    }

    [Fact]
    public void Out_of_range_values_are_kept_within_their_ends()
    {
        var c = CameraSettings.Of(500f, -2f)!;
        Assert.Equal(1f, c.ActionAllowance);
        Assert.Equal(0f, c.ThirdPersonFollowSeconds);
        var n = CameraSettings.Of(float.NaN, 9f)!;
        Assert.Equal(0.04f, n.ActionAllowance);
        Assert.Equal(0.3f, n.ThirdPersonFollowSeconds);
    }

    [Fact]
    public void A_tape_carries_the_cameras_numbers_and_one_without_them_has_the_defaults()
    {
        var mine = new CameraSettings { ActionAllowance = 0.1f, ThirdPersonFollowSeconds = 0.2f, MeleeFraming = true };
        Assert.Equal(mine, CameraSettings.FromNote(mine.ToNote()));
        Assert.Equal(CameraSettings.Rigid, CameraSettings.FromNote(CameraSettings.Rigid.ToNote()));
        Assert.Null(CameraSettings.FromNote(""));
        Assert.Null(CameraSettings.FromNote("1;2"));

        var options = new SessionOptions { Level = "x" };
        var header = new TapeHeader { Notes = new() { [SessionOptions.CamerasNote] = mine.ToNote() } };
        Assert.Equal(mine, options.From(header).Cameras);
        Assert.Null(options.From(new TapeHeader()).Cameras);
        Assert.Null(options.From(new TapeHeader { Notes = new() { [SessionOptions.CamerasNote] = "" } }).Cameras);
    }
}
