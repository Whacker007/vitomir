// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;
using Vitomir.Engine.Features;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The guard under one declaration**, as <see cref="LocomotionTagTests"/> guards the gait
/// Fixes. The Fix `soakedvoid` is declared presentation: its switch point (<c>CodexScenes.Soaked</c>) decides
/// which script methods the soak message calls — 1.1 looks `soaked` up by name and full signature (the callback
/// table has <c>soaked(IIFIFI)F</c>), so a handler declared returning `void` is never
/// called, and with the Fix on it is. A script could do anything, so the declaration is true only if **the
/// handlers the Fix calls do nothing the rules read** — which is a fact about the install's scripts, and this
/// reads it from their source text: the classes that declare a `void soaked` of that signature are exactly the
/// two the declaration names (<see cref="GameFeatures.SoakedVoidHandlers"/>), and the body of each does nothing
/// but set or end a shell on a character. A mod's class with such a handler would be run too, whatever it does;
/// the test reads the install it is given.
/// </summary>
public partial class SoakedHandlerTests
{
    /// <summary>A method named `soaked`, with its return type, its parameters and where its body opens.</summary>
    [GeneratedRegex(@"\b(?<returns>[A-Za-z_][\w\[\]]*)\s+soaked\s*\((?<params>[^)]*)\)\s*\{")]
    private static partial Regex Declaration();

    /// <summary>Block and line comments.</summary>
    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comments();

    /// <summary>Anything called: a name, dotted or not, before an opening parenthesis.</summary>
    [GeneratedRegex(@"(?<new>\bnew\s+)?(?<name>[A-Za-z_][\w.]*)\s*\(")]
    private static partial Regex Calls();

    /// <summary>An assignment: `=` that is no part of a comparison; and the compound ones, and `++` and `--`.</summary>
    [GeneratedRegex(@"(?<![=!<>])=(?!=)|\+\+|--|[-+*/%&|^]=")]
    private static partial Regex Assignments();

    /// <summary>The one assignment a body may make: a local wrapper round a character's GUID.</summary>
    [GeneratedRegex(@"\bCodexActor\s+(?<local>[A-Za-z_]\w*)\s*=\s*new\s+CodexActor\s*\(\s*[A-Za-z_]\w*\s*\)\s*;")]
    private static partial Regex Wrapper();

    static readonly string[] Signature = ["int", "int", "float", "int", "float", "int"];

    sealed record Handler(string Class, string Returns, string[] Parameters, string Body);

    static List<Handler> Handlers(string codex)
    {
        var found = new List<Handler>();
        foreach (var path in Directory.EnumerateFiles(codex, "*.java", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            string text = Comments().Replace(File.ReadAllText(path), " ");
            foreach (Match m in Declaration().Matches(text))
            {
                // The body: from the brace the declaration ends on to the one that closes it.
                int open = m.Index + m.Length - 1, depth = 0, at = open;
                for (; at < text.Length; at++)
                {
                    if (text[at] == '{') depth++;
                    else if (text[at] == '}' && --depth == 0) break;
                }
                string[] parameters = [.. m.Groups["params"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                                  .Select(p => p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0])];
                found.Add(new Handler(Path.GetFileNameWithoutExtension(path), m.Groups["returns"].Value, parameters, text[(open + 1)..at]));
            }
        }
        return found;
    }

    [Fact]
    public void The_only_void_soaked_handlers_are_the_two_the_declaration_names_and_each_only_sets_a_shell()
    {
        using var files = Install.Open();
        Assert.SkipUnless(Directory.Exists(files.CodexFolder), "the install has no script sources");
        int sources = Directory.EnumerateFiles(files.CodexFolder, "*.java", SearchOption.AllDirectories).Count();
        Assert.True(sources > 300, $"only {sources} script sources were read");

        var handlers = Handlers(files.CodexFolder);
        // Every `soaked` in the install has the engine's parameters, and none returns the engine's `float`: 1.1
        // sends the message to nobody, and the Fix sends it to exactly the `void` ones.
        Assert.All(handlers, h => Assert.Equal(Signature, h.Parameters));
        Assert.DoesNotContain(handlers, h => h.Returns == "float");
        Assert.Equal(GameFeatures.SoakedVoidHandlers.Order(), handlers.Where(h => h.Returns == "void").Select(h => h.Class).Order());
        Assert.Equal(GameFeatures.SoakedVoidHandlers.Length, handlers.Count);

        var more = new List<string>();
        foreach (var h in handlers)
        {
            // The one local: a wrapper round the character's GUID. Every other assignment would be a field's or a
            // variable's the rest of the script reads.
            var locals = Wrapper().Matches(h.Body).Select(m => m.Groups["local"].Value).ToHashSet();
            string rest = Wrapper().Replace(h.Body, " ");
            foreach (Match m in Assignments().Matches(rest)) more.Add($"{h.Class}.soaked assigns: …{Around(rest, m.Index)}…");
            // Everything called: `if`, and the shell's two natives on that local. `SetShell` and `EndShell` are
            // the model's shell and nothing else (`World/ModelFx.cs`).
            int shells = 0;
            foreach (Match m in Calls().Matches(rest))
            {
                string name = m.Groups["name"].Value;
                if (name == "if" && !m.Groups["new"].Success) continue;
                string[] parts = name.Split('.');
                if (!m.Groups["new"].Success && parts.Length == 2 && locals.Contains(parts[0]) && parts[1] is "SetShell" or "EndShell") { shells++; continue; }
                more.Add($"{h.Class}.soaked calls {(m.Groups["new"].Success ? "new " : "")}{name}");
            }
            foreach (string word in new[] { "return", "throw", "while", "for", "do", "switch", "try", "synchronized" })
                if (Regex.IsMatch(rest, $@"\b{word}\b")) more.Add($"{h.Class}.soaked has a `{word}`");
            Assert.True(shells > 0, $"{h.Class}.soaked sets no shell: the Fix's one line would be wrong");
        }
        Assert.True(more.Count == 0,
            "soakedvoid cannot be declared presentation: a `void soaked` handler of the install does more than set a shell:\n  " + string.Join("\n  ", more));

        // What the declaration then says.
        var fix = GameFeatures.Registry.Find("fixes", "soakedvoid")!;
        Assert.False(GameFeatures.Points.Soaked.Rules);
        Assert.False(GameFeatures.Data.ModelShell.RulesRead);
        Assert.Equal([GameFeatures.Points.Soaked], fix.Takes);
        Assert.Equal([GameFeatures.Data.ModelShell], fix.Changes);
        Assert.Equal(FeatureAxis.Presentation, fix.Axis);
        Assert.Equal(FeatureWhose.Host, fix.Whose);                       // the host runs the script
        TestContext.Current.SendDiagnosticMessage($"soaked handlers: {string.Join(", ", handlers.Select(h => $"{h.Returns} {h.Class}.soaked"))} of {sources} sources");

        static string Around(string text, int at) =>
            Regex.Replace(text[Math.Max(0, at - 30)..Math.Min(text.Length, at + 30)], @"\s+", " ");
    }

    /// <summary>
    /// The reading itself, on invented sources: a handler that does more is caught, by what it does — so a pass
    /// over the install means its two were read and found to do nothing else.
    /// </summary>
    [Fact]
    public void A_handler_that_does_more_than_set_a_shell_is_found()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vitomir-soaked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "EffectHarmless.java"), """
                public class EffectHarmless extends Codex {
                    private int who;
                    public void soaked(int guid, int causeGUID, float damageAmount, int damageType, float soakAmount, int captureID)
                    {
                        CodexActor me = new CodexActor(who);
                        // a comment that says me.SetHealth(0) changes nothing
                        if ((soakAmount > 0) && (soakAmount >= damageAmount)) { me.SetShell("white", 0x5000, 0, 1, 1, 1); }
                    }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "EffectMeddling.java"), """
                public class EffectMeddling extends Codex {
                    private int who, count;
                    public float soaked(int guid, int causeGUID, float damageAmount, int damageType, float soakAmount, int captureID)
                    {
                        CodexActor me = new CodexActor(who);
                        count = count + 1;
                        me.SetHealth(0);
                        return soakAmount * 2;
                    }
                }
                """);
            var handlers = Handlers(dir);
            Assert.Equal(["EffectHarmless", "EffectMeddling"], handlers.Select(h => h.Class));
            Assert.Equal(["void", "float"], handlers.Select(h => h.Returns));
            Assert.All(handlers, h => Assert.Equal(Signature, h.Parameters));
            var (harmless, meddling) = (handlers[0], handlers[1]);
            Assert.DoesNotContain("SetHealth", harmless.Body);                               // the comment is not the code
            Assert.Empty(Assignments().Matches(Wrapper().Replace(harmless.Body, " ")));
            Assert.Equal(["if", "me.SetShell"], Calls().Matches(Wrapper().Replace(harmless.Body, " ")).Select(m => m.Groups["name"].Value));
            Assert.Single(Assignments().Matches(Wrapper().Replace(meddling.Body, " ")));
            Assert.Contains("me.SetHealth", Calls().Matches(meddling.Body).Select(m => m.Groups["name"].Value));
        }
        finally { Directory.Delete(dir, true); }
    }
}
