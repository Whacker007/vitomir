// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the setup asks of the machine beyond its files: the registry, the drives, the free space (a seam, so tests fake it).
namespace Vitomir.Platform.Setup;

/// <summary>
/// **The machine, as the setup asks it**: what is not a file — the registry's values (GOG's and Steam's records of where they
/// installed things), the fixed drives (where the usual folders are looked for), an environment variable, the free space of a drive.
/// The files themselves are read for real; a test gives a fake of this with folders of its own (the cloud has no registry).
/// </summary>
public interface ISetupMachine
{
    /// <summary>A registry value as text (<c>HKEY_LOCAL_MACHINE\SOFTWARE\…</c>, the value's name), or null when there is none.</summary>
    string? RegistryValue(string key, string name);

    /// <summary>The roots of the fixed drives, in order (<c>C:\</c>, <c>D:\</c>, …).</summary>
    IReadOnlyList<string> FixedDrives();

    /// <summary>An environment variable, or null.</summary>
    string? Environment(string name);

    /// <summary>The bytes free on the drive that holds <paramref name="folder"/> (the drive of its root; the folder need not exist yet).</summary>
    long FreeBytes(string folder);
}

/// <summary>This machine: the real registry on Windows (none elsewhere), its fixed drives, its environment, its free space.</summary>
public sealed class ThisMachine : ISetupMachine
{
    public static readonly ThisMachine Instance = new();

    public string? RegistryValue(string key, string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return Microsoft.Win32.Registry.GetValue(key, name, null)?.ToString(); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
    }

    public IReadOnlyList<string> FixedDrives()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try { return [.. DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    public string? Environment(string name) => System.Environment.GetEnvironmentVariable(name);

    public long FreeBytes(string folder) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
}
