// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using DotRecast.Core.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

public static partial class CellCook
{
    /// <summary>The resident cell's id: the triangles wider than a cell, loaded with the level and never unloaded.</summary>
    public const string ResidentId = "all";

    /// <summary>
    /// Cooks a level (ours, Phase 3). With a
    /// <see cref="CellCookInput.CellSize"/>, the level is cut into a grid of square cells, each a file
    /// with its own <c>COLL</c>, <c>NAVM</c> tile, <c>LITE</c>, <c>ANCH</c> and <c>MESH</c>; the blocks of
    /// cells get far versions; the manifest is version 2. Without one, it is one cell covering
    /// everything, with a navigation mesh of one tile over the walkable ground, as before Phase 3.
    ///
    /// **Which cell a triangle is in: its centroid's** (not clipped). Every triangle lands in exactly
    /// one cell, whole, so none is lost or doubled, and a cell's collision box reaches as far past its
    /// square as its triangles do; a query anywhere asks every loaded cell whose box it meets
    /// (<see cref="CellWorld"/>), so a border has no seam. A triangle wider than a cell in X or Y (a
    /// field plane to the horizon) belongs to no one cell and goes to the resident cell
    /// <see cref="ResidentId"/>. The navigation mesh is not cut this way: each tile is Recast's over its
    /// square and a border of the neighbours' triangles, so tiles meet edge to edge and Detour links them.
    /// </summary>
    public static CellCookResult Cook(CellCookInput input, string packageFolder)
    {
        float scale = L2Manifest.EngineUnitsPerMetre;
        var log = input.Log ?? (_ => { });
        bool gridded = input.CellSize > 0;
        int reps = input.Repeat.Count;
        var offsets = Enumerable.Range(0, reps).Select(input.Repeat.Offset).ToArray();

        // 1. The collision triangles: every one but the passable, in metres, Z up, each Solid and Walk if it can be stood on.
        int triangles = input.Indices.Length / 3;
        var keep = new List<int>(triangles);
        var surfaceList = new List<L2Surface>(triangles);
        var materialList = new List<ushort>(triangles);
        var passable = input.Materials.Select(input.Passable).ToArray();
        var ground = input.Materials.Select(input.Ground).ToArray();
        for (int t = 0; t < triangles; t++)
        {
            int m = input.TriangleMaterials[t];
            if (m >= 0 && m < passable.Length && passable[m]) continue;
            if (input.TrianglePassable is { } decoration && decoration[t]) continue;
            var (a, b, c) = (input.Vertices[input.Indices[3 * t]], input.Vertices[input.Indices[3 * t + 1]], input.Vertices[input.Indices[3 * t + 2]]);
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-12f) continue;                        // a sliver has no face to stand on or hit
            n = Vector3.Normalize(n);
            var s = L2Surface.Solid;
            // glTF's winding is counter-clockwise from the front, so an up-facing floor's normal is +Z.
            if (m >= 0 && m < ground.Length && ground[m] && n.Z >= MinFloorNormalZ
                && (input.TriangleWalkable is null || input.TriangleWalkable[t])) s |= L2Surface.Walk;
            if (input.TriangleNoClimb is { } noClimb && noClimb[t]) s |= L2Surface.NoClimb;   // ours
            if (input.TriangleNoStand is { } noStand && noStand[t]) s = (s & ~L2Surface.Walk) | L2Surface.NoStand;   // ours: never Walk
            keep.Add(t); surfaceList.Add(s); materialList.Add((ushort)Math.Max(m, 0));
        }
        var metres = input.Vertices.Select(v => v / scale).ToArray();
        var surfaces = surfaceList.ToArray();
        var materials = materialList.ToArray();
        int kept = keep.Count;
        Vector3 CollCorner(int k, int corner) => metres[input.Indices[3 * keep[k] + corner]];

        // The render triangles, numbered across the meshes.
        var meshFirst = new int[input.Meshes.Count + 1];
        for (int i = 0; i < input.Meshes.Count; i++) meshFirst[i + 1] = meshFirst[i] + input.Meshes[i].Indices.Length / 3;
        int renderTris = meshFirst[^1];
        var meshOfTri = new int[renderTris];
        for (int i = 0; i < input.Meshes.Count; i++) Array.Fill(meshOfTri, i, meshFirst[i], meshFirst[i + 1] - meshFirst[i]);
        int MeshOf(int g) => meshOfTri[g];
        Vector3 RenderCorner(int g, int corner)
        {
            var mesh = input.Meshes[MeshOf(g)];
            int t = g - meshFirst[MeshOf(g)];
            return mesh.Vertices[mesh.Indices[3 * t + corner]].Position;
        }

        // 2. The grid, over the centroids of everything no wider than a cell, repeats and all.
        // A cell is a whole number of navigation voxels across, so its tile and its square are the same.
        float size = gridded ? MathF.Max(1, MathF.Round(input.CellSize / input.Nav.CellSize)) * input.Nav.CellSize : 0f;
        bool Wide(Vector3 a, Vector3 b, Vector3 c) =>
            gridded && (MathF.Max(a.X, MathF.Max(b.X, c.X)) - MathF.Min(a.X, MathF.Min(b.X, c.X)) > size
                        || MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) > size);
        var collWide = new bool[kept];
        var renderWide = new bool[renderTris];
        var cmin = new Vector2(float.MaxValue); var cmax = new Vector2(float.MinValue);
        for (int k = 0; k < kept; k++)
        {
            var (a, b, c) = (CollCorner(k, 0), CollCorner(k, 1), CollCorner(k, 2));
            if (collWide[k] = Wide(a, b, c)) continue;
            var m = (a + b + c) / 3f; cmin = Vector2.Min(cmin, new(m.X, m.Y)); cmax = Vector2.Max(cmax, new(m.X, m.Y));
        }
        for (int g = 0; g < renderTris; g++)
        {
            var (a, b, c) = (RenderCorner(g, 0), RenderCorner(g, 1), RenderCorner(g, 2));
            if (renderWide[g] = Wide(a, b, c)) continue;
            var m = (a + b + c) / 3f; cmin = Vector2.Min(cmin, new(m.X, m.Y)); cmax = Vector2.Max(cmax, new(m.X, m.Y));
        }
        if (cmin.X > cmax.X) cmin = cmax = Vector2.Zero;
        var omax = offsets.Aggregate(Vector3.Zero, Vector3.Max);
        cmax += new Vector2(omax.X, omax.Y);
        if (!gridded) size = MathF.Max(1f, MathF.Ceiling(MathF.Max(cmax.X - cmin.X, cmax.Y - cmin.Y) + 1f));
        var origin = gridded ? new Vector2(MathF.Floor(cmin.X / size) * size, MathF.Floor(cmin.Y / size) * size) : cmin - new Vector2(0.5f);
        int columns = Math.Max(1, (int)MathF.Floor((cmax.X - origin.X) / size) + 1);
        int rows = Math.Max(1, (int)MathF.Floor((cmax.Y - origin.Y) / size) + 1);
        int cellCount = columns * rows;
        int CellOf(Vector3 p) => Math.Clamp((int)MathF.Floor((p.Y - origin.Y) / size), 0, rows - 1) * columns
                                 + Math.Clamp((int)MathF.Floor((p.X - origin.X) / size), 0, columns - 1);

        // 3. Every triangle (and every repeat of it) into the cell its centroid is in: entries are k·reps + r.
        var collBucket = new List<int>?[cellCount];
        var renderBucket = new List<int>?[cellCount];
        var collResident = new List<int>();
        var renderResident = new List<int>();
        for (int k = 0; k < kept; k++)
        {
            if (collWide[k]) { collResident.Add(k * reps); continue; }
            var m = (CollCorner(k, 0) + CollCorner(k, 1) + CollCorner(k, 2)) / 3f;
            for (int r = 0; r < reps; r++) (collBucket[CellOf(m + offsets[r])] ??= []).Add(k * reps + r);
        }
        for (int g = 0; g < renderTris; g++)
        {
            if (renderWide[g]) { renderResident.Add(g * reps); continue; }
            var m = (RenderCorner(g, 0) + RenderCorner(g, 1) + RenderCorner(g, 2)) / 3f;
            for (int r = 0; r < reps; r++) (renderBucket[CellOf(m + offsets[r])] ??= []).Add(g * reps + r);
        }
        log($"cook: grid {columns} × {rows} cells of {size:0.#} m from ({origin.X:0.#}, {origin.Y:0.#}); " +
            $"{collResident.Count:N0} collision and {renderResident.Count:N0} drawn triangles wider than a cell");

        // The walkable ground's height range over the whole level (every repeat is at the same heights).
        var (walkLo, walkHi) = WalkBounds(metres, [.. keep.SelectMany(t => new[] { input.Indices[3 * t], input.Indices[3 * t + 1], input.Indices[3 * t + 2] })], surfaces);
        bool anyWalk = walkLo.X <= walkHi.X;
        if (anyWalk)
        {
            var olo = offsets.Aggregate(Vector3.Zero, Vector3.Min);
            walkLo += new Vector3(olo.X, olo.Y, 0); walkHi += new Vector3(omax.X, omax.Y, 0);
        }
        // The voxels' layers are counted up from a fixed datum, not from the level's lowest ground (NavFloor).
        var gridMinR = new RcVec3f(origin.X, anyWalk ? NavFloor(walkLo.Z, input.Nav.CellHeight) : 0, -(origin.Y + rows * size));
        var gridMaxR = new RcVec3f(origin.X + columns * size, anyWalk ? walkHi.Z : 0, -origin.Y);
        int tileVoxels = (int)MathF.Round(size / input.Nav.CellSize);
        int borderVoxels = (int)MathF.Ceiling(input.Nav.Radius / input.Nav.CellSize) + 3;
        float border = borderVoxels * input.Nav.CellSize;

        // The manifest's pieces.
        Directory.CreateDirectory(Path.Combine(packageFolder, L2Package.CellsFolder));
        var anchors = input.Anchors.ToList();
        var lightsIn = new List<(L2Light Light, int Cell)>();
        for (int r = 0; r < reps; r++)
            foreach (var l in input.Lights)
            {
                var p = l.Position + offsets[r];
                lightsIn.Add((l with { Name = reps > 1 ? $"{l.Name}_{r}" : l.Name, Position = p }, CellOf(p)));
            }
        var anchorCell = anchors.Select(a => CellOf(new Vector3(a.Position[0], a.Position[1], a.Position[2]))).ToArray();

        // 4. The cells, a few at once.
        var entries = new ConcurrentDictionary<int, L2CellEntry>();
        long lod0 = 0, lod1 = 0, cellBytes = 0, leaves0 = 0, leaves1 = 0, leavesFar = 0;
        Func<int, bool>? foliage = input.Foliage is { } fol ? m => m >= 0 && m < fol.Length && fol[m] : null;
        int navPolys = 0, navWalk = 0, collisionTotal = 0, walkTotal = 0;
        long navTicks = 0L, collTicks = 0, meshTicks = 0, lodTicks = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        var work = Enumerable.Range(0, cellCount).Where(i => collBucket[i] is not null || renderBucket[i] is not null
                                                              || lightsIn.Any(l => l.Cell == i) || anchorCell.Contains(i)).ToArray();
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, input.Threads) }, flat =>
        {
            int column = flat % columns, row = flat / columns;
            string id = gridded ? L2Grid.CellId(column, row) : CellId;
            var cell = new L2Cell();
            var box = new Box();

            // COLL: the cell's own triangles.
            long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var coll = Collision(collBucket[flat] ?? [], ref box);
            Interlocked.Add(ref collTicks, System.Diagnostics.Stopwatch.GetTimestamp() - c0);
            int walkHere = coll.Surfaces.Count(s => (s & L2Surface.Walk) != 0);

            // NAVM: the tile over the cell's square and a border of whatever is round it.
            byte[] tile = []; int polys = 0, walkPolys = 0;
            var sq = (X0: origin.X + column * size, Y0: origin.Y + row * size);
            var tlo = new Vector3(sq.X0 - border, sq.Y0 - border, walkLo.Z);
            var thi = new Vector3(sq.X0 + size + border, sq.Y0 + size + border, walkHi.Z);
            if (anyWalk && (gridded ? thi.X >= walkLo.X && tlo.X <= walkHi.X && thi.Y >= walkLo.Y && tlo.Y <= walkHi.Y : true))
            {
                var near = new List<int>();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int c = column + dx, r = row + dy;
                        if (c < 0 || r < 0 || c >= columns || r >= rows) continue;
                        if (collBucket[r * columns + c] is { } list) near.AddRange(list);
                    }
                near.AddRange(collResident);
                var (nv, ni, ns) = Gather(near, gridded ? tlo : null, gridded ? thi : null);
                if (ns.Any(s => (s & L2Surface.Walk) != 0))
                {
                    var solid = new CellCollision(CellCollision.Build(nv, ni, ns, new ushort[ns.Length]), scale);
                    var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    var (tx, tz) = (column, rows - 1 - row);
                    (tile, polys, walkPolys) = BuildNavMesh(nv, ni, ns, solid, input.Nav,
                        gridded ? new NavTileSpec(gridMinR, gridMaxR, tx, tz, tileVoxels, borderVoxels) : null);
                    Interlocked.Add(ref navTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                }
            }

            // LITE and ANCH: those whose place is in the cell.
            var lights = lightsIn.Where(l => l.Cell == flat).Select(l => l.Light).ToList();
            foreach (var l in lights) box.Add(l.Position);
            var cellAnchors = anchors.Where((a, i) => anchorCell[i] == flat).ToList();
            foreach (var a in cellAnchors) a.Cell = id;

            // MESH: the cell's drawn triangles, two levels of detail.
            L2MeshBuffers? mesh = null;
            if (input.Meshes.Count > 0 && renderBucket[flat] is { } drawn)
            {
                long m0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var full = DrawnMesh(drawn, ref box);
                long m1 = System.Diagnostics.Stopwatch.GetTimestamp();
                // A vertex on an open edge near the cell's square may be where a neighbour's triangles meet this cell's: it stays.
                float x0 = sq.X0, y0 = sq.Y0, x1 = sq.X0 + size, y1 = sq.Y0 + size, near = gridded ? CutMargin : float.MaxValue;
                var reduced = Reduce(full, input.Lod1Ratio, input.Lod1MaxError,
                                     p => MathF.Min(MathF.Min(MathF.Abs(p.X - x0), MathF.Abs(p.X - x1)), MathF.Min(MathF.Abs(p.Y - y0), MathF.Abs(p.Y - y1))) < near,
                                     foliage);
                Interlocked.Add(ref leaves0, FoliageTriangles(full, foliage)); Interlocked.Add(ref leaves1, FoliageTriangles(reduced, foliage));
                Interlocked.Add(ref meshTicks, m1 - m0); Interlocked.Add(ref lodTicks, System.Diagnostics.Stopwatch.GetTimestamp() - m1);
                mesh = new L2MeshBuffers { Lods = [full, reduced] };
                Interlocked.Add(ref lod0, full.Triangles); Interlocked.Add(ref lod1, reduced.Triangles);
            }
            if (mesh is not null) cell.Chunks.Add(mesh.Encode());
            if (coll.Triangles > 0) cell.Chunks.Add(coll.Encode());
            if (tile.Length > 0)
                cell.Chunks.Add(new L2NavMesh
                {
                    AgentRadius = input.Nav.Radius, AgentHeight = input.Nav.Height, AgentClimb = input.Nav.Climb,
                    MaxSlope = input.Nav.MaxSlope, CellSize = input.Nav.CellSize, CellHeight = input.Nav.CellHeight, Tile = tile,
                }.Encode());
            cell.Chunks.Add(L2LightChunk.Encode(lights));
            cell.Chunks.Add(L2AnchorChunk.Encode(cellAnchors));
            var file = $"{L2Package.CellsFolder}/{id}.cell";
            var bytes = cell.Write();
            File.WriteAllBytes(Path.Combine(packageFolder, file), bytes);
            if (box.Empty) box.Add(new Vector3(sq.X0 + size / 2, sq.Y0 + size / 2, 0));
            entries[flat] = new L2CellEntry
            {
                Id = id, Bounds = box.ToL2(), File = file, Region = Region, At = gridded ? [column, row] : null,
                NavPolygons = polys, Bytes = bytes.Length,
                FarFile = gridded ? $"far/{L2Grid.FarId(column / input.BlockCells, row / input.BlockCells)}.cell" : null,
            };
            Interlocked.Add(ref cellBytes, bytes.Length);
            Interlocked.Add(ref navPolys, polys); Interlocked.Add(ref navWalk, walkPolys);
            Interlocked.Add(ref collisionTotal, coll.Triangles); Interlocked.Add(ref walkTotal, walkHere);
            int d = Interlocked.Increment(ref done);
            if (d % 16 == 0 || d == work.Length) log($"cook: {d} of {work.Length} cells ({watch.Elapsed.TotalSeconds:0}s)");
            CookMemory.Settle();                                            // the cell's own meshes, tree and tile are garbage now
        });

        double S(long ticks) => ticks / (double)System.Diagnostics.Stopwatch.Frequency;
        log($"cook: thread-seconds: collision {S(collTicks):0.0}, navigation {S(navTicks):0.0}, meshes {S(meshTicks):0.0}, second level of detail {S(lodTicks):0.0}");

        // 5. The resident cell: what is wider than a cell, once, drawn at one level of detail.
        var cellList = entries.OrderBy(e => e.Key).Select(e => e.Value).ToList();
        if (collResident.Count > 0 || renderResident.Count > 0)
        {
            var cell = new L2Cell();
            var box = new Box();
            var coll = Collision(collResident, ref box);
            if (input.Meshes.Count > 0 && renderResident.Count > 0)
            {
                var full = DrawnMesh(renderResident, ref box);
                cell.Chunks.Add(new L2MeshBuffers { Lods = [full] }.Encode());
                lod0 += full.Triangles;
            }
            if (coll.Triangles > 0) cell.Chunks.Add(coll.Encode());
            var file = $"{L2Package.CellsFolder}/{ResidentId}.cell";
            var bytes = cell.Write();
            File.WriteAllBytes(Path.Combine(packageFolder, file), bytes);
            cellList.Add(new L2CellEntry { Id = ResidentId, Bounds = box.ToL2(), File = file, Region = Region, Resident = true, Bytes = bytes.Length });
            cellBytes += bytes.Length; collisionTotal += coll.Triangles;
            walkTotal += coll.Surfaces.Count(s => (s & L2Surface.Walk) != 0);
        }

        // 6. The far versions: each block's drawn triangles merged and clustered (foliage thinned by whole cards).
        CookMemory.Settle();
        var far = new ConcurrentBag<L2FarEntry>();
        long farTris = 0, farBytes = 0;
        if (gridded && input.Meshes.Count > 0)
        {
            int bc = Math.Max(1, input.BlockCells);
            var blocks = cellList.Where(e => e.At is not null).GroupBy(e => (e.At![0] / bc, e.At![1] / bc)).ToList();
            float clusterSize = FarClusterSize(size * bc);
            Parallel.ForEach(blocks, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, input.Threads) }, block =>
            {
                var drawn = new List<int>();
                foreach (var e in block) if (renderBucket[e.At![1] * columns + e.At![0]] is { } list) drawn.AddRange(list);
                if (drawn.Count == 0) return;
                var box = new Box();
                var full = DrawnMesh(drawn, ref box);
                var lod = Clustered(full, clusterSize, foliage);
                Interlocked.Add(ref leavesFar, FoliageTriangles(lod, foliage));
                var cell = new L2Cell { Chunks = [new L2MeshBuffers { Lods = [lod] }.Encode()] };
                var id = L2Grid.FarId(block.Key.Item1, block.Key.Item2);
                var file = $"far/{id}.cell";
                Directory.CreateDirectory(Path.Combine(packageFolder, "far"));
                var bytes = cell.Write();
                File.WriteAllBytes(Path.Combine(packageFolder, file), bytes);
                far.Add(new L2FarEntry { Id = id, At = [block.Key.Item1, block.Key.Item2], Bounds = box.ToL2(), File = file,
                                         Cells = [.. block.Select(e => e.Id)], Bytes = bytes.Length });
                Interlocked.Add(ref farTris, lod.Triangles); Interlocked.Add(ref farBytes, bytes.Length);
                CookMemory.Settle();
            });
        }
        foreach (var e in cellList.Where(e => e.FarFile is not null))
            if (!far.Any(f => f.File == e.FarFile)) e.FarFile = null;
        if (foliage is not null)
            log($"cook: foliage, by whole cards: {leaves0:N0} triangles near, {leaves1:N0} at the second level of detail, {leavesFar:N0} in the far versions");

        // 7. The manifest.
        var all = new Box();
        foreach (var e in cellList) { all.Add(new Vector3(e.Bounds.Min[0], e.Bounds.Min[1], e.Bounds.Min[2])); all.Add(new Vector3(e.Bounds.Max[0], e.Bounds.Max[1], e.Bounds.Max[2])); }
        foreach (var a in anchors.Where(a => a.Cell.Length == 0)) a.Cell = cellList.FirstOrDefault()?.Id ?? CellId;
        var manifest = new L2Manifest
        {
            Version = L2Manifest.Version2,
            Name = input.Name, Bounds = all.Empty ? new L2Box() : all.ToL2(), Cells = cellList, Materials = [.. input.Materials],
            GlobalLight = input.Sun, Anchors = anchors, Extras = input.Extras ?? [], Date = input.Date, North = input.North, Areas = input.Areas, Exits = input.Exits ?? [],
            Scene = input.Scene, Lifts = input.Lifts,
            // How the navigation was baked, where it is not Recast's own (ours): absent for every cook before.
            Navigation = input.Nav.StairLedges ? new L2Navigation { Ledges = L2Navigation.StairLedges } : null,
            // The level's ladders (ours): absent for every cook whose layout has none.
            Traversal = input.Traversal,
            Grid = !gridded ? null : new L2Grid
            {
                Origin = [origin.X, origin.Y], CellSize = size, Columns = columns, Rows = rows, BlockCells = Math.Max(1, input.BlockCells),
                NavOrigin = [gridMinR.X, gridMinR.Y, gridMinR.Z],
            },
            Far = gridded ? [.. far.OrderBy(f => f.At[1]).ThenBy(f => f.At[0])] : null,
        };
        if (input.Asset is { } source)
        {
            // Hashed and copied a buffer at a time (ours): a town's glb is the better part of a gigabyte.
            string hash;
            using (var stream = File.OpenRead(source)) hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            manifest.Source = new L2Source
            {
                Kind = "gltf", File = Path.GetFileName(source), Bytes = new FileInfo(source).Length, Sha256 = hash,
            };
            Directory.CreateDirectory(Path.Combine(packageFolder, "assets"));
            File.Copy(source, Path.Combine(packageFolder, "assets", Path.GetFileName(source)), overwrite: true);
            manifest.Extras["asset"] = $"assets/{Path.GetFileName(source)}";
        }
        File.WriteAllText(Path.Combine(packageFolder, L2Package.ManifestFile), manifest.Write(), new System.Text.UTF8Encoding(false));
        return new CellCookResult(packageFolder, collisionTotal, walkTotal, navPolys, navWalk, lightsIn.Count, anchors.Count,
                                  TimeSpan.FromSeconds(navTicks / (double)System.Diagnostics.Stopwatch.Frequency))
        {
            Cells = cellList.Count(e => e.Resident != true), FarVersions = far.Count, Lod0Triangles = lod0, Lod1Triangles = lod1,
            FarTriangles = farTris, CellBytes = cellBytes, FarBytes = farBytes,
        };

        // The collision chunk of some entries (k·reps + r), vertices shared within it.
        L2Collision Collision(List<int> list, ref Box box)
        {
            var at = new Dictionary<long, int>();
            var verts = new List<Vector3>();
            var idx = new int[list.Count * 3];
            var surf = new L2Surface[list.Count];
            var mats = new ushort[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                int k = list[i] / reps, r = list[i] % reps;
                for (int corner = 0; corner < 3; corner++)
                {
                    int v = input.Indices[3 * keep[k] + corner];
                    long key = (long)v * reps + r;
                    if (!at.TryGetValue(key, out int n)) { n = verts.Count; at[key] = n; verts.Add(metres[v] + offsets[r]); box.Add(verts[n]); }
                    idx[3 * i + corner] = n;
                }
                surf[i] = surfaces[k]; mats[i] = materials[k];
            }
            return CellCollision.Build([.. verts], idx, surf, mats);
        }

        // Triangles of some entries whose boxes meet lo..hi (all, when unbounded), unshared, for a navigation tile.
        (Vector3[] V, int[] I, L2Surface[] S) Gather(List<int> list, Vector3? lo, Vector3? hi)
        {
            var v = new List<Vector3>(); var s = new List<L2Surface>();
            foreach (int e in list)
            {
                int k = e / reps, r = e % reps;
                var (a, b, c) = (CollCorner(k, 0) + offsets[r], CollCorner(k, 1) + offsets[r], CollCorner(k, 2) + offsets[r]);
                if (lo is { } l && hi is { } h)
                {
                    var tl = Vector3.Min(a, Vector3.Min(b, c)); var th = Vector3.Max(a, Vector3.Max(b, c));
                    if (th.X < l.X || tl.X > h.X || th.Y < l.Y || tl.Y > h.Y || th.Z < l.Z - 4f || tl.Z > h.Z + 4f) continue;
                }
                v.Add(a); v.Add(b); v.Add(c); s.Add(surfaces[k]);
            }
            return ([.. v], [.. Enumerable.Range(0, v.Count)], [.. s]);
        }

        // The full level of detail of some drawn entries (g·reps + r): per material, vertices shared within each.
        L2MeshLod DrawnMesh(List<int> list, ref Box box)
        {
            var byMaterial = list.GroupBy(e => input.Meshes[MeshOf(e / reps)].Material).OrderBy(g => g.Key);
            var verts = new List<L2Vertex>();
            var idx = new List<uint>();
            var draws = new List<L2MeshDraw>();
            foreach (var group in byMaterial)
            {
                var at = new Dictionary<(int, uint, int), uint>();
                int first = idx.Count, firstVertex = verts.Count;
                var dmin = new Vector3(float.MaxValue); var dmax = new Vector3(float.MinValue);
                foreach (int e in group)
                {
                    int g = e / reps, r = e % reps, mi = MeshOf(g);
                    var mesh = input.Meshes[mi];
                    int t = g - meshFirst[mi];
                    for (int corner = 0; corner < 3; corner++)
                    {
                        uint v = mesh.Indices[3 * t + corner];
                        if (!at.TryGetValue((mi, v, r), out uint n))
                        {
                            n = (uint)verts.Count; at[(mi, v, r)] = n;
                            var vx = mesh.Vertices[v];
                            vx.Position += offsets[r];
                            verts.Add(vx);
                            dmin = Vector3.Min(dmin, vx.Position); dmax = Vector3.Max(dmax, vx.Position);
                        }
                        idx.Add(n);
                    }
                }
                box.Add(dmin); box.Add(dmax);
                draws.Add(new L2MeshDraw(group.Key, first, idx.Count - first, dmin, dmax, 0f));
            }
            var vArr = verts.ToArray(); var iArr = idx.ToArray();
            for (int d = 0; d < draws.Count; d++)
                draws[d] = draws[d] with { UvDensity = MeshSimplify.UvDensity(vArr, iArr, draws[d].FirstIndex, draws[d].IndexCount) };
            return new L2MeshLod { Vertices = vArr, Indices = iArr, Draws = draws };
        }
    }

    /// <summary>A level of detail's triangles in foliage draws.</summary>
    static long FoliageTriangles(L2MeshLod lod, Func<int, bool>? foliage) =>
        foliage is null ? 0 : lod.Draws.Where(d => foliage(d.Material)).Sum(d => (long)d.IndexCount / 3);

    /// <summary>
    /// How near a cell's square (metres) a vertex on an open edge of its mesh stays put in the second level
    /// of detail: the cut runs between whole triangles, so the edges it leaves lie within a triangle of the
    /// square's side. 3 m covers the generated towns' ground and walls.
    /// </summary>
    public const float CutMargin = 3f;

    /// <summary>The far versions' clustering grid: a block's size over 96 (a 96 m block is clustered at 1 m).</summary>
    public static float FarClusterSize(float blockMetres) => MathF.Max(0.25f, blockMetres / 96f);

    /// <summary>
    /// A level of detail's simplified copy (<see cref="MeshSimplify.Simplify"/>, per draw), its vertices
    /// compacted, its draws' boxes and densities kept. A draw whose material is <paramref name="foliage"/>
    /// is thinned by whole cards instead (<see cref="CellFoliage.Lod1Crown"/>, ours).
    /// </summary>
    public static L2MeshLod Reduce(L2MeshLod full, float ratio, float maxError, Func<Vector3, bool>? pinned = null, Func<int, bool>? foliage = null) =>
        PerDraw(full, (d, v, i) =>
        {
            if (foliage?.Invoke(d.Material) == true)
            {
                var (fv, fi) = CellFoliage.Thin(v, i, CellFoliage.Lod1Crown, CellFoliage.Lod1Rooted);
                return (fv, fi, 0f);
            }
            var reduced = MeshSimplify.Simplify(v, i, ratio, maxError, out float e, pinned);
            return (v, reduced, e);
        });

    /// <summary>
    /// A level of detail clustered at <paramref name="size"/> metres (<see cref="MeshSimplify.Cluster"/>), per draw;
    /// a <paramref name="foliage"/> material's draw thinned by whole cards instead (<see cref="CellFoliage.FarCrown"/>,
    /// its bins in units of <paramref name="size"/>; ours).
    /// </summary>
    public static L2MeshLod Clustered(L2MeshLod full, float size, Func<int, bool>? foliage = null) => PerDraw(full, (d, v, i) =>
    {
        if (foliage?.Invoke(d.Material) == true)
        {
            var far = CellFoliage.FarCrown with { Bin = CellFoliage.FarCrown.Bin * size };
            var (fv, fi) = CellFoliage.Thin(v, i, far, CellFoliage.FarRooted with { Bin = CellFoliage.FarRooted.Bin * size });
            return (fv, fi, size);
        }
        var (cv, ci) = MeshSimplify.Cluster(v, i, size);
        return (cv, ci, size);
    });

    static L2MeshLod PerDraw(L2MeshLod full, Func<L2MeshDraw, L2Vertex[], uint[], (L2Vertex[] V, uint[] I, float Error)> reduce)
    {
        var verts = new List<L2Vertex>();
        var idx = new List<uint>();
        var draws = new List<L2MeshDraw>();
        float error = 0f;
        foreach (var d in full.Draws)
        {
            var sub = full.Indices.AsSpan(d.FirstIndex, d.IndexCount).ToArray();
            var (v, i, e) = reduce(d, full.Vertices, sub);
            var (cv, ci) = MeshSimplify.Compact(v, i);
            if (ci.Length == 0) continue;
            error = MathF.Max(error, e);
            uint baseVertex = (uint)verts.Count;
            int first = idx.Count;
            verts.AddRange(cv);
            foreach (var x in ci) idx.Add(x + baseVertex);
            draws.Add(d with { FirstIndex = first, IndexCount = ci.Length });
        }
        return new L2MeshLod { Error = error, Vertices = [.. verts], Indices = [.. idx], Draws = draws };
    }

    /// <summary>A growing box, in metres.</summary>
    struct Box
    {
        Vector3 min, max;
        bool any;
        public readonly bool Empty => !any;
        public void Add(Vector3 p)
        {
            if (!any) { min = max = p; any = true; return; }
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        public readonly L2Box ToL2() => new() { Min = [min.X, min.Y, min.Z], Max = [max.X, max.Y, max.Z] };
    }
}
