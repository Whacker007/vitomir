// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's own single-player saves, as the load screen offers them for import.
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// The saves 1.1 wrote into the install's `SaveGames` folder — `SP0` to `SP29`, each a folder with a
/// `!main.nsg` — as the load screen lists them for an import
/// (<see cref="Ui.MenuRequest.Import"/>). Nothing here writes: the folder is only listed, and a save's
/// description is its file's first 0x78 bytes, which is all 1.1's own load list reads.
/// </summary>
public sealed class OriginalSaves(string installRoot)
{
    /// <summary>
    /// The saves for a folder of game data: the install's own, or — for Vitomir's copy, which leaves the
    /// saves out — the install the copy was made from, while it is still there (`GameData.OriginalInstall`).
    /// </summary>
    public static OriginalSaves For(string gameDir) => new(Vfs.GameData.OriginalInstall(gameDir) ?? gameDir);

    /// <summary>`SAVE_MAXSPSLOTS` (1.1 asserts below it).</summary>
    public const int Slots = 30;

    /// <summary>The install's save folder.</summary>
    public string Folder { get; } = Path.Combine(installRoot, "SaveGames");

    /// <summary>Slot <paramref name="slot"/>'s folder (`SaveGames\SP4`).</summary>
    public string PathOf(int slot) => Path.Combine(Folder, $"SP{slot}");

    /// <summary>Every slot with a `!main.nsg`, in order, with the description it carries.</summary>
    public IReadOnlyList<(int Slot, string Description)> List()
    {
        var found = new List<(int, string)>();
        if (!Directory.Exists(Folder)) return found;
        for (int slot = 0; slot < Slots; slot++)
            if (Description(slot) is { } said) found.Add((slot, said));
        return found;
    }

    /// <summary>The description at the head of slot <paramref name="slot"/>'s `!main.nsg`, or null with none there.</summary>
    public string? Description(int slot)
    {
        string main = Path.Combine(PathOf(slot), "!main.nsg");
        if (!File.Exists(main)) return null;
        try
        {
            using var f = new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[0x78];
            int got = f.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return BinReader.CString(head.AsSpan(0, got));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Ui.FrontEnd.Unreadable; }
    }
}
