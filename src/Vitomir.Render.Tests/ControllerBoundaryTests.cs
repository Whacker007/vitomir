// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The frame controller (the Classic module's library) reaches SDL and the renderers only through its host and the listed exceptions,
// the old renderer's glue not at all (the module never draws).
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace Vitomir.Render.Tests;

/// <summary>
/// **Where the line falls for the frame controller**:
/// the controller is a library of the Classic
/// module's, <c>Vitomir.Classic.dll</c> — the front end, the game run and its parts, the film screen, the run's start and the loop
/// of runs, the binding layer and the tape, the network game and the Storyteller, the console and the harness, and a
/// place's frame as the module says it — and **every type in it** takes no SDL type, no type of <c>vitomir-tools</c> and, since
/// **No type of the old renderer's glue** (<c>Vitomir.Render</c>, whose list emptied:, the module never
/// draws) in its fields, its members' signatures, its locals or the code of its methods (every type, method and field the IL
/// names; lambdas' and iterators' generated types included), and references none of the three assemblies. What it may use of
/// the platform's renderer (<c>Vitomir.Platform.Render</c>) is the list here: the host contract, the host's renderer it hands
/// each frame to, and the data it hands it — no GPU object, no drawer of the 1.1 look. Each entry must still be in use, so
/// **the list only shrinks** (or is added to by design). Reads the built library beside the program's build (skipped when it
/// is not built); no GPU.
/// </summary>
public class ControllerBoundaryTests
{
    /// <summary>
    /// **What the controller may use of the platform's renderer** (the module never
    /// draws): the host contract and its asks; the host's renderer for 1.1's frames and the handles to its drawers, which take
    /// only data; what goes over a frame and the developer overlay's quads; the host's picture and a still's file.
    /// </summary>
    static readonly string[] PlatformRender =
    [
        // The host contract: what the controller asks of its host.
        "Vitomir.Platform.Render.Classic.IClassicHost", "Vitomir.Platform.Render.Classic.WindowAsk", "Vitomir.Platform.Render.Classic.GpuAsk",
        "Vitomir.Platform.Render.Classic.IInputHost", "Vitomir.Platform.Render.Classic.IScriptedInput", "Vitomir.Platform.Render.Classic.IWindowModes",
        // The game's keys the scripted input (the platform's host's since) is given.
        "Vitomir.Platform.Render.Classic.ScriptKeys", "Vitomir.Platform.Render.Classic.ScriptKey",
        // The host's renderer for 1.1's frames and its drawers' handles (they take only data).
        "Vitomir.Platform.Render.Classic.IClassicRenderer", "Vitomir.Platform.Render.Classic.IClassicWorld", "Vitomir.Platform.Render.Classic.IClassicRing",
        "Vitomir.Platform.Render.Classic.IClassicScreen", "Vitomir.Platform.Render.Classic.IClassicInterface", "Vitomir.Platform.Render.Classic.IClassicOverlay",
        "Vitomir.Platform.Render.Classic.IClassicEffects", "Vitomir.Platform.Render.Classic.IClassicWorldBelow",
        // What the module says a frame is besides its snapshot: what goes over it, the developer overlay's quads (its font's
        // sizes are constants, compiled in).
        "Vitomir.Platform.Render.Classic.ClassicOver", "Vitomir.Platform.Render.Classic.ClassicOverlayQuads", "Vitomir.Platform.Render.Classic.OverlayVertex",
        // The picture the host acquired (the controller reads its size and hands it back) and a still's file written.
        "Vitomir.Platform.Render.GpuPicture", "Vitomir.Platform.Render.Png",
    ];

    /// <summary>The assemblies of the tools' program, of the old renderer's glue and of SDL, which the controller's library may not even reference.</summary>
    static readonly string[] NotReferenced = ["vitomir-tools", "Vitomir.Render", "SDL3-CS"];

    static string? Program()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var tfm = dir.Name;
        var config = dir.Parent?.Name;
        var src = dir.Parent?.Parent?.Parent?.Parent;
        if (src is null || config is null) return null;
        var dll = Path.Combine(src.FullName, "Vitomir.Tools", "bin", config, tfm, "Vitomir.Classic.dll");
        return File.Exists(dll) ? dll : null;
    }

    static bool Watched(Type t, out string name)
    {
        while (t.HasElementType) t = t.GetElementType()!;
        if (t.IsGenericType && !t.IsGenericTypeDefinition) t = t.GetGenericTypeDefinition();
        name = (t.FullName ?? t.Name).Split('`')[0];
        var asm = t.Assembly.GetName().Name ?? "";
        return asm is "SDL3-CS" or "Vitomir.Render" or "Vitomir.Platform.Render" or "vitomir-tools" || t.Namespace == "SDL";
    }

    static IEnumerable<Type> Within(Type t)
    {
        foreach (var x in t.IsGenericType ? t.GetGenericArguments().SelectMany(Within) : []) yield return x;
        if (t.HasElementType) foreach (var x in Within(t.GetElementType()!)) yield return x;
        yield return t;
    }

    static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>Every type a method's IL names: the tokens of its calls, fields, types and ldtoken, resolved.</summary>
    static IEnumerable<Type> Named(MethodBase m)
    {
        foreach (var member in Members(m))
            switch (member)
            {
                case Type t: yield return t; break;
                case FieldInfo f: yield return f.DeclaringType!; yield return f.FieldType; break;
                case MethodBase mb:
                    yield return mb.DeclaringType!;
                    if (mb is MethodInfo mi) yield return mi.ReturnType;
                    foreach (var p in mb.GetParameters()) yield return p.ParameterType;
                    break;
            }
    }

    /// <summary>Every member a method's IL names (its calls, fields, types and ldtoken), resolved.</summary>
    static IEnumerable<MemberInfo> Members(MethodBase m)
    {
        var il = m.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;
        var module = m.Module;
        Type[]? typeArgs = m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        Type[]? methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
        for (int i = 0; i < il.Length;)
        {
            short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            var op = Codes[value];
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                int token = BitConverter.ToInt32(il, i);
                MemberInfo? member = null;
                try { member = module.ResolveMember(token, typeArgs, methodArgs); } catch (ArgumentException) { }
                if (member is not null) yield return member;
            }
            i += size;
        }
    }

    static Dictionary<string, List<string>> Reach()
    {
        var dll = Program();
        Assert.SkipWhen(dll is null, "vitomir-tools (and with it the controller's library, Vitomir.Classic) is not built beside the tests (build src/Vitomir.Tools/Vitomir.Tools.csproj in the same configuration)");
        var folder = Path.GetDirectoryName(dll)!;
        var context = new AssemblyLoadContext("controller-boundary", isCollectible: true);
        context.Resolving += (c, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? c.LoadFromAssemblyPath(Path.Combine(folder, name.Name + ".dll")) : null;
        try
        {
            var program = context.LoadFromAssemblyPath(dll!);
            var reached = new Dictionary<string, List<string>>();
            void Note(Type? t, string where)
            {
                if (t is null) return;
                foreach (var x in Within(t))
                    if (Watched(x, out var name)) (reached.TryGetValue(name, out var l) ? l : reached[name] = []).Add(where);
            }
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            // Every type of the library: the controller is the whole of it.
            var controller = program.GetTypes().ToList();
            Assert.Contains(controller, t => t.FullName == "Vitomir.Classic.ClassicGame");
            foreach (var r in program.GetReferencedAssemblies())
                if (NotReferenced.Contains(r.Name)) (reached.TryGetValue(r.Name!, out var l) ? l : reached[r.Name!] = []).Add("the assembly's references");
            foreach (var type in controller)
            {
                foreach (var f in type.GetFields(all)) Note(f.FieldType, $"{type.Name}.{f.Name}");
                foreach (var m in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
                {
                    if (m is MethodInfo mi) Note(mi.ReturnType, $"{type.Name}.{m.Name}");
                    foreach (var p in m.GetParameters()) Note(p.ParameterType, $"{type.Name}.{m.Name}");
                    foreach (var local in m.GetMethodBody()?.LocalVariables ?? []) Note(local.LocalType, $"{type.Name}.{m.Name}");
                    foreach (var t in Named(m)) Note(t, $"{type.Name}.{m.Name}");
                }
            }
            return reached;
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void The_controller_reaches_SDL_and_the_renderers_only_through_its_host_and_the_listed_exceptions()
    {
        var reached = Reach();
        var offenders = reached.Where(kv => !PlatformRender.Contains(kv.Key))
                               .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.Distinct().Take(6))}").ToList();
        Assert.True(offenders.Count == 0, "the controller reaches past its host:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// **The levels generator's cell code stays off in the module** (switched off, no player's switch): the
    /// one way a place's frame gets a cell world is <c>CellWorlds.Make</c>, and nothing in the controller's library sets it —
    /// only <c>vitomir-tools</c>' own host does (its tools, <c>--level-l2</c> and <c>--cook-l2</c>, are the only way into a cell level),
    /// which this proves the scan finds. The platform's host's side is <c>ClassicHeadlessTests</c>'.
    /// </summary>
    [Fact]
    public void Nothing_in_the_controller_switches_the_cell_world_on_and_only_vitomir_tools_does()
    {
        var dll = Program();
        Assert.SkipWhen(dll is null, "vitomir-tools (and with it the controller's library, Vitomir.Classic) is not built beside the tests (build src/Vitomir.Tools/Vitomir.Tools.csproj in the same configuration)");
        var folder = Path.GetDirectoryName(dll)!;
        var context = new AssemblyLoadContext("cell-world-off", isCollectible: true);
        context.Resolving += (c, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? c.LoadFromAssemblyPath(Path.Combine(folder, name.Name + ".dll")) : null;
        try
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            List<string> Setters(Assembly assembly) =>
                [.. assembly.GetTypes()
                    .SelectMany(t => t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)).Select(m => (t, m)))
                    .Where(p => Members(p.m).Any(x => x is MethodInfo { Name: "set_Make", DeclaringType.FullName: "Vitomir.Classic.CellWorlds" }))
                    .Select(p => $"{p.t.FullName}.{p.m.Name}")];
            var controller = context.LoadFromAssemblyPath(dll!);
            Assert.Empty(Setters(controller));
            // The tools' assembly, in this context (these tests reference vitomir-tools too, and the default context's copy names
            // the default context's types).
            var tools = context.LoadFromAssemblyPath(Path.Combine(folder, "vitomir-tools.dll"));
            Assert.Contains(Setters(tools), m => m.StartsWith("Vitomir.SdlClassicHost.", StringComparison.Ordinal));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void Every_listed_exception_is_still_in_use()
    {
        var reached = Reach();
        var stale = PlatformRender.Where(name => !reached.ContainsKey(name)).ToList();
        Assert.True(stale.Count == 0, "listed but no longer reached (take them off the list):\n" + string.Join("\n", stale));
    }
}
