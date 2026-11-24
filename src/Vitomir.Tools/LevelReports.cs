// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
// The reports a run can be once its place is loaded (--things, --scripts-all, --storm, --sky-groups, --modes, --find): tools.
using System.Globalization;
using System.Numerics;
using SDL;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Scene;
using Vitomir.Render;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Engine.Video;
using Vitomir.Formats;
using Vitomir.Platform.Audio;
using Vitomir.Scripting;
using Vitomir;
using static SDL.SDL3;

namespace Vitomir;

/// <summary>
/// The reports a game run can be once its place is loaded and its scripts compiled (developer views, which go to
/// <c>vitomir-tools</c>): <c>--things</c>, <c>--scripts-all</c>, <c>--storm</c>, <c>--sky-groups</c>,
/// <c>--modes</c>, <c>--find</c>. Each prints and answers the run's exit code.
/// </summary>
sealed class LevelReports(string[] args, Location location, CodexScenes? scenes, GameFiles files, ChronicleSet chronicles, string levelName)
{
    readonly Sky? sky = location.Sky;
    readonly MaterialCache materials = location.Materials;
    readonly SceneMesh sceneMesh = location.Mesh;

    string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    bool Flag(string name) => Array.IndexOf(args, name) >= 0;
    float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The report asked for, printed (its exit code), or null when none was.</summary>
    internal int? Run()
    {

        // A viewer aid: what a place's things are, for the thing model. Both scenes, in the order the
        // engine numbers them: the level's own world first, then the chronicle's chapter on top.
        if (Flag("--things"))
        {
            foreach (var wt in location.Things)
            {
                var t = wt.Scene;
                Console.WriteLine($"{wt.Index,4} {wt.Template,-24} {wt.Type,-10} {(wt.Model.Length > 0 ? wt.Model : "-"),-22}" +
                                  $" keyframes {t.Keyframes.Length,2}" +
                                  $" flags 0x{wt.Flags:x}" +
                                  $" desc {(wt.DescriptionId.Length > 0 ? wt.DescriptionId : "-"),-14}" +
                                  $" at {t.Position.X:F0},{t.Position.Y:F0},{t.Position.Z:F0}" +
                                  (wt.Actor is { CastId.Length: > 0 } ca ? $" cast \"{ca.CastId}\"" : "") +
                                  (wt.Animation is { Mode: { } nowPlaying } ? $" playing {wt.MotionSet}/{nowPlaying.Name}"
                                   : wt.MotionSet.Length > 0 ? $" motions {wt.MotionSet} (not playing)" : "") +
                                  (t.Script.Length > 0 ? " script " + t.Script : "") +
                                  (t.Params.Length == 0 ? "" : " (" + string.Join(", ", t.Params.Select(pp =>
                                      pp.Type == NsdParam.Float ? pp.AsFloat.ToString("0.##")
                                      : pp.Type == NsdParam.String ? "\"" + pp.Text + "\""
                                      : pp.Bits.ToString())) + ")"));
                for (int k = 0; k < t.Keyframes.Length; k++)
                    Console.WriteLine($"        frame {k}: pos {t.Keyframes[k].Position.X:F0},{t.Keyframes[k].Position.Y:F0},{t.Keyframes[k].Position.Z:F0}" +
                                      $"  rot {t.Keyframes[k].Orientation.X:F1},{t.Keyframes[k].Orientation.Y:F1},{t.Keyframes[k].Orientation.Z:F1}" +
                                      (k == 0 ? $"   (thing at {t.Position.X:F0},{t.Position.Y:F0},{t.Position.Z:F0} rot {t.Orientation.X:F1},{t.Orientation.Y:F1},{t.Orientation.Z:F1})" : ""));
            }
            return 0;
        }

        // A viewer aid: create every scripted thing in every level and report what the scripts asked
        // the engine for. This is the roadmap for which natives to implement next.
        if (Flag("--scripts-all") && scenes is not null)
        {
            var levels = files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                              .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray();
            int scripts = 0, failed = 0;
            var byClass = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in levels)
            {
                // The chronicle's own starting scene where there is one, so the scripts that actually
                // run in the game are the ones this reports on.
                var here = Location.Load(files, name, scene: chronicles.ForLevel(name)?.Entry?.SceneName);
                scenes.Enter(here);
                foreach (var n in scenes.Running) byClass[n] = byClass.GetValueOrDefault(n) + 1;
                scripts += scenes.Running.Count();
                failed += here.Things.Count(t => t.Script.Length > 0) - scenes.Running.Count();
                scenes.Leave();
            }
            Console.WriteLine($"{levels.Length} levels: {scripts} scripts created, {failed} not created, " +
                              $"{byClass.Count} distinct classes");
            // `--verbose` lists every one of them; without it, the head of the list is the roadmap.
            int show = Flag("--verbose") ? int.MaxValue : 20;
            Console.WriteLine($"natives asked for and not implemented: {scenes.Engine.Missing.Count}, " +
                              $"{scenes.Engine.Missing.Values.Sum()} calls" +
                              (show == int.MaxValue ? ":" : ". The twenty most asked for:"));
            foreach (var kv in scenes.Engine.Missing.OrderByDescending(k => k.Value).Take(show))
                Console.WriteLine($"  {kv.Value,6}  {kv.Key}");
            Console.WriteLine("the ten most used script classes:");
            foreach (var kv in byClass.OrderByDescending(k => k.Value).Take(10))
                Console.WriteLine($"  {kv.Value,6}  {kv.Key}");
            return 0;
        }

        // A viewer aid: how often the storm actually strikes, without drawing anything.
        if (Arg("--storm") is { } stormSeconds)
        {
            var styles = location.Styles;
            scenes?.Enter(location);
            var bolts = new[] { "lightning1", "lightning2", "lightning3" }
                .Select(n => (Name: n, Mat: materials.Find(n))).Where(b => b.Mat is not null).ToArray();
            int steps = (int)(F(stormSeconds) * 60), anyLit = 0, strikes = 0;
            var lit = new int[bolts.Length];
            var wasLit = new bool[bolts.Length];
            for (int i = 0; i < steps; i++)
            {
                scenes?.Step(1f / 60f);
                styles.Step(1000f / 60f);
                materials.Animate(1000f / 60f, styles);
                bool any = false;
                for (int b = 0; b < bolts.Length; b++)
                {
                    bool on = bolts[b].Mat!.CurrentFrame == 1;
                    if (on) { lit[b]++; any = true; if (!wasLit[b]) strikes++; }
                    wasLit[b] = on;
                }
                if (any) anyLit++;
            }
            Console.WriteLine($"{F(stormSeconds):0} s of {levelName}: {strikes} strikes, " +
                              $"one every {F(stormSeconds) / Math.Max(1, strikes):0.00} s; " +
                              $"some bolt lit {100.0 * anyLit / steps:0.0}% of the time");
            for (int b = 0; b < bolts.Length; b++)
                Console.WriteLine($"  {bolts[b].Name}: lit {100.0 * lit[b] / steps:0.0}% of the time");
            return 0;
        }

        // A viewer aid: the sky model's groups in the order they are drawn, with their materials.
        if (Flag("--sky-groups") && sky is not null)
        {
            var model = sky.Model;
            for (int i = 0; i < model.Groups.Length; i++)
            {
                var grp = model.Groups[i];
                if (grp.Material < 0 || grp.Material >= sky.Materials.Length || grp.NumFaces == 0) continue;
                var m = materials.Items[sky.Materials[grp.Material]];
                var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
                var uvLo = new Vector2(float.MaxValue); var uvHi = new Vector2(float.MinValue);
                for (int f = 0; f < grp.NumFaces * 3; f++)
                {
                    var v = model.Vertices[grp.FirstVertex + model.Indices[grp.FirstFace * 3 + f]];
                    lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position);
                    uvLo = Vector2.Min(uvLo, v.Uv); uvHi = Vector2.Max(uvHi, v.Uv);
                }
                Console.WriteLine($"{i,3}: {m.Name[(m.Name.LastIndexOf('/') + 1)..],-22} faces {grp.NumFaces,3}" +
                                  $" x[{lo.X,8:F0},{hi.X,8:F0}] y[{lo.Y,8:F0},{hi.Y,8:F0}] z[{lo.Z,8:F0},{hi.Z,8:F0}]" +
                                  $" uv[{uvLo.X:F2},{uvHi.X:F2}]x[{uvLo.Y:F2},{uvHi.Y:F2}] {DrawPlan.ForThing(m.Traits)}");
            }
            return 0;
        }

        // A viewer aid: what mode every surface ends up in, to compare against the original's own choice.
        if (Flag("--modes"))
        {
            var steps = new List<DrawStep>();
            var styles = new LightStyles();
            var tally = new Dictionary<string, (int Batches, string Example)>();
            foreach (var b in sceneMesh.Batches)
            {
                var m = materials.Items[b.Material];
                string plan;
                if (m.HasPasses) plan = "pass " + string.Join(" + ", m.Passes.Select(p => p.Kind));
                else if (b.Layer == BatchLayer.Thing) plan = DrawPlan.ForThing(m.Traits).ToString();
                else
                {
                    var lit = b.Layers.Select(l => new LayerLight(l.Style, styles.Brightness(l.Style))).ToArray();
                    DrawPlan.ForSurface(m.Traits, lit, b.AlphaScale < 1f, steps);
                    plan = string.Join(" + ", steps.Select(x => x.Kind));
                }
                var had = tally.GetValueOrDefault(plan);
                tally[plan] = (had.Batches + 1, had.Example ?? m.Name);
            }
            foreach (var (plan, v) in tally.OrderByDescending(t => t.Value.Batches))
                Console.WriteLine($"{v.Batches,6}  {plan}   (e.g. {v.Example})");
            return 0;
        }

        if (Arg("--find") is { } find)
        {
            foreach (var group in sceneMesh.Batches
                         .Select((b, i) => (b, m: materials.Items[b.Material]))
                         .Where(t => t.m.Name.Contains(find, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(t => t.m.Name))
            {
                var centres = group.Select(t => t.b.Center).ToArray();
                var c = centres.Aggregate(Vector3.Zero, (a, v) => a + v) / centres.Length;
                Console.WriteLine($"{group.Key}: {centres.Length} batches, centre {c.X:F0},{c.Y:F0},{c.Z:F0}" +
                                  (group.First().m.Passes.Length > 0
                                      ? " passes " + string.Join(" + ", group.First().m.Passes.Select(x => x.Kind)) : ""));
                foreach (var t in group.Take(6))
                    Console.WriteLine($"    at {t.b.Center.X:F0},{t.b.Center.Y:F0},{t.b.Center.Z:F0}" +
                                      $" light {t.b.Light} alpha {t.b.AlphaScale:F2} layer {t.b.Layer}" +
                                      $" twosided {t.b.TwoSided} blend {t.m.Blend}" +
                                      $" vertex colour ~{Avg(t.b):F2}");
            }
            return 0;
        }
        return null;
    }


    // A viewer aid: where in the level a material is used, so a still can be aimed at it.
    float Avg(DrawBatch b)
    {
        double sum = 0; int n = 0;
        for (uint i = b.FirstIndex; i < b.FirstIndex + b.IndexCount; i++)
        {
            var c = sceneMesh.Vertices[sceneMesh.Indices[i]].Color;
            sum += ((c & 0xff) + ((c >> 8) & 0xff) + ((c >> 16) & 0xff)) / 3.0 / 255.0; n++;
        }
        return n == 0 ? 0 : (float)(sum / n);
    }
}
