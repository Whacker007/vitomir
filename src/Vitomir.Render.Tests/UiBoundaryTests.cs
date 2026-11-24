// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The interface's 2D backend reads the draw list and nothing of the engine.
using System.Reflection;
using Vitomir.Engine.Ui;

namespace Vitomir.Render.Tests;

/// <summary>
/// **Where the line falls for the interface**: the 2D backend (<see cref="UiRenderer"/>)
/// and the model inserts' drawer (<see cref="SceneInserts"/>) hold no engine object and name no engine type; they read the
/// platform's DrawList, its resource channel and the Classic snapshot's scenes. The interface passes are the engine's
/// (Engine.Ui, <see cref="IUiPass"/>); what is left of them outside the engine is the module's layer that hands a pass's list to
/// the backend (<see cref="ListLayer"/>, the old glue's <c>ListPass</c> until then). No GPU needed.
/// </summary>
public class UiBoundaryTests
{
    static readonly Type[] Backend = [typeof(UiRenderer), typeof(SceneInserts), typeof(IModelInserts)];

    static readonly string[] Files = ["UiRenderer.cs", "SceneInserts.cs"];

    /// <summary>The interface passes the old program draws, every one moved into the engine as an emitter.</summary>
    static readonly Type[] Passes =
    [
        typeof(StatusBarPass), typeof(HeadsPass), typeof(AdvancementBarPass), typeof(EffectIconsPass), typeof(FeedBarPass),
        typeof(CharacterPass), typeof(QuestPass), typeof(BuySellPass), typeof(InventoryPass), typeof(DisciplinePass),
        typeof(ConversationPass), typeof(CaptionPass), typeof(ReadPass), typeof(RolloverPass), typeof(NetPass), typeof(MenuPass),
        typeof(CursorPass), typeof(VideoPass), typeof(PortraitPass),
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
    public void The_2D_backend_holds_and_takes_no_engine_type()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var offenders = new List<string>();
        foreach (var type in Backend)
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
        Assert.True(offenders.Count == 0, "the 2D backend reaches the engine:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_2D_backends_sources_name_no_engine_namespace()
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
        Assert.True(offenders.Count == 0, "the 2D backend names the engine:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Every_interface_pass_is_an_engine_emitter_and_none_is_left_in_the_renderer()
    {
        foreach (var pass in Passes)
        {
            Assert.Equal("Vitomir.Engine", pass.Assembly.GetName().Name);
            Assert.True(typeof(IUiPass).IsAssignableFrom(pass), $"{pass.Name} is not an IUiPass");
        }
        // The layers over a frame (the module's, the old glue's passes until then) say the screen's effects and the
        // overlay: none takes the interface's canvas or its assets but the one that hands a pass's list to the backend.
        var left = typeof(IFrameLayer).Assembly.GetTypes()
            .Where(t => typeof(IFrameLayer).IsAssignableFrom(t) && t != typeof(ListLayer))
            .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(UiCanvas) || p.ParameterType == typeof(UiAssets)))
            .Select(t => t.Name).ToList();
        Assert.True(left.Count == 0, "interface passes left in the layers: " + string.Join(", ", left));
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
