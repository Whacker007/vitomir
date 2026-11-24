// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Finding the player's own FFmpeg.
//
// The game's films are Bink. Vitomir does not decode Bink and does not
// ship anything that does: it asks for an `ffmpeg` the player already has, and **when there is none
// it plays no film at all** rather than half-working, by design: *"requiring
// the player to have their own ffmpeg installed somehow. If the player doesn't have it, then have
// the engine fail closed and just skip the video."*
//
// Shipping FFmpeg ourselves would have been allowed — its Bink decoders are LGPL 2.1, the same
// licence as the rest of a default build (`libavcodec/bink.c`; a build wants neither `--enable-gpl`
// nor `--enable-nonfree`) — but not distributing it at all costs nothing and owes nothing, and the
// repository already keeps every dependency out of the tree (`docs/THIRD_PARTY.md`).
namespace Vitomir.Engine.Video;

/// <summary>Where FFmpeg is, if it is anywhere.</summary>
/// <param name="FFmpeg">The decoder, which is the part that is required.</param>
/// <param name="FFprobe">The inspector, which is a convenience — the decoder can answer alone.</param>
/// <param name="Version">The first line of its banner, for the log.</param>
public sealed record FFmpegTool(string FFmpeg, string? FFprobe, string Version)
{
    /// <summary>The environment variable that names an ffmpeg, or the folder holding one.</summary>
    public const string Variable = Vitomir.Platform.Setup.FFmpegSearch.Variable;

    /// <summary>
    /// Looks for ffmpeg, in this order: what the caller was told to use (the `--ffmpeg` flag), then
    /// <see cref="Variable"/>, then the PATH, then the handful of places the Windows installers put
    /// it. Either of the first two may name the program itself or the folder it is in. The search is the
    /// platform's (`Vitomir.Platform.Setup.FFmpegSearch`, the setup asks the same one).
    ///
    /// Answers null when there is none, and that is not an error — it is the whole point of the
    /// arrangement. Nothing here throws.
    /// </summary>
    public static FFmpegTool? Find(string? told = null) => Find(told, InstallerFolders());

    /// <summary>
    /// The same search with the installers' folders given by the caller, in the order to try them: what
    /// <see cref="Find(string?)"/> does with <see cref="InstallerFolders"/>. A test that wants a machine
    /// with no FFmpeg passes none, since emptying the PATH does not hide one winget installed.
    /// </summary>
    public static FFmpegTool? Find(string? told, IEnumerable<string> installerFolders) =>
        Vitomir.Platform.Setup.FFmpegSearch.Find(told, installerFolders) is { } found ? new FFmpegTool(found.FFmpeg, found.FFprobe, found.Version) : null;

    /// <summary>
    /// Where winget, choco and scoop leave it on Windows, and where a person who unzipped it usually does
    /// (it may be in a folder itself or in its `bin`). None anywhere else: there the PATH is the answer.
    /// </summary>
    public static IReadOnlyList<string> InstallerFolders() => Vitomir.Platform.Setup.FFmpegSearch.InstallerFolders();

    /// <summary>What to tell the player when there is none. One line, and not an apology.</summary>
    public const string Absent =
        "video: no ffmpeg found, so the films will be skipped — install ffmpeg, or point " +
        Variable + " at it, to see them";
}
