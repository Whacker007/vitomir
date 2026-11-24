// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The menus' thirty save slots as files: `SP0.vsave` (the autosave) to `SP29.vsave`, named after 1.1's
// own `SaveGames\SP<n>` folders, in the player's save folder beside their settings and logs. A network
// game's are `MP0.vsave` … — 1.1 keeps them apart, `SaveGames\MP<n>`, whenever a network session is up
// (the family picked by whether a network session is up).
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Save;

/// <summary>The save slots the load and save screens list, one file each.</summary>
public sealed class SaveSlotFiles(string folder, string family = SaveSlotFiles.Single) : ISaveSlots
{
    /// <summary>The single-player family, `SP`, and the network games', `MP`.</summary>
    public const string Single = "SP", Network = "MP";

    public string Folder { get; } = folder;
    public string Family { get; } = family;

    /// <summary>Slot <paramref name="slot"/>'s file (`SP3.vsave`, or `MP3.vsave` for a network game).</summary>
    public string PathOf(int slot) => System.IO.Path.Combine(Folder, $"{Family}{slot}{SaveArchive.Extension}");

    public bool Exists(int slot) => File.Exists(PathOf(slot)) || Directory.Exists(PathOf(slot));

    /// <summary>The description a save's header carries — what the player named it — or null when it will not open.</summary>
    public string? Description(int slot)
    {
        try { return SaveArchive.ReadHeader(PathOf(slot)).Description; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException or NotSupportedException) { return null; }
    }
}
