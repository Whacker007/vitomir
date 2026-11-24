// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **What ships**: release 1's package (<c>tools/package.ps1</c>) and a developer's platform package
/// (<c>tools/package-platform.ps1</c>) carry no <c>vitomir-tools</c> (nor the retired old program). Each script publishes the
/// platform's three programs; none of them reaches the tools through any reference, the ones it compiles against or the modules
/// it lays out beside it; each script refuses a stage holding them by name before its zip is made; and the platform's program as
/// built here holds no such file in its folder or its modules' (the publish lays out the same). Release 1's script, besides,
/// takes the Enhanced module and its game out, refuses a stage that still holds either, a game list but classic's, a people file
/// without <c>-WithPeople</c>, a <c>vitomir.ini</c> (the player's own), the game's file types and this machine in a text file, all before the zip, and writes
/// <c>bin\files.sha256</c> and <c>SHA256SUMS.txt</c>.
/// </summary>
public sealed partial class PackageContentsTests
{
    [GeneratedRegex(@"dotnet publish \(Join-Path \$repo ""src\\([A-Za-z.]+)\\\1\.csproj""\)")]
    private static partial Regex Publish();

    static string Tools => Path.Combine(Path.GetDirectoryName(PlatformBoundaryTests.SourceRoot(""))!, "tools");

    static string Release1 => Path.Combine(Tools, "package.ps1");

    static string Developer => Path.Combine(Tools, "package-platform.ps1");

    /// <summary>Every project a project's build brings in: compiled against or laid out beside it (a module), followed through.</summary>
    static HashSet<string> Brought(string project)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<string>();
        todo.Push(Path.GetFullPath(Path.Combine(PlatformBoundaryTests.SourceRoot(project), project + ".csproj")));
        while (todo.Count > 0)
        {
            var file = todo.Pop();
            if (!File.Exists(file) || !all.Add(Path.GetFileNameWithoutExtension(file))) continue;
            foreach (var r in System.Xml.Linq.XDocument.Load(file).Descendants("ProjectReference"))
                if ((string?)r.Attribute("Include") is { } include)
                    todo.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, include.Replace('\\', Path.DirectorySeparatorChar))));
        }
        return all;
    }

    static readonly string[] Programs = ["Vitomir.Platform.Host", "Vitomir.Platform.Cli", "Vitomir.Launcher"];

    [Fact]
    public void A_players_package_carries_no_vitomir_tools()
    {
        Assert.SkipWhen(!File.Exists(Developer) || !File.Exists(Release1), "the source tree's tools folder is not beside the tests");
        // Release 1's script publishes the three in a loop; the developer's, one line each.
        string release1 = File.ReadAllText(Release1), developer = File.ReadAllText(Developer);
        Assert.Contains("foreach ($project in \"Vitomir.Platform.Host\", \"Vitomir.Platform.Cli\", \"Vitomir.Launcher\")", release1, StringComparison.Ordinal);
        Assert.Contains("dotnet publish (Join-Path $repo \"src\\$project\\$project.csproj\")", release1, StringComparison.Ordinal);
        Assert.Equal(Programs, Publish().Matches(developer).Select(m => m.Groups[1].Value));
        Assert.Contains("\"bin\\vitomir-launcher.exe\"", developer, StringComparison.Ordinal);   // a stage without the launcher is refused
        Assert.Contains("\"vitomir-launcher$exe\"", release1, StringComparison.Ordinal);
        foreach (var project in Programs)
        {
            var brought = Brought(project);
            if (project is "Vitomir.Platform.Host" or "Vitomir.Launcher") Assert.Contains("Vitomir.Modules.Classic", brought);   // the modules it lays out are followed too
            Assert.DoesNotContain("Vitomir.Tools", brought);                            // vitomir-tools
        }
        // Each script's own refusal, before its zip is made.
        foreach (var (script, zipped) in new[] { (release1, "ZipFile]::CreateFromDirectory"), (developer, "Compress-Archive") })
        {
            Assert.Contains("$notPlayers = ", script);
            Assert.True(script.IndexOf("if ($notPlayers) { throw", StringComparison.Ordinal) < script.IndexOf(zipped, StringComparison.Ordinal));
        }

        // The program as built here, its modules laid out beside it: no tools' file anywhere in it.
        var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var built = Path.Combine(PlatformBoundaryTests.SourceRoot("Vitomir.Platform.Host"), "bin", here.Parent?.Name ?? "Debug", here.Name);
        if (!Directory.Exists(built)) return;
        var names = new Regex(@"^(vitomir|vitomir-tools|Vitomir\.Tools)(\.(exe|dll|pdb|deps\.json|runtimeconfig\.json))?$", RegexOptions.IgnoreCase);
        var bad = Directory.EnumerateFiles(built, "*", SearchOption.AllDirectories).Where(f => names.IsMatch(Path.GetFileName(f))).ToList();
        Assert.True(bad.Count == 0, "the platform's program's folder holds " + string.Join(", ", bad));
        Assert.True(File.Exists(Path.Combine(built, "Modules", "Classic", "bin", "Vitomir.Classic.dll")), $"the Classic module is not laid out in {built}");
    }

    /// <summary>
    /// **Release 1's package is the base game alone, and refuses anything else before its zip**: the Enhanced module and its game
    /// are taken out of the stage and refused if left, the game list must be classic's alone, the people need <c>-WithPeople</c>,
    /// and the game's file types and this machine's paths and names in a text file refuse; the file list and the zip's sum are
    /// written; <c>build-info.json</c> names no path.
    /// </summary>
    [Fact]
    public void Release_1s_package_is_the_base_game_alone_and_refuses_the_rest_before_its_zip()
    {
        Assert.SkipWhen(!File.Exists(Release1), "the source tree's tools folder is not beside the tests");
        string script = File.ReadAllText(Release1);
        int zip = script.IndexOf("ZipFile]::CreateFromDirectory", StringComparison.Ordinal);
        Assert.True(zip > 0);
        foreach (var refusal in new[]
                 {
                     "throw \"the package has no $need\"", "throw \"the Enhanced module in the package:", "throw \"the game list is not classic alone:",
                     "throw \"people in the package without -WithPeople:", "throw \"game content in the package:", "throw \"this machine in the package's text files:",
                     "throw \"the Classic module's IKVM image is for", "throw \"a vitomir.ini in the package",
                 })
        {
            int at = script.IndexOf(refusal, StringComparison.Ordinal);
            Assert.True(at > 0 && at < zip, $"the script does not refuse before its zip: {refusal}");
        }
        Assert.Contains("foreach ($left in \"Modules\\Enhanced\", \"Games\\enhanced.json\")", script, StringComparison.Ordinal);
        Assert.Contains("if ($WithPeople) {", script, StringComparison.Ordinal);
        int sums = script.IndexOf("\"files.sha256\"", StringComparison.Ordinal);
        Assert.True(sums > 0 && sums < zip, "no file list before the zip");
        Assert.Contains("\"SHA256SUMS.txt\"", script, StringComparison.Ordinal);
        Assert.Contains("\"Vitomir-$version-$Runtime.zip\"", script, StringComparison.Ordinal);
        // build-info.json: the commit, the branch, the dirty flag, the diff's hash, the version and the batch; no path.
        var info = Regex.Match(script, @"\$info = \[ordered\]@\{([^}]*)\}").Groups[1].Value;
        foreach (var key in new[] { "format", "version", "batch", "commit", "branch", "dirty", "diffSha256", "packagedUtc" })
            Assert.Contains($"{key} = ", info, StringComparison.Ordinal);
        Assert.DoesNotContain("source", info, StringComparison.Ordinal);
        Assert.DoesNotContain("$repo", info, StringComparison.Ordinal);
        // The player's files at the top are in the tree.
        foreach (var file in new[] { "README.txt", "THIRD_PARTY.txt" })
            Assert.True(File.Exists(Path.Combine(Tools, "package", file)), $"tools/package/{file} is not there");
    }

    /// <summary>
    /// **Each component's licence in full** (<c>tools/package/licences</c>): every file <c>THIRD_PARTY.txt</c> names is there and every
    /// one there is named, each a licence text with where its source is; the script copies the folder and refuses a stage without the
    /// licences that carry a source obligation, before its zip.
    /// </summary>
    [Fact]
    public void Every_component_s_licence_is_in_the_package_s_licences_folder()
    {
        string folder = Path.Combine(Tools, "package", "licences");
        Assert.SkipWhen(!Directory.Exists(folder), "the source tree's tools folder is not beside the tests");
        string notice = File.ReadAllText(Path.Combine(Tools, "package", "THIRD_PARTY.txt"));
        var named = Regex.Matches(notice, @"\b[A-Za-z0-9.-]+\.txt\b").Select(m => m.Value).Where(n => n is not ("LICENSE.txt" or "THIRD_PARTY.txt")).ToHashSet();
        var there = Directory.GetFiles(folder).Select(Path.GetFileName).ToHashSet();
        Assert.Equal(named.Order(StringComparer.Ordinal), there.Order(StringComparer.Ordinal)!);
        foreach (var file in there)
        {
            string text = File.ReadAllText(Path.Combine(folder, file!));
            Assert.True(text.Contains("Source", StringComparison.Ordinal) || text.Contains("source", StringComparison.Ordinal), $"{file} does not say where the source is");
            Assert.Matches(@"(?i)(permission is hereby granted|provided 'as-is'|eclipse public license|general public license)", text);
        }
        Assert.Contains("third-party\\ikvm-8.16.0", notice, StringComparison.Ordinal);
        string script = File.ReadAllText(Release1);
        int zip = script.IndexOf("ZipFile]::CreateFromDirectory", StringComparison.Ordinal);
        int copy = script.IndexOf("Copy-Item (Join-Path $PSScriptRoot \"package\\licences\")", StringComparison.Ordinal);
        Assert.True(copy > 0 && copy < zip, "the script does not copy the licences before its zip");
        Assert.Contains("\"licences\\dotnet.txt\", \"licences\\IKVM.txt\", \"licences\\ECJ.txt\"", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// **IKVM's source, in the repository** (<c>third-party/ikvm-&lt;version&gt;</c>, for the GPLv2 of its Java class library): the
    /// folder is the version the build references, its manifest's sums are its archives', and its README names both archives.
    /// </summary>
    [Fact]
    public void IKVM_s_source_is_in_the_repository_for_the_version_the_build_uses()
    {
        string repo = Path.GetDirectoryName(Tools)!;
        string scripting = Path.Combine(repo, "src", "Vitomir.Scripting", "Vitomir.Scripting.csproj");
        Assert.SkipWhen(!File.Exists(scripting) || !Directory.Exists(Path.Combine(repo, "third-party")), "the source tree is not beside the tests");
        string version = Regex.Match(File.ReadAllText(scripting), @"<PackageReference Include=""IKVM"" Version=""([^""]+)""").Groups[1].Value;
        string folder = Path.Combine(repo, "third-party", $"ikvm-{version}");
        Assert.True(Directory.Exists(folder), $"no third-party/ikvm-{version} for the IKVM the build references");
        var sums = File.ReadAllLines(Path.Combine(folder, "MANIFEST.txt")).Select(l => Regex.Match(l, @"^([0-9a-f]{64})  (\S+)$")).Where(m => m.Success).ToList();
        Assert.Equal(2, sums.Count);
        string readme = File.ReadAllText(Path.Combine(folder, "README.md"));
        foreach (var m in sums)
        {
            string file = Path.Combine(folder, m.Groups[2].Value);
            Assert.True(File.Exists(file), $"{m.Groups[2].Value} is not there");
            Assert.Equal(m.Groups[1].Value, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
            Assert.Contains(m.Groups[2].Value, readme, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// **The README's vitomir.ini** (its "Moving your data / portable" section): the example reads as the launcher and the game read it,
    /// and the README passes the package's own check of absolute paths (its example's path among the generic ones the script allows).
    /// </summary>
    [Fact]
    public void The_readme_shows_a_vitomir_ini_that_reads_and_the_package_allows_its_example_path()
    {
        string readme = Path.Combine(Tools, "package", "README.txt");
        Assert.SkipWhen(!File.Exists(readme), "the source tree's tools folder is not beside the tests");
        string text = File.ReadAllText(readme);
        Assert.Contains("Moving your data / portable", text, StringComparison.Ordinal);
        Assert.Contains("Vitomir never ships a vitomir.ini", text, StringComparison.Ordinal);
        var lines = text.Split('\n');
        int at = Array.FindIndex(lines, l => l.Trim() == "[folders]");
        Assert.True(at > 0, "no [folders] in the README");
        var ini = Vitomir.Platform.Setup.VitomirIni.Parse("README.txt", lines[at].Trim() + "\n" + lines[at + 1].Trim() + "\n");
        Assert.Empty(ini.Problems);
        Assert.Equal(@"D:\Vitomir-data", ini.User);
        // The script's own patterns, as it holds the package's text files to them.
        string script = File.ReadAllText(Release1);
        static string Quoted(string script, string name) => Regex.Match(script, @"\$" + name + @" = '((?:[^']|'')+)'").Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);   // PowerShell's '' is a quote
        string absolute = Quoted(script, "absolute"), generic = Quoted(script, "generic");
        Assert.NotEmpty(absolute);
        foreach (Match m in Regex.Matches(text, absolute)) Assert.Matches(generic, m.Value);
    }
}
