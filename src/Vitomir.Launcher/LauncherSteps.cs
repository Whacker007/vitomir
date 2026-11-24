// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher with no window: stills of its pages, and a developer's steps through them, for the proof.
using System.Globalization;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render;
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>
/// **The launcher's stills and steps** (a developer's: <c>--headless</c>): the launcher run with no window, drawn by the same
/// renderer into a picture of its own (<see cref="PlatformRenderer.CreateHeadless"/>), through a list of steps separated by
/// <c>;</c> (<c>--steps "&lt;steps&gt;"</c>, or <c>--steps @&lt;file&gt;</c>, a step a line):
/// <list type="bullet">
/// <item><c>page &lt;name&gt;</c>, <c>act &lt;action&gt; [argument]</c> (what a button fires: <c>check</c>, <c>import</c>, <c>play</c>,
/// <c>choose 0</c>, <c>selectall</c>, <c>notice a</c>, <c>addfrom &lt;pack&gt;</c>, <c>moduletoggle &lt;id&gt;</c>, ...), <c>path &lt;folder&gt;</c> (the folder box's words), <c>pick &lt;save&gt;</c> (a save
/// selected), <c>focus &lt;piece&gt;</c> (as a key would move it there, so a look shows it), <c>show &lt;piece&gt;</c> (the page scrolled to it),
/// <c>hover &lt;piece&gt;</c> (the pointer over its middle), <c>press &lt;piece&gt;</c> (and its button down), <c>release</c> (the pointer
/// taken off it and its button up: no click);</item>
/// <item><c>wait idle</c> (the running step ended, the modules' import and check with it, and the user folder's move), <c>wait move &lt;share&gt;</c> (the move that far), <c>wait progress &lt;share&gt;</c> (the running
/// step that far), <c>wait module &lt;share&gt;</c> (a module's import that far), <c>wait game</c>
/// (the game ended and the launcher back);</item>
/// <item><c>still &lt;png&gt;</c>: the window's picture as it is now; <c>size &lt;W&gt;x&lt;H&gt;[@&lt;scale&gt;]</c>: the steps after it laid out and
/// drawn at that size (and display scale), so one run makes a state's pictures at several sizes.</item>
/// </list>
/// Each step is said in the launcher's log. Answers 0, or 2 for a step it does not know.
/// </summary>
public static class LauncherSteps
{
    /// <summary>
    /// Runs <paramref name="steps"/> on <paramref name="launcher"/> into pictures <paramref name="size"/> pixels, laid out as on a display
    /// scaled by <paramref name="scale"/> (<c>--scale 1.5</c>: the picture a 1280 x 800 window makes at Windows' 150 % is 1920 x 1200).
    /// </summary>
    public static int Run(Launcher launcher, IReadOnlyList<string> steps, (int Width, int Height) size, Action<string> say, float scale = 1f)
    {
        // A renderer (and a list, which carries the look's pictures to it) for each size asked for.
        var drawers = new Dictionary<(int, int), (PlatformRenderer Renderer, DrawList List)>();
        var view = new FrameView { ShowReticle = false };
        try { return Steps(launcher, steps, size, scale, say, drawers, view); }
        finally
        {
            // The last made first: the first made SDL's video and lets it go with it.
            foreach (var d in drawers.Values.Reverse()) d.Renderer.Dispose();
        }
    }

    static int Steps(Launcher launcher, IReadOnlyList<string> steps, (int Width, int Height) size, float scale, Action<string> say,
                     Dictionary<(int, int), (PlatformRenderer Renderer, DrawList List)> drawers, FrameView view)
    {
        foreach (var raw in steps)
        {
            string step = raw.Trim();
            if (step.Length == 0 || step.StartsWith('#')) continue;
            say($"launcher step: {step}");
            int space = step.IndexOf(' ');
            string verb = space < 0 ? step : step[..space], rest = space < 0 ? "" : step[(space + 1)..].Trim();
            launcher.Poll();
            switch (verb)
            {
                case "page": launcher.Act("page", rest); break;
                case "act":
                    {
                        int at = rest.IndexOf(' ');
                        launcher.Act(at < 0 ? rest : rest[..at], at < 0 ? "" : rest[(at + 1)..].Trim());
                        break;
                    }
                case "path": launcher.Setup.Path = rest; break;
                case "pick": launcher.Setup.Picked.Add(rest); break;
                case "show":
                    {
                        // The page scrolled so that the piece is at its top (the stills' own, to show a part further down).
                        launcher.Fill();
                        var metrics = LauncherWindow.Metrics(size.Width, size.Height, scale);
                        launcher.Screen.Layout(metrics);
                        if (launcher.Screen.Find(rest) is not { } shown) { say($"launcher step: no piece {rest}"); break; }
                        for (var up = shown.Parent; up is not null; up = up.Parent)
                            if (up is Scroll scroll) { scroll.Offset = Math.Max(0f, scroll.Offset + shown.Rect.Y - scroll.Rect.Y - 10f); break; }
                        launcher.Screen.Layout(metrics);
                        break;
                    }
                case "focus":
                    launcher.Fill();
                    launcher.Screen.Layout(LauncherWindow.Metrics(size.Width, size.Height, scale));
                    launcher.TakeFocus();
                    if (launcher.Screen.Find(rest) is { } piece)
                    {
                        launcher.Screen.SetFocus(piece);
                        launcher.Screen.FocusShown = true;      // as a key would have moved it there
                    }
                    else say($"launcher step: no piece {rest}");
                    break;
                case "hover" or "press":
                    {
                        launcher.Fill();
                        var metrics = LauncherWindow.Metrics(size.Width, size.Height, scale);
                        launcher.Screen.Layout(metrics);
                        if (launcher.Screen.Find(rest) is not { } under) { say($"launcher step: no piece {rest}"); break; }
                        var r = under.Rect;
                        launcher.Screen.PointerMove((r.X + r.Width * 0.5f) * metrics.Pixel, (r.Y + r.Height * 0.5f) * metrics.Pixel);
                        if (verb == "press") launcher.Screen.PointerDown();
                        break;
                    }
                case "release":
                    launcher.Screen.PointerMove(-1f, -1f);
                    launcher.Screen.PointerUp();
                    break;
                case "drag":
                    {
                        // "drag <list> <from> <to>": a row's handle pressed and the pointer moved to where the row would land (held there, the
                        // drop line shown); "drop" lets it go there.
                        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        launcher.Fill();
                        var metrics = LauncherWindow.Metrics(size.Width, size.Height, scale);
                        launcher.Screen.Layout(metrics);
                        if (words.Length != 3 || launcher.Screen.Find(words[0]) is not ListPiece rows
                            || !int.TryParse(words[1], CultureInfo.InvariantCulture, out int from) || !int.TryParse(words[2], CultureInfo.InvariantCulture, out int to)
                            || from < 0 || to < 0 || from >= rows.Children.Count || to >= rows.Children.Count
                            || rows.Children[from].Walk().OfType<DragHandle>().FirstOrDefault() is not { } grip)
                        {
                            say($"launcher step: drag {rest}: name a list and two of its rows");
                            break;
                        }
                        float px = metrics.Pixel, x = (grip.Rect.X + grip.Rect.Width * 0.5f) * px;
                        launcher.Screen.PointerMove(x, (grip.Rect.Y + grip.Rect.Height * 0.5f) * px);
                        launcher.Screen.PointerDown();
                        var target = rows.Children[to].Rect;
                        launcher.Screen.PointerMove(x, (target.Y + target.Height * (to > from ? 0.8f : to < from ? 0.2f : 0.5f)) * px);
                        break;
                    }
                case "drop":
                    launcher.Screen.PointerUp();
                    break;
                case "wait":
                    if (!Wait(launcher, rest, say)) return 1;
                    break;
                case "size":
                    {
                        var parts = rest.Split('@');
                        var wh = parts[0].Split('x');
                        if (wh.Length != 2 || !int.TryParse(wh[0], CultureInfo.InvariantCulture, out int w) || !int.TryParse(wh[1], CultureInfo.InvariantCulture, out int h) || w <= 0 || h <= 0)
                        {
                            say($"launcher step: size {rest} is not WxH[@scale]");
                            return 2;
                        }
                        size = (w, h);
                        scale = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) && sc >= 1f ? sc : 1f;
                        break;
                    }
                case "still":
                    {
                        if (!drawers.TryGetValue(size, out var drawer))
                            drawers[size] = drawer = (PlatformRenderer.CreateHeadless(size.Width, size.Height, new RendererOptions(VSync: false)), new DrawList());
                        var (renderer, list) = drawer;
                        launcher.Poll();
                        launcher.Fill();
                        var screen = launcher.Screen;
                        screen.Layout(LauncherWindow.Metrics(size.Width, size.Height, scale));
                        if (launcher.TakeFocus() is { } wanted && screen.Find(wanted) is { } focused) screen.SetFocus(focused);
                        screen.Draw(list);
                        renderer.Draw(view, list);
                        renderer.SaveScreenshot(rest);
                        say($"launcher still: {Path.GetFullPath(rest)} ({size.Width}x{size.Height}{(scale > 1f ? $" at {scale * 100:0} %" : "")}, page {launcher.Page}{(launcher.Notice is { } n ? $", notice {n.Kind}" : "")})");
                        break;
                    }
                default:
                    say($"launcher step: {verb} is not a step (page, act, path, pick, focus, show, hover, press, release, drag, drop, wait, size, still)");
                    return 2;
            }
            launcher.TakeActions();
        }
        if (launcher.Setup.Busy) { launcher.Setup.Cancel(); launcher.Setup.Wait(TimeSpan.FromSeconds(30)); }
        if (launcher.Modules.Importing) launcher.Modules.Cancel();
        if (launcher.Modules.Importing || launcher.Modules.Checking) launcher.Modules.Wait(TimeSpan.FromSeconds(60));
        return 0;
    }

    static bool Wait(Launcher launcher, string what, Action<string> say)
    {
        var until = DateTime.UtcNow + TimeSpan.FromMinutes(20);
        string[] w = what.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        while (DateTime.UtcNow < until)
        {
            launcher.Poll();
            bool met = w.FirstOrDefault() switch
            {
                "idle" => !launcher.Setup.Busy && !launcher.Modules.Importing && !launcher.Modules.Checking && launcher.Modules.Verifying is null && !launcher.UserFolder.Moving,
                // The user folder's move that far.
                "move" => !launcher.UserFolder.Moving || launcher.UserFolder.Progress is { Total: > 0 } up && up.Done >= up.Total * double.Parse(w.ElementAtOrDefault(1) ?? "0.5", CultureInfo.InvariantCulture),
                // The step's own part (an import's copying, not its first pass over the sizes) that far.
                "progress" => !launcher.Setup.Busy || launcher.Setup.Progress is { } p && (launcher.Setup.BusyWith != "import" || p.Step == SetupProgress.Copy)
                              && p.Share >= float.Parse(w.ElementAtOrDefault(1) ?? "0.5", CultureInfo.InvariantCulture),
                // A module's import that far.
                "module" => !launcher.Modules.Importing || launcher.Modules.Progress is { } mp && mp.Share >= float.Parse(w.ElementAtOrDefault(1) ?? "0.5", CultureInfo.InvariantCulture),
                "game" => launcher.Running is null,
                _ => true,
            };
            if (met) return true;
            Thread.Sleep(20);
        }
        say($"launcher step: wait {what}: not met in 20 minutes");
        return false;
    }

    /// <summary>The steps from <c>--steps</c>: its words split at <c>;</c>, or a file's lines (<c>@file</c>).</summary>
    public static IReadOnlyList<string> Parse(string steps) =>
        steps.StartsWith('@') ? File.ReadAllLines(steps[1..]) : steps.Split(';');
}
