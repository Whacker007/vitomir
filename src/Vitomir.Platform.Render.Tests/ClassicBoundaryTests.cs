// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The platform's renderer, the 1.1 look in it included, names nothing of the old engine in its built code.
using System.Reflection;
using System.Reflection.Emit;
using Vitomir.Platform.Render.Classic;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// **Where the line falls for the platform's renderer**:
/// <c>Vitomir.Platform.Render</c>, with the 1.1 look moved into it (<c>Classic/</c>), references no
/// assembly of the old engine, and no type it declares names one in its fields, its members' signatures, its locals or the
/// code of its methods (every type, method and field the IL names, resolved). The 1.1 look reads the Classic snapshot
/// (<c>Vitomir.Platform.Presentation.Classic</c>) and the draw list (<c>Vitomir.Platform.Interface</c>) and nothing else of
/// the game. <c>PlatformBoundaryTests</c> checks the assemblies' references; this checks the code. No GPU needed.
/// </summary>
public class ClassicBoundaryTests
{
    /// <summary>
    /// The old engine's assemblies, the old renderer and the tools' program. Anything built on one of them is old too
    /// (<see cref="BuiltOnOld"/>): the Classic module's library, the tools, a developer's instrument.
    /// </summary>
    static readonly string[] Old = ["Vitomir.Engine", "Vitomir.Scripting", "Vitomir.Render", "vitomir-tools", "Vitomir.Modules.Classic"];

    static readonly Assembly Renderer = typeof(WorldRenderer).Assembly;

    static bool IsOld(Type t)
    {
        while (t.HasElementType) t = t.GetElementType()!;
        if (t.IsGenericType && !t.IsGenericTypeDefinition && t.GetGenericArguments().Any(IsOld)) return true;
        return Old.Contains(t.Assembly.GetName().Name) || BuiltOnOld(t.Assembly);
    }

    /// <summary>Whether an assembly references one of the old ones (<see cref="Old"/>), so whatever it declares belongs to the old side.</summary>
    static bool BuiltOnOld(Assembly a) =>
        builtOnOld.GetOrAdd(a, x => x != Renderer && x.GetReferencedAssemblies().Any(n => Old.Contains(n.Name)));

    static readonly System.Collections.Concurrent.ConcurrentDictionary<Assembly, bool> builtOnOld = new();

    static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>Every type a method's IL names (its calls', fields' and types' tokens, resolved), or why one would not resolve.</summary>
    static IEnumerable<(Type? Type, string? Unresolved)> Named(MethodBase m)
    {
        var il = m.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;
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
                string? failed = null;
                try { member = m.Module.ResolveMember(token, typeArgs, methodArgs); }
                catch (ArgumentException) { }
                catch (Exception e) when (e is FileNotFoundException or FileLoadException or TypeLoadException) { failed = e.Message; }
                if (failed is not null) yield return (null, failed);
                switch (member)
                {
                    case Type t: yield return (t, null); break;
                    case FieldInfo f: yield return (f.DeclaringType, null); yield return (f.FieldType, null); break;
                    case MethodBase mb:
                        yield return (mb.DeclaringType, null);
                        if (mb is MethodInfo mi) yield return (mi.ReturnType, null);
                        foreach (var p in mb.GetParameters()) yield return (p.ParameterType, null);
                        break;
                }
            }
            i += size;
        }
    }

    [Fact]
    public void The_platforms_renderer_references_no_assembly_of_the_old_engine()
    {
        var refs = Renderer.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.DoesNotContain(refs, Old.Contains);
    }

    [Fact]
    public void The_platforms_renderer_and_the_11_look_in_it_name_no_engine_type_in_their_code()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var offenders = new List<string>();
        void Note(Type? t, string where)
        {
            if (t is not null && IsOld(t)) offenders.Add($"{where}: {t.FullName}");
        }
        var types = Renderer.GetTypes();
        // The 1.1 look is here: the world renderer, the sky, the shadows, the mirrors, the particles, the blobs, the decals,
        // the halos, the ring, the screen's effects, the 2D backend and its model inserts.
        foreach (var name in new[] { "WorldRenderer", "SkyRenderer", "SkySwap", "ShadowRenderer", "ReflectionPass", "ParticleRenderer", "LightBlobRenderer",
                                     "DecalRenderer", "HaloRenderer", "WalkTargetRenderer", "ScreenEffectsPass", "UiRenderer", "SceneInserts", "IModelInserts" })
            Assert.Contains(types, t => t.FullName == "Vitomir.Platform.Render.Classic." + name);
        foreach (var type in types)
        {
            foreach (var f in type.GetFields(all)) Note(f.FieldType, $"{type.Name}.{f.Name}");
            foreach (var p in type.GetProperties(all)) Note(p.PropertyType, $"{type.Name}.{p.Name}");
            foreach (var m in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (m is MethodInfo mi) Note(mi.ReturnType, $"{type.Name}.{m.Name}");
                foreach (var p in m.GetParameters()) Note(p.ParameterType, $"{type.Name}.{m.Name}");
                foreach (var local in m.GetMethodBody()?.LocalVariables ?? []) Note(local.LocalType, $"{type.Name}.{m.Name}");
                foreach (var (t, unresolved) in Named(m))
                {
                    if (unresolved is not null) offenders.Add($"{type.Name}.{m.Name}: names what does not load beside the renderer ({unresolved})");
                    Note(t, $"{type.Name}.{m.Name}");
                }
            }
        }
        Assert.True(offenders.Count == 0, "the platform's renderer names the old engine:\n" + string.Join("\n", offenders.Distinct().Take(40)));
    }
}
