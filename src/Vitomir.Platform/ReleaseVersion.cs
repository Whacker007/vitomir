// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;

namespace Vitomir.Platform;

/// <summary>
/// **The build's version line** (<c>src/Directory.Build.props</c>): the release version, one semantic number for the whole
/// build, and in a development build its development tag, in brackets — "Vitomir Platform 1.0.0", or
/// "Vitomir Platform 1.0.0 (tag)". Both are read once: the informational version from this assembly's attribute
/// (without a source revision after a <c>+</c>, which the build no longer adds), and the tag from <c>vitomir-batch.txt</c>
/// beside the assembly, which every build writes and only a development build fills (the tag is kept out of the assembly so
/// that it cannot change the join's build; the file is written empty in a public build so that a package unzipped over an
/// older one cannot keep the older one's tag: <c>Vitomir.Platform.csproj</c>).
/// </summary>
public static class ReleaseVersion
{
    /// <summary>The release version, "1.0.0"; "0.0.0" when the assembly says none.</summary>
    public static string Number { get; } = ReadNumber();

    /// <summary>The development tag of this build, or null in a build without one (the public build).</summary>
    public static string? Batch { get; } = ReadBatch();

    /// <summary>One word for the build, for a file's name: the development tag where there is one, else the release version.</summary>
    public static string Word => Batch ?? Number;

    /// <summary>The version line of <paramref name="program"/>: its name, the release version, and the development tag in brackets when there is one.</summary>
    public static string Line(string program) => Batch is { } batch ? $"{program} {Number} ({batch})" : $"{program} {Number}";

    static string ReadNumber()
    {
        var info = typeof(ReleaseVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "0.0.0";
        int plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? info[..plus] : info;
    }

    /// <summary>
    /// The build's notice for the player ("Private beta - please do not distribute"), or null: <c>VitomirNotice</c>, which only
    /// a package sets (<c>tools/package.ps1 -Notice</c>), in <see cref="NoticeFile"/> beside the assembly.
    /// </summary>
    public static string? Notice { get; } = ReadNotice(AssemblyFolder());

    /// <summary>The file beside the assembly that holds the notice: empty in a build without one.</summary>
    public const string NoticeFile = "vitomir-notice.txt";

    /// <summary>A version line as a player sees it: <paramref name="line"/>, then the build's notice after a dot when there is one.</summary>
    public static string Shown(string line) => Shown(line, Notice);

    internal static string Shown(string line, string? notice) => notice is { Length: > 0 } n ? $"{line} · {n}" : line;

    internal static string? ReadNotice(string folder)
    {
        try
        {
            string file = Path.Combine(folder, NoticeFile);
            string? text = File.Exists(file) ? File.ReadLines(file).FirstOrDefault()?.Trim().TrimStart('\uFEFF') : null;
            return text is { Length: > 0 } && !text.Any(char.IsControl) ? text : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static string AssemblyFolder()
    {
        string? dir = Path.GetDirectoryName(typeof(ReleaseVersion).Assembly.Location);
        return string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir;
    }

    /// <summary>The file beside the assembly that holds the development tag: empty in a build without one.</summary>
    public const string BatchFile = "vitomir-batch.txt";

    static string? ReadBatch()
    {
        string? dir = Path.GetDirectoryName(typeof(ReleaseVersion).Assembly.Location);
        string file = Path.Combine(string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir, BatchFile);
        try
        {
            string? tag = File.Exists(file) ? File.ReadLines(file).FirstOrDefault()?.Trim() : null;
            return tag is { Length: > 0 } && tag.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? tag : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
