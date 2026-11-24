// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A switch whose value is never written (the network game's --password).
namespace Vitomir.Platform.Modules;

/// <summary>
/// **The switches whose value is never written anywhere**: <c>--password</c>, the network game's password,
/// a host's or a joining player's. Wherever a program echoes its command line — the platform's log (<c>its command line: …</c>),
/// a tape's <c>arg</c> lines, a scenario's record — it echoes <see cref="Masked"/>'s, in which the value is <see cref="Mask"/>.
/// The run itself is given the real value.
/// </summary>
public static class SecretSwitches
{
    /// <summary>The switches whose value is secret.</summary>
    public static readonly IReadOnlyList<string> Names = ["--password"];

    /// <summary>What a secret value is written as.</summary>
    public const string Mask = "****";

    /// <summary>Whether <paramref name="arg"/> is a secret switch (or one written with its value, <c>--password=…</c>).</summary>
    public static bool IsSecret(string arg) =>
        Names.Any(n => arg == n || arg.StartsWith(n + "=", StringComparison.Ordinal));

    /// <summary>
    /// <paramref name="args"/> as they may be written: the word after a secret switch (when there is one and it is not itself a
    /// switch) and a value written with <c>=</c> are <see cref="Mask"/>; every other word as it is.
    /// </summary>
    public static string[] Masked(IEnumerable<string> args)
    {
        var all = args.ToArray();
        var shown = new string[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            string a = all[i];
            if (a.IndexOf('=') is var eq and > 0 && IsSecret(a)) shown[i] = a[..(eq + 1)] + Mask;
            else if (i > 0 && Names.Contains(all[i - 1]) && !a.StartsWith("--", StringComparison.Ordinal)) shown[i] = Mask;
            else shown[i] = a;
        }
        return shown;
    }

    /// <summary>The command line as one line to write: <see cref="Masked"/>'s words joined by spaces.</summary>
    public static string Line(IEnumerable<string> args) => string.Join(' ', Masked(args));
}
