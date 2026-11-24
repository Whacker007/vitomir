// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The engine library under the game's modules: the three places
/// where one of its leaves decides something a player meets. Which archives a folder mounts and
/// which of them wins; what a glyph's edge looks like (the text ramp)
/// and the one character the blitters pass over; and a text's width,
/// which is where every wrap falls.
/// </summary>
public sealed class EngineLibraryTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-lib").FullName;

    void Nob(string name, params (string Path, string Text)[] entries)
    {
        using var zip = ZipFile.Open(Path.Combine(root, name), ZipArchiveMode.Create);
        foreach (var (path, text) in entries)
            using (var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.NoCompression).Open())) w.Write(text);
    }

    static string Text(GameFiles files, string path) =>
        System.Text.Encoding.ASCII.GetString(files.Read(path));

    [Fact]
    public void A_folder_mounts_its_archives_in_the_order_windows_lists_them()
    {
        foreach (var n in new[] { "Resource.nob", "Local_Eng.nob", "Sounds.nob", "codex.NOB", "LMaterials.nob",
                                  "Levels.nob", ".hidden.nob", "notes.txt", "Resource.nob.bak" })
            File.WriteAllText(Path.Combine(root, n), "");
        Assert.Equal(["codex.NOB", "Levels.nob", "LMaterials.nob", "Local_Eng.nob", "Resource.nob", "Sounds.nob"],
                     GameFiles.MountOrder(root).Select(Path.GetFileName));
    }

    [Fact]
    public void Every_nob_in_the_folder_is_mounted_and_the_first_in_that_order_wins()
    {
        Nob("Resource.nob", ("scenes/a.nsd", "resource"), ("misc/only.txt", "resource"));
        Nob("Codex.nob", ("scenes/a.nsd", "codex"));
        Nob("zz_mod.nob", ("misc/mod.txt", "a mod's own archive"));
        File.WriteAllText(Path.Combine(root, "broken.nob"), "not an archive");   // skipped, not counted

        using var files = new GameFiles(root);
        Assert.Equal("codex", Text(files, "scenes/a.nsd"));
        Assert.Equal("resource", Text(files, "misc/only.txt"));
        Assert.Equal("a mod's own archive", Text(files, "misc/mod.txt"));
    }

    [Fact]
    public void A_glyph_edge_is_the_colour_darkened_and_opaque_not_blended()
    {
        Assert.Equal(((byte)0, (byte)0), NfdFont.Texel(0));        // nothing written
        Assert.Equal(((byte)64, (byte)255), NfdFont.Texel(64));    // a quarter of the colour, opaque
        Assert.Equal(((byte)192, (byte)255), NfdFont.Texel(192));
        Assert.Equal(((byte)255, (byte)255), NfdFont.Texel(255));
    }

    [Fact]
    public void The_blitters_pass_over_byte_0xff_but_the_width_still_counts_it()
    {
        Assert.True(NfdFont.Skipped('ÿ'));
        Assert.False(NfdFont.Skipped('y'));
        Assert.False(NfdFont.Skipped(' '));

        var glyphs = new NfdGlyph[NfdFont.GlyphCount];
        glyphs['a'] = new NfdGlyph(0, 0, 5, 7, 6, 0);
        glyphs[0xff] = new NfdGlyph(0, 0, 5, 7, 4, -1);
        var font = new NfdFont { Glyphs = glyphs };
        Assert.Equal(6 + 4 + 6, font.Measure("aÿa"));
    }

    /// <summary>
    /// The width adds, for each byte to the terminator, the word at <c>font + (c + 1) * 0x18</c>:
    /// past the two-word header, the glyph's fifth word. Read the font's bytes that way and the
    /// width is the one <see cref="NfdFont.Measure"/> gives, so a wrap falls where 1.1's does.
    /// </summary>
    [Fact]
    public void A_text_is_as_wide_as_the_engine_adds_it_up()
    {
        using var files = Install.Open();
        var bytes = files.Read("ui/defaultFont15.nfd");
        var font = NfdFont.Read(bytes);
        const string line = "The Prince of Prague awaits you, Christof.";
        int engine = 0;
        foreach (char c in line) engine += BitConverter.ToInt32(bytes, ((byte)c + 1) * 0x18);
        Assert.Equal(engine, font.Measure(line));
        Assert.True(engine > 0);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
