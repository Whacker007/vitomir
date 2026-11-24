// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
// The reports a run can be before it loads a place: the saves, the chronicles, 1.1's save slots, the gaits (tools).
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
using static Vitomir.Classic.ClassicRun;

namespace Vitomir;

/// <summary>
/// The reports a run can be before a place is loaded (developer views and tools, which go to <c>vitomir-tools</c>):
/// <c>--list-saves</c>, <c>--chronicles</c>, <c>--saves</c>, <c>--gait</c>. Each prints and answers its exit code.
/// </summary>
static class ReportCommands
{
    public static int ListSaves()
    {
        string folder = SaveFolder();
        var found = Directory.Exists(folder) ? Directory.GetFiles(folder, "*" + SaveArchive.Extension) : [];
        Console.WriteLine($"{found.Length} saves in {folder}:");
        foreach (var f in found.OrderByDescending(File.GetLastWriteTime))
        {
            try
            {
                var h = SaveArchive.ReadHeader(f);
                Console.WriteLine($"  {Path.GetFileName(f),-28} {h.Description,-36} {h.Saved}  {h.Chronicle}");
            }
            catch (Exception e) { Console.WriteLine($"  {Path.GetFileName(f),-28} unreadable: {e.Message}"); }
        }
        return 0;
    }

    public static int Chronicles(ChronicleSet chronicles, string gameDir, bool verbose)
    {
        Console.WriteLine($"{chronicles.All.Count} chronicles in {gameDir}:");
        foreach (var c in chronicles.All)
        {
            Console.WriteLine($"  {c.Name,-18} {c.Count,3} locations  flags 0x{c.Flags:x5} {c.Kind}");
            Console.WriteLine($"    script {(c.Data.Scripts.Count > 0 ? string.Join(", ", c.Data.Scripts) : "none")}" +
                              $", metafile {(c.Data.Metafile.Length > 0 ? c.Data.Metafile : "none")}" +
                              (c.Meta is { Player.Length: > 0 } m ? $" (player {m.Player})" : "") +
                              $", starts in {c.Data.Start?.Name ?? "nowhere"}");
            if (verbose)
                for (int i = 0; i < c.Count; i++)
                {
                    var loc = c.At(i)!;
                    var exits = string.Join(", ", Enumerable.Range(0, loc.ExitCount)
                        .Where(n => loc.Exits[n].To >= 0)
                        .Select(n => $"{n}->{c.NameOf(loc.Exits[n].To)},{loc.Exits[n].Entrance}{(loc.Exits[n].Closed ? " (closed)" : "")}"));
                    Console.WriteLine($"    {i,3} {loc.Name,-24} {loc.LevelName,-10} {(loc.SceneName.Length > 0 ? loc.SceneName : "-"),-16} " +
                                      $"0x{loc.Flags:x4}  {exits}");
                }
            foreach (var line in c.Data.Ignored) Console.WriteLine($"    ignored: {line}");
        }
        return 0;
    }

    // --saves: the player's own save slots, read the way the original writes them. A `.nsg` has no
    // lengths in it, so this walks the whole file field by field — which makes the report a running
    // check on the reader as much as a look at a playthrough. --verbose adds every carried item.
    public static int Saves(string? given, string gameDir, bool verbose)
    {
        string dir = given is not null && !given.StartsWith("--")
            ? given : OriginalSaves.For(gameDir).Folder;
        if (!Directory.Exists(dir)) { Console.WriteLine($"no save folder at {dir}"); return 1; }
        var slots = Directory.EnumerateDirectories(dir)
                             .Where(d => File.Exists(Path.Combine(d, "!main.nsg")))
                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        Console.WriteLine($"{slots.Count} saves in {dir}:");
        foreach (var slotDir in slots)
        {
            string main = Path.Combine(slotDir, "!main.nsg");
            NsgSave save;
            try { save = NsgSave.ReadFile(main); }
            catch (NodFormatException e) { Console.WriteLine($"  {Path.GetFileName(slotDir),-6} unreadable: {e.Message}"); continue; }

            int worlds = Directory.GetFiles(slotDir, "location_*.nsw").Length;
            Console.WriteLine($"  {Path.GetFileName(slotDir),-6} {save.Description}");
            Console.WriteLine($"         {save.Chronicle.Name} at {save.Chronicle.MapFile}, " +
                              $"{save.Chronicle.Locations.Length} locations, {worlds} of them visited, " +
                              $"{save.Chronicle.Quests.Length} quests, {save.Party.Gold} gold");
            // The allocators, which anything hand-building a record has to stay clear of: a GUID
            // below the thing counter is one the engine may hand out again.
            Console.WriteLine($"         counters: thing {save.ThingCounter}, codex {save.CodexCounter}, " +
                              $"treasure {save.TreasureCounter}, effect {save.EffectCounter}");
            foreach (var member in save.Party.Players)
            {
                var actor = member.Actor!;
                var inventory = actor.Inventory.Items;
                Console.WriteLine($"         {member.Name} ({member.DescriptionId})  " +
                                  $"health {actor.Health:0.#}/{actor.MaxHealth:0.#}  " +
                                  $"{inventory.Length} carried, {inventory.Count(i => i.Item!.IsEquipped)} worn, " +
                                  $"{actor.Effects.Length} effects, {actor.Disciplines.Length} disciplines");
                if (!verbose) continue;
                foreach (var item in inventory.OrderByDescending(i => i.Item!.IsEquipped).ThenBy(i => i.Name))
                {
                    var rpg = item.Item!;
                    string carries = string.Join(", ", rpg.Effects.Select(e => e.Name));
                    // The byte range is what a save *editor* needs: the format has no lengths and no
                    // index, so a record can only be lifted out or spliced in by the bounds the
                    // sequential walk itself reported.
                    Console.WriteLine($"           {(rpg.IsEquipped ? "worn " : "     ")}{item.Name,-20} " +
                                      $"{item.Type,-8} slot 0x{rpg.Location:x4} cost {rpg.BaseCost,6} " +
                                      $"guid {item.Guid,-5} @{item.Start:x}..{item.End:x} ({item.End - item.Start} B)" +
                                      (carries.Length > 0 ? $"  {carries}" : ""));
                }
            }
            // One record per pane-open after a load, not one per shop.
            // Record 1 is the live one; the rest are rolls from earlier sessions that nothing reads.
            foreach (var group in save.Shops.GroupBy(x => x.OwnerGuid))
            {
                var records = group.ToList();
                var live = records[0];
                Console.WriteLine($"         shop {live.Name,-14} keeper {live.OwnerGuid,-5} " +
                                  $"{records.Count} record{(records.Count == 1 ? "" : "s")}, " +
                                  $"{live.Stock.Length} lines, " +
                                  $"buy x{BitConverter.UInt32BitsToSingle(live.BuyRate)}, " +
                                  $"sell x{BitConverter.UInt32BitsToSingle(live.SellRate)}, " +
                                  $"trades 0x{live.SellFlags:x}");
                if (!verbose) continue;

                static string Line(NsgStock line) =>
                    line.Treasure is { } t ? $"{t.Template}{(line.Infinite ? "" : $" x{line.Remaining}")}"
                    : line.SoldOut ? "(sold out)" : "(empty)";

                Console.WriteLine($"           live:  {string.Join(", ", live.Stock.Select(Line))}");
                // The older records only matter where they differ, and the differences are the roll.
                for (int r = 1; r < records.Count; r++)
                {
                    var was = records[r];
                    var changed = Enumerable.Range(0, Math.Min(was.Stock.Length, live.Stock.Length))
                        .Where(k => Line(was.Stock[k]) != Line(live.Stock[k]))
                        .Select(k => $"[{k}] {Line(was.Stock[k])}")
                        .ToList();
                    Console.WriteLine(changed.Count == 0
                        ? $"           stale {r}: identical to the live roll"
                        : $"           stale {r}: {changed.Count}/{live.Stock.Length} lines differ — {string.Join(", ", changed)}");
                }
            }
        }
        return 0;
    }

    // --gait: every character in the game, the cycle its speed makes it play, the ground that cycle
    // is drawn as covering, and what the animator drew it for. Needs no level — it is a report on
    // the shipped data, and it is the measurement behind 1.1's gait.
    public static int Gait(GameFiles files, bool verbose)
    {
        var templates = new TemplateSet(files);
        var motions = new MotionSets(files);
        int measured = 0, runsOnItsWalk = 0, walksOnItsRun = 0;
        var rows = new List<(string Name, string Gait, float Speed, float Frames, float Flat,
                             float? Stride, string Anim)>();
        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            if (!tags.TryGetValue(Tags.MotionSet, out var setTag)) continue;
            if (!tags.TryGetValue(Tags.ActorWalkSpeed, out var walkTag)) continue;
            float walk = walkTag.AsFloat(), run = tags.TryGetValue(Tags.ActorRunSpeed, out var r) ? r.AsFloat() : 0f;
            if (walk <= 0f) continue;
            var set = motions.Set(setTag.AsString());
            if (set is null) continue;
            measured++;
            if (run > 0f && run <= Player.RunAnimationBeyond) runsOnItsWalk++;
            if (walk > Player.RunAnimationBeyond) walksOnItsRun++;

            NodModel? model = null;
            if (tags.TryGetValue(Tags.Model, out var modelTag) &&
                files.TryRead("3d/models/" + modelTag.AsString(), out var md))
                try { model = NodModel.Read(md, modelTag.AsString()); } catch (NodFormatException) { }

            foreach (var (mode, gait, speed) in new[] { (Player.ModeWalk, "walk", walk), (Player.ModeRun, "run", run) })
            {
                if (speed <= 0f || motions.Find(set, mode) is not { } found) continue;
                float? stride = model is null ? null : Stride.Measure(model, set, found.Animation);
                rows.Add((name, gait, speed, found.Animation.Duration,
                          speed * found.Animation.Duration / ThingAnimation.Rate, stride, found.Mode.File));
            }
        }
        var walks = rows.Where(x => x.Gait == "walk").ToList();
        Console.WriteLine($"{measured} templates have both a motion set and a walk speed; " +
                          $"{walks.Count} of them have a walk animation to measure.");
        Console.WriteLine($"the run cycle starts at {Player.RunAnimationBeyond:F0} units a second, absolutely:");
        Console.WriteLine($"  {runsOnItsWalk} run at or below it, so they play their WALK cycle while running");
        Console.WriteLine($"  {walksOnItsRun} walk above it, so they play their RUN cycle while walking");
        var units = walks.Select(p => p.Flat).Order().ToList();
        if (units.Count > 0)
            Console.WriteLine($"ground covered in one walk cycle at {ThingAnimation.Rate:F0} fps: " +
                              $"min {units[0]:F0}, median {units[units.Count / 2]:F0}, max {units[^1]:F0} " +
                              $"({units[^1] / units[0]:F0}x spread)");
        var shared = walks.GroupBy(p => p.Anim, StringComparer.OrdinalIgnoreCase)
                          .Where(sh => sh.Select(p => p.Speed).Distinct().Count() > 1).ToList();
        Console.WriteLine($"{shared.Count} of {walks.Select(p => p.Anim).Distinct(StringComparer.OrdinalIgnoreCase).Count()} " +
                          "walk animations are shared by characters with different walk speeds:");
        foreach (var sh in shared.OrderByDescending(sh => sh.Select(p => p.Speed).Distinct().Count()).Take(8))
            Console.WriteLine($"  {sh.Key,-28} {string.Join(", ", sh.Select(p => p.Speed).Distinct().Order().Select(v => v.ToString("F0")))}");

        // What the animator drew, against what the template asks for. The ratio is the correction a
        // "matched gait" fix would apply, and 1.00 is a character already moving at its drawn pace.
        Console.WriteLine();
        int have = rows.Count(x => x.Stride is not null);
        Console.WriteLine($"stride measured from the animation's own footfall tags: {have} of {rows.Count} " +
                          "walk and run modes across those templates");
        foreach (var gait in new[] { "walk", "run" })
        {
            var m = rows.Where(x => x.Gait == gait && x.Stride is not null)
                        .Select(x => (x.Name, Natural: Stride.NaturalSpeed(x.Frames, x.Stride!.Value), x.Speed, x.Anim))
                        .Where(x => x.Natural > 0f).ToList();
            if (m.Count == 0) continue;
            var ratio = m.Select(x => x.Speed / x.Natural).Order().ToList();
            Console.WriteLine($"  {gait}: drawn pace min {m.Min(x => x.Natural):F0}, median " +
                              $"{m.OrderBy(x => x.Natural).ElementAt(m.Count / 2).Natural:F0}, " +
                              $"max {m.Max(x => x.Natural):F0} units a second");
            Console.WriteLine($"    speed ÷ drawn pace: min {ratio[0]:F2}x, median {ratio[ratio.Count / 2]:F2}x, " +
                              $"max {ratio[^1]:F2}x  ({ratio.Count(v => v is > 0.9f and < 1.1f)} of {ratio.Count} " +
                              "are already within a tenth)");
        }
        if (verbose)
            foreach (var p in rows.OrderBy(p => p.Gait).ThenBy(p => p.Flat))
            {
                string drawn = p.Stride is null ? "no footfall tags"
                    : $"stride {p.Stride:F0} -> drawn for {Stride.NaturalSpeed(p.Frames, p.Stride.Value),6:F0} u/s" +
                      $" ({p.Speed / Stride.NaturalSpeed(p.Frames, p.Stride.Value),5:F2}x)";
                Console.WriteLine($"  {p.Name,-26} {p.Gait,-4} {p.Speed,6:F0} u/s  {p.Frames,5:F0} frames " +
                                  $"-> {p.Flat,6:F0} units/cycle  {drawn}  ({p.Anim})");
            }
        return 0;
    }
}
