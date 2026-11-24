// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render;

namespace Vitomir.Platform.Host.Arena;

/// <summary>
/// Pictures of an arena bout (<c>--picture-at s, s, …</c>): the first bout of the first point run again (it is the
/// same bout, digest for digest) and drawn at each time asked from the session's spectator (<see cref="Session.Spectator"/>:
/// 4.5 m out to the side of the first two fighters' line, 1.7 m up, looking at their middle), with the hit volumes when
/// <c>--show-hit-volumes</c>. Drawing changes nothing in the world.
/// </summary>
internal static class ArenaPictures
{
    internal static void Draw(ArenaRun run, ArenaSpec spec, string folder, IReadOnlyList<float> times, (int W, int H) size, PlatformSettings settings,
        bool hitVolumes, Action<string> say, bool cues = false, bool swing = false)
    {
        if (times.Count == 0) return;
        Directory.CreateDirectory(Path.Combine(folder, "pictures"));
        var options = run.OptionsFor(0, 0);
        using var session = Session.Create(options);
        using var renderer = PlatformRenderer.CreateHeadless(size.W, size.H, new RendererOptions(settings.Driver, settings.Quality, false, Look: Session.LookFor(options.Night)));
        renderer.CharacterTextures = session.Texture;
        renderer.CursorArt = session.CursorArt;
        renderer.LoadLevel(options.Level);
        session.ShowHitVolumes = hitVolumes;
        session.ShowCues = cues;   // the flurry's cue with --show-cues
        session.ShowSwingTrail = swing;   // the swing's trail with --show-swing
        session.PictureAspect = size.W / (float)size.H;
        var w = session.World;
        foreach (float t in times.Order())
        {
            long until = (long)MathF.Round(t * World.TicksPerSecond);
            while (session.Tick < until) session.Step(ActionFrame.Idle);
            var view = new FrameView { InterfaceScale = settings.InterfaceScale };
            session.Fill(view, 1f);
            view.StatusLine = string.Create(CultureInfo.InvariantCulture,
                $"{PlatformVersion.Text}   arena, bout 0 (seed {options.Seed}), {t:0.00} s, {Gap(w, session):0.00} m apart: {string.Join(", ", session.Fighters.Select(f => $"{session.NameOf(f.Packed)} {Doing(w, f)}"))}");
            for (int i = 0; i < Math.Max(1, renderer.StillFrames); i++) renderer.Draw(view);
            string png = Path.Combine(folder, "pictures", string.Create(CultureInfo.InvariantCulture, $"bout0-{t:00.00}s.png"));
            renderer.SaveScreenshot(png);
            say($"arena: picture {png}: {view.StatusLine}");
        }
    }

    // ---- the film ----

    /// <summary>
    /// A film of an arena bout (<c>--film-at s0:s1[:fps]</c>, who judges a motion by looking): the first bout of
    /// the first point run again (the same bout, digest for digest) and drawn at <paramref name="fps"/> frames a second of the
    /// bout's time from <paramref name="from"/> to <paramref name="to"/> (by default 120, one a tick; between ticks the bodies
    /// are drawn where they are between them, the poses the tick's), from <paramref name="view"/>: <c>side</c> (the arena's
    /// spectator), <c>near</c> (the same side, 2.4 m out), <c>first</c> (the first fighter's eyes: its head not drawn, looking at
    /// its foe's chest) or <c>third</c> (2.5 m behind its shoulder, as the game's third person). Frames go to
    /// <c>film/&lt;name&gt;/f0000.png</c>…; with <c>ffmpeg</c> on the path, <c>film/&lt;name&gt;.mp4</c> (real time, 60 fps),
    /// <c>&lt;name&gt;-quarter.mp4</c> (a quarter of real time) and a small <c>&lt;name&gt;.gif</c> (quarter speed, 480 wide).
    /// <paramref name="caption"/> is drawn on the status line with the time. Drawing changes nothing in the world.
    /// </summary>
    internal static void Film(ArenaRun run, string folder, string name, float from, float to, float fps, string view, string? caption, (int W, int H) size,
        PlatformSettings settings, bool hitVolumes, bool swing, Action<string> say, float arc = 0f, bool combatLog = false)
    {
        if (to <= from || fps <= 0f) return;
        string frames = Path.Combine(folder, "film", name);
        Directory.CreateDirectory(frames);
        foreach (var old in Directory.GetFiles(frames, "f*.png")) File.Delete(old);
        var options = run.OptionsFor(0, 0);
        using var session = Session.Create(options);
        using var renderer = PlatformRenderer.CreateHeadless(size.W, size.H, new RendererOptions(settings.Driver, settings.Quality, false, Look: Session.LookFor(options.Night)));
        renderer.CharacterTextures = session.Texture;
        renderer.CursorArt = session.CursorArt;
        renderer.LoadLevel(options.Level);
        session.ShowHitVolumes = hitVolumes;
        session.ShowSwingTrail = swing;
        session.PictureAspect = size.W / (float)size.H;
        var w = session.World;
        if (view == "first" && session.Fighters.Count > 0) session.FilmFirstPerson = session.Fighters[0];
        // The first fighter's swing arc (--swing-arc) and the combat log's window (--show-combat-log).
        session.ShowSwingArc = arc > 0f;
        session.SwingArcOpacity = arc;
        if (arc > 0f && session.Fighters.Count > 0) session.ArcOf = session.Fighters[0];
        session.ShowCombatLog = combatLog;
        int count = (int)MathF.Floor((to - from) * fps + 1e-3f) + 1;
        for (int i = 0; i < count; i++)
        {
            double t = from + i / (double)fps;
            double ticks = t * World.TicksPerSecond;
            long until = (long)Math.Ceiling(ticks - 1e-6);
            while (session.Tick < until) session.Step(ActionFrame.Idle);
            float alpha = (float)Math.Clamp(1.0 - (until - ticks), 0.0, 1.0);
            var frame = new FrameView { InterfaceScale = settings.InterfaceScale };
            session.Fill(frame, alpha);
            if (view != "side" && session.Fighters.Count >= 2) frame.View = FilmView(w, session, view, frame.View);
            frame.ShowReticle = false;
            frame.StatusLine = string.Create(CultureInfo.InvariantCulture, $"{caption ?? PlatformVersion.Text}   {t:0.000} s   {Doing(w, session.Fighters[0])}");
            for (int s = 0; s < Math.Max(1, renderer.StillFrames); s++) renderer.Draw(frame);
            renderer.SaveScreenshot(Path.Combine(frames, string.Create(CultureInfo.InvariantCulture, $"f{i:0000}.png")));
        }
        session.FilmFirstPerson = default;
        say(string.Create(CultureInfo.InvariantCulture, $"arena: film {frames}: {count} frames, {from:0.###} to {to:0.###} s at {fps:0.#} a second, {view}"));
        Encode(frames, Path.Combine(folder, "film", name), fps, say);
    }

    /// <summary>The film's frames made into videos with <c>ffmpeg</c> when it is on the path: real time, a quarter of it, and a small gif.</summary>
    internal static void Encode(string frames, string stem, float fps, Action<string> say)
    {
        string inRate = fps.ToString("0.###", CultureInfo.InvariantCulture), quarter = (fps / 4f).ToString("0.###", CultureInfo.InvariantCulture);
        string pattern = Path.Combine(frames, "f%04d.png");
        bool ok = Run("ffmpeg", $"-y -loglevel error -framerate {inRate} -i \"{pattern}\" -r 60 -c:v libx264 -pix_fmt yuv420p -crf 18 \"{stem}.mp4\"")
                  && Run("ffmpeg", $"-y -loglevel error -framerate {quarter} -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p -crf 18 \"{stem}-quarter.mp4\"")
                  && Run("ffmpeg", $"-y -loglevel error -framerate {quarter} -i \"{pattern}\" -vf \"fps=30,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse\" \"{stem}.gif\"");
        say(ok ? $"arena: film {stem}.mp4, {stem}-quarter.mp4 and {stem}.gif" : $"arena: film {frames}: frames only (ffmpeg not on the path, or it failed)");
    }

    static bool Run(string program, string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program, args) { UseShellExecute = false, RedirectStandardError = true });
            if (p is null) return false;
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>
    /// The film's camera: <c>near</c>, the spectator's side 2.4 m out; <c>first</c>, the first fighter's eyes (1.59 m over
    /// its feet, 0.2 m ahead, as the game's steady first-person eye) looking at its foe's chest; <c>third</c>, 2.2 m behind it and
    /// 0.9 m to its right, 1.8 m up (the game's third person is 2.5 m back and 0.5 m aside, where the body hides the blade's
    /// meeting), looking at the space 0.9 m before it.
    /// </summary>
    static ViewPoint FilmView(World w, Session session, string view, ViewPoint side)
    {
        var a = w.Get<Body>(session.Fighters[0]);
        var b = w.Get<Body>(session.Fighters[1]);
        var toward = new Vector2(b.Feet.X - a.Feet.X, b.Feet.Y - a.Feet.Y);
        float yaw = toward.LengthSquared() > 1e-6f && toward.Length() < 3f ? MathF.Atan2(toward.Y, toward.X) : a.Yaw;
        var fwd = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        var right = new Vector3(fwd.Y, -fwd.X, 0f);
        var chest = toward.Length() < 3f ? b.Feet + new Vector3(0f, 0f, 1.25f) : a.Feet + fwd * 1.5f + new Vector3(0f, 0f, 1.25f);
        ViewPoint Looking(Vector3 eye, Vector3 at, float field, float near = 0f)
        {
            var look = at - eye;
            return new ViewPoint(eye, MathF.Atan2(look.Y, look.X), MathF.Atan2(look.Z, new Vector2(look.X, look.Y).Length()), field, near);
        }
        return view switch
        {
            "near" => Looking(a.Feet + fwd * 0.65f - right * 2.4f + new Vector3(0f, 0f, 1.5f), a.Feet + fwd * 0.65f + new Vector3(0f, 0f, 1.25f), 60f),
            "first" => Looking(a.Feet + fwd * 0.2f + new Vector3(0f, 0f, 1.59f), chest, 80f, 0.05f),
            "third" => Looking(a.Feet - fwd * 2.2f + right * 0.9f + new Vector3(0f, 0f, 1.8f), a.Feet + fwd * 0.9f + new Vector3(0f, 0f, 1.3f), 60f),
            _ => side,
        };
    }
    // ---- end

    static float Gap(World w, Session session)
    {
        var a = w.Get<Body>(session.Fighters[0]).Feet;
        var b = w.Get<Body>(session.Fighters[1]).Feet;
        return new Vector2(a.X - b.X, a.Y - b.Y).Length();
    }

    static string Doing(World w, EntityId e)
    {
        if (!w.Has<Animation.CharacterAnimation>(e)) return "";
        var a = w.Get<Animation.CharacterAnimation>(e);
        string doing = a.Action == Animation.ActionRole.None || a.ActionEnding ? "ready" : a.Action.ToString().ToLowerInvariant();
        if (w.Has<Combat.DuelBrain>(e) && w.Get<Combat.DuelBrain>(e) is var b && w.Has<Bodies.MoveIntent>(e) && w.Get<Bodies.MoveIntent>(e) is var m)
            doing += string.Create(CultureInfo.InvariantCulture, $" ({(m.Direction.LengthSquared() > 0.01f ? m.Gait.ToString().ToLowerInvariant() : "still")}{(b.BackOffLeft > 0 ? ", backing off" : "")})");
        // A dodge under way, its phase.
        if (w.TryStore<Bodies.Dodging>(out var dodges) && dodges.Has(e) && dodges.Get(e) is { Active: true } d)
            doing += $", dodging ({d.Phase.ToString().ToLowerInvariant()})";
        return doing;
    }
}
