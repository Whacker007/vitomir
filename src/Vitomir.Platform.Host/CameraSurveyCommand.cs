// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a developer's measurement of how the camera follows the body at speed
// ("some level of camera steadying is needed so that it's not constantly whipping around, especially when the player starts
// moving really fast").
using System.Globalization;
using System.Numerics;
using System.Text;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host;

/// <summary>
/// <c>--camera-survey &lt;folder&gt;</c> (a developer's tool, not a test): the player (Christof, the
/// sandbox's sheet) walks, runs and sprints, strafes, dodges and steps off a 2 m drop, in third person over the right
/// shoulder (and first person for the drop), each run twice — with the earlier build's camera (<see cref="CameraSettings.ThirdPersonFollowSeconds"/>
/// and <see cref="CameraSettings.BoomOutSeconds"/> 0: rigid) and with the camera's defaults. Learn Celerity
/// (<c>--learn celerity=5</c>) and the runs named <c>-cel</c> cast it first (the quick slot 1). Per tick
/// (<c>ticks.csv</c>): the body's feet, the camera's eye and look, where a point 1.3 m over the feet (the chest) falls on a
/// 16:9 picture (−1 to 1 across and up; beyond ±1 it is out of the picture), and the camera's acceleration. Per run
/// (<c>summary.csv</c>, and the log): the top speed, the chest's spread on the picture, the most it strays from where it
/// stands at rest, and the camera's peak acceleration (a whip is a large one).
/// </summary>
internal static class CameraSurveyCommand
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>A run: its name, where it starts (a place, or a named start), Celerity cast first, first person, and its frames by tick.</summary>
    sealed record Scenario(string Name, string? Place, string? Start, bool Celerity, bool First, int Ticks, Func<int, ActionFrame> Frame);

    static ActionFrame Move(Vector2 keys, Buttons held = Buttons.None) => new(keys, Vector2.Zero, held, default);

    static int T(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    static IEnumerable<Scenario> Runs(bool celerity)
    {
        // The free yard (x 20 to 56, y −15 to 21, free: Celerity's pace is not held to a human's): along +X from x 35 (a wall stands across x 32 to 34).
        const string west = "35,-10,0,0,0", south = "35,-12,0,90,90", middle = "38,3,0,0,0";
        foreach (var (name, gait) in new[] { ("walk", Buttons.Walk), ("run", Buttons.None), ("sprint", Buttons.Sprint) })
            yield return new Scenario(name, west, null, false, false, T(4f), t => t >= T(0.5f) && t < T(3f) ? Move(new(0, 1), gait) : ActionFrame.Idle);
        if (celerity)
        {
            // Cast at 0.05 s; the sprint from 0.6 s until the yard's end is near (about 30 m), then a stop.
            yield return new Scenario("sprint-cel", west, null, true, false, T(3f), t => t >= T(0.6f) && t < T(1.6f) ? Move(new(0, 1), Buttons.Sprint) : ActionFrame.Idle);
            yield return new Scenario("strafe-cel", south, null, true, false, T(3f), t => t >= T(0.6f) && t < T(1.6f) ? Move(new(1, 0)) : ActionFrame.Idle);
            yield return new Scenario("sprint-cel-first", west, null, true, true, T(3f), t => t >= T(0.6f) && t < T(1.6f) ? Move(new(0, 1), Buttons.Sprint) : ActionFrame.Idle);
        }
        yield return new Scenario("sprint-first", west, null, false, true, T(4f), t => t >= T(0.5f) && t < T(3f) ? Move(new(0, 1), Buttons.Sprint) : ActionFrame.Idle);
        // Into the wall across x 32 at a sprint (and at Celerity 5): the stop at the wall.
        yield return new Scenario("slam", "21,-10,0,0,0", null, false, false, T(3.5f), t => t >= T(0.5f) && t < T(3f) ? Move(new(0, 1), Buttons.Sprint) : ActionFrame.Idle);
        if (celerity) yield return new Scenario("slam-cel", "21,-10,0,0,0", null, true, false, T(2.5f), t => t >= T(0.6f) && t < T(2f) ? Move(new(0, 1), Buttons.Sprint) : ActionFrame.Idle);
        yield return new Scenario("strafe", south, null, false, false, T(4f), t => t >= T(0.5f) && t < T(3f) ? Move(new(1, 0)) : ActionFrame.Idle);
        yield return new Scenario("dodge", middle, null, false, false, T(2f), t => t == T(0.5f) ? Move(new(1, 0), Buttons.Dodge) : ActionFrame.Idle);
        if (celerity) yield return new Scenario("dodge-cel", middle, null, true, false, T(2f), t => t == T(0.6f) ? Move(new(1, 0), Buttons.Dodge) : ActionFrame.Idle);
        yield return new Scenario("drop2", null, "drops-2", false, false, T(4.5f), t => t >= T(0.3f) ? Move(new(0, 1), Buttons.Walk) : ActionFrame.Idle);
        yield return new Scenario("drop2-first", null, "drops-2", false, true, T(4.5f), t => t >= T(0.3f) ? Move(new(0, 1), Buttons.Walk) : ActionFrame.Idle);
    }

    public static int Run(Cli cli, PlatformSettings settings, HostLog log, string folder)
    {
        Directory.CreateDirectory(folder);
        var options = Program.Options(cli, settings, log) with { People = 0, Say = _ => { }, Body = Program.BodyOf(cli, settings, combat: true) };   // 1.1's Christof unless --body
        bool celerity = options.Learn.Any(l => l.Power.Contains("celerity", StringComparison.OrdinalIgnoreCase));
        var ticks = new StringBuilder("camera,run,t,feet_x,feet_y,feet_z,eye_x,eye_y,eye_z,yaw_deg,pitch_deg,boom_m,screen_x,screen_y,eye_over_feet_m,accel,bob_x,bob_y,bob_z\n");
        var summary = new StringBuilder("camera,run,top_speed,screen_x_min,screen_x_max,screen_y_min,screen_y_max,stray_most,cam_accel_peak,body_accel_peak,eye_over_feet_min,eye_over_feet_max\n");
        var cameras = new (string Name, CameraSettings Settings)[]
        {
            ("rigid", CameraSettings.Rigid),
            ("default", new CameraSettings()),
        };
        foreach (var run in Runs(celerity))
            foreach (var (camName, cam) in cameras)
            {
                using var session = Session.Create(options with { Cameras = cam, Place = run.Place, Start = run.Start ?? options.Start });
                var w = session.World;
                var e = session.Player;
                var view = w.Service<ViewState>();
                var eyes = new List<Vector3>();
                var feet = new List<Vector3>();
                float topSpeed = 0f, xMin = 9f, xMax = -9f, yMin = 9f, yMax = -9f, stray = 0f, camPeak = 0f, bodyPeak = 0f, overMin = 99f, overMax = -99f;
                Vector2 rest = default;
                for (int t = 0; t < run.Ticks; t++)
                {
                    var frame = run.Frame(t);
                    if (run.First && t is 0 or 2) frame = frame with { Held = frame.Held | Buttons.SwitchView, Move = frame.Move };
                    if (run.First && t == 1) frame = frame with { Held = frame.Held & ~Buttons.SwitchView };
                    if (run.Celerity && t == T(0.05f)) frame = frame with { Held = frame.Held | Buttons.Slot1 };
                    session.Step(frame);
                    var f = w.Get<Body>(e).Feet;
                    var v = view.View;
                    eyes.Add(v.Eye);
                    feet.Add(f);
                    var screen = Screen(v, f + new Vector3(0, 0, 1.3f));
                    if (t == T(0.4f)) rest = screen;
                    float accel = 0f, bodyAccel = 0f, speed = 0f;
                    if (eyes.Count >= 3)
                    {
                        accel = (eyes[^1] - 2f * eyes[^2] + eyes[^3]).Length() / (World.TickSeconds * World.TickSeconds);
                        bodyAccel = (feet[^1] - 2f * feet[^2] + feet[^3]).Length() / (World.TickSeconds * World.TickSeconds);
                    }
                    if (feet.Count >= 2) speed = (feet[^1] - feet[^2]).Length() / World.TickSeconds;
                    float over = v.Eye.Z - f.Z;
                    if (t >= T(0.4f))
                    {
                        topSpeed = MathF.Max(topSpeed, speed);
                        (xMin, xMax, yMin, yMax) = (MathF.Min(xMin, screen.X), MathF.Max(xMax, screen.X), MathF.Min(yMin, screen.Y), MathF.Max(yMax, screen.Y));
                        stray = MathF.Max(stray, Vector2.Distance(screen, rest));
                        // A placing (more than a walk's move in a tick) is not a whip.
                        if (speed < 60f) (camPeak, bodyPeak) = (MathF.Max(camPeak, accel), MathF.Max(bodyPeak, bodyAccel));
                        (overMin, overMax) = (MathF.Min(overMin, over), MathF.Max(overMax, over));
                    }
                    ticks.Append(string.Create(Ic, $"{camName},{run.Name},{t / (float)World.TicksPerSecond:0.000},{f.X:0.000},{f.Y:0.000},{f.Z:0.000},{v.Eye.X:0.000},{v.Eye.Y:0.000},{v.Eye.Z:0.000},{v.Yaw * DetMath.RadToDeg:0.0},{v.Pitch * DetMath.RadToDeg:0.0},{view.Boom:0.000},{screen.X:0.0000},{screen.Y:0.0000},{over:0.000},{accel:0.0},{view.Head.X - view.Eyes.X:0.0000},{view.Head.Y - view.Eyes.Y:0.0000},{view.Head.Z - view.Eyes.Z:0.0000}\n"));
                }
                summary.Append(string.Create(Ic, $"{camName},{run.Name},{topSpeed:0.00},{xMin:0.000},{xMax:0.000},{yMin:0.000},{yMax:0.000},{stray:0.000},{camPeak:0},{bodyPeak:0},{overMin:0.000},{overMax:0.000}\n"));
                log.Say(string.Create(Ic, $"camera survey: {camName} {run.Name}: top {topSpeed:0.0} m/s; the chest on the picture x {xMin:0.000} to {xMax:0.000}, y {yMin:0.000} to {yMax:0.000} (strays {stray:0.000} from rest); the camera's peak acceleration {camPeak:0} m/s² (the body's {bodyPeak:0}); the eye over the feet {overMin:0.00} to {overMax:0.00} m"));
            }
        File.WriteAllText(Path.Combine(folder, "ticks.csv"), ticks.ToString());
        File.WriteAllText(Path.Combine(folder, "summary.csv"), summary.ToString());
        log.Say($"camera survey: {Path.GetFullPath(folder)}");
        return 0;
    }

    /// <summary>Where <paramref name="p"/> falls on a 16:9 picture from <paramref name="v"/>: −1 to 1 across (right positive) and up; ±9 behind the eye.</summary>
    static Vector2 Screen(in ViewPoint v, Vector3 p)
    {
        var forward = DetMath.Direction(v.Yaw, v.Pitch);
        var (s, c) = DetMath.SinCos(v.Yaw);
        var right = new Vector3(s, -c, 0f);
        var up = Vector3.Cross(right, forward);
        var d = p - v.Eye;
        float z = Vector3.Dot(d, forward);
        if (z < 0.01f) return new Vector2(9f, 9f);
        float tan = DetMath.Tan(MathF.Max(v.FieldOfViewDegrees, 1f) * DetMath.DegToRad / 2f);
        return new Vector2(Vector3.Dot(d, right) / (z * tan * 16f / 9f), Vector3.Dot(d, up) / (z * tan));
    }
}
