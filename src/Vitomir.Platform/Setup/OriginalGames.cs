// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's own saves, network games and multiplayer characters in an install, and their import into the Classic module's folders.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Vitomir.Formats;

namespace Vitomir.Platform.Setup;

/// <summary>What one of 1.1's own things is: a single-player save, a network game's save, or a multiplayer character.</summary>
public enum OriginalKind { Save, NetworkSave, Character }

/// <summary>
/// One of 1.1's own things in an install: its kind, its name as 1.1 has it (<c>SP4</c>, <c>MP0</c>, <c>npf000001</c>), what it says of
/// itself (a save's description, the one 1.1's load list shows; a character's name and clan, as 1.1's select list shows them, and its
/// experience), when it was written, where it is, and the file it is imported as in the Classic module's folder (a save's slot in
/// <c>Saves</c>, <c>SP4.vsave</c>; a character's file in <c>Crypt</c>, <c>Anna.vplayer</c>; null for one that cannot be imported, with
/// <see cref="Why"/>). A character's <see cref="Name"/> is the one it is kept by.
/// </summary>
public sealed record OriginalItem(OriginalKind Kind, string Id, string Description, DateTime WrittenUtc, string Path, string? ImportsAs,
                                  string? Why = null, string? Name = null);

/// <summary>
/// **1.1's own saves and characters**. Where 1.1 keeps them, each beside the game: the single-player saves in
/// <c>SaveGames\SP0</c> to <c>SP29</c> (the name built from the slot, held under 30),
/// the network games' saves in <c>SaveGames\MP&lt;n&gt;</c> (the same builder while a network session is up;
/// Load Saved), and the multiplayer characters as <c>Crypt\npf%06d.npf</c>, one actor record each (the first free
/// number taken). A save's description is the first 0x78 bytes of its
/// <c>!main.nsg</c>, all 1.1's own load list reads. A character is listed by its header, as 1.1's select list lists it
/// (The magic <c>npf</c> and version 5, then the name and the clan's name; <see cref="Npf"/>), and read whole besides,
/// so one 1.1 would list and Vitomir cannot read is said before it is picked. Nothing here writes.
/// </summary>
public static class OriginalGames
{
    /// <summary>1.1's single-player slots (<c>SAVE_MAXSPSLOTS</c>).</summary>
    public const int Slots = 30;

    /// <summary>Everything of 1.1's own in <paramref name="install"/>: the saves by slot, the network games' by slot, then the characters by name.</summary>
    public static IReadOnlyList<OriginalItem> List(string install)
    {
        var items = new List<OriginalItem>();
        string saves = Path.Combine(install, "SaveGames");
        if (Directory.Exists(saves))
            foreach (var (kind, prefix) in new[] { (OriginalKind.Save, "SP"), (OriginalKind.NetworkSave, "MP") })
                foreach (var (slot, dir) in Directory.EnumerateDirectories(saves)
                             .Select(d => (Match: Regex.Match(System.IO.Path.GetFileName(d), "^" + prefix + "([0-9]{1,2})$", RegexOptions.IgnoreCase), Dir: d))
                             .Where(m => m.Match.Success).Select(m => (int.Parse(m.Match.Groups[1].Value, CultureInfo.InvariantCulture), m.Dir))
                             .Where(s => s.Item1 < Slots).OrderBy(s => s.Item1))
                {
                    string? main = InstallFinder.FileIn(dir, "!main.nsg");
                    if (main is null) continue;
                    items.Add(new(kind, prefix + slot.ToString(CultureInfo.InvariantCulture), Description(main), File.GetLastWriteTimeUtc(main), dir,
                                  prefix + slot.ToString(CultureInfo.InvariantCulture) + ".vsave"));
                }
        string crypt = Path.Combine(install, "Crypt");
        if (Directory.Exists(crypt))
            foreach (var npf in Directory.EnumerateFiles(crypt).Where(f => f.EndsWith(".npf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
                items.Add(Character(npf));
        return items;
    }

    /// <summary>
    /// One character file as the list has it: its name and clan's name from the header (1.1's row, <c>"%d. %s (%s)"</c>) and
    /// its experience; the file it is imported as, <c>&lt;name&gt;.vplayer</c> (as <c>Net.Crypt</c> names one), or why it is not: the header
    /// fails 1.1's own check, or the character past it does not read.
    /// </summary>
    public static OriginalItem Character(string npf)
    {
        string id = System.IO.Path.GetFileNameWithoutExtension(npf);
        DateTime when = File.GetLastWriteTimeUtc(npf);
        string? why = null;
        NpfHeader? header = null;
        string name = "";
        try
        {
            header = Npf.ReadHeaderFile(npf);
            if (header is null) why = $"it is shorter than the {Npf.HeaderSize}-byte header 1.1's select list reads: 1.1 does not list it";
            else if (header.Refusal is { } refused) why = $"1.1's own select list leaves it out: {refused}";
            else
            {
                var whole = Npf.ReadFile(npf);
                name = CleanName(whole.Player.Text3C4.Length > 0 ? whole.Player.Text3C4 : header.Name);
                if (name.Length == 0) name = header.Login.Length > 0 ? header.Login : id;
            }
        }
        catch (NodFormatException e) { why = $"its header is 1.1's, but the character after it cannot be read ({e.Message})"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = $"it cannot be read ({e.Message})"; }
        string description = header is { Refusal: null }
            ? $"{header.Name} ({header.ClanName}), {header.Experience.ToString("N0", CultureInfo.InvariantCulture)} experience"
            : "a file in 1.1's Crypt that is not a character 1.1 reads";
        return why is null ? new(OriginalKind.Character, id, description, when, npf, CharacterFile(name), null, name)
                           : new(OriginalKind.Character, id, description, when, npf, null, why);
    }

    /// <summary>The longest name a character has (1.1's name box, 0x20; <c>NetCharacter.NameLimit</c>).</summary>
    public const int NameLimit = 32;

    /// <summary>A name as a character keeps it: trimmed, at most <see cref="NameLimit"/> characters (<c>NetCharacter.CleanName</c>).</summary>
    public static string CleanName(string name)
    {
        name = name.Trim();
        return name.Length > NameLimit ? name[..NameLimit] : name;
    }

    /// <summary>The file a character of that name is kept as in a Crypt folder: <c>&lt;name&gt;.vplayer</c>, as <c>Net.Crypt</c> names it.</summary>
    public static string CharacterFile(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + CharacterExtension;

    /// <summary>A Vitomir character's file (<c>Net.Crypt.Extension</c>).</summary>
    public const string CharacterExtension = ".vplayer";

    /// <summary>The description at the head of a <c>!main.nsg</c> (its first 0x78 bytes, up to the first zero).</summary>
    public static string Description(string main)
    {
        try
        {
            using var f = new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[0x78];
            int got = f.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return BinReader.CString(head.AsSpan(0, got));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "(it cannot be read)"; }
    }

}

/// <summary>What one import of one of 1.1's things did: imported, refused (and why), or failed; the import's own lines.</summary>
public sealed record SaveImportResult(OriginalItem Item, string? Target, bool Imported, bool Refused, string Why, IReadOnlyList<string> Lines);

/// <summary>
/// An import by the game's own code: 1.1's save folder made into a Vitomir save at a path, or 1.1's character file into a Vitomir
/// character (the seam a test may fake).
/// </summary>
public interface ISaveImporter
{
    /// <summary>Imports <paramref name="source"/> (a save's folder, or a <c>.npf</c>) as <paramref name="target"/> with the game's files at <paramref name="dataFolder"/>; the exit code and its lines.</summary>
    (int Code, IReadOnlyList<string> Lines) Import(string source, string target, string dataFolder, CancellationToken cancel);

    /// <summary>A character imported under another name than its own (<paramref name="name"/>; null: its own).</summary>
    (int Code, IReadOnlyList<string> Lines) Import(string source, string target, string dataFolder, string? name, CancellationToken cancel) =>
        Import(source, target, dataFolder, cancel);
}

/// <summary>
/// **The existing save import, as the Classic game runs it** (<c>SaveImport</c>): the
/// platform's program started as <c>--game classic --developer --oneone &lt;data&gt; --import &lt;slot&gt; --import-to &lt;file&gt;</c>, one
/// process an import (one Classic a process: the engine's statics), the user data the setup's. It writes the file and stops; nothing is
/// written into 1.1's folder. A character is the same line with the <c>.npf</c> in the slot's place (<c>OriginalCharacter</c>), and
/// <c>--import-name &lt;name&gt;</c> for another name.
/// </summary>
public sealed class ProgramSaveImporter(string program, string userData) : ISaveImporter
{
    public (int Code, IReadOnlyList<string> Lines) Import(string source, string target, string dataFolder, CancellationToken cancel) =>
        Import(source, target, dataFolder, null, cancel);

    public (int Code, IReadOnlyList<string> Lines) Import(string source, string target, string dataFolder, string? name, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        string log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vitomir-setup-import-{Guid.NewGuid():N}.log");
        foreach (var a in new[] { "--game", "classic", "--developer", "--oneone", dataFolder, "--import", source, "--import-to", target, "--log-file", log })
            start.ArgumentList.Add(a);
        if (name is not null) { start.ArgumentList.Add("--import-name"); start.ArgumentList.Add(name); }
        start.Environment["VITOMIR_USERDATA"] = userData;
        var lines = new List<string>();
        try
        {
            using var p = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started");
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            p.BeginErrorReadLine();
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null) lock (lines) lines.Add(line);
            while (!p.WaitForExit(200))
                if (cancel.IsCancellationRequested) { p.Kill(entireProcessTree: true); cancel.ThrowIfCancellationRequested(); }
            p.WaitForExit();
            lock (lines) return (p.ExitCode, [.. lines]);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            lock (lines) { lines.Add($"the import could not be run: {e.Message}"); return (-1, [.. lines]); }
        }
        finally { try { File.Delete(log); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
}
