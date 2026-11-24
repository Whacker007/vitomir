// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The launcher's words** (ours): every text the launcher can show (its screen file's texts, and the string literals of the code that
/// makes its lines and notices, the setup's, the modules' and the movies') read for phrasings a player finds stiff or wrong, each with
/// the word to say instead. Log lines and comments are not the player's and are left alone. A new awkward phrasing is added here once,
/// and never comes back.
/// </summary>
public sealed class LauncherWordingTests
{
    /// <summary>A phrasing to avoid (case-insensitive), and what to say instead.</summary>
    static readonly (string Avoid, string Say)[] Rules =
    [
        (@"\bfetch(ed|es|ing)?\b", "download"),
        (@"\bmend(ed|s)?\b", "fix"),
        (@"\bmodules on\b", "the enabled modules"),
        (@"\bwhile\b[^.:;""]*\b(is|are) (imported|checked|moved|verified|repaired|made|measured)\b", "while ... is being ..."),
        (@"\bwhile the game runs\b", "while the game is running"),
        (@"\b(does|do) not read\b", "cannot be read"),
        (@"\bcarry on\b", "continue"),
        (@"\bcould not be kept\b", "could not be saved"),
        (@"\bthrown away\b", "discarded"),
    ];

    [Fact]
    public void No_text_the_launcher_shows_uses_a_phrasing_the_player_finds_stiff()
    {
        string src = PlatformBoundaryTests.SourceRoot("");
        Assert.True(Directory.Exists(src), "the sources are not beside the tests");
        var files = new[] { "Vitomir.Launcher", Path.Combine("Vitomir.Platform", "Setup"), Path.Combine("Vitomir.Platform", "Films"), Path.Combine("Vitomir.Platform", "PlayerModules") }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(src, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith("launcher.json", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var literal = new Regex(@"""((?:[^""\\\n]|\\.)*)""");
        var hits = new List<string>();
        foreach (var file in files)
        {
            int n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || line.Contains("say(", StringComparison.Ordinal) || line.Contains("Log(", StringComparison.Ordinal)) continue;
                foreach (Match m in literal.Matches(line))
                {
                    string text = Regex.Replace(m.Groups[1].Value, @"\{[^{}]*\}", " ");   // an interpolation's holes are code, not words
                    foreach (var (avoid, say) in Rules)
                        if (Regex.IsMatch(text, avoid, RegexOptions.IgnoreCase))
                            hits.Add($"{Path.GetRelativePath(src, file)}:{n}: \"{m.Groups[1].Value}\" (say \"{say}\")");
                }
            }
        }
        Assert.True(hits.Count == 0, "Stiff phrasings in the launcher's words:\n" + string.Join("\n", hits));
    }
}
