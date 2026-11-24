// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's harness: the headless walks, the settle, and the stills (--headless).
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Scene;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Engine.Video;
using Vitomir.Formats;
using Vitomir.Platform.Audio;
using Vitomir.Scripting;
using Vitomir;
using Vitomir.Platform.Modules;

namespace Vitomir.Classic;

unsafe sealed partial class ClassicGame
{
    int? StartHarness()
    {

        // A still can be asked for a moment in the level's life: run the clock up to it in frames, so
        // the sliding textures and the light styles reach that moment the way they would have.
        if (Arg("--time") is { } t0) frame.Game.Advance(F(t0));

        // A headless check that click-to-move works: aim at a point (or at whatever a screen pixel is
        // over, which is the real thing) and walk the route the search comes back with.
        if (player is not null && (Arg("--walk-to") is not null || Arg("--click") is not null))
        {
            Vector3 target; int node; string how;
            if (Arg("--click") is { } pixel)
            {
                var px = pixel.Split(',').Select(F).ToArray();
                // The interface gets the click first, and it decides at the pane level, so a click on
                // the bar's own artwork never reaches the ground (then the world's click). The same
                // test the live path makes, so this reports what playing would do.
                if (hud.Enabled)
                {
                    var (ux, uy) = StatusBar.FromWindow(px[0], px[1], width, height);
                    var held = Arg("--ui-modifier")?.ToLowerInvariant() switch
                    {
                        "shift" => InputModifier.Shift,
                        "ctrl" or "control" => InputModifier.Ctrl,
                        "alt" => InputModifier.Alt,
                        "space" => InputModifier.Space,
                        _ => InputModifier.None,
                    };
                    if (hud.Bar.Click(ux, uy, held, Flag("--ui-right")) is { Consumed: true } onUi)
                    {
                        Console.WriteLine($"  click at {pixel} is on the status bar ({ux},{uy} in the pane): {onUi.Did}");
                        Shutdown();
                        return 0;
                    }
                }
                // And off the world's view it is nobody's, as in play (`WorldHas`).
                if (!WorldHas(null, px[0], px[1], width, height))
                {
                    Console.WriteLine($"  click at {pixel} is off the world's view: nobody's");
                    Shutdown();
                    return 0;
                }
                var (o, d) = Pick.Ray(cam, px[0], px[1], width, height, frame.WorldRows(height));
                var hit = WorldSweep.Pointer(location, o, d);
                if (hit is null) { Console.WriteLine($"  click at {pixel}: the ray hit nothing"); Shutdown(); return 0; }
                var spot = hit.Value;
                Console.WriteLine($"  click at {pixel} hit sector {spot.Sector} surface {spot.Surface} at " +
                                  $"{spot.Point.X:0},{spot.Point.Y:0},{spot.Point.Z:0}, {spot.Distance:0} away, " +
                                  (spot.Walkable ? $"walkable (node {spot.PathNode})" : "NOT walkable — the original ignores this click"));
                if (!spot.Walkable) { Shutdown(); return 0; }
                (target, node, how) = (spot.Point, spot.PathNode, "click");
            }
            else
            {
                var c = Arg("--walk-to")!.Split(',').Select(F).ToArray();
                target = new Vector3(c[0], c[1], c.Length > 2 ? c[2] : player.Position.Z);
                node = location.Walk!.Find(target, 256f);
                how = "point";
                if (node < 0) { Console.WriteLine($"  {how}: no walkable ground at {Arg("--walk-to")}"); Shutdown(); return 0; }
            }
            var began = player.Position;
            if (!player.WalkTo(target, node))
                Console.WriteLine(player.Occupant is { } sitting
                    ? $"  refused: {sitting.Template} is standing on that spot"
                    : $"  no route from node {player.Node} to node {node} " +
                      $"({Vector3.Distance(began, target):0} units away in a straight line)");
            else
            {
                var aim = player.Destination;
                Console.WriteLine($"  {(player.Running ? "running" : "walking")} to node {node} by {player.Route.Length} waypoints" +
                                  (Bodies.Distance2D(aim, target) > 0.5f ? $" (the radius clamp moved the point {Bodies.Distance2D(aim, target):0.#})" : ""));
                float want = Arg("--walk") is { } ws ? F(ws) : 30f, step = 1f / 60f;
                float clock = 0;
                var dump = FrameDump.From(Arg("--frame-dump"), Arg("--frame-dump-fps"));   // a film of the walk (ours, harness)
                while (clock < want && player.Walking)
                {
                    frame.Game.Advance(step, step);          // the route is walked by the world (SteeredWalk)
                    clock += step;
                    dump?.Tick(step, AimNow, () => Screenshots.Rgba(Still(frame, width, height), gpu.ColourIsBgra), width, height);
                }
                // An arrival is one at the goal, not over or under it (ours, World/WalkOutcome.cs).
                Console.WriteLine("  " + WalkOutcome.Line(WalkOutcome.Of(player, location.Walk, aim, node), clock, Vector3.Distance(began, player.Position)));
            }
            AimNow();
        }
        // A headless check that walking works: push the player forward for a while, then draw.
        else if (Arg("--walk") is { } walkSeconds && player is not null)
        {
            float want = F(walkSeconds), step = 1f / 60f;
            var startAt = player.Position;
            player.Running = Flag("--run");
            int blocked = 0;
            var dump = FrameDump.From(Arg("--frame-dump"), Arg("--frame-dump-fps"));   // a film of the walk (ours, harness)
            for (float t = 0; t < want; t += step)
            {
                player.Move(player.Facing, MathF.Min(step, want - t));
                if (player.Blocked) blocked++;
                frame.Game.Advance(MathF.Min(step, want - t), step);
                Listen(MathF.Min(step, want - t));
                dump?.Tick(MathF.Min(step, want - t), AimNow, () => Screenshots.Rgba(Still(frame, width, height), gpu.ColourIsBgra), width, height);
            }
            AimNow();
            Console.WriteLine($"walked {Vector3.Distance(startAt, player.Position):0} units in {want:0.0}s " +
                              $"({(player.Running ? "running" : "walking")}), blocked on {blocked} of {(int)(want / step)} ticks, " +
                              $"now at {player.Position.X:0},{player.Position.Y:0},{player.Position.Z:0} on node {player.Node}, " +
                              $"playing {player.Thing.Animation?.Mode?.Name ?? "nothing"}" +
                              (player.BlockedBy is { } by ? $", stopped by {by.Template} ({by.Collide})" : ""));
        }

        // Harness knobs for stills of the discipline interface, which otherwise needs hands: the pane
        // open, effects on the character (for their icons), a feed under way (for the feed bar), and an
        // advancement session with experience to spend. None of them is a game feature.
        if (Flag("--discipline-pane")) ShowDisciplines(true);
        if (Arg("--effects") is { } effectList && !effectList.StartsWith("--") && location.Steered is { } effectsOn)
            foreach (var name in effectList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Console.WriteLine(Vitomir.Engine.Rpg.EffectRuntime.Apply(effectsOn, name, 0, durationMs: 60_000, creator: effectsOn) is { } put
                    ? $"effects: {put.Definition.Name} on {Watch.Name(effectsOn)}" : $"effects: no effect called {name}");
        // The steered character in another's shape (Rpg.ShapeShift): `template` or
        // `template:flags` — `wolf_Grey`, or `Civ09monk1:12` for Mask of 1,000 Faces' NOFOLEY + WEAPONLESS;
        // a comma-separated list is done in order, and `end` undoes the shape.
        if (Arg("--shapeshift") is { } shapeArg && !shapeArg.StartsWith("--") && location.Steered is { } shifter)
            foreach (var shape in shapeArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = shape.Split(':');
                uint shapeFlags = parts.Length > 1 && uint.TryParse(parts[1], out uint f) ? f : 0;
                bool done = parts[0] == "end" ? Vitomir.Engine.Rpg.ShapeShift.End(shifter)
                          : Vitomir.Engine.Rpg.ShapeShift.Shift(shifter, parts[0], shapeFlags);
                Console.WriteLine(done ? $"shapeshift: {Watch.Name(shifter)} {parts[0]} ({shifter.Model}, {shifter.MotionSet})"
                                       : $"shapeshift: {parts[0]} refused");
            }
        if (Arg("--feed") is { } feedArg && !feedArg.StartsWith("--") && location.Steered is { } feeder)
        {
            var victim = int.TryParse(feedArg, out int feedGuid) ? location.Thing(feedGuid)
                       : location.Things.Where(t => t.InWorld && t.IsActor && !ReferenceEquals(t, feeder)
                                                    && t.Template.Equals(feedArg, StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(t => Vector3.Distance(t.Position, feeder.Position)).FirstOrDefault();
            if (victim is null) Console.WriteLine($"feed: nothing called {feedArg} here");
            else
            {
                Vitomir.Engine.Rpg.Feeding.Bite(feeder, victim, forced: true);
                feedBar.Update(feeder, hud.Enabled, audio);
                Console.WriteLine($"feed: {Watch.Name(feeder)} on {Watch.Name(victim)}, {feedBar.Blood:0}/{feedBar.Pool:0} blood");
            }
        }
        if (Arg("--advance") is { } advanceArg && location.Player?.Thing is { Actor: { } advancer } advancing)
        {
            if (long.TryParse(advanceArg, out long spend)) advancer.SpendableExperience = spend;
            OpenAdvancement(advancing);
        }
        return null;
    }

    int? Stills()
    {

        // Let the place live for a while before the picture is taken: the AI thinks, everybody walks,
        // and the animations run. Without it a headless still is always the instant of load, which is
        // no use for seeing whether the people in the street actually go anywhere.
        // `--material-film` (any number of them): before the settle, so the settle plays them.
        if (scenes is not null) foreach (var materialFilm in Args("--material-film")) MaterialFilmHost.Play(scenes, materialFilm);
        bool consoleRanEarly = false;
        if (Arg("--settle") is { } settleFor && float.TryParse(settleFor, out var settleSeconds))
        {
            // `--settle-ms <n>` (a harness): the settle stepped n ms at a time, for the slow-frame check.
            float step = float.TryParse(Arg("--settle-ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var settleMs) ? settleMs / 1000f : 1f / 30f;
            // What each character actually did with the time, which is the only way to tell a walker
            // that is getting somewhere from one that is playing its walk cycle against a wall.
            var watched = minds?.All ?? [];
            var startAt = watched.Select(m => m.Thing.Position).ToArray();
            var ground = new float[watched.Length];
            var animated = new float[watched.Length];
            var onTheSpot = new float[watched.Length];
            var visited = watched.Select(_ => new List<int>()).ToArray();
            var was = (Vector3[])startAt.Clone();
            Tell((o, run) => o.SettleStarted(run, settleSeconds));   // the host program's observers (RunAdditions)
            // `--console-run` with `--settle`: the lines run during the settle rather than at the
            // still, so a watched settle sees what they set (`sethide 3`) — on its tenth step, as a
            // windowed run's go on its tenth frame; the still does not run them again.
            var settleConsole = headless is not null ? SettleConsole.From(Arg("--console-run")) : null;
            consoleRanEarly = settleConsole is not null;
            for (float t = 0; t < settleSeconds; t += step)
            {
                // One step of the whole world — the minds included — then read what moved. The story
                // moves on as it would in play: a script's jump or exit is taken between steps.
                frame.Game.Advance(step, step);
                settleConsole?.Stepped(menus.DevConsole);
                Listen(step);
                AutoAnswer();
                useOrder?.Tick(location, t, frame.Sim.Milliseconds);
                if (!buyUseDone && t >= 1f && Arg("--buy-use") is { } buyUseShop && !buyUseShop.StartsWith("--")) { buyUseDone = true; BuyUse(buyUseShop); }
                journey?.Tick(location, campaign?.Chronicle, t);
                castOrder?.Tick(location, frame.Sim.Milliseconds);
                Tell((o, run) => o.SettleOrders(run, t));
                Sequence();
                net?.Frame(step, frame.Sim.Milliseconds, real: true);
                // A player's machine sent to another place: a still goes on to the run that loads it (NetPlaces.StillArgs).
                if (net?.WantsReload == true) { NetPlay.Carried = net; host.NextRun = [.. RunSettings.Settings(args), "--mp-game", .. NetPlaces.StillArgs(args)]; Shutdown(); return 0; }
                Tell((o, run) => o.SettleStepped(run, t));
                for (int i = 0; i < watched.Length; i++)
                {
                    float went = Vector3.Distance(was[i], watched[i].Thing.Position);
                    ground[i] += went;
                    was[i] = watched[i].Thing.Position;
                    if (watched[i].LastSpot >= 0 && (visited[i].Count == 0 || visited[i][^1] != watched[i].LastSpot))
                        visited[i].Add(watched[i].LastSpot);
                    if (watched[i].Walker.Speed > 0f)
                    {
                        animated[i] += step;
                        // A gait playing while the character makes no ground is the thing that reads as
                        // walking on the spot, so it is counted rather than argued about.
                        if (went < watched[i].Walker.Speed * step * 0.05f) onTheSpot[i] += step;
                    }
                }
            }
            // A journey's still is taken from the camera asked for: an arrival points it at the place's start (ours, HarnessJourney.cs).
            if (journey is not null && Arg("--pos") is { } stillPos)
            {
                var sp = stillPos.Split(',').Select(F).ToArray();
                cam.Position = new Vector3(sp[0], sp[1], sp[2]);
                if (Arg("--yaw") is { } stillYaw) cam.Yaw = F(stillYaw);
                if (Arg("--pitch") is { } stillPitch) cam.Pitch = F(stillPitch);
            }
            Tell((o, run) => o.SettleEnded(run, settleSeconds));
            if (minds is not null)
                Console.WriteLine($"settled {settleSeconds:0.0}s: {minds.All.Sum(m => m.Moves)} orders given, "
                    + $"{minds.All.Sum(m => m.OutOfRange)} spots refused, "
                    + $"{minds.All.Count(m => m.Walker.Walking)} of {minds.All.Length} walking");
            if (Flag("--verbose"))
                for (int i = 0; i < watched.Length; i++)
                {
                    var mind = watched[i];
                    var legs = mind.Walker;
                    Console.WriteLine(
                        $"  {mind.Thing.Guid,5} {mind.Thing.Template,-24} {AiModes.Name(mind.Mode),-12} "
                        + $"moved {Vector3.Distance(startAt[i], mind.Thing.Position),7:0} of {ground[i],8:0} covered, "
                        + $"{animated[i],5:0.0}s in a gait ({onTheSpot[i],4:0.0}s on the spot), "
                        + $"{mind.Moves} orders, {mind.OutOfRange} refused"
                        + (mind.Target is { } enemy ? $", fighting {enemy.Template}"
                           : mind.Acquisitions > 0 ? $", {mind.Acquisitions} targets taken" : "")
                        + (legs.Walking
                            ? $", walking {(legs.Step < legs.Route.Length ? $"leg {legs.Step + 1}/{legs.Route.Length}" : "the last leg")}"
                              + $" to {legs.Target.X:0},{legs.Target.Y:0},{legs.Target.Z:0}"
                              + $" ({Vector3.Distance(mind.Thing.Position, legs.Target):0} away)"
                            : legs.GaveUp ? ", gave up" : ", standing")
                        + (legs.BlockedBy is { } by ? $", blocked by {by.Template}" : legs.Blocked ? ", blocked by the world" : ""));
                    // Which of its own interest spots a wanderer actually used: a spot further than
                    // wanderRadius from where it came into the world is refused on every draw, so a
                    // character with few spots in reach shuttles between them.
                    if (visited[i].Count > 0)
                        Console.WriteLine($"        spots {mind.FirstSpot}..{mind.FirstSpot + mind.SpotCount - 1}, "
                            + $"visited {visited[i].Distinct().Count()} distinct: {string.Join(" -> ", visited[i])}");
                }
        }

        // `--party-drag x,y;x,y` (harness): the left button pressed at the first window pixel and the pointer moved to the
        // second, so a still shows a carried portrait (ours, `partyorder`); `--party-drop` lets it go there too.
        if (Arg("--party-drag") is { } partyDragAt && hud.Enabled)
        {
            var ends = partyDragAt.Split(';').Select(p => p.Split(',').Select(F).ToArray()).ToArray();
            hud.Bar.Update();
            var (bx, by) = StatusBar.FromWindow(ends[0][0], ends[0][1], width, height);
            if (!PartyPress(ends[0][0], ends[0][1], bx, by, InputModifier.None))
                Console.WriteLine($"  party-drag: the press at {bx},{by} on the bar lifts nothing");
            else if (ends.Length > 1)
            {
                PartyMove(ends[1][0], ends[1][1], width, height);
                var (ex, ey) = StatusBar.FromWindow(ends[1][0], ends[1][1], width, height);
                Console.WriteLine($"  party-drag: {string.Join(", ", hud.Bar.PartyThings.Select(Watch.Name))} "
                                + $"({(hud.Bar.CarriedLands ? "lands" : "nothing")}) at {ex},{ey} on the bar");
                if (Flag("--party-drop")) PartyRelease(ex, ey);
            }
        }

        // `--shop-click x,y[;x,y...]` presses and releases the mouse at window pixels on an open shop,
        // in order — a row, Buy, Sell, OK — and `--pointer x,y` then rests the pointer at one, so a still
        // can show the shelf's hover and the description boxes. Harness knobs for the headless path.
        if (Arg("--shop-click") is { } shopClicks && buyPass.Enabled)
            foreach (var at in shopClicks.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p2 = at.Split(',').Select(F).ToArray();
                var (sx, sy) = StatusBar.PaneFromWindow(p2[0], p2[1], width, height);
                if (!ShopPress(sx, sy) && hands is { } shopHand && OverPane((int)sx, (int)sy))
                    Console.WriteLine($"  inv: {shopHand.Click((int)sx, (int)sy).Say}");
                ShopRelease(sx, sy);
            }
        if (Arg("--pointer") is { } restAt)
        {
            var p3 = restAt.Split(',').Select(F).ToArray();
            if (net?.Story is { On: true } seat) { var (sx3, sy3) = StatusBar.FromWindow(p3[0], p3[1], width, height); seat.PointAt(sx3, sy3); }
            Pointer(p3[0], p3[1], width, height);
            rolloverPass.NowMs += Vitomir.Engine.Ui.Rollover.LargeAfterMs;
        }

        // `--hover <guid or template>` rests the pointer on a thing and lets its glow come all the way
        // up, so a still can show what hovering it looks like. The same `HoverGlow` a live pointer
        // drives, stepped a full second so the fade has finished.
        if (Arg("--hover") is { } hoverArg && player is not null)
        {
            var target = int.TryParse(hoverArg, out int hoverGuid) ? location.Thing(hoverGuid)
                       : location.Things.Where(t => t.InWorld && t.Template.Equals(hoverArg, StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(t => Vector3.Distance(t.Position, player.Position)).FirstOrDefault();
            if (target is null) Console.WriteLine($"hover: nothing called {hoverArg} here");
            else
            {
                glow.Point(target, player.Thing);
                glow.Step(1f);
                // And the description boxes, both of them: the small one at once, the large one once
                // its second and a half is up — which a still has to be given.
                rolloverPass.Hovered = target;
                _ = rolloverPass.LargeUp;
                rolloverPass.NowMs += Vitomir.Engine.Ui.Rollover.LargeAfterMs;
                var shown = Vitomir.Engine.Ui.HoverGlow.Shown(glow.Colour(target), Fixes.Current.GlowColours) * 255f;
                Console.WriteLine($"hover: {target.Template} (guid {target.Guid}) at {target.Position}, " +
                                  $"colour 0x{glow.Colour(target):x8} shown as ({shown.X:0}, {shown.Y:0}, {shown.Z:0}), " +
                                  $"intensity {glow.Intensity(target):0.00}, light raised by {glow.Base(target):0} of 255");
            }
        }

        // `--show-objects` (ours): the show-objects key held for the still (`ShowObjectsStill`).
        if (Flag("--show-objects") && player is not null) ShowObjectsStill.Hold(glow, location, player.Thing, cam, width, height, frame.WorldRows(height));

        // The menus in a headless still: `--menu-open escape|save|load|message|input`, then clicks.
        // A save they ask for is written; a load or the main menu is only reported.
        if (headless is not null && (Arg("--menu-open") is not null || Arg("--menu-click") is not null || Flag("--options") || Flag("--map")
                                     || Flag("--console") || Arg("--console-run") is not null))
        {
            menus.Frame((int)(uiClock * 1000));
            // `--console-run "line;line"` runs console lines; `--console` leaves it open for the still.
            foreach (var line in (consoleRanEarly ? "" : Arg("--console-run") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) menus.DevConsole?.Run(line);
            if (Flag("--console")) menus.DevConsole?.Open();
            // `--map-reveal` (harness only): every place marked as found, so a still shows the markers
            // a story would uncover one by one (SHOWINMAP, 0x8).
            if (Flag("--map-reveal")) foreach (var l in session.Chronicle?.Data.Locations ?? []) l.Flags |= MapScreen.ShowOnMap;
            if (Flag("--map")) OptionsHost.OpenMap(menus, session.Chronicle, files, location.Name);
            MenuOpen(menus, strings);
            MenuClicks(menus);
            var (askedFor, askedSlot) = menus.TakeRequest();
            if (askedFor == MenuRequest.Save) SaveNow(SlotPath(askedSlot), plain: false, menus.SaveName);
            else if (askedFor != MenuRequest.None) Console.WriteLine($"  menu: asked for {askedFor} {askedSlot}");
            menus.Frame((int)(uiClock * 1000));
        }
        // `--explode <template>[@x,y,z]`: an explosion (or any template) made in front of the steered
        // character — or at the point — owned by it, then `--explode-ms` (150) of the world run before
        // the still. `--no-dynalights` switches the light blobs off.
        if (Flag("--no-dynalights")) location.Lights.BlobsOff = true;
        if (Flag("--no-reflections")) frame.Game.Reflections = false;   // the mirrors off
        if (Arg("--explode") is { } explodeArg && !explodeArg.StartsWith("--")
            && (location.Steered is not null || explodeArg.Contains('@')))
        {
            var exploder = location.Steered;
            var parts = explodeArg.Split('@');
            var at = parts.Length > 1 ? parts[1].Split(',').Select(F).ToArray() is var xyz ? new Vector3(xyz[0], xyz[1], xyz[2]) : default
                   : exploder!.Position + Vector3.TransformNormal(new Vector3(0f, 120f, 0f), exploder.Rotation) + new Vector3(0f, 0f, 60f);
            var blast = location.Place(parts[0], at, exploder?.Orientation ?? Vector3.Zero, exploder?.Sector ?? location.At(at, 0));
            if (blast is null) Console.WriteLine($"explode: no template called {parts[0]}");
            else
            {
                if (blast.Type == ThingType.Explosion && exploder is not null) blast.SpawnedBy = exploder.Guid;
                float ms = F(Arg("--explode-ms") ?? "150");
                frame.Game.Advance(ms / 1000f, 1f / 100f);
                var light = location.Lights.Of(blast);
                Console.WriteLine($"explode: {blast.Template} at {at}, {ms:0} ms on: {location.Lights.Blobs.Count} light blobs"
                                  + (light is null ? "" : $", its light {light.Intensity:0.#}") + $", {location.Decals.Live.Count()} decals");
                // Who its last blow reached, so a run can tell a blast that hurt nobody (a fireball's
                // has no SETDAMAGE; a grenade's reaches 128) from one that went unseen.
                var hits = Vitomir.Engine.Explosions.ExplosionDamage.Last;
                Console.WriteLine(hits.Count == 0 ? "explode: its blow reached nobody"
                    : "explode: its blow reached " + string.Join(", ", hits.Select(h => $"{h.Target.Template} {h.Amount:0.#} at {h.Distance:0}")));
                if (Vitomir.Engine.Explosions.ExplosionDamage.LastWalled is { Count: > 0 } behind)
                    Console.WriteLine("explode: a wall kept it from " + string.Join(", ", behind.Select(t => t.Template)) + " (the repair blastsight; 1.1 would hurt them)");
            }
        }
        // `--swing`: the steered character strikes at the point 100 ahead of it, as an attack on the
        // ground does, then `--swing-ms` (250) of the world run before the still — the blade trail
        // of a sword or an axe.
        if (Flag("--swing") && location.Steered is { Actor.Weapon: { } swingWeapon } swinger)
        {
            var point = swinger.Position + Vector3.TransformNormal(new Vector3(0f, 100f, 0f), swinger.Rotation);
            // `--swing-attack <n>`: which of the weapon's attacks (most swords trail only on their second).
            if (Arg("--swing-attack") is { } swingAttack) swingWeapon.Select((int)F(swingAttack));
            Actions.StrikeAt(swinger, point, swingWeapon, frame.Sim.Milliseconds, location.Motions);
            frame.Game.Advance(F(Arg("--swing-ms") ?? "250") / 1000f, 1f / 100f);
            var blade = swingWeapon.TrailGuid != 0 ? location.Thing(swingWeapon.TrailGuid) : null;
            Console.WriteLine($"swing: {swinger.Template} with {swingWeapon.Name}: "
                              + (blade is null ? "no blade trail" : $"its trail {blade.Template} ({(location.Particles.Of(blade) as Vitomir.Engine.Particles.TrailEmitter)?.Segments.Count(g => g.Life > 0f) ?? 0} segments alive, "
                                 + $"{(location.Particles.Of(blade)?.Registered == true ? "live" : "not live")}, {location.Particles.Live.Count(e => e is not null)} of 128 live)"));
        }
        // `--film <name>`: a film started as a script's `PlayVideo` starts one (harness).
        if (Arg("--film") is { } filmName) filmScreen.Start(filmName);
        if (headless is not null && filmScreen.Playing
            && filmScreen.Still(width, height, (int)(F(Arg("--film-at") ?? "0") * 1000f)) is { } filmStill)
        {
            Png.Write(headless, width, height, filmStill);
            Console.WriteLine($"wrote {headless} ({width}x{height}): {filmScreen.Film.Name} at {filmScreen.Film.ElapsedMs} ms");
            Shutdown();
            return 0;
        }
        if (headless is not null)
        {
            HarnessCamera();
            SaveAsAsked();
            SeatFrame();
            ScreenState();
            if (menus.Up)
            {
                cursor.Set(Vitomir.Engine.Ui.CursorId.Default);
                cursorPass.X = menus.PointerX;
                cursorPass.Y = menus.PointerY;
                cursorPass.Enabled = !Flag("--no-cursor");
            }
            // A cell level's still is more than one frame of its view (ours: its screen reflections read the frame before).
            for (int warm = frame.Below?.StillFrames ?? 1; warm > 1; warm--) Still(frame, width, height);
            Png.Write(headless, width, height, Still(net?.View?.Frame ?? frame, width, height));
            Console.WriteLine($"wrote {headless} ({width}x{height}) from {cam.Position} yaw {cam.Yaw} pitch {cam.Pitch}");
            // `--thing-lights`: what lights each party member in that still (World.ModelLight).
            if (Flag("--thing-lights"))
                Console.WriteLine($"relight: {frame.Poses.Relighting.VerticesLit} vertices lit, {frame.Poses.Relighting.Changed.Count} still models sent up, {frame.Poses.Relighting.LastMs:0.00} ms in the last frame");
            if (Flag("--thing-lights"))
                foreach (var who in location.Things.Where(t => t.Actor is { } a && (a.Has(Vitomir.Engine.World.Actor.Flag.Party) || ReferenceEquals(t, location.Player?.Thing))))
                    foreach (var line in Vitomir.Engine.World.ModelLightReport.Describe(location, who, frame.Poses.Relighting.MaxLights)) Console.WriteLine(line);
            Shutdown();
            return 0;
        }
        return null;
    }
}
