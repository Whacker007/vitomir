// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// JDK 1.1 source-compatibility fixes applied at script-compile time.
//
// The game's scripts were compiled in 2000 by JDK 1.1 javac, which accepted some code a
// standards-conforming compiler rejects. The one case in the shipped Codex tree is a local
// variable read on a path where it may be unassigned (DisciplineEyesOfChaos: a switch without a
// default). The 1.1 bytecode reads an uninitialised slot on that path; the 1998 VM did not verify
// local classes, so it ran. Modern compilers, ECJ included, refuse to compile it.
//
// Fix: when the only compile errors are "may not have been initialized", give each such local its
// default value (null / 0 / false) in a *shadow copy* of the file (the player's file is never
// touched) and compile again. The only behaviour this changes is the unreachable-in-practice read
// of garbage, which is a code bug, not a functional one. Every fix is printed so it stays visible.
using System.Text;
using System.Text.RegularExpressions;

namespace Vitomir.Scripting;

public static class Compat
{
    static readonly Regex Header = new(@"^\s*\d+\.\s+ERROR in (?<file>.+?) \(at line (?<line>\d+)\)", RegexOptions.Multiline);
    static readonly Regex Uninit = new(@"The local variable (?<var>\w+) may not have been initialized");
    static readonly Dictionary<string, string> Defaults = new()
    {
        ["int"] = "0", ["short"] = "0", ["byte"] = "0", ["long"] = "0L", ["char"] = "'\\0'",
        ["float"] = "0.0f", ["double"] = "0.0", ["boolean"] = "false",
    };

    /// <summary>Parses ECJ's error log. If every error is an uninitialised-local error, patches the
    /// declarations into shadow copies under <paramref name="shadowDir"/>, swaps those paths into
    /// <paramref name="sources"/>, and returns a description of each fix. Returns an empty list
    /// when there is nothing it can (or should) fix.</summary>
    public static List<string> FixUninitializedLocals(string ecjErrors, List<string> sources, string shadowDir)
    {
        var found = new List<(string File, int Line, string Var)>();
        foreach (var block in ecjErrors.Split("----------"))
        {
            var h = Header.Match(block);
            if (!h.Success) continue;                                  // warnings, summary lines
            var u = Uninit.Match(block);
            if (!u.Success) return new List<string>();                 // some other error: don't guess
            found.Add((h.Groups["file"].Value.Trim(), int.Parse(h.Groups["line"].Value), u.Groups["var"].Value));
        }
        var done = new List<string>();
        var latin1 = Encoding.Latin1;
        foreach (var byFile in found.GroupBy(f => f.File))
        {
            var lines = File.ReadAllLines(byFile.Key, latin1);
            foreach (var v in byFile.GroupBy(f => f.Var))
            {
                int errLine = v.Min(x => x.Line);
                var decl = new Regex(@"\b(?<type>[A-Za-z_][\w\.]*(?:\s*\[\s*\])*)\s+" + Regex.Escape(v.Key) + @"\s*;");
                for (int i = Math.Min(errLine - 1, lines.Length - 1); i >= 0; i--)
                {
                    var m = decl.Match(lines[i]);
                    if (!m.Success) continue;
                    var type = m.Groups["type"].Value;
                    var dflt = Defaults.TryGetValue(type, out var d) ? d : "null";
                    lines[i] = lines[i][..m.Index] + $"{type} {v.Key} = {dflt};" + lines[i][(m.Index + m.Length)..];
                    done.Add($"initialised local '{v.Key}' ({type}) to {dflt} in {Path.GetFileName(byFile.Key)} line {i + 1}");
                    break;
                }
            }
            Directory.CreateDirectory(shadowDir);
            var shadow = Path.Combine(shadowDir, Path.GetFileName(byFile.Key));
            File.WriteAllLines(shadow, lines, latin1);
            int idx = sources.FindIndex(s => string.Equals(Path.GetFullPath(s), Path.GetFullPath(byFile.Key), StringComparison.OrdinalIgnoreCase));
            // ECJ names a file by its real path: through a linked folder (a scratch install of links) that is not the path it
            // was given, and the shadow went unused, so the tree did not compile (the stills ran with no scripts). The tree's
            // names are unique, so the one source of that name is the file.
            if (idx < 0 && sources.Count(s => Path.GetFileName(s).Equals(Path.GetFileName(byFile.Key), StringComparison.OrdinalIgnoreCase)) == 1)
                idx = sources.FindIndex(s => Path.GetFileName(s).Equals(Path.GetFileName(byFile.Key), StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) sources[idx] = Path.GetFullPath(shadow);
        }
        return done;
    }
}
