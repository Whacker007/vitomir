// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Finding the player's own FFmpeg (moved here from Vitomir.Engine/Video/FFmpegTool.cs, so the setup asks the same search the
// game does; the engine's FFmpegTool.Find calls this).
//
// The game's films are Bink. Vitomir does not decode Bink and does not ship anything that does: it asks
// for an `ffmpeg` the player already has, and **when there is none it plays no film at all** rather than half-working: the
// player has their own ffmpeg installed somehow, and if the player does not have it, the engine
// fails closed and skips the video.
using System.Diagnostics;

namespace Vitomir.Platform.Setup;

/// <summary>An FFmpeg found: the decoder, its inspector beside it (a convenience), and the first line of its banner.</summary>
public sealed record FFmpegFound(string FFmpeg, string? FFprobe, string Version);

/// <summary>**Where the player's FFmpeg is**, as the game looks for it.</summary>
public static class FFmpegSearch
{
    /// <summary>The environment variable that names an ffmpeg, or the folder holding one.</summary>
    public const string Variable = "VITOMIR_FFMPEG";

    /// <summary>winget's package of FFmpeg (Gyan's build, the one FFmpeg's own site links for Windows), as the setup offers it.</summary>
    public const string WingetPackage = "Gyan.FFmpeg";

    /// <summary>The line the setup offers (and runs only when asked, <c>--install-ffmpeg</c>).</summary>
    public const string WingetLine = "winget install --exact --id " + WingetPackage;

    /// <summary>What is said when there is none: the films are skipped.</summary>
    public const string Skipped = "no ffmpeg found, so the films will be skipped (Vitomir never ships one; the game plays on without them)";

    /// <summary>
    /// Looks for ffmpeg, in this order: what the caller was told to use (the <c>--ffmpeg</c> flag), then <see cref="Variable"/>, then the
    /// PATH, then the places the Windows installers put it (<see cref="InstallerFolders"/>). Either of the first two may name the program
    /// itself or the folder it is in. Each candidate is run (<c>-version</c>) before it is believed. Null when there is none; nothing throws.
    /// </summary>
    public static FFmpegFound? Find(string? told = null) => Find(told, InstallerFolders());

    /// <summary>The same search with the installers' folders given (a test that wants a machine with no FFmpeg passes none).</summary>
    public static FFmpegFound? Find(string? told, IEnumerable<string> installerFolders)
    {
        foreach (var candidate in Candidates(told, installerFolders))
        {
            if (candidate is null || !File.Exists(candidate)) continue;
            if (Banner(candidate) is not { } version) continue;
            return new FFmpegFound(candidate, Beside(candidate, "ffprobe"), version);
        }
        return null;
    }

    /// <summary>
    /// Where winget, choco and scoop leave it on Windows, and where a person who unzipped it usually does (it may be in a folder itself or
    /// in its <c>bin</c>). None anywhere else: there the PATH is the answer.
    /// </summary>
    public static IReadOnlyList<string> InstallerFolders()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return [@"C:\ffmpeg", Path.Combine(programs, "ffmpeg"), @"C:\ProgramData\chocolatey",
                Path.Combine(local, "Microsoft", "WinGet", "Links"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims")];
    }

    static IEnumerable<string?> Candidates(string? told, IEnumerable<string> installerFolders)
    {
        foreach (var named in new[] { told, Environment.GetEnvironmentVariable(Variable) })
        {
            if (string.IsNullOrWhiteSpace(named)) continue;
            yield return named;                                   // the program itself
            yield return Path.Combine(named, Exe("ffmpeg"));      // or the folder it is in
            yield return Path.Combine(named, "bin", Exe("ffmpeg"));
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir))
                yield return Path.Combine(dir.Trim('"'), Exe("ffmpeg"));
        foreach (var root in installerFolders)
        {
            yield return Path.Combine(root, Exe("ffmpeg"));
            yield return Path.Combine(root, "bin", Exe("ffmpeg"));
        }
    }

    static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    static string? Beside(string ffmpeg, string name)
    {
        var at = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? ".", Exe(name));
        return File.Exists(at) ? at : null;
    }

    /// <summary>Runs <c>-version</c> and answers its first line, or null if it will not run at all.</summary>
    static string? Banner(string ffmpeg)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(ffmpeg, "-hide_banner -version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (p is null) return null;
            string? first = p.StandardOutput.ReadLine();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch (InvalidOperationException) { } return null; }
            return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(first) ? first.Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// **Runs winget to install FFmpeg** (only when the player asks: <c>--install-ffmpeg</c>; never by a test). Windows only; its lines are
    /// handed to <paramref name="say"/>; the exit code is winget's (−1 when it could not be started, or on another system).
    /// </summary>
    public static int InstallWithWinget(Action<string> say)
    {
        if (!OperatingSystem.IsWindows()) { say("ffmpeg: winget is Windows' (install ffmpeg with this system's own package manager)"); return -1; }
        try
        {
            var start = new ProcessStartInfo("winget") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "install", "--exact", "--id", WingetPackage, "--accept-source-agreements", "--accept-package-agreements" }) start.ArgumentList.Add(a);
            using var p = Process.Start(start);
            if (p is null) { say("ffmpeg: winget could not be started"); return -1; }
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) say("winget: " + e.Data.Trim()); };
            p.BeginErrorReadLine();
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
                if (!string.IsNullOrWhiteSpace(line)) say("winget: " + line.Trim());
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            say($"ffmpeg: winget could not be started ({e.Message}): install it from the Microsoft Store (App Installer), or run {WingetLine} yourself");
            return -1;
        }
    }
}
