// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's own settings, kept in the user folder.
using System.Text.Json;

namespace Vitomir.Launcher;

/// <summary>
/// **The launcher's settings** (<c>&lt;user folder&gt;\Launcher\settings.json</c>): what the player set on its pages and expects it to
/// remember. Read at the launcher's start (a file that is not there or does not read: the defaults), written at each change through a
/// temporary file.
/// </summary>
public sealed record LauncherSettings
{
    /// <summary>The file's folder in the user folder, and its name.</summary>
    public const string FolderName = "Launcher", FileName = "settings.json";

    /// <summary>
    /// **Close the launcher when the game starts** (the Play page's checkbox; off by default): Play starts the game and the launcher exits,
    /// rather than hiding while the game runs and coming back when it ends.
    /// </summary>
    public bool CloseOnStart { get; init; }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>The settings' file under <paramref name="userData"/>.</summary>
    public static string PathIn(string userData) => Path.Combine(userData, FolderName, FileName);

    /// <summary>The settings kept under <paramref name="userData"/>, or the defaults.</summary>
    public static LauncherSettings Read(string userData)
    {
        try
        {
            string path = PathIn(userData);
            return File.Exists(path) ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    /// <summary>Writes them under <paramref name="userData"/>; answers why not, or null.</summary>
    public string? Write(string userData)
    {
        try
        {
            string path = PathIn(userData), temp = path + ".writing";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }
}
