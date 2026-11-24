// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Levels.Export;

/// <summary>What the exporter is asked to do.</summary>
/// <param name="Scale">
/// Metres per NOD unit. Ours: 1/72, chosen so that Christof, 128 units tall, stands 1.78 m. The
/// engine itself has no metre; nothing in 1.1 says what a unit is.
/// </param>
/// <param name="MaterialPerLightmapPage">
/// Write one glTF material per NIL material and lightmap page, each naming its page in
/// <c>extras.lightmap</c>, so that a tool that only sees materials (Blender's importer, say) can
/// find the lightmap without reading the primitives' extras.
/// </param>
public sealed record GltfExportOptions(float Scale = 1f / 72f, bool MaterialPerLightmapPage = false);

/// <summary>What an export wrote, and the counts the tests check it by.</summary>
public sealed record GltfExportResult(string GltfPath, string BinPath, string WalkPath, int Triangles, int Materials,
                                      int Images, int LightmapPages, int SkippedGroups);

/// <summary>
/// **A 1.1 level as a glTF 2.0 scene** (ours). Reads the level's
/// <c>.nil</c> and its materials the way the engine does (<see cref="MaterialCache"/>, the same
/// <c>materials/</c> + name path <see cref="SceneMesh.AddLevel"/> uses, so a missing material is
/// the default one as in 1.1) and writes a <c>.gltf</c>, one <c>.bin</c> buffer, a PNG per texture
/// and per lightmap page, and the walk data as a JSON file beside them.
///
/// Every sector is a node, its drawn groups primitives of one mesh; its convex surface polygons
/// become triangle fans exactly as <see cref="SceneMesh.AddLevel"/> fans them. Sky and nodraw
/// groups (and groups with no material) are left out, as the game never draws them; the sector's
/// extras count them. Nothing the game does is changed: this only reads.
/// </summary>
public static class GltfLevelExporter
{
    /// <summary>A NIL (Z-up, right-handed, NOD units) point as glTF's (Y-up, right-handed, metres).</summary>
    public static Vector3 ToGltf(Vector3 p, float scale) => new(p.X * scale, p.Z * scale, -p.Y * scale);

    /// <summary>A direction, converted without scaling.</summary>
    public static Vector3 DirToGltf(Vector3 d) => new(d.X, d.Z, -d.Y);

    /// <summary>The triangles a level's drawn groups make: a fan per surface, as the engine draws them.</summary>
    public static int DrawnTriangles(NilLevel level)
    {
        int n = 0;
        foreach (var s in level.Sectors)
            foreach (var g in s.Groups)
            {
                if (!Drawn(g)) continue;
                for (int k = 0; k < g.NumSurfaces; k++) n += Math.Max(0, s.Surfaces[g.FirstSurface + k].NumVerts - 2);
            }
        return n;
    }

    /// <summary>A group the game draws: not sky, not nodraw, and with a material (<see cref="SceneMesh.AddLevel"/>).</summary>
    public static bool Drawn(NilGroup g) => (g.SurfaceFlags & (NilGroup.FlagSky | NilGroup.FlagNodraw)) == 0 && g.Material >= 0;

    /// <summary>
    /// The lightmap page a group draws with: its steady layer (style 0), else its first — the same
    /// choice as <see cref="SceneMesh.AddLevel"/>; −1 for a vertex-lit group.
    /// </summary>
    public static int StaticPage(NilGroup g)
    {
        foreach (var ly in g.Layers) if (ly.Style == 0) return (int)ly.Page;
        return g.Layers.Length > 0 ? (int)g.Layers[0].Page : -1;
    }

    /// <summary>Reads <c>levels/&lt;name&gt;.nil</c> from the game files and exports it into <paramref name="outDir"/>.</summary>
    public static GltfExportResult Export(GameFiles files, string levelName, string outDir, GltfExportOptions? options = null)
    {
        var level = NilLevel.Read(files.Read($"levels/{levelName}.nil"), levelName + ".nil");
        return Export(level, new MaterialCache(files), levelName, outDir, options);
    }

    public static GltfExportResult Export(NilLevel level, MaterialCache materials, string levelName, string outDir,
                                          GltfExportOptions? options = null)
    {
        options ??= new GltfExportOptions();
        float scale = options.Scale;
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "textures"));
        Directory.CreateDirectory(Path.Combine(outDir, "lightmaps"));

        var buffer = new GltfBuffer();
        var images = new JsonArray();
        var textures = new JsonArray();
        var gltfMaterials = new JsonArray();
        var meshes = new JsonArray();
        var nodes = new JsonArray();
        var lights = new JsonArray();

        // Samplers: 0 repeats and mipmaps (the materials), 1 clamps (the lightmaps, as the renderer's clamp sampler).
        var samplers = new JsonArray
        {
            new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 },
            new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9729, ["wrapS"] = 33071, ["wrapT"] = 33071 },
        };

        // The lightmap pages: RGB565 expanded as the renderer expands them (NilLightmap.Rgb), row 0 at the top of the PNG.
        var lightmapTexture = new int[level.Lightmaps.Length];
        var lightmapUri = new string[level.Lightmaps.Length];
        for (int p = 0; p < level.Lightmaps.Length; p++)
        {
            var lm = level.Lightmaps[p];
            var rgba = new byte[lm.Width * lm.Height * 4];
            for (int i = 0; i < lm.Pixels.Length; i++)
            {
                var (r, g, b) = lm.Rgb(i);
                rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 255;
            }
            string rel = $"lightmaps/{levelName}_lm{p:D3}.png";
            PngEncoder.Write(Path.Combine(outDir, rel), lm.Width, lm.Height, rgba);
            images.Add(new JsonObject { ["uri"] = rel, ["name"] = $"lightmap {p}" });
            textures.Add(new JsonObject { ["source"] = images.Count - 1, ["sampler"] = 1, ["name"] = $"lightmap {p}" });
            lightmapTexture[p] = textures.Count - 1;
            lightmapUri[p] = rel;
        }

        // The level's materials, loaded as the engine loads them; each loaded material's first frame as a PNG.
        var loaded = level.Materials.Select(m => materials.Get("materials/" + m)).ToArray();
        var textureOf = new Dictionary<int, int>();   // loaded material index -> glTF texture, or -1
        int BaseTexture(int item)
        {
            if (textureOf.TryGetValue(item, out int have)) return have;
            var m = materials.Items[item];
            var image = m.Frames.Length > 0 && m.Images.Length > m.Frames[0] ? m.Images[m.Frames[0]] : m.Images.FirstOrDefault();
            if (image is null) return textureOf[item] = -1;
            string stem = SafeName(Path.GetFileName(m.Name.Replace('\\', '/')));
            string rel = $"textures/{stem}.png";
            for (int k = 2; File.Exists(Path.Combine(outDir, rel)) && images.Any(i => (string?)i!["uri"] == rel); k++) rel = $"textures/{stem}_{k}.png";
            PngEncoder.Write(Path.Combine(outDir, rel), image.Width, image.Height, image.Rgba);
            images.Add(new JsonObject { ["uri"] = rel, ["name"] = stem });
            textures.Add(new JsonObject { ["source"] = images.Count - 1, ["sampler"] = 0, ["name"] = stem });
            return textureOf[item] = textures.Count - 1;
        }

        JsonObject LightmapRef(int page) => new()
        {
            ["page"] = page, ["texture"] = lightmapTexture[page], ["texCoord"] = 1, ["uri"] = lightmapUri[page],
        };

        const uint VariantFlags = NilGroup.FlagTwoSided | NilGroup.FlagTrans33 | NilGroup.FlagTrans66 | NilGroup.FlagDecal;
        var materialOf = new Dictionary<(int Nil, uint Flags, int Page, bool Lightmapped), int>();
        bool usesUnlit = false;
        int Material(int nil, uint groupFlags, int page)
        {
            var key = (nil, groupFlags & VariantFlags, options.MaterialPerLightmapPage ? page : -2, page >= 0);
            if (materialOf.TryGetValue(key, out int have)) return have;
            var m = materials.Items[loaded[nil]];
            int tex = BaseTexture(loaded[nil]);
            float alpha = (key.Item2 & NilGroup.FlagTrans33) != 0 ? 0.33f : (key.Item2 & NilGroup.FlagTrans66) != 0 ? 0.66f : 1f;
            var t = m.Traits;
            string blend = t.AddBlend ? "additive" : t.SubBlend ? "subtractive" : t.Masked ? "masked" : t.AlphaTexture ? "alpha" : "opaque";
            var pbr = new JsonObject
            {
                ["baseColorFactor"] = Json.Array(1, 1, 1, alpha),
                ["metallicFactor"] = 0, ["roughnessFactor"] = 1,
            };
            if (tex >= 0) pbr["baseColorTexture"] = new JsonObject { ["index"] = tex, ["texCoord"] = 0 };
            else pbr["baseColorFactor"] = Json.Array(0.5f, 0.5f, 0.5f, alpha);
            string name = level.Materials[nil];
            if (key.Item2 != 0) name += $"#{key.Item2:x}";
            if (key.Item3 >= 0) name += $"@lm{key.Item3}";
            else if (!key.Item4) name += "@vertex";
            var mat = new JsonObject
            {
                ["name"] = name,
                ["pbrMetallicRoughness"] = pbr,
                // 1.1 never culls a lightmapped surface's back faces, only an unlit one's.
                ["doubleSided"] = (groupFlags & NilGroup.FlagTwoSided) != 0 || t.TwoSided || page >= 0,
            };
            if (blend == "masked" && alpha >= 1f) { mat["alphaMode"] = "MASK"; mat["alphaCutoff"] = 0.5; }
            else if (blend != "opaque" || alpha < 1f) mat["alphaMode"] = "BLEND";
            if (t.FullyLit) { mat["extensions"] = new JsonObject { ["KHR_materials_unlit"] = new JsonObject() }; usesUnlit = true; }
            var extras = new JsonObject
            {
                ["nilMaterial"] = nil, ["nilName"] = level.Materials[nil], ["loadedAs"] = m.Name.Replace('\\', '/'),
                ["blend"] = blend, ["fullyLit"] = t.FullyLit, ["variantFlags"] = key.Item2,
                ["passes"] = m.Passes.Length, ["frames"] = m.Frames.Length,
            };
            if (key.Item3 >= 0) extras["lightmap"] = LightmapRef(key.Item3);
            else if (options.MaterialPerLightmapPage) extras["lightmap"] = null;   // vertex-lit: COLOR_0 carries the light
            mat["extras"] = extras;
            gltfMaterials.Add(mat);
            return materialOf[key] = gltfMaterials.Count - 1;
        }

        var sectorNodes = new JsonArray();
        int triangles = 0, skipped = 0;
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var s = level.Sectors[si];
            var node = new JsonObject { ["name"] = $"sector_{si}" };
            var children = new JsonArray();
            var primitives = new JsonArray();
            int attrs = -1, normals = -1, uv0 = -1, uv1 = -1, colors = -1;
            int sectorSkipped = 0;
            for (int gi = 0; gi < s.Groups.Length; gi++)
            {
                var g = s.Groups[gi];
                if (!Drawn(g)) { sectorSkipped++; continue; }
                if (attrs < 0) (attrs, normals, uv0, uv1, colors) = SectorAttributes(s, buffer, scale);
                var idx = new List<ushort>();
                for (int k = 0; k < g.NumSurfaces; k++)
                {
                    var sf = s.Surfaces[g.FirstSurface + k];
                    for (int i = 1; i + 1 < sf.NumVerts; i++)
                    {
                        idx.Add((ushort)sf.FirstVertex); idx.Add((ushort)(sf.FirstVertex + i)); idx.Add((ushort)(sf.FirstVertex + i + 1));
                    }
                }
                if (idx.Count == 0) continue;
                triangles += idx.Count / 3;
                int page = StaticPage(g);
                var extras = new JsonObject
                {
                    ["sector"] = si, ["group"] = gi, ["surfaceFlags"] = g.SurfaceFlags,
                    ["lighting"] = page >= 0 ? "lightmap" : "vertex",
                    ["layers"] = Json.Of(g.Layers.Select(l => (JsonNode)new JsonObject { ["page"] = l.Page, ["style"] = l.Style })),
                };
                if (page >= 0) extras["lightmap"] = LightmapRef(page);
                primitives.Add(new JsonObject
                {
                    ["attributes"] = new JsonObject
                    {
                        ["POSITION"] = attrs, ["NORMAL"] = normals, ["TEXCOORD_0"] = uv0, ["TEXCOORD_1"] = uv1, ["COLOR_0"] = colors,
                    },
                    ["indices"] = buffer.Indices(idx),
                    ["material"] = Material(g.Material, g.SurfaceFlags, page),
                    ["mode"] = 4,
                    ["extras"] = extras,
                });
            }
            skipped += sectorSkipped;
            if (primitives.Count > 0)
            {
                meshes.Add(new JsonObject { ["name"] = $"sector_{si}", ["primitives"] = primitives });
                node["mesh"] = meshes.Count - 1;
            }

            var lightNodes = new JsonArray();
            for (int li = 0; li < s.Lights.Length; li++)
            {
                int ln = LightNode(s.Lights[li], $"light_{si}_{li}", nodes, lights, scale);
                children.Add(ln); lightNodes.Add(ln);
            }
            node["extras"] = new JsonObject
            {
                ["sector"] = si, ["flags"] = s.Flags, ["ambient"] = Json.Array(s.Ambient.X, s.Ambient.Y, s.Ambient.Z),
                ["skippedGroups"] = sectorSkipped,
                ["lightNodes"] = lightNodes,
                ["portals"] = Json.Of(s.Portals.Select(p => (JsonNode)new JsonObject
                {
                    ["id"] = p.PortalId, ["neighbourSector"] = p.NeighbourSector, ["neighbourPortal"] = p.NeighbourPortal,
                    ["quad"] = Json.Of(p.Quad.Select(q => { var v = ToGltf(q, scale); return (JsonNode)Json.Array(v.X, v.Y, v.Z); })),
                    ["normal"] = Vec(DirToGltf(p.Normal)),
                })),
            };
            if (children.Count > 0) node["children"] = children;
            nodes.Add(node);
            sectorNodes.Add(nodes.Count - 1);
        }

        int global = LightNode(level.GlobalLight, "global_light", nodes, lights, scale, directional: true);
        sectorNodes.Add(global);

        string walkName = $"{levelName}.walk.json";
        var root = new JsonObject
        {
            ["name"] = levelName,
            ["children"] = sectorNodes,
            ["extras"] = new JsonObject
            {
                ["level"] = levelName, ["nilVersion"] = level.Version,
                ["ambient"] = Json.Array(level.Ambient.X, level.Ambient.Y, level.Ambient.Z),
                ["metresPerUnit"] = Json.Round(scale),
                ["axes"] = "glTF (x, y, z) = metresPerUnit * NIL (x, z, -y)",
                ["texcoord0"] = "u, 1 - v of the NIL texture coordinates; textures written top row first",
                ["texcoord1"] = "the NIL lightmap coordinates as stored; pages written row 0 at the top",
                ["lightmapPages"] = level.Lightmaps.Length,
                ["materialsInNil"] = level.Materials.Length,
                ["sectors"] = level.Sectors.Length,
                ["triangles"] = triangles,
                ["skippedGroups"] = skipped,
                ["walk"] = walkName,
            },
        };
        nodes.Add(root);
        int rootIndex = nodes.Count - 1;

        var bin = buffer.ToArray();
        string binName = $"{levelName}.bin";
        var extensionsUsed = new JsonArray("KHR_lights_punctual");
        if (usesUnlit) extensionsUsed.Add("KHR_materials_unlit");
        var doc = new JsonObject
        {
            ["asset"] = new JsonObject
            {
                ["version"] = "2.0", ["generator"] = "Vitomir level exporter",
                ["extras"] = new JsonObject { ["source"] = $"levels/{levelName}.nil" },
            },
            ["extensionsUsed"] = extensionsUsed,
            ["extensions"] = new JsonObject { ["KHR_lights_punctual"] = new JsonObject { ["lights"] = lights } },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["name"] = levelName, ["nodes"] = new JsonArray(rootIndex) }),
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = gltfMaterials,
            ["textures"] = textures,
            ["images"] = images,
            ["samplers"] = samplers,
            ["buffers"] = new JsonArray(new JsonObject { ["uri"] = binName, ["byteLength"] = bin.Length }),
            ["bufferViews"] = buffer.BufferViews,
            ["accessors"] = buffer.Accessors,
        };
        string gltfPath = Path.Combine(outDir, $"{levelName}.gltf");
        string binPath = Path.Combine(outDir, binName);
        File.WriteAllBytes(binPath, bin);
        File.WriteAllText(gltfPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        string walkPath = Path.Combine(outDir, walkName);
        File.WriteAllText(walkPath, WalkJson(level, levelName, scale).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return new GltfExportResult(gltfPath, binPath, walkPath, triangles, gltfMaterials.Count, images.Count,
                                    level.Lightmaps.Length, skipped);
    }

    /// <summary>
    /// One sector's vertex attributes, shared by all its primitives: position, the surface plane's
    /// normal (every sector vertex belongs to exactly one surface), both coordinate sets and the
    /// baked colour, which the file stores B, G, R, A (<see cref="SceneVertex.FromBgra"/>).
    /// </summary>
    static (int Pos, int Nrm, int Uv0, int Uv1, int Col) SectorAttributes(NilSector s, GltfBuffer buffer, float scale)
    {
        int n = s.SectorVertices.Length;
        var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv0 = new Vector2[n]; var uv1 = new Vector2[n]; var col = new uint[n];
        for (int i = 0; i < n; i++)
        {
            var sv = s.SectorVertices[i];
            pos[i] = ToGltf(s.Vertices[sv.Vertex], scale);
            uv0[i] = new Vector2(sv.Uv.X, 1f - sv.Uv.Y);
            uv1[i] = sv.LightmapUv;
            col[i] = SceneVertex.FromBgra(sv.Rgba);
            nrm[i] = Vector3.UnitY;
        }
        foreach (var sf in s.Surfaces)
        {
            var nn = DirToGltf(s.SurfacePlanes[sf.Plane].Normal);
            nn = nn.LengthSquared() > 1e-12f ? Vector3.Normalize(nn) : Vector3.UnitY;
            for (int i = 0; i < sf.NumVerts; i++) nrm[sf.FirstVertex + i] = nn;
        }
        return (buffer.Positions(pos), buffer.Vec3s(nrm), buffer.Vec2s(uv0), buffer.Vec2s(uv1), buffer.Colors(col));
    }

    /// <summary>
    /// A light as a node: a KHR_lights_punctual point or spot light where the type has one (box
    /// lights have none, and keep only their extras), with the record's raw fields in extras.
    /// The punctual light's intensity is left at 1: 1.1's light is baked into the lightmaps and
    /// vertex colours, and its LIGHT value has no physical unit to convert.
    /// </summary>
    static int LightNode(NilLight l, string name, JsonArray nodes, JsonArray lights, float scale, bool directional = false)
    {
        var color = Json.Array(l.Color.X / 255f, l.Color.Y / 255f, l.Color.Z / 255f);
        var node = new JsonObject { ["name"] = name };
        JsonObject? light = null;
        if (directional)
            light = new JsonObject { ["type"] = "directional", ["color"] = color, ["intensity"] = 1 };
        else if (l.Type == NilLight.Point)
            light = new JsonObject { ["type"] = "point", ["color"] = color, ["intensity"] = 1, ["range"] = Json.Round(Math.Max(l.Range, 1f) * scale) };
        else if (l.Type == NilLight.Spot)
        {
            float outer = MathF.Acos(Math.Clamp(l.CosPenumbra, -1f, 1f)), inner = MathF.Acos(Math.Clamp(l.CosUmbra, -1f, 1f));
            outer = Math.Clamp(outer, 0.001f, MathF.PI / 2); inner = Math.Clamp(Math.Min(inner, outer - 0.0005f), 0f, outer);
            light = new JsonObject
            {
                ["type"] = "spot", ["color"] = color, ["intensity"] = 1, ["range"] = Json.Round(Math.Max(l.Range, 1f) * scale),
                ["spot"] = new JsonObject { ["innerConeAngle"] = Json.Round(inner), ["outerConeAngle"] = Json.Round(outer) },
            };
        }
        if (!directional) node["translation"] = Vec(ToGltf(l.Position, scale));
        if (directional || l.Type == NilLight.Spot)
        {
            var d = DirToGltf(l.Direction);
            if (d.LengthSquared() > 1e-12f)
            {
                var q = RotateMinusZTo(Vector3.Normalize(d));
                node["rotation"] = Json.Array(q.X, q.Y, q.Z, q.W);
            }
        }
        if (light is not null)
        {
            light["name"] = name;
            lights.Add(light);
            node["extensions"] = new JsonObject { ["KHR_lights_punctual"] = new JsonObject { ["light"] = lights.Count - 1 } };
        }
        node["extras"] = new JsonObject
        {
            ["type"] = l.Type, ["style"] = l.Style, ["flags"] = l.Flags, ["inSector"] = l.InSector,
            ["color"] = Json.Array(l.Color.X, l.Color.Y, l.Color.Z), ["intensity"] = Json.Round(l.Intensity),
            ["intensity2"] = Json.Round(l.Intensity2), ["range"] = Json.Round(l.Range), ["falloff"] = Json.Round(l.Falloff),
            ["cosUmbra"] = Json.Round(l.CosUmbra), ["cosPenumbra"] = Json.Round(l.CosPenumbra),
            ["positionNil"] = Json.Array(l.Position.X, l.Position.Y, l.Position.Z),
            ["directionNil"] = Json.Array(l.Direction.X, l.Direction.Y, l.Direction.Z),
        };
        nodes.Add(node);
        return nodes.Count - 1;
    }

    /// <summary>The rotation taking a glTF light's own direction, −Z, onto <paramref name="d"/>.</summary>
    static Quaternion RotateMinusZTo(Vector3 d)
    {
        var from = -Vector3.UnitZ;
        float dot = Vector3.Dot(from, d);
        if (dot > 0.99999f) return Quaternion.Identity;
        if (dot < -0.99999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(from, d));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }

    /// <summary>
    /// The walk data, beside the glTF: each path node's walkable surface (sector, surface, group
    /// and its polygon in glTF metres) and its links, with the half-planes kept raw in NIL units
    /// (they are 2-D, over the NIL's x and y).
    /// </summary>
    static JsonObject WalkJson(NilLevel level, string levelName, float scale)
    {
        var surfaceOf = new (int Sector, int Surface)[level.PathNodes.Length];
        Array.Fill(surfaceOf, (-1, -1));
        for (int si = 0; si < level.Sectors.Length; si++)
            for (int k = 0; k < level.Sectors[si].Surfaces.Length; k++)
                if (level.Sectors[si].Surfaces[k].PathNode is int pn and >= 0 && surfaceOf[pn].Sector < 0) surfaceOf[pn] = (si, k);

        var nodes = new JsonArray();
        for (int i = 0; i < level.PathNodes.Length; i++)
        {
            var pn = level.PathNodes[i];
            var (si, k) = surfaceOf[i];
            var n = new JsonObject { ["node"] = i, ["sector"] = si, ["surface"] = k };
            if (si >= 0)
            {
                var s = level.Sectors[si]; var sf = s.Surfaces[k];
                n["group"] = sf.Group;
                n["polygon"] = Json.Of(Enumerable.Range(0, sf.NumVerts).Select(v =>
                {
                    var p = ToGltf(s.Vertices[s.SectorVertices[sf.FirstVertex + v].Vertex], scale);
                    return (JsonNode)Json.Array(p.X, p.Y, p.Z);
                }));
            }
            n["links"] = Json.Of(pn.Links.Select(l => (JsonNode)new JsonObject
            {
                ["target"] = l.Target, ["flags"] = l.Flags, ["length"] = Json.Round(l.Length * scale),
                ["origin"] = Vec(ToGltf(l.Origin, scale)), ["direction"] = Vec(DirToGltf(l.Direction)),
            }));
            n["halfPlanesNil"] = Json.Of(pn.Planes.Select(p => (JsonNode)Json.Array(p.X, p.Y, p.Z)));
            nodes.Add(n);
        }
        return new JsonObject
        {
            ["level"] = levelName,
            ["metresPerUnit"] = Json.Round(scale),
            ["axes"] = "positions and directions: glTF (x, y, z) = metresPerUnit * NIL (x, z, -y); halfPlanesNil: raw (a, b, d) over NIL x, y",
            ["linkFlags"] = "1/2: clamp the edge's start/end by the walker's radius; 4: impassable for some walkers; 8: pass-through node",
            ["nodes"] = nodes,
        };
    }

    static JsonArray Vec(Vector3 v) => Json.Array(v.X, v.Y, v.Z);

    static string SafeName(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
}
