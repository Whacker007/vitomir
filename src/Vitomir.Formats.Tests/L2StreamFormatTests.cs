// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The L2 format's streaming parts (ours, Phase 3): <c>MESH</c> version 2's GPU-ready
/// buffers written and read back and refused when wrong, the version 2 manifest's grid and far versions,
/// material definitions, and a cell file read for some of its chunks only.
/// </summary>
public class L2StreamFormatTests
{
    static L2MeshLod Lod(int quads, float error)
    {
        var v = new List<L2Vertex>(); var i = new List<uint>();
        for (int q = 0; q < quads; q++)
        {
            uint at = (uint)v.Count;
            for (int k = 0; k < 4; k++)
                v.Add(new L2Vertex { Position = new(q + (k & 1), k >> 1, 0.5f), Normal = Vector3.UnitZ, Tangent = new(1, 0, 0, -1), Uv0 = new(k, q), Uv1 = new(0.5f, 0.25f), Color = 0xff8040c0 });
            i.AddRange([at, at + 1, at + 3, at, at + 3, at + 2]);
        }
        return new L2MeshLod { Error = error, Vertices = [.. v], Indices = [.. i],
                               Draws = [new(3, 0, 6, new(0, 0, 0.5f), new(1, 1, 0.5f), 2f), new(7, 6, i.Count - 6, new(1, 0, 0.5f), new(quads, 1, 0.5f), 0.5f)] };
    }

    [Fact]
    public void GPU_ready_mesh_buffers_come_back_as_written()
    {
        var mesh = new L2MeshBuffers { Lods = [Lod(5, 0), Lod(2, 0.2f)] };
        var chunk = mesh.Encode();
        Assert.Equal(("MESH", 2u), (chunk.Tag, chunk.Version));
        var back = L2MeshBuffers.Decode(chunk);
        Assert.Equal(2, back.Lods.Count);
        for (int l = 0; l < 2; l++)
        {
            Assert.Equal(mesh.Lods[l].Error, back.Lods[l].Error);
            Assert.Equal(mesh.Lods[l].Indices, back.Lods[l].Indices);
            Assert.Equal(mesh.Lods[l].Draws, back.Lods[l].Draws);
            Assert.Equal(mesh.Lods[l].Vertices, back.Lods[l].Vertices);
        }
        Assert.Equal(60, System.Runtime.InteropServices.Marshal.SizeOf<L2Vertex>());
        Assert.Equal(20 * 60 + 30 * 4, mesh.Lods[0].Bytes);
    }

    [Fact]
    public void Mesh_buffers_that_point_outside_themselves_or_are_another_version_are_refused()
    {
        var bad = Lod(1, 0);
        bad.Indices[2] = 99;
        Assert.Throws<NodFormatException>(() => L2MeshBuffers.Decode(new L2MeshBuffers { Lods = [bad] }.Encode()));
        var overrun = new L2MeshLod { Vertices = Lod(1, 0).Vertices, Indices = Lod(1, 0).Indices, Draws = [new(0, 3, 6, default, default, 0)] };
        Assert.Throws<NodFormatException>(() => L2MeshBuffers.Decode(new L2MeshBuffers { Lods = [overrun] }.Encode()));
        var good = new L2MeshBuffers { Lods = [Lod(1, 0)] }.Encode();
        Assert.Throws<NodFormatException>(() => L2MeshBuffers.Decode(good with { Version = 1 }));
        Assert.Throws<NodFormatException>(() => L2MeshBuffers.Decode(good with { Payload = good.Payload[..^4] }));
    }

    [Fact]
    public void A_version_2_manifest_keeps_its_grid_far_versions_and_material_files()
    {
        var m = new L2Manifest
        {
            Version = L2Manifest.Version2, Name = "grid",
            Grid = new L2Grid { Origin = [-96, -48], CellSize = 48, Columns = 5, Rows = 4, BlockCells = 2, NavOrigin = [-96, -3, -144] },
            Cells = [new L2CellEntry { Id = "c1_2", File = "cells/c1_2.cell", Region = "cell", At = [1, 2], NavPolygons = 12, Bytes = 345, FarFile = "far/f0_1.cell" },
                     new L2CellEntry { Id = "all", File = "cells/all.cell", Region = "cell", Resident = true }],
            Far = [new L2FarEntry { Id = "f0_1", At = [0, 1], File = "far/f0_1.cell", Cells = ["c1_2"], Bytes = 99 }],
            MaterialFiles = ["materials/stone.json"],
        };
        var back = L2Manifest.Read(m.Write());
        Assert.Equal(2, back.Version);
        Assert.Equal((5, 4, 48f, 2), (back.Grid!.Columns, back.Grid.Rows, back.Grid.CellSize, back.Grid.BlockCells));
        Assert.Equal([1, 2], back.Cells[0].At!);
        Assert.Equal((12, 345L, "far/f0_1.cell"), (back.Cells[0].NavPolygons!.Value, back.Cells[0].Bytes!.Value, back.Cells[0].FarFile));
        Assert.True(back.Cells[1].Resident);
        Assert.Equal(["c1_2"], back.Far![0].Cells);
        Assert.Equal(["materials/stone.json"], back.MaterialFiles);
        Assert.Equal((1, 2), back.Grid.CellAt(-30, 60));
        Assert.Equal((1, 1), back.Grid.Tile(1, 2));                               // Recast's z runs against y
        Assert.Equal("c1_2", L2Grid.CellId(1, 2));
        Assert.Throws<NodFormatException>(() => L2Manifest.Read(m.Write().Replace("\"version\": 2", "\"version\": 3")));
    }

    [Fact]
    public void A_material_definition_comes_back_as_written()
    {
        var m = new L2Material
        {
            Name = "glb_opuka", BaseColorFactor = [0.9f, 0.8f, 0.7f, 1], RoughnessFactor = 0.8f, MetallicFactor = 0,
            BaseColorTexture = new L2MaterialMap { File = "textures/opuka_color.ktx2" },
            NormalTexture = new L2MaterialMap { File = "textures/opuka_normal.ktx2", RepeatV = false }, AlphaMode = "MASK", Ground = true,
        };
        var back = L2Material.Read(m.Write());
        Assert.Equal(m.BaseColorFactor, back.BaseColorFactor);
        Assert.Equal(("textures/opuka_normal.ktx2", false), (back.NormalTexture!.File, back.NormalTexture.RepeatV));
        Assert.Equal(("MASK", true, false), (back.AlphaMode, back.Ground, back.Passable));
        Assert.Equal([("textures/opuka_color.ktx2", true), ("textures/opuka_normal.ktx2", false)], back.Maps().Select(x => (x.Map.File, x.Srgb)));
    }

    [Fact]
    public void A_cell_file_can_be_read_for_some_of_its_chunks()
    {
        var cell = new L2Cell { Chunks = [new("MESH", 2, new byte[1000]), new("COLL", 1, [1, 2, 3]), new("ZZZZ", 9, [4])] };
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-cell-{Guid.NewGuid():N}.cell");
        try
        {
            File.WriteAllBytes(path, cell.Write());
            var some = L2Cell.ReadFile(path, t => t != "MESH");
            Assert.Equal(["COLL", "ZZZZ"], some.Chunks.Select(c => c.Tag));
            Assert.Equal([1, 2, 3], some.Find("COLL")!.Payload);
            File.WriteAllBytes(path, cell.Write()[..^1]);
            Assert.Throws<NodFormatException>(() => L2Cell.ReadFile(path, _ => true));
        }
        finally { File.Delete(path); }
    }
}
