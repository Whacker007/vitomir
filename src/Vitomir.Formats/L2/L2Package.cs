// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;

namespace Vitomir.Formats.L2;

/// <summary>
/// An L2 level package (ours): a folder <c>&lt;Name&gt;.l2/</c> holding
/// <c>level.json</c> (<see cref="L2Manifest"/>) and <c>cells/&lt;id&gt;.cell</c> (<see cref="L2Cell"/>).
/// Cells are read when asked for, not with the manifest.
/// </summary>
public sealed class L2Package
{
    public const string Extension = ".l2", ManifestFile = "level.json", CellsFolder = "cells";

    public string Folder { get; }
    public L2Manifest Manifest { get; }

    L2Package(string folder, L2Manifest manifest) { Folder = folder; Manifest = manifest; }

    /// <summary>The package folder for <paramref name="level"/> under <paramref name="root"/>: <c>root/LEVEL.l2</c>.</summary>
    public static string PathFor(string root, string level) => Path.Combine(root, level + Extension);

    public static L2Package Open(string folder)
    {
        var path = Path.Combine(folder, ManifestFile);
        return new L2Package(folder, L2Manifest.Read(File.ReadAllText(path), path));
    }

    public L2Cell ReadCell(L2CellEntry entry)
    {
        var path = Path.Combine(Folder, entry.File);
        return L2Cell.Read(File.ReadAllBytes(path), path);
    }

    /// <summary>Writes the manifest and the cells (keyed by their manifest entry's id) into <paramref name="folder"/>.</summary>
    public static void Write(string folder, L2Manifest manifest, IReadOnlyDictionary<string, L2Cell> cells)
    {
        Directory.CreateDirectory(Path.Combine(folder, CellsFolder));
        foreach (var entry in manifest.Cells)
            File.WriteAllBytes(Path.Combine(folder, entry.File), cells[entry.Id].Write());
        File.WriteAllText(Path.Combine(folder, ManifestFile), manifest.Write(), new UTF8Encoding(false));
    }
}

/// <summary>
/// The <c>ANCH</c> chunk (ours, version 1): the anchors that live in a cell, the same records the
/// manifest lists. Per anchor: id, kind, scene, name, template, script as u16-length UTF-8 strings;
/// i32 index; i32 GUID (<see cref="int.MinValue"/> for none); i64 sector (−1 for none); position and
/// rotation as 3 f32 each (metres, degrees); u32 group (0xffffffff for none); u32 n and n u32 sectors.
/// </summary>
public static class L2AnchorChunk
{
    public const string Tag = "ANCH";
    public const uint Version1 = 1;

    public static L2Chunk Encode(IReadOnlyList<L2Anchor> anchors)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(anchors.Count);
            foreach (var a in anchors)
            {
                foreach (var s in new[] { a.Id, a.Kind, a.Scene, a.Name, a.Template ?? "", a.Script ?? "" })
                {
                    var b = Encoding.UTF8.GetBytes(s);
                    w.Write(checked((ushort)b.Length)); w.Write(b);
                }
                w.Write(a.Index); w.Write(a.Guid ?? int.MinValue); w.Write(a.Sector ?? -1L);
                foreach (var f in a.Position.Concat(a.Rotation)) w.Write(f);
                w.Write(a.Group ?? uint.MaxValue);
                w.Write(a.Sectors?.Length ?? 0);
                foreach (var s in a.Sectors ?? []) w.Write(s);
            }
        }
        return new L2Chunk(Tag, Version1, ms.ToArray());
    }

    public static List<L2Anchor> Decode(L2Chunk chunk, string cell)
    {
        var r = new BinReader(chunk.Payload, Tag);
        var list = new List<L2Anchor>();
        int n = r.Count32(10_000_000);
        for (int i = 0; i < n; i++)
        {
            string S() => Encoding.UTF8.GetString(r.Bytes(r.U16()));
            string id = S(), kind = S(), scene = S(), name = S(), template = S(), script = S();
            int index = r.I32(), guid = r.I32(); long sector = BitConverter.ToInt64(r.Bytes(8));
            var pos = r.Floats(3); var rot = r.Floats(3); uint group = r.U32();
            var sectors = r.U32s(r.Count32(1_000_000));
            list.Add(new L2Anchor
            {
                Id = id, Kind = kind, Scene = scene, Name = name, Template = template.Length > 0 ? template : null,
                Script = script.Length > 0 ? script : null, Index = index, Guid = guid == int.MinValue ? null : guid,
                Sector = sector < 0 ? null : sector, Position = pos, Rotation = rot,
                Group = group == uint.MaxValue ? null : group, Sectors = sectors.Length > 0 ? sectors : null, Cell = cell,
            });
        }
        r.ExpectEof();
        return list;
    }
}
