// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;

namespace Vitomir;

/// <summary>
/// **Ours.** <c>vitomir --cook-l2 &lt;scene.glb&gt; [--layout layout.json] &lt;out.l2&gt;</c>: a glTF scene
/// cooked into a cell level (<see cref="CellCook"/>), and
/// <c>--level-l2 &lt;package.l2&gt;</c>, which plays one: the same as <c>--l2 &lt;its folder&gt; --level
/// &lt;its name&gt;</c>.
/// </summary>
public static class CookL2Command
{
    /// <summary>Material names (any part of them, any case) that are ground unless <c>--ground</c> says otherwise.</summary>
    public static readonly string[] DefaultGround = ["ground", "street", "floor", "cobble", "paving", "path"];

    /// <summary>Material names that stop nothing unless <c>--passable</c> says otherwise: foliage and fire.</summary>
    public static readonly string[] DefaultPassable = ["leaves", "leaf", "foliage", "flame", "fire", "smoke"];

    /// <summary>
    /// The sun the cook puts in a level when <c>--sun</c> names none: bearing 150°, 38° up — late
    /// morning — by the level's compass (the layout's <c>frame.north</c> or <c>--north</c>; north
    /// along +X, <see cref="ModernMath.OldTownNorthYaw"/>, without one). Ours.
    /// </summary>
    public const float DefaultSunAzimuth = 150f, DefaultSunElevation = 38f;

    /// <summary>
    /// Runs <c>--cook-l2</c> when asked; turns <c>--level-l2 &lt;package&gt;</c> into <c>--l2</c> and
    /// <c>--level</c> in place, for the rest of the run to read. A package that carries a 1.1 scene
    /// (<c>--cook-l2 … --scene CHAPTER</c>, <see cref="L2Scene"/>) is played as the 1.1
    /// level it stands for, with its chapter scene, unless <c>--no-scene</c> asks for the bare level.
    /// </summary>
    public static int? Handle(ref string[] args)
    {
        int at = Array.IndexOf(args, "--level-l2");
        if (at >= 0 && at + 1 < args.Length)
        {
            var package = Path.GetFullPath(args[at + 1].TrimEnd('/', '\\'));
            if (!File.Exists(Path.Combine(package, L2Package.ManifestFile)))
            {
                Console.Error.WriteLine($"l2: {package} is not a level package (no {L2Package.ManifestFile})");
                return 2;
            }
            var manifest = L2Package.Open(package).Manifest;
            var name = manifest.Name;
            var root = Path.GetDirectoryName(package)!;
            if (!string.Equals(Path.GetFileName(package), name + L2Package.Extension, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"l2: {package} holds level {name}; a package is found by its folder's name, {name}{L2Package.Extension}");
            L2Levels.Folder = root;
            // Opened at an entrance, the level reads that entrance's cells first (ours; CellLevel.Load).
            if (Array.IndexOf(args, "--entrance") is var en and >= 0 && en + 1 < args.Length && int.TryParse(args[en + 1], out int entranceAsked))
                CellStreamOptions.OpenAtEntrance = entranceAsked;
            string[] sceneArgs = [];
            // A level that carries a 1.1 scene is played as that level, with its chapter scene (ours;
            // L2Levels.StandIns); --no-scene plays the bare level, as before.
            if (manifest.Scene is { Level.Length: > 0 } carried && !args.Contains("--no-scene"))
            {
                L2Levels.StandIns[carried.Level] = package;
                Console.WriteLine($"l2: {name} stands in for {carried.Level}" + (carried.Chapter is null ? "" : $", scene {carried.Chapter}"));
                name = carried.Level;
                if (carried.Chapter is not null && !args.Contains("--scene")) sceneArgs = ["--scene", carried.Chapter];
            }
            args = [.. args[..at], "--l2", root, "--level", name, .. sceneArgs, .. args[(at + 2)..].Where(a => a != "--no-scene")];
            // A quality asked for is kept for the next run only from a played window, not a still or a replay (ours).
            Render.CellLooks.RememberQuality = !args.Contains("--headless") && !args.Any(a => a.StartsWith("--replay", StringComparison.Ordinal));
            args = LookOptions(args);
        }
        return args.Contains("--cook-l2") ? Run(args) : null;
    }

    /// <summary>
    /// A played cell level's <c>--quality low|medium|high|ultra|auto</c> (the graphics preset), <c>--taa</c> or <c>--no-taa</c>, <c>--cloud-pass half|full</c>,
    /// <c>--look morning|chronicle|golden|dusk|level</c>, <c>--time-of-day h</c>, <c>--day n</c> (the day of the year, over the level's date),
    /// <c>--clouds cover</c>, <c>--wind from,speed</c>, <c>--cloud-steps n</c> and <c>--cloud-time s</c> (the modern renderer's
    /// looks, sun and clouds), taken out of the arguments into
    /// <see cref="Render.CellLooks"/>. Ours.
    /// </summary>
    static string[] LookOptions(string[] args)
    {
        var kept = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--look" && i + 1 < args.Length) { Render.CellLooks.Look = args[++i]; continue; }
            if (args[i] == "--time-of-day" && i + 1 < args.Length)
            {
                Render.CellLooks.TimeOfDay = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            if (args[i] == "--day" && i + 1 < args.Length) { Render.CellLooks.Day = (int)F(args[++i]); continue; }
            if (args[i] == "--quality" && i + 1 < args.Length) { Render.CellLooks.Quality = args[++i]; Render.Modern.ModernQualities.Check(Render.CellLooks.Quality); continue; }
            if (args[i] == "--cloud-pass" && i + 1 < args.Length) { Render.CellLooks.CloudsHalf = args[++i] != "full"; continue; }
            if (args[i] is "--taa" or "--no-taa") { Render.CellLooks.Taa = args[i] == "--taa"; continue; }
            if (args[i] == "--probes" && i + 1 < args.Length) { Render.CellLooks.Probes = args[++i] != "off"; continue; }
            if (args[i] == "--tiling" && i + 1 < args.Length) { Render.CellLooks.Tiling = args[++i] != "plain"; continue; }
            if (args[i] == "--probe-bounce" && i + 1 < args.Length) { Render.CellLooks.ProbeBounce = F(args[++i]); continue; }
            if (args[i] == "--exposure" && i + 1 < args.Length) { Render.CellLooks.Exposure = F(args[++i]); continue; }
            if (args[i] == "--ao-radius" && i + 1 < args.Length) { Render.CellLooks.AoRadius = F(args[++i]); continue; }
            if (args[i] == "--clouds" && i + 1 < args.Length) { Render.CellLooks.CloudCover = F(args[++i]); continue; }
            if (args[i] == "--cloud-steps" && i + 1 < args.Length) { Render.CellLooks.CloudSteps = (int)F(args[++i]); continue; }
            if (args[i] == "--cloud-time" && i + 1 < args.Length) { Render.CellLooks.CloudTime = F(args[++i]); continue; }
            if (args[i] == "--wind" && i + 1 < args.Length)
            {
                var w = args[++i].Split(',');
                Render.CellLooks.Wind = (F(w[0]), w.Length > 1 ? F(w[1]) : 6f);
                continue;
            }
            kept.Add(args[i]);
        }
        return [.. kept];
        static float F(string x) => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture);
    }

    static int Run(string[] args)
    {
        int at = Array.IndexOf(args, "--cook-l2");
        var positional = new List<string>();
        for (int i = at + 1; i < args.Length && positional.Count < 2; i++)
        {
            if (args[i] == "--layout" || args[i] == "--name" || args[i] == "--ground" || args[i] == "--passable" || args[i] == "--sun" || args[i] == "--north"
                || args[i] == "--walk-nodes" || args[i] == "--cell-size" || args[i] == "--block" || args[i] == "--repeat" || args[i] == "--skyline" || args[i] == "--scene" || args[i] == "--scene-drop"
                || args[i] == "--nav-agent") { i++; continue; }
            if (args[i].StartsWith("--")) break;
            positional.Add(args[i]);
        }
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("usage: vitomir --cook-l2 <scene.glb|gltf> [--layout layout.json] <out.l2> " +
                                    "[--name N] [--ground a,b] [--passable a,b] [--walk-nodes a,b] [--sun azimuth,elevation] [--north yaw] " +
                                    "[--cell-size metres (48; 0 for one cell)] [--block cells (2)] [--repeat CxR[,stepX,stepY]] [--skyline city.glb] [--scene [LEVEL/]CHAPTER] [--scene-drop a,b] [--nav-agent human|radius,height,climb,slope] [--no-textures] [--no-probes] [--probes-only]");
            return 2;
        }
        string source = positional[0], output = positional[1].TrimEnd('/', '\\');
        string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        // --nav-agent (ours): the navigation mesh baked for that agent, recorded in
        // every cell's NAVM; without it the cook's own NavSettings.Default, so a cook without it makes the same package.
        // With it (and only then) the cook's own ledge filter for steep stairs (ours, for cell levels;
        // NavSettings.StairLedges), recorded in level.json's "navigation".
        var nav = NavSettings.Default;
        if (args.Contains("--nav-agent"))
        {
            if (NavSettings.ParseAgent(Opt("--nav-agent"), out string navError) is not { } agent) { Console.Error.WriteLine($"cook: {navError}"); return 2; }
            nav = agent with { StairLedges = true };
            Console.WriteLine($"cook: the navigation mesh for an agent of {nav.Describe()}");
        }
        string name = Opt("--name") ?? Path.GetFileNameWithoutExtension(output);
        var ground = Opt("--ground")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? DefaultGround;
        var passable = Opt("--passable")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? DefaultPassable;
        var sun = Opt("--sun")?.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray()
                  ?? [DefaultSunAzimuth, DefaultSunElevation];

        var watch = System.Diagnostics.Stopwatch.StartNew();
        // The images stay undecoded until their maps are cooked, one or two at a time (ours: a town's maps
        // decoded at once were two gigabytes of the cook's peak).
        var scene = GltfReader.Load(source, new GltfReadOptions(DeferImages: true));
        Console.WriteLine($"cook: read {source}: {scene.Primitives.Count} primitives, {scene.TriangleCount:N0} triangles, " +
                          $"{scene.Materials.Count} materials ({watch.Elapsed.TotalSeconds:0.0}s)");
        // The skyline (ours): its materials join the level's; the level's meshes it stands for go.
        var skyline = Opt("--skyline") is { } skylinePath ? SkylineSource.Read(skylinePath, scene) : null;

        // Every primitive's triangles into one list, with its material's name.
        var materialNames = scene.Materials.Select((m, i) => string.IsNullOrEmpty(m.Name) ? $"material{i}" : m.Name).ToList();
        int defaultMaterial = -1;
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var triMaterials = new List<int>();
        var draws = new List<L2Draw>();
        var triWalkable = new List<bool>();
        // The meshes that may be walked on, when the level says: --walk-nodes, else the layout's ground.walkNodes,
        // else the first word of its ground.walkable (the generator's own note). Otherwise any ground material.
        var layoutEarly = Opt("--layout") is { } lpe && File.Exists(lpe) ? JsonNode.Parse(File.ReadAllText(lpe)) : null;
        // The level's date (ours; L2Date): the layout's, kept in the manifest; the level's own sun and the
        // light probes stand where the morning's sun does on it, unless --sun says otherwise.
        var date = L2Date.FromLayout(layoutEarly);
        if (date is not null)
        {
            if (Opt("--sun") is null)
            {
                var (az, el) = ModernMath.SunAt(date.Hour ?? Render.Modern.ModernLooks.MorningHour, date.DayOfYear);
                sun = [MathF.Round(az, 2), MathF.Round(el, 2)];
            }
            Console.WriteLine($"cook: the level's date: {date}; its sun at azimuth {sun[0]:0.#}°, elevation {sun[1]:0.#}°");
        }
        // The level's compass (ours; L2North): the layout's frame.north or --north, kept in the manifest;
        // the sun's bearing above is counted from it, here and in the light probes. None: north along +X, as before.
        var north = Opt("--north") is { } ny ? new L2North { Yaw = float.Parse(ny, CultureInfo.InvariantCulture) } : L2North.FromLayout(layoutEarly);
        if (north is not null) Console.WriteLine($"cook: the level's compass: {north} (its +X bears {north.Yaw:0.#}°)");
        // The level's named volumes (ours; L2Area): the layout's interiors, kept in the manifest.
        var areas = L2Area.FromLayout(layoutEarly);
        if (areas is not null) Console.WriteLine($"cook: {areas.Count} area{(areas.Count == 1 ? "" : "s")}: {string.Join("; ", areas)}");
        // The 1.1 scene the level stands for (ours; L2Scene): named, never copied — read from the install at load.
        var sceneFor = L2Scene.FromLayout(layoutEarly, Opt("--scene"), Opt("--scene-drop"));
        var scenePlaces = sceneFor is null ? [] : L2Scene.Places(layoutEarly);
        if (sceneFor is not null)
            Console.WriteLine($"cook: carries 1.1's scene {sceneFor}" + (scenePlaces.Count > 0 ? $"; {scenePlaces.Count} of its things placed by anchors" : ""));
        // Where the new ground is deliberately not 1.1's (ours; L2Lift): the layout's lifts, kept in the manifest
        // for the scene's loader. A patch that is not sound stops the cook: dropped in silence it leaves 1.1's things buried.
        var liftFaults = new List<string>();
        var lifts = L2Lift.FromLayout(layoutEarly, liftFaults);
        if (liftFaults.Count > 0)
        {
            foreach (var fault in liftFaults) Console.Error.WriteLine($"cook: the layout's {fault}");
            return 2;
        }
        if (lifts is not null) Console.WriteLine($"cook: {lifts.Count} ground lift{(lifts.Count == 1 ? "" : "s")}: {string.Join("; ", lifts)}");
        // The level's ladders (ours, L2Traversal): the layout's traversal.ladders, kept in the manifest for the
        // platform. A ladder that is not sound stops the cook: dropped in silence it is a way up that is not there.
        var ladderFaults = new List<string>();
        var traversal = L2Traversal.FromLayout(layoutEarly, ladderFaults);
        if (ladderFaults.Count > 0)
        {
            foreach (var fault in ladderFaults) Console.Error.WriteLine($"cook: the layout's {fault}");
            return 2;
        }
        if (traversal?.Ladders is { } ladders) Console.WriteLine($"cook: {ladders.Count} ladder{(ladders.Count == 1 ? "" : "s")}: {string.Join("; ", ladders)}");
        var walkNodes = CellCook.WalkNodes(Opt("--walk-nodes"), layoutEarly);
        if (walkNodes is not null) Console.WriteLine($"cook: walkable meshes: {string.Join(", ", walkNodes)}");
        var passMeshes = CellCook.PassableMeshes(layoutEarly);
        // Meshes kept for collision and navigation and not drawn (ours; CellCook.CollisionOnlyMeshes).
        var collisionOnly = CellCook.CollisionOnlyMeshes(layoutEarly);
        if (collisionOnly.Length > 0) Console.WriteLine($"cook: collision only, not drawn: {string.Join(", ", collisionOnly)}");
        // Meshes not to be climbed (ours, CellCook.NoClimbMeshes): their collision triangles carry the NoClimb flag.
        var noClimb = CellCook.NoClimbMeshes(layoutEarly);
        if (noClimb.Length > 0) Console.WriteLine($"cook: not to be climbed: {string.Join(", ", noClimb)}");
        var triNoClimb = new List<bool>();
        // Meshes no body is to stand on (ours, CellCook.NoStandMeshes): their collision triangles carry NoStand, never Walk.
        var noStand = CellCook.NoStandMeshes(layoutEarly);
        if (noStand.Length > 0) Console.WriteLine($"cook: not to be stood on: {string.Join(", ", noStand)}");
        var triNoStand = new List<bool>();
        var triPassable = new List<bool>();
        const float Scale = L2Manifest.EngineUnitsPerMetre;
        foreach (var p in scene.Primitives)
        {
            int m = p.Material;
            if (m < 0) { if (defaultMaterial < 0) { defaultMaterial = materialNames.Count; materialNames.Add("(default)"); } m = defaultMaterial; }
            int baseVertex = vertices.Count;
            vertices.AddRange(p.Vertices.Select(v => v.Position));
            foreach (var i in p.Indices) indices.Add(baseVertex + (int)i);
            // A primitive is named "<mesh>#<index>" (GltfReader); the level names meshes.
            bool walkOk = walkNodes is null || walkNodes.Contains(p.Name.Split('#')[0], StringComparer.OrdinalIgnoreCase);
            bool passOk = passMeshes.Contains(p.Name.Split('#')[0], StringComparer.OrdinalIgnoreCase);
            bool climbNo = noClimb.Contains(p.Name.Split('#')[0], StringComparer.OrdinalIgnoreCase);
            bool standNo = noStand.Contains(p.Name.Split('#')[0], StringComparer.OrdinalIgnoreCase);
            for (int t = 0; t < p.Indices.Length / 3; t++) { triMaterials.Add(m); triWalkable.Add(walkOk); triPassable.Add(passOk); triNoClimb.Add(climbNo); triNoStand.Add(standNo); }
            if (CellCook.Drawn(p.Name, collisionOnly)) draws.Add(new L2Draw(materialNames[m], p.Indices.Length / 3, p.Min / Scale, p.Max / Scale));
        }
        bool Named(string material, string[] parts) => parts.Any(x => material.Contains(x, StringComparison.OrdinalIgnoreCase));
        // The areas no body is to stand in (ours, CellCook.MarkNoStandAreas): every triangle wholly inside one's box is NoStand.
        var standProblems = new List<string>();
        var noStandAreas = CellCook.NoStandAreas(areas, standProblems);
        foreach (var problem in standProblems) Console.WriteLine($"cook: the layout's {problem}");
        bool[]? noStandTriangles = noStand.Length == 0 && noStandAreas.Count == 0 ? null : [.. triNoStand];
        if (noStandAreas.Count > 0)
        {
            var held = CellCook.MarkNoStandAreas(noStandAreas, vertices, indices, Scale, noStandTriangles!);
            for (int k = 0; k < noStandAreas.Count; k++)
                Console.WriteLine($"cook: area {noStandAreas[k].Name} not to be stood on: {held[k]} triangle{(held[k] == 1 ? "" : "s")}" +
                                  (held[k] == 0 ? " (no triangle lies wholly inside its box: nothing marked)" : ""));
        }

        var lights = Torches(vertices, indices, triMaterials, materialNames);
        var layout = Opt("--layout") is { } lp && File.Exists(lp) ? JsonNode.Parse(File.ReadAllText(lp)) : null;
        // The street's lanterns by day (ours; L2LayoutLights.LanternsOut): out, so no torch stands at one.
        if (L2LayoutLights.LanternsOut(layout) is { } lanternsOut)
        {
            int torches = lights.Count;
            lights = L2LayoutLights.WithoutLanterns(lights, L2LayoutLights.LanternFlames(layout));
            Console.WriteLine($"cook: the street's lanterns are out ({lanternsOut}); {torches - lights.Count} of the {torches} torches stood at one and are left out");
        }
        // The lights the layout places (ours; L2LayoutLights): a room's fire and lanterns, in place of the torches at their flames.
        if (L2LayoutLights.FromLayout(layout) is { Count: > 0 } placed)
        {
            int torches = lights.Count;
            lights = L2LayoutLights.With(lights, placed);
            Console.WriteLine($"cook: {placed.Count} lights placed by the layout ({string.Join(", ", placed.Select(l => l.Name))}), in place of {torches + placed.Count - lights.Count} of the {torches} torches");
        }
        // Every entrance's starts and the chronicle's exits, when the layout has them (ours; L2LayoutExits).
        var anchors = L2LayoutExits.Starts(layout, name) ?? new List<L2Anchor> { Start(name, layout, vertices) };
        var exits = L2LayoutExits.Exits(layout);
        if (exits is not null) Console.WriteLine($"cook: {anchors.Count} starts; {exits.Count} exits: {string.Join(", ", exits.Select(e => $"{e.Number} {e.To}{(e.Volume is null ? "" : " (walk-in)")}"))}");
        anchors.AddRange(scenePlaces);                      // the carried scene's things moved by the layout (ours)

        float northYaw = north?.Yaw ?? L2North.DefaultYaw;
        var toSun = ModernMath.SunDirection(sun[0], sun[1], northYaw);
        var extras = new JsonObject
        {
            ["modern"] = new JsonObject { ["sunAzimuth"] = sun[0], ["sunElevation"] = sun[1], ["northYaw"] = northYaw },
            ["cookedFrom"] = Path.GetFileName(source),
        };
        if (Opt("--layout") is { } layoutPath) extras["layout"] = Path.GetFileName(layoutPath);

        // --probes-only (ours): bake the light probes again into a package already cooked from this scene.
        if (args.Contains("--probes-only"))
        {
            if (!File.Exists(Path.Combine(output, L2Package.ManifestFile))) { Console.Error.WriteLine($"cook: {output} is not a level package"); return 2; }
            ModernProbeBake.Cook(scene, m => m < materialNames.Count && Named(materialNames[m], passable), layout, output, 2, Console.WriteLine, date, north);
            return 0;
        }
        // A package is cooked afresh; anything else already there is not ours to delete.
        if (Directory.Exists(output))
        {
            if (!File.Exists(Path.Combine(output, L2Package.ManifestFile)))
            {
                Console.Error.WriteLine($"cook: {output} exists and is not a level package; not writing over it");
                return 2;
            }
            Directory.Delete(output, recursive: true);
        }
        // The render meshes for the cells' MESH buffers (metres, the renderer's own vertex layout), one per primitive.
        var meshes = new List<CookMesh>(scene.Primitives.Count);
        foreach (var p in scene.Primitives)
        {
            if (!CellCook.Drawn(p.Name, collisionOnly)) continue;
            var vs = System.Runtime.InteropServices.MemoryMarshal.Cast<ModernVertex, L2Vertex>(p.Vertices).ToArray();
            for (int i = 0; i < vs.Length; i++) vs[i].Position /= Scale;
            int m = p.Material < 0 ? defaultMaterial : p.Material;
            meshes.Add(new CookMesh(p.Name, m, vs, p.Indices));
        }
        float cellSize = Opt("--cell-size") is { } cs ? float.Parse(cs, CultureInfo.InvariantCulture) : CellCook.DefaultCellSize;
        int block = Opt("--block") is { } bs ? int.Parse(bs, CultureInfo.InvariantCulture) : 2;
        var repeat = Opt("--repeat") is { } rs ? Repeat(rs, scene) : CookRepeat.None;
        if (repeat.Count > 1) Console.WriteLine($"cook: the scene laid down {repeat.Columns} × {repeat.Rows} times, {repeat.StepX:0.#} m × {repeat.StepY:0.#} m apart (a stress level)");
        var result = CellCook.Cook(new CellCookInput
        {
            Meshes = meshes, CellSize = cellSize, BlockCells = block, Repeat = repeat, Log = Console.WriteLine,
            Name = name, Vertices = [.. vertices], Indices = [.. indices], TriangleMaterials = [.. triMaterials],
            Materials = [.. materialNames], Ground = m => Named(m, ground), Passable = m => Named(m, passable),
            Foliage = [.. materialNames.Select((_, i) => i < scene.Materials.Count && scene.Materials[i].Foliage)],
            TriangleWalkable = walkNodes is null ? null : [.. triWalkable],
            TrianglePassable = passMeshes.Length == 0 ? null : [.. triPassable],
            TriangleNoClimb = noClimb.Length == 0 ? null : [.. triNoClimb],
            TriangleNoStand = noStandTriangles,
            Asset = source, Draws = draws, Lights = lights, Anchors = anchors,
            Sun = new L2GlobalLight
            {
                Type = 2, Color = [255, 230, 170], Direction = [-toSun.X, -toSun.Y, -toSun.Z], Intensity = 250,
                Ambient = [51000, 46000, 34000],
            },
            Extras = extras, Date = date, North = north, Areas = areas, Exits = exits, Scene = sceneFor, Lifts = lifts,
            Traversal = traversal,
            Nav = nav,
        }, output);
        Console.WriteLine($"cook: {result.Package}: {result.Triangles:N0} collision triangles ({result.Walkable:N0} walkable), " +
                          $"navigation mesh {result.NavWalkPolygons:N0} walkable polygons of {result.NavPolygons:N0} ({result.NavTime.TotalSeconds:0.0}s), " +
                          $"{result.Lights} lights, {result.Anchors} anchors; {watch.Elapsed.TotalSeconds:0.0}s in all");
        CookMemory.Settle();
        if (!args.Contains("--no-textures")) CookMaterials(scene, materialNames, m => Named(m, ground), m => Named(m, passable), output);
        skyline?.Cook(output);
        CookMemory.Settle();
        // The light probes (ours; ModernProbeBake): passable materials let light by.
        if (!args.Contains("--no-probes"))
            ModernProbeBake.Cook(scene, m => m < materialNames.Count && Named(materialNames[m], passable), layout, output, 2, Console.WriteLine, date, north);
        Console.WriteLine($"cook: {result.Cells} cells, {result.FarVersions} far versions; drawn triangles {result.Lod0Triangles:N0} (LOD0), " +
                          $"{result.Lod1Triangles:N0} (LOD1), {result.FarTriangles:N0} (far); {result.CellBytes / 1048576.0:0.0} MB of cells, " +
                          $"{result.FarBytes / 1048576.0:0.0} MB of far versions; {watch.Elapsed.TotalSeconds:0}s, {CookMemory.PeakMB():N0} MB of memory at the most");
        Console.WriteLine($"cook: play it with  vitomir-tools --level-l2 {result.Package} --player");
        return 0;
    }

    /// <summary>
    /// The materials' definitions and their maps (ours, Phase 3): each glTF material to
    /// <c>materials/&lt;name&gt;.json</c> (<see cref="L2Material"/>), each map it uses to
    /// <c>textures/&lt;image&gt;.ktx2</c> — BC7 sRGB colour, BC7 linear data, BC5 normals, BC4 height, full mips
    /// (<see cref="TextureCook"/>; a cut-out material's colour with mips that keep its cover) — and the manifest's
    /// <c>materialFiles</c> in its materials' order. The images are decoded here, one or two at a time.
    /// </summary>
    static void CookMaterials(GltfScene scene, List<string> names, Func<string, bool> ground, Func<string, bool> passable, string output)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var jobs = new Dictionary<(int Image, TextureKind Kind), string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        L2MaterialMap? Map(GltfTextureSlot slot, TextureKind kind)
        {
            if (!slot.Present || slot.Texture >= scene.Textures.Count) return null;
            var t = scene.Textures[slot.Texture];
            if (t.Image < 0 || t.Image >= scene.Images.Count || !scene.Images[t.Image].Present) return null;
            if (!jobs.TryGetValue((t.Image, kind), out var file))
            {
                var stem = Safe(scene.Images[t.Image].Name is { Length: > 0 } n ? n : $"image{t.Image}");
                file = $"textures/{stem}.ktx2";
                if (!used.Add(file)) { file = $"textures/{stem}_{kind.ToString().ToLowerInvariant()}.ktx2"; used.Add(file); }
                jobs[(t.Image, kind)] = file;
            }
            return new L2MaterialMap { File = file, TexCoord = slot.TexCoord, RepeatU = t.RepeatU, RepeatV = t.RepeatV, MirrorU = t.MirrorU, MirrorV = t.MirrorV };
        }
        var files = new List<string>();
        var materials = new List<L2Material>();
        for (int i = 0; i < names.Count; i++)
        {
            var m = i < scene.Materials.Count ? scene.Materials[i] : GltfMaterial.Default();
            var def = CellMaterials.Define(m, names[i], Map, ground(names[i]), passable(names[i]));
            materials.Add(def);
            var file = $"materials/{Safe(names[i])}.json";
            for (int k = 2; files.Contains(file, StringComparer.OrdinalIgnoreCase); k++) file = $"materials/{Safe(names[i])}_{k}.json";
            files.Add(file);
        }
        // A cut-out material's base colour keeps its cover down the mips (ours; TextureCook.KeepCoverage):
        // the alpha its test reads is the map's times the factor's, so the map's own cutoff is the cutoff over that.
        var cutoffs = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in materials)
            if (def.AlphaMode == "MASK" && def.BaseColorTexture?.File is { } map && def.BaseColorFactor[3] > 0f)
                cutoffs.TryAdd(map, Math.Clamp(def.AlphaCutoff / def.BaseColorFactor[3], 0f, 1f));
        Directory.CreateDirectory(Path.Combine(output, "textures"));
        Directory.CreateDirectory(Path.Combine(output, "materials"));
        long bytes = 0, source = 0, decodeTicks = 0;
        var kindTicks = new long[Enum.GetValues<TextureKind>().Length];
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 2 }, job =>
        {
            // Decoded here and let go when its file is written: only the maps being cooked are in memory.
            var img = scene.Images[job.Key.Image];
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var decoded = img.Decode();
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            Interlocked.Add(ref decodeTicks, t1 - t0);
            if (decoded is not { } rgba) { Console.WriteLine($"cook: image {img.Name}: a PNG the reader cannot decode; {job.Value} left out"); return; }
            if (job.Key.Kind == TextureKind.Colour) img.Mean ??= ModernProbeBake.MeanLinear(rgba, img.Width, img.Height);
            float? cutoff = job.Key.Kind == TextureKind.Colour && cutoffs.TryGetValue(job.Value, out var c) ? c : null;
            var ktx = TextureCook.Cook(rgba, img.Width, img.Height, job.Key.Kind, cutoff).Write();
            Interlocked.Add(ref kindTicks[(int)job.Key.Kind], System.Diagnostics.Stopwatch.GetTimestamp() - t1);
            File.WriteAllBytes(Path.Combine(output, job.Value), ktx);
            Interlocked.Add(ref bytes, ktx.Length);
            Interlocked.Add(ref source, (long)img.Width * img.Height * 4 * 4 / 3);
            CookMemory.Settle();
        });
        for (int i = 0; i < files.Count; i++) File.WriteAllText(Path.Combine(output, files[i]), materials[i].Write(), new System.Text.UTF8Encoding(false));
        var package = L2Package.Open(output);
        package.Manifest.MaterialFiles = files;
        File.WriteAllText(Path.Combine(output, L2Package.ManifestFile), package.Manifest.Write(), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"cook: {files.Count} materials, {jobs.Count} textures to KTX2 (BC7/BC5/BC4, full mips): {bytes / 1048576.0:0.0} MB " +
                          $"(RGBA8 with mips would be {source / 1048576.0:0.0} MB) in {watch.Elapsed.TotalSeconds:0.0}s");
        double S(long ticks) => ticks / (double)System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"cook: maps' thread-seconds: reading and decoding the PNGs {S(decodeTicks):0}, mips and compressing colour (BC7) " +
                          $"{S(kindTicks[(int)TextureKind.Colour]):0}, data (BC7) {S(kindTicks[(int)TextureKind.Data]):0}, " +
                          $"normals (BC5) {S(kindTicks[(int)TextureKind.Normal]):0}, height (BC4) {S(kindTicks[(int)TextureKind.Height]):0}");
    }

    /// <summary>A name as a file name: letters, digits, '-' and '_' kept, anything else '_'.</summary>
    static string Safe(string name) => new([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);

    /// <summary>
    /// <c>--repeat CxR[,stepX,stepY]</c>: the scene laid down C × R times (ours, a stress level), stepped by
    /// the given metres or else by the scene's own extent (its triangles no wider than 150 m) and 4 m more.
    /// </summary>
    static CookRepeat Repeat(string text, GltfScene scene)
    {
        var parts = text.Split(',');
        var cr = parts[0].Split('x');
        int c = int.Parse(cr[0], CultureInfo.InvariantCulture), r = int.Parse(cr[1], CultureInfo.InvariantCulture);
        if (parts.Length >= 3)
            return new CookRepeat(c, r, float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
        const float Scale = L2Manifest.EngineUnitsPerMetre;
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var p in scene.Primitives)
        {
            if ((p.Max.X - p.Min.X) / Scale > 150 || (p.Max.Y - p.Min.Y) / Scale > 150) continue;
            lo = Vector3.Min(lo, p.Min / Scale); hi = Vector3.Max(hi, p.Max / Scale);
        }
        return new CookRepeat(c, r, MathF.Ceiling(hi.X - lo.X) + 4f, MathF.Ceiling(hi.Y - lo.Y) + 4f);
    }

    /// <summary>
    /// The torches: every flame-coloured cluster of triangles (a material named <c>flame</c> or
    /// <c>fire</c>) as one point light a little above its middle, warm, reaching 8 m. Ours.
    /// </summary>
    static List<L2Light> Torches(List<Vector3> vertices, List<int> indices, List<int> triMaterials, List<string> materials)
    {
        const float Scale = L2Manifest.EngineUnitsPerMetre, Join = 0.6f * Scale;
        var centres = new List<Vector3>();
        for (int t = 0; t < triMaterials.Count; t++)
        {
            var m = materials[triMaterials[t]];
            if (!m.Contains("flame", StringComparison.OrdinalIgnoreCase) && !m.Contains("fire", StringComparison.OrdinalIgnoreCase)) continue;
            centres.Add((vertices[indices[3 * t]] + vertices[indices[3 * t + 1]] + vertices[indices[3 * t + 2]]) / 3f);
        }
        var clusters = new List<(Vector3 Sum, int Count)>();
        foreach (var c in centres)
        {
            int found = clusters.FindIndex(k => Vector3.Distance(k.Sum / k.Count, c) < Join);
            if (found < 0) clusters.Add((c, 1));
            else clusters[found] = (clusters[found].Sum + c, clusters[found].Count + 1);
        }
        return [.. clusters.Select((k, i) => new L2Light($"torch{i:00}", L2Light.Point, k.Sum / k.Count / Scale + new Vector3(0, 0, 0.1f),
                                                         new Vector3(1f, 0.62f, 0.28f), 2f, 8f))];
    }

    /// <summary>
    /// The player's start: the layout's own (<c>start</c> or <c>player_start</c>: a position in metres, Z up,
    /// and a yaw in degrees) when it has one; otherwise 1.5 m in front of its street camera looking
    /// the way the camera looks; otherwise the middle of the scene. The height is found on the ground
    /// when the level loads (<see cref="CellShell.Scene"/>).
    /// </summary>
    static L2Anchor Start(string level, JsonNode? layout, List<Vector3> vertices)
    {
        Vector3 at; float facing;   // facing: the compass of the walk, radians, 0 along +X
        float? startYaw = null;     // a start's own rotation, when the layout carries one (already a start's yaw)
        if (layout?["playerStart"] is JsonObject ps && ps["position"] is JsonArray pp)
        {
            // The Old Town generator's: 1.1's own start anchor, in the manifest's frame, with its NSD rotation.
            at = new Vector3((float)pp[0]!, (float)pp[1]!, (float)pp[2]!);
            facing = 0f;
            if (ps["rotation"] is JsonArray pr && pr.Count == 3) startYaw = (float)pr[2]!;
        }
        else if ((layout?["start"] ?? layout?["player_start"]) is JsonObject start && start["position"] is JsonArray sp)
        {
            at = new Vector3((float)sp[0]!, (float)sp[1]!, (float)sp[2]!);
            facing = (start["yaw"] is { } y ? (float)y : 0f) * MathF.PI / 180f;
        }
        else if (layout?["cameras"]?["street"] is JsonObject cam && cam["loc"] is JsonArray loc && cam["target"] is JsonArray target)
        {
            // Blender's frame is the manifest's (metres, Z up; engine = 72 × Blender).
            var from = new Vector3((float)loc[0]!, (float)loc[1]!, (float)loc[2]!);
            var to = new Vector3((float)target[0]!, (float)target[1]!, (float)target[2]!);
            var flat = Vector2.Normalize(new Vector2(to.X - from.X, to.Y - from.Y));
            at = new Vector3(from.X + flat.X * 1.5f, from.Y + flat.Y * 1.5f, from.Z);
            facing = MathF.Atan2(flat.Y, flat.X);
        }
        else
        {
            var min = vertices.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = vertices.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            at = (min + max) * 0.5f / L2Manifest.EngineUnitsPerMetre;
            at.Z = max.Z / L2Manifest.EngineUnitsPerMetre;
            facing = 0f;
        }
        // A start faces its own +Y, so its yaw is the walk's compass less 90°.
        float yaw = startYaw ?? facing * 180f / MathF.PI - 90f;
        return new L2Anchor
        {
            Id = $"{level}/starts/0", Kind = "start", Scene = level, Index = 0, Name = "playerstart00",
            Position = [at.X, at.Y, at.Z], Rotation = [0, 0, yaw], Group = 0,
        };
    }
}
