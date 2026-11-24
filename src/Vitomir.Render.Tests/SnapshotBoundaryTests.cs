// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 3D world's renderer reads the Classic snapshot and nothing of the engine.
using System.Reflection;

namespace Vitomir.Render.Tests;

/// <summary>
/// **Where the line falls** (in <c>Vitomir.Platform.Render.Classic</c>, a comparison with 1.1
/// and <c>ClassicBoundaryTests</c> there checks its built code): the 1.1 renderer's 3D world — the world renderer, the
/// sky, the shadows, the mirrors, the particles, the light blobs, the decals, the halos, the click ring, the brightness
/// curve and the screen's fade and flash, and the frame's own drawer and the screen's (<see cref="ClassicFrameRenderer"/>,
/// <see cref="ClassicScreenRenderer"/>, the glue's drawing half) and the developer overlay's — holds no engine object and names no engine
/// type; it reads the Classic snapshot (Vitomir.Platform's). What fills the snapshot and holds the place is the module's
/// (<see cref="PlaceFrame"/>); the interface's side is <see cref="UiBoundaryTests"/>'.
/// No GPU needed.
/// </summary>
public class SnapshotBoundaryTests
{
    /// <summary>The 3D world's drawing types.</summary>
    static readonly Type[] World =
    [
        typeof(WorldRenderer), typeof(SkyRenderer), typeof(ShadowRenderer), typeof(ReflectionPass), typeof(ParticleRenderer),
        typeof(LightBlobRenderer), typeof(DecalRenderer), typeof(HaloRenderer), typeof(WalkTargetRenderer), typeof(Presenter),
        typeof(ScreenEffectsPass),                                       // the screen's fade and flash
        // The glue's drawing half, the screen with no world, the developer overlay, what goes over a frame, the root.
        typeof(ClassicFrameRenderer), typeof(ClassicScreenRenderer), typeof(DeveloperOverlay), typeof(ClassicOver), typeof(ClassicRenderer),
    ];

    /// <summary>Their source files (the ring's glue constructor, WalkTargetRenderer.Engine.cs, is gone).</summary>
    static readonly string[] Files =
    [
        "WorldRenderer.cs", "WorldRenderer.Translucent.cs", "WorldRenderer.Modern.cs", "WorldRenderer.Particles.cs",
        "WorldRenderer.Films.cs", "SkyRenderer.cs", "SkySwap.cs", "ShadowRenderer.cs", "ReflectionPass.cs", "ParticleRenderer.cs",
        "LightBlobRenderer.cs", "DecalRenderer.cs", "HaloRenderer.cs", "WalkTargetRenderer.cs", "ScreenEffectsPass.cs",
        "ClassicFrameRenderer.cs", "ClassicScreenRenderer.cs", "DeveloperOverlay.cs", "OverlayFont.cs", "ClassicOver.cs", "ClassicRenderer.cs",
        "IClassicRenderer.cs",
        Path.Combine("..", "Device", "Presenter.cs"),                     // 1.1's present, merged into the platform's presenter
    ];

    static bool Old(Type? t)
    {
        if (t is null) return false;
        if (t.HasElementType) return Old(t.GetElementType());
        if (t.IsGenericType && t.GetGenericArguments().Any(Old)) return true;
        var name = t.Assembly.GetName().Name ?? "";
        return name is "Vitomir.Engine" or "Vitomir.Scripting" or "Vitomir.Formats";
    }

    [Fact]
    public void The_worlds_renderers_hold_and_take_no_engine_type()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var offenders = new List<string>();
        foreach (var type in World)
        {
            foreach (var f in type.GetFields(all)) if (Old(f.FieldType)) offenders.Add($"{type.Name}.{f.Name}: {f.FieldType}");
            foreach (var p in type.GetProperties(all)) if (Old(p.PropertyType)) offenders.Add($"{type.Name}.{p.Name}: {p.PropertyType}");
            foreach (var m in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (m is MethodInfo mi && Old(mi.ReturnType)) offenders.Add($"{type.Name}.{m.Name} returns {mi.ReturnType}");
                foreach (var p in m.GetParameters()) if (Old(p.ParameterType)) offenders.Add($"{type.Name}.{m.Name}({p.ParameterType} {p.Name})");
            }
            foreach (var nested in type.GetNestedTypes(all))
                foreach (var f in nested.GetFields(all)) if (Old(f.FieldType)) offenders.Add($"{type.Name}.{nested.Name}.{f.Name}: {f.FieldType}");
        }
        Assert.True(offenders.Count == 0, "the 3D world's renderers reach the engine:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_worlds_renderers_sources_name_no_engine_namespace()
    {
        var root = Root();
        Assert.SkipWhen(root is null, "not running from inside the tree");
        var offenders = new List<string>();
        foreach (var name in Files)
        {
            var path = Path.Combine(root!, name);
            Assert.True(File.Exists(path), $"no {path}");
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                var code = comment >= 0 ? line[..comment] : line;
                if (code.Contains("Vitomir.Engine", StringComparison.Ordinal) || code.Contains("Vitomir.Formats", StringComparison.Ordinal)
                    || code.Contains("Vitomir.Scripting", StringComparison.Ordinal) || code.Contains("Engine.", StringComparison.Ordinal))
                    offenders.Add($"{name}:{i + 1}: {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "the 3D world's renderers name the engine:\n" + string.Join("\n", offenders));
    }

    static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            // The 1.1 look's sources: Vitomir.Platform.Render/Classic.
            if (File.Exists(Path.Combine(dir.FullName, "Vitomir.Platform.Render", "Vitomir.Platform.Render.csproj")))
                return Path.Combine(dir.FullName, "Vitomir.Platform.Render", "Classic");
        return null;
    }
}
