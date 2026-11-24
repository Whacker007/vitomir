// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The platform's boundaries: it references
/// no SDL, no module and nothing of the old engine; its code never calls the C runtime's transcendental
/// functions, whose last bits differ between machines; and the program references no module. (A comparison with 1.1
/// section 9): the platform's renderer and interface reference nothing old either; the program no engine and no scripting;
/// only the Classic module, its tests and the tools reference the engine and the scripting; and those reference no SDL and no
/// renderer. the Classic module carries 1.1's frame controller as a library (Vitomir.Classic), which drew; the module's
/// own assembly names no SDL and not the old renderer. nothing of the module,
/// its controller's library included, references the old renderer's glue (Vitomir.Render) any more; the host's renderer draws
/// what it says.
/// </summary>
public sealed partial class PlatformBoundaryTests
{
    static readonly string[] Forbidden = ["Vitomir.Engine", "Vitomir.Render", "Vitomir.Scripting", "vitomir-tools", "Vitomir.Tools", "SDL3-CS", "SDL3", "Vitomir.Modules"];

    [Fact]
    public void The_platform_references_nothing_of_the_old_engine_no_module_and_no_SDL()
    {
        var refs = typeof(World).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        foreach (var name in refs)
            Assert.DoesNotContain(name, Forbidden.Where(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
    }

    [GeneratedRegex(@"\b(MathF?|Math)\s*\.\s*(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|SinCos|Sinh|Cosh|Tanh|Exp|Log|Log2|Log10|Pow|Cbrt)\s*\(|\b(Quaternion\s*\.\s*(CreateFromYawPitchRoll|CreateFromAxisAngle|Slerp)|Matrix4x4\s*\.\s*(CreateRotationX|CreateRotationY|CreateRotationZ|CreateFromYawPitchRoll|CreateFromAxisAngle)|Vector[234]\s*\.\s*(Cos|Sin|Exp|Log))\s*\(")]
    private static partial Regex Transcendental();

    [Fact]
    public void The_platforms_code_uses_its_own_trigonometry_never_the_C_runtimes()
    {
        var offenders = new List<string>();
        // The interface too: a recording replays its layouts, so they must come out the same everywhere.
        foreach (var root in new[] { SourceRoot("Vitomir.Platform"), SourceRoot("Vitomir.Platform.Interface") })
        {
        Assert.True(Directory.Exists(root), $"no source at {root}");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                var code = comment >= 0 ? line[..comment] : line;
                if (Transcendental().IsMatch(code)) offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }
        }
        Assert.True(offenders.Count == 0, "C-runtime trigonometry in the platform (use DetMath):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_program_references_no_module_project_and_names_no_modules_types()
    {
        // The program knows a module only through the loader and IModule. It may build a module first and
        // lay it out beside itself (ReferenceOutputAssembly="false"), never compile against it.
        var root = SourceRoot("Vitomir.Platform.Host");
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(root, "Vitomir.Platform.Host.csproj"));
        var modules = project.Descendants("ProjectReference")
            .Where(r => ((string?)r.Attribute("Include") ?? "").Contains("Vitomir.Modules.", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.All(modules, r => Assert.Equal("false", (string?)r.Attribute("ReferenceOutputAssembly"), ignoreCase: true));
        Assert.All(modules, r => Assert.Equal("true", (string?)r.Attribute("LayOutAsModule"), ignoreCase: true));
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains("Vitomir.Modules.", StringComparison.Ordinal) || lines[i].Contains("EnhancedModule", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0, "the program names a module's code:\n" + string.Join("\n", offenders));
    }

    // ---- the boundaries of release 1 ----

    /// <summary>1.1's own libraries: the Classic module's, never the platform's or the program's.</summary>
    static readonly string[] OldLibraries = ["Vitomir.Engine", "Vitomir.Scripting"];

    /// <summary>The old renderer and the tools (vitomir-tools), which nothing new may reference.</summary>
    static readonly string[] OldProgram = ["Vitomir.Render", "Vitomir.Tools"];

    /// <summary>
    /// The projects that may reference the engine and the scripting: the Classic module and its tests, the old renderer with
    /// the tests of the engine, the scripting and the renderer, the tools (vitomir-tools), and the two libraries themselves
    /// (the scripting is built on the engine). A private project may too (<see cref="IsPrivate"/>): no list here names one.
    /// </summary>
    static readonly string[] MayUseOldLibraries =
    [
        "Vitomir.Modules.Classic", "Vitomir.Modules.Classic.Tests", "Vitomir.Classic",
        "Vitomir.Render", "Vitomir.Engine.Tests", "Vitomir.Scripting.Tests", "Vitomir.Render.Tests",
        "Vitomir.Tools", "Vitomir.Engine", "Vitomir.Scripting",
    ];

    /// <summary>
    /// **Whether a project is private**: its project file says <c>&lt;PrivateProject&gt;true&lt;/PrivateProject&gt;</c>. A private
    /// project is one the public copy of the tree leaves out (a developer's instrument or its tests), so the public tree builds
    /// and passes without it, and the lists of these tests never name one: a private project may reference what the tools may
    /// (the engine, the scripting, vitomir-tools), and no other project may reference it
    /// (<see cref="No_project_but_a_private_one_references_a_private_one"/>).
    /// </summary>
    internal static bool IsPrivate(string project)
    {
        var file = Path.Combine(SourceRoot(project), project + ".csproj");
        return File.Exists(file) && System.Xml.Linq.XDocument.Load(file).Descendants("PrivateProject")
            .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The solution's projects, by name.</summary>
    static List<string> SolutionProjects() =>
        System.Xml.Linq.XDocument.Load(Path.Combine(SourceRoot(""), "Vitomir.slnx")).Descendants("Project")
            .Select(p => Path.GetFileNameWithoutExtension((string?)p.Attribute("Path") ?? "")).Where(n => n.Length > 0).ToList();

    /// <summary>
    /// **What the public copy keeps never compiles against what it leaves out**: no project that is not private references a
    /// private one, built against or only built first, so the public tree builds without them. Each project's own file is read,
    /// whichever projects this tree has.
    /// </summary>
    [Fact]
    public void No_project_but_a_private_one_references_a_private_one()
    {
        var offenders = new List<string>();
        foreach (var project in SolutionProjects().Where(p => !IsPrivate(p)))
        {
            var file = Path.Combine(SourceRoot(project), project + ".csproj");
            Assert.True(File.Exists(file), $"no project file for {project}");
            foreach (var r in System.Xml.Linq.XDocument.Load(file).Descendants("ProjectReference"))
                if (Path.GetFileNameWithoutExtension((string?)r.Attribute("Include") ?? "") is { Length: > 0 } named && IsPrivate(named))
                    offenders.Add($"{project} -> {named}");
        }
        Assert.True(offenders.Count == 0, "a project the public copy keeps references a private one:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_platform_its_renderer_and_its_interface_reference_nothing_old_and_no_module()
    {
        foreach (var project in new[] { "Vitomir.Platform", "Vitomir.Platform.Render", "Vitomir.Platform.Interface" })
        {
            var graph = Closure(project);
            Assert.True(graph.Projects.Count > 0, $"no project file for {project}");
            var bad = graph.Projects.Where(p => OldLibraries.Contains(p) || OldProgram.Contains(p) || p.StartsWith("Vitomir.Modules.", StringComparison.Ordinal)).ToList();
            Assert.True(bad.Count == 0, $"{project} references {string.Join(", ", bad)}");
            var refs = BuiltReferences(project);
            var badRefs = refs.Where(r => OldLibraries.Contains(r) || r is "vitomir-tools" or "Vitomir.Render" || r.StartsWith("Vitomir.Modules.", StringComparison.Ordinal)).ToList();
            Assert.True(badRefs.Count == 0, $"{project}'s assembly references {string.Join(", ", badRefs)}");
        }
        // The platform and its interface draw nothing either (the renderer is the platform's one SDL user).
        foreach (var project in new[] { "Vitomir.Platform", "Vitomir.Platform.Interface" })
            Assert.DoesNotContain(Closure(project).Packages, p => p.StartsWith("ppy.SDL", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A project's assembly's name: the programs' own (vitomir-platform, vitomir-platform-cli, vitomir-launcher), else the project's.</summary>
    static string Assembly(string project) => project switch
    {
        "Vitomir.Platform.Host" => "vitomir-platform",
        "Vitomir.Platform.Cli" => "vitomir-platform-cli",
        "Vitomir.Launcher" => "vitomir-launcher",
        _ => project,
    };

    [Fact]
    public void The_program_references_no_module_no_engine_and_no_scripting()
    {
        foreach (var project in new[] { "Vitomir.Platform.Host", "Vitomir.Platform.Cli", "Vitomir.Launcher" })   // the launcher too
        {
            var graph = Closure(project);
            Assert.True(graph.Projects.Count > 0, $"no project file for {project}");
            var bad = graph.Projects.Where(p => OldLibraries.Contains(p) || OldProgram.Contains(p) || p.StartsWith("Vitomir.Modules.", StringComparison.Ordinal)).ToList();
            Assert.True(bad.Count == 0, $"{project} compiles against {string.Join(", ", bad)} (a module is built first and laid out, never referenced: ReferenceOutputAssembly=\"false\")");
            var badRefs = BuiltReferences(project, Assembly(project))
                .Where(r => OldLibraries.Contains(r) || r is "vitomir-tools" or "Vitomir.Render" || r.StartsWith("Vitomir.Modules.", StringComparison.Ordinal)).ToList();
            Assert.True(badRefs.Count == 0, $"{project}'s assembly references {string.Join(", ", badRefs)}");
        }
    }

    [Fact]
    public void Only_the_Classic_module_its_tests_and_the_tools_reference_the_engine_and_the_scripting()
    {
        var projects = SolutionProjects();
        Assert.Contains("Vitomir.Modules.Classic", projects);
        var offenders = projects.Where(p => !MayUseOldLibraries.Contains(p) && !IsPrivate(p))
            .Select(p => (p, Closure(p).Projects.Where(OldLibraries.Contains).ToList())).Where(x => x.Item2.Count > 0)
            .Select(x => $"{x.p} -> {string.Join(", ", x.Item2)}").ToList();
        Assert.True(offenders.Count == 0, "outside the Classic module, its tests and the tools:\n" + string.Join("\n", offenders));
        Assert.Contains("Vitomir.Engine", Closure("Vitomir.Modules.Classic").Projects);
        Assert.Contains("Vitomir.Scripting", Closure("Vitomir.Modules.Classic").Projects);
    }

    /// <summary>
    /// **vitomir-tools** (the tools out of the old program): the platform's programs, the platform's
    /// libraries and the modules never reference it (it is the old engine's, and goes nowhere near a player's package), and
    /// it references only what its commands need — the engine, the scripting, the formats, the old renderer, the controller's
    /// library and the platform's renderer under it — never the platform's programs or a module. Only the
    /// tests of the code moved into it (the engine's, the scripting's, the old renderer's) reference it, and a private project
    /// (<see cref="IsPrivate"/>) may.
    /// </summary>
    [Fact]
    public void Vitomir_platform_never_references_vitomir_tools_and_the_tools_only_what_they_need()
    {
        var projects = SolutionProjects();
        Assert.Contains("Vitomir.Tools", projects);
        foreach (var project in new[] { "Vitomir.Platform.Host", "Vitomir.Platform.Cli", "Vitomir.Launcher", "Vitomir.Platform", "Vitomir.Platform.Render",
                                        "Vitomir.Platform.Interface", "Vitomir.Modules.Classic", "Vitomir.Modules.Enhanced", "Vitomir.Classic" })
        {
            Assert.DoesNotContain("Vitomir.Tools", Closure(project).Projects);
            Assert.DoesNotContain("vitomir-tools", BuiltReferences(project, Assembly(project)));
        }
        var tools = Closure("Vitomir.Tools").Projects;
        var allowed = new[] { "Vitomir.Engine", "Vitomir.Scripting", "Vitomir.Formats", "Vitomir.Render", "Vitomir.Classic",
                              "Vitomir.Platform.Render", "Vitomir.Platform", "Vitomir.Platform.Interface" };
        var extra = tools.Where(p => !allowed.Contains(p)).ToList();
        Assert.True(extra.Count == 0, "vitomir-tools references " + string.Join(", ", extra));
        var users = projects.Where(p => p != "Vitomir.Tools" && !IsPrivate(p) && Closure(p).Projects.Contains("Vitomir.Tools")).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Vitomir.Engine.Tests", "Vitomir.Render.Tests", "Vitomir.Scripting.Tests"], users);
    }

    [Fact]
    public void The_engine_and_the_scripting_reference_no_SDL_and_no_renderer()
    {
        foreach (var project in new[] { "Vitomir.Engine", "Vitomir.Scripting" })
        {
            var graph = Closure(project);
            Assert.True(graph.Projects.Count > 0, $"no project file for {project}");
            var bad = graph.Projects.Where(p => p is "Vitomir.Render" or "Vitomir.Platform.Render" or "Vitomir.Tools" or "Vitomir.Platform.Host" or "Vitomir.Classic").ToList();
            Assert.True(bad.Count == 0, $"{project} references {string.Join(", ", bad)}");
            Assert.DoesNotContain(graph.Packages, p => p.StartsWith("ppy.SDL", StringComparison.OrdinalIgnoreCase));
            var badRefs = BuiltReferences(project).Where(r => r.StartsWith("SDL", StringComparison.OrdinalIgnoreCase) || r is "Vitomir.Render" or "Vitomir.Platform.Render").ToList();
            Assert.True(badRefs.Count == 0, $"{project}'s assembly references {string.Join(", ", badRefs)}");
        }
    }

    /// <summary>
    /// **The Classic module since the move**: it carries 1.1's frame controller as a library of its own
    /// (<c>Vitomir.Classic</c>; <c>ControllerBoundaryTests</c> holds its line), and its own assembly offers the windowed game through
    /// the platform renderer's host contract (<c>IClassicGame</c> until then; its loop is handed the program's host,
    /// <c>IClassicHost</c>, at its start and the program runs its frames). It references no program (<c>vitomir-tools</c>, the platform's
    /// program), and its own assembly no SDL and not the old renderer: it reaches the platform's renderer only through the
    /// controller's library and the platform's host. **The old renderer's glue is in its closure no more** (by design:
    /// The module never draws; the controller hands the host's renderer what each frame is), so it is not in its bin.
    /// </summary>
    [Fact]
    public void The_Classic_module_reaches_SDL_and_the_renderers_only_through_its_controllers_library()
    {
        var graph = Closure("Vitomir.Modules.Classic");
        Assert.True(graph.Projects.Count > 0, "no project file for Vitomir.Modules.Classic");
        var bad = graph.Projects.Where(p => p is "Vitomir.Tools" or "Vitomir.Platform.Host" or "Vitomir.Render").ToList();
        Assert.True(bad.Count == 0, $"Vitomir.Modules.Classic references {string.Join(", ", bad)}");
        Assert.Contains("Vitomir.Classic", graph.Projects);
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(SourceRoot("Vitomir.Modules.Classic"), "Vitomir.Modules.Classic.csproj"));
        Assert.DoesNotContain(project.Descendants("PackageReference"), r => ((string?)r.Attribute("Include") ?? "").StartsWith("ppy.SDL", StringComparison.OrdinalIgnoreCase));
        var direct = project.Descendants("ProjectReference").Select(r => Path.GetFileNameWithoutExtension((string?)r.Attribute("Include") ?? "")).ToList();
        Assert.DoesNotContain("Vitomir.Render", direct);
        Assert.DoesNotContain("Vitomir.Platform.Render", direct);
        var badRefs = BuiltReferences("Vitomir.Modules.Classic").Where(r => r.StartsWith("SDL", StringComparison.OrdinalIgnoreCase) || r is "Vitomir.Render" or "vitomir-tools").ToList();
        Assert.True(badRefs.Count == 0, $"Vitomir.Modules.Classic's assembly references {string.Join(", ", badRefs)}");
    }

    /// <summary>A project's references, followed through: the projects it compiles against (not those only built first and laid out) and the packages of each.</summary>
    internal static (HashSet<string> Projects, HashSet<string> Packages) Closure(string project)
    {
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = Path.Combine(SourceRoot(project), project + ".csproj");
        var todo = new Stack<string>();
        if (File.Exists(start)) todo.Push(Path.GetFullPath(start));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (todo.Count > 0)
        {
            var file = todo.Pop();
            if (!seen.Add(file)) continue;
            projects.Add(Path.GetFileNameWithoutExtension(file));
            var xml = System.Xml.Linq.XDocument.Load(file);
            foreach (var r in xml.Descendants("PackageReference")) if ((string?)r.Attribute("Include") is { } name) packages.Add(name);
            foreach (var r in xml.Descendants("ProjectReference"))
            {
                if (string.Equals((string?)r.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)) continue;
                if ((string?)r.Attribute("Include") is not { } include) continue;
                todo.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, include.Replace('\\', Path.DirectorySeparatorChar))));
            }
        }
        projects.Remove(project);
        return (projects, packages);
    }

    /// <summary>
    /// The assemblies a project's built assembly references, read from its metadata (nothing loaded), when it is built in
    /// this test's configuration beside it (<c>src/&lt;project&gt;/bin/&lt;config&gt;/&lt;tfm&gt;/</c>); none when it is not.
    /// </summary>
    internal static List<string> BuiltReferences(string project, string? assemblyName = null)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string tfm = here.Name, config = here.Parent?.Name ?? "Debug";
        var dll = Path.Combine(SourceRoot(project), "bin", config, tfm, (assemblyName ?? project) + ".dll");
        if (!File.Exists(dll)) return [];
        using var stream = File.OpenRead(dll);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return [.. md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))];
    }

    /// <summary>The source folder of a project, found by walking up from the test's own folder to <c>src</c>.</summary>
    internal static string SourceRoot(string project)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vitomir.slnx"))) dir = dir.Parent;
        return dir is null ? "" : Path.Combine(dir.FullName, project);
    }
}
