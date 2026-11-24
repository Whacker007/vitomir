// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Text;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>File lookup order: data folders (last given first), loose files in the game folder, archives.</summary>
public sealed class GameFilesTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-vfs").FullName;

    public GameFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        using (var zip = ZipFile.Open(Path.Combine(root, "game", "Resource.nob"), ZipArchiveMode.Create))
        {
            foreach (var (name, text) in new[] { ("3d/Materials/moon.tga", "archive"), ("scenes/a.nsd", "archive") })
                using (var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.NoCompression).Open())) w.Write(text);
        }
        Write("game/scenes/A.NSD", "game folder");
        Write("pack1/3D/materials/MOON.tga", "pack 1");
        Write("pack2/3d/Materials/moon.TGA", "pack 2");
    }

    void Write(string rel, string text)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    string Read(GameFiles files, string path) => Encoding.UTF8.GetString(files.Read(path));

    [Fact]
    public void Loose_files_override_archives_case_insensitively()
    {
        using var files = new GameFiles(Path.Combine(root, "game"));
        Assert.Equal("game folder", Read(files, "scenes/a.nsd"));
        Assert.Equal("archive", Read(files, "3d/materials/moon.tga"));
    }

    [Fact]
    public void The_last_data_folder_wins()
    {
        using var files = new GameFiles(Path.Combine(root, "game"), [Path.Combine(root, "pack1"), Path.Combine(root, "pack2")]);
        Assert.Equal("pack 2", Read(files, "3d/materials/moon.tga"));
        Assert.Equal("game folder", Read(files, "SCENES/a.nsd"));
        using var one = new GameFiles(Path.Combine(root, "game"), [Path.Combine(root, "pack1")]);
        Assert.Equal("pack 1", Read(one, "3d/materials/moon.tga"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
