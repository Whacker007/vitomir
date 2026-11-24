// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.RegularExpressions;
using Vitomir.Engine.Vfs;

namespace Vitomir.Scripting.Tests;

/// <summary>One cache for the overlay's tests: the shipped classes' base is made once, each test's overlay beside it.</summary>
public sealed class OverlayCache : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "vitomir-codex-overlay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

/// <summary>
/// **The Codex folder over the shipped classes** (the overlay): the SDK's own sources, byte for byte the setup
/// manifest's, are ignored (the shipped class wins); a changed or new source is compiled against the shipped classes into the
/// overlay, which the loader searches first; a loose class goes in as it is (bridged if a `Codex*` one, a local read unassigned
/// fixed); the overlay is made again when its folder is not what its marker lists. The fixtures are written at run time into
/// temporary folders, from the install's own SDK sources and classes: nothing of the game is in this file.
/// </summary>
public class OverlayTests(OverlayCache cache) : IClassFixture<OverlayCache>
{
    static string? Install(out string why)
    {
        why = "";
        var dir = GameFiles.Locate();
        if (dir is null) { why = "no game install (set VTMR_GAME_DIR to the folder holding Resource.nob)"; return null; }
        if (!File.Exists(Path.Combine(dir, "Codex.nob"))) { why = "the install has no Codex.nob"; return null; }
        if (!File.Exists(Path.Combine(dir, "Codex", "SP", "OLDT_7_1.java"))) { why = "the install has no SDK sources to make a mod of"; return null; }
        return dir;
    }

    CodexCompiler Over(string install, string codex) =>
        CodexCompiler.FromClasses(Path.Combine(install, "Codex.nob"), codex, Path.Combine(install, "classes.zip"), cache.Root);

    static string TempCodex()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vitomir-mod-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "SP"));
        return dir;
    }

    /// <summary>
    /// **The modified scene** (the proof's fixture): Old Town's chapter scene as the SDK has it, with a field added and its
    /// <c>beginscene</c> first printing a line to the game's console, written to <paramref name="into"/>'s <c>SP</c>.
    /// </summary>
    public static string ModifiedScene(string install, string into)
    {
        var text = File.ReadAllText(Path.Combine(install, "Codex", "SP", "OLDT_7_1.java"), Encoding.Latin1);
        text = Regex.Replace(text, @"(public class OLDT_7_1 extends Codex\s*\{)",
                             "$1\n\tpublic static String vitomirOverlay = \"from the Codex folder\";", RegexOptions.None, TimeSpan.FromSeconds(5));
        text = Regex.Replace(text, @"(public void beginscene\(int clientGuid, int captureID\)\s*\{)",
                             "$1\n\t\tCodexConsole.Print(0, 0, \"vitomir overlay: OLDT_7_1 from the Codex folder\");", RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.Contains("vitomirOverlay", text);
        Assert.Contains("vitomir overlay:", text);
        var path = Path.Combine(into, "SP", "OLDT_7_1.java");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.Latin1);
        return path;
    }

    [Fact]
    public void The_SDKs_own_sources_are_ignored_and_the_shipped_classes_run()
    {
        var install = Install(out var why);
        Assert.SkipWhen(install is null, why);
        var compiler = Over(install!, Path.Combine(install!, "Codex"));
        var build = compiler.Build();
        Assert.True(build.FromClasses);
        Assert.Empty(build.Overlay!);
        Assert.Empty(Directory.GetFiles(build.OutputDir, "*.class"));
        Assert.Equal(465, build.SdkIgnored);
        Assert.Null(compiler.SourceOf("OLDT_7_1"));                  // nothing to compile on load
        Assert.Contains(build.Lines(), l => l.Contains("overlay: none", StringComparison.Ordinal));
        Assert.Contains(build.Lines(), l => l.Contains("465 of the Codex folder's files are the SDK's own sources, unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void A_changed_scene_is_compiled_into_the_overlay_and_runs_from_it()
    {
        var install = Install(out var why);
        Assert.SkipWhen(install is null, why);
        var codex = TempCodex();
        try
        {
            var source = ModifiedScene(install!, codex);
            var compiler = Over(install!, codex);
            var build = compiler.Build();
            Assert.Equal([new OverlayClass("OLDT_7_1", "SP/OLDT_7_1.java")], build.Overlay!);
            Assert.True(File.Exists(Path.Combine(build.OutputDir, "OLDT_7_1.class")));
            Assert.True(File.Exists(Path.Combine(build.BaseDir!, "OLDT_7_1.class")));   // the shipped one, under it
            Assert.Contains(build.Lines(), l => l.Contains("overlay: 1 class from the Codex folder over the shipped ones: OLDT_7_1 (SP/OLDT_7_1.java)", StringComparison.Ordinal));
            Assert.Equal(source, compiler.SourceOf("OLDT_7_1"));
            Assert.False(compiler.NeedsRecompile(source));

            var rt = ScriptRuntime.Start(compiler, new CodexHost());
            var cls = rt.ClassOf("OLDT_7_1");
            Assert.NotNull(cls);
            Assert.Equal("from the Codex folder", (string)cls!.getField("vitomirOverlay").get(null));
            // The shipped classes under it: a class no mod touched comes from the base.
            Assert.NotNull(rt.ClassOf("OLDT_7_4"));

            // Made once: the next build finds it.
            var again = Over(install!, codex).Build();
            Assert.True(again.CacheHit);
            Assert.Equal(build.OutputDir, again.OutputDir);

            // A class the folder's marker does not list (a compile on load's, a file put there by hand) makes it again.
            File.WriteAllBytes(Path.Combine(build.OutputDir, "Stray.class"), File.ReadAllBytes(Path.Combine(build.BaseDir!, "OLDT_7_4.class")));
            var mended = Over(install!, codex).Build();
            Assert.False(mended.CacheHit);
            Assert.False(File.Exists(Path.Combine(build.OutputDir, "Stray.class")));
            Assert.Equal(build.Overlay, mended.Overlay);
        }
        finally { Directory.Delete(codex, true); }
    }

    [Fact]
    public void Loose_classes_go_in_as_they_are_bridged_and_mended_and_a_changed_Codex_source_is_bridged()
    {
        var install = Install(out var why);
        Assert.SkipWhen(install is null, why);
        var codex = TempCodex();
        try
        {
            var shipped = CodexClasses.Read(Path.Combine(install!, "Codex.nob"));
            File.WriteAllBytes(Path.Combine(codex, "Lightning.class"), shipped["Lightning"]);
            File.WriteAllBytes(Path.Combine(codex, "SP", "DisciplineEyesOfChaos.class"), shipped["DisciplineEyesOfChaos"]);
            File.WriteAllBytes(Path.Combine(codex, "CodexVector.class"), shipped["CodexVector"]);
            Directory.CreateDirectory(Path.Combine(codex, "System"));
            var console = File.ReadAllText(Path.Combine(install!, "Codex", "System", "CodexConsole.java"), Encoding.Latin1)
                              .Replace("public class CodexConsole", "public class CodexConsole\n// a mod's change", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(codex, "System", "CodexConsole.java"), console, Encoding.Latin1);

            var compiler = Over(install!, codex);
            var build = compiler.Build();
            Assert.Equal(["CodexConsole", "CodexVector", "DisciplineEyesOfChaos", "Lightning"], build.Overlay!.Select(o => o.Name));
            Assert.Equal("System/CodexConsole.java", build.Overlay!.Single(o => o.Name == "CodexConsole").From);
            Assert.Contains(build.Notes, n => n.StartsWith("initialised local 6 (a reference) to null in DisciplineEyesOfChaos.cast(III)I", StringComparison.Ordinal));
            Assert.Equal(shipped["Lightning"], File.ReadAllBytes(Path.Combine(build.OutputDir, "Lightning.class")));
            // The changed Codex source bridged as the source path bridges it: no native left.
            var bridged = ClassFile.Read(File.ReadAllBytes(Path.Combine(build.OutputDir, "CodexConsole.class")));
            Assert.DoesNotContain(bridged.Methods, m => (m.Access & ClassFile.AccNative) != 0);
            Assert.Contains(bridged.Methods, m => bridged.Utf8(m.Name) == "PrintFormatNLS");

            var rt = ScriptRuntime.Start(compiler, new CodexHost());
            foreach (var name in build.Overlay!.Select(o => o.Name))
            {
                var c = rt.ClassOf(name);
                Assert.NotNull(c);
                c!.getDeclaredMethods();                              // linked: verified by IKVM
            }
        }
        finally { Directory.Delete(codex, true); }
    }
}
