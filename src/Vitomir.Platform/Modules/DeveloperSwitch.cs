// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The players' switches and the developer's.
namespace Vitomir.Platform.Modules;

/// <summary>
/// **Which switches a game played by a module's own loop takes**: its command line is the module's (the program's own
/// switches aside), and the module's <c>module.json</c> declares the players' switches (<see cref="ModuleManifest.Switches"/>);
/// any other switch — a harness's, a developer's, a tool's — is taken only with <see cref="Flag"/> given, and without it the
/// program refuses the start plainly, naming the switch and <see cref="Flag"/> (<see cref="Refusal"/>). The developer switch
/// itself changes nothing in the game: the program takes it out of the module's command line. The enhanced game's command
/// line is the platform's own and is not gated (its module's switches are its rules' choices).
/// </summary>
public static class DeveloperSwitch
{
    /// <summary>The developer switch's name.</summary>
    public const string Name = "developer";

    /// <summary>The developer switch on the command line.</summary>
    public const string Flag = "--developer";

    /// <summary>
    /// The switches in <paramref name="args"/> (each word that starts with <c>--</c> and has a name after it) that
    /// <paramref name="declared"/> does not name, once each, in the order given. A value (a word after a switch that does not
    /// start with <c>--</c>) is not a switch.
    /// </summary>
    public static IReadOnlyList<string> Undeclared(IEnumerable<ModuleSwitch> declared, IEnumerable<string> args)
    {
        var names = new HashSet<string>(declared.Select(s => s.Name), StringComparer.Ordinal);
        var undeclared = new List<string>();
        foreach (var a in args)
        {
            if (a.Length <= 2 || !a.StartsWith("--", StringComparison.Ordinal)) continue;
            if (!names.Contains(a[2..]) && !undeclared.Contains(a)) undeclared.Add(a);
        }
        return undeclared;
    }

    /// <summary>The one line a refused start says: the switches, whose they are not, and the developer switch.</summary>
    public static string Refusal(string gameId, IReadOnlyList<string> undeclared) =>
        $"refused: {string.Join(", ", undeclared)} {(undeclared.Count == 1 ? "is not one of" : "are not among")} the game {gameId}'s "
        + $"players' switches (its module.json declares them; --check-modules lists them); a developer's switch needs {Flag}";
}
