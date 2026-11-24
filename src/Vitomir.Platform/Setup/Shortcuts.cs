// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace Vitomir.Platform.Setup;

/// <summary>Where a shortcut to Vitomir can be: the user's desktop or the user's Start menu (its Programs folder).</summary>
public enum ShortcutPlace { Desktop, StartMenu }

/// <summary>What a <c>.lnk</c> holds that Vitomir reads and writes: what it opens, the folder it opens in, and its icon.</summary>
public sealed record ShortcutLink(string Target, string WorkingFolder, string IconPath);

/// <summary>
/// **The shortcuts' hand on the machine**: the two folders and the <c>.lnk</c> files in them. Windows' is
/// <see cref="WindowsShortcutWriter"/> (the shell's own <c>IShellLinkW</c>, through COM); a test gives its own.
/// </summary>
public interface IShortcutWriter
{
    /// <summary>The folder a place's shortcut goes in, or null when the machine has none.</summary>
    string? Folder(ShortcutPlace place);
    /// <summary>The shortcut at <paramref name="path"/>, or null when there is none (or it does not read as one).</summary>
    ShortcutLink? Read(string path);
    /// <summary>Makes (or makes again) the shortcut at <paramref name="path"/>.</summary>
    void Write(string path, ShortcutLink link);
    /// <summary>Removes the file at <paramref name="path"/>.</summary>
    void Delete(string path);
}

/// <summary>How a place's shortcut stands.</summary>
public enum ShortcutState
{
    /// <summary>No <c>Vitomir.lnk</c> there.</summary>
    Off,
    /// <summary>Vitomir's, opening this Vitomir folder's launcher.</summary>
    On,
    /// <summary>Vitomir's, but opening a launcher in another folder (the Vitomir folder was moved): Update re-targets it.</summary>
    Moved,
    /// <summary>A <c>Vitomir.lnk</c> that does not open a Vitomir launcher: not Vitomir's, never touched.</summary>
    NotOurs,
    /// <summary>The machine has no such folder (or no shortcuts: not Windows).</summary>
    NoFolder,
}

/// <summary>A place's shortcut found on disk: its state, the file, and what it opens when there is one.</summary>
public sealed record ShortcutStatus(ShortcutPlace Place, ShortcutState State, string? Path, string? PointsAt);

/// <summary>
/// **The Desktop and Start menu shortcuts** (the launcher's Setup page and <c>--setup shortcut</c>; offered, never imposed: nothing
/// is made until the player asks, and only the user's own folders are written, so no administrator is needed). A shortcut is
/// <c>Vitomir.lnk</c> (shown as "Vitomir"), opening <c>vitomir-launcher.exe</c> at the top of the Vitomir folder, in that folder,
/// with the launcher's own icon. Off removes only a shortcut Vitomir made: one that opens a <c>vitomir-launcher.exe</c>.
/// </summary>
public sealed class Shortcuts(IShortcutWriter writer, string vitomirFolder)
{
    /// <summary>The shortcut's file name: the name the desktop and the Start menu show is "Vitomir".</summary>
    public const string FileName = "Vitomir.lnk";

    /// <summary>The program a shortcut opens.</summary>
    public const string LauncherName = "vitomir-launcher.exe";

    /// <summary>This machine's writer: Windows', else null (there are no shortcuts to make).</summary>
    public static IShortcutWriter? ThisMachine() => OperatingSystem.IsWindows() ? new WindowsShortcutWriter() : null;

    /// <summary>The launcher a shortcut of this Vitomir folder opens.</summary>
    public string Launcher => System.IO.Path.Combine(vitomirFolder, LauncherName);

    /// <summary>The place's word for the command line and the log: desktop or startmenu.</summary>
    public static string Word(ShortcutPlace place) => place == ShortcutPlace.Desktop ? "desktop" : "startmenu";

    /// <summary>The place's name for a person: the desktop, the Start menu.</summary>
    public static string Name(ShortcutPlace place) => place == ShortcutPlace.Desktop ? "the desktop" : "the Start menu";

    /// <summary>A place from its word (desktop, startmenu, start-menu, start), or null.</summary>
    public static ShortcutPlace? Parse(string word) => word.ToLowerInvariant() switch
    {
        "desktop" => ShortcutPlace.Desktop,
        "startmenu" or "start-menu" or "start" => ShortcutPlace.StartMenu,
        _ => null,
    };

    /// <summary>The place's shortcut as it is on disk now.</summary>
    public ShortcutStatus Status(ShortcutPlace place)
    {
        string? folder;
        try { folder = writer.Folder(place); }
        catch (Exception e) when (Expected(e)) { folder = null; }
        if (string.IsNullOrEmpty(folder)) return new(place, ShortcutState.NoFolder, null, null);
        string path = System.IO.Path.Combine(folder, FileName);
        ShortcutLink? link;
        try { link = writer.Read(path); }
        catch (Exception e) when (Expected(e)) { link = null; }
        if (link is null) return new(place, ShortcutState.Off, path, null);
        if (!Ours(link)) return new(place, ShortcutState.NotOurs, path, link.Target);
        return new(place, Same(link.Target, Launcher) ? ShortcutState.On : ShortcutState.Moved, path, link.Target);
    }

    /// <summary>
    /// **The switch turned**: on makes the shortcut (and re-targets one of Vitomir's that opens another folder); off removes it when
    /// it is Vitomir's. A shortcut named Vitomir that is not Vitomir's is left as it is, either way. Answers the state found after,
    /// and a line saying what was done.
    /// </summary>
    public (ShortcutStatus Status, string Line) Set(ShortcutPlace place, bool on)
    {
        var now = Status(place);
        string where = Name(place);
        switch (now.State)
        {
            case ShortcutState.NoFolder:
                return (now, $"There is no shortcut folder for {where} on this machine.");
            case ShortcutState.NotOurs:
                return (now, $"A shortcut named Vitomir on {where} opens {now.PointsAt}, not Vitomir: it was left as it is.");
            case ShortcutState.Off when !on:
                return (now, $"There is no Vitomir shortcut on {where}.");
            case ShortcutState.On when on:
                return (now, $"The Vitomir shortcut on {where} is there.");
            case ShortcutState.Off or ShortcutState.Moved when on:
                return Make(place, now);
            default:
                try { writer.Delete(now.Path!); }
                catch (Exception e) when (Expected(e)) { return (Status(place), $"The Vitomir shortcut on {where} could not be removed: {e.Message}"); }
                return (Status(place), $"The Vitomir shortcut on {where} was removed.");
        }
    }

    /// <summary>**Update**: a shortcut of Vitomir's that opens another folder pointed at this one.</summary>
    public (ShortcutStatus Status, string Line) Update(ShortcutPlace place)
    {
        var now = Status(place);
        return now.State == ShortcutState.Moved ? Make(place, now) : Set(place, now.State == ShortcutState.On);
    }

    (ShortcutStatus, string) Make(ShortcutPlace place, ShortcutStatus now)
    {
        string where = Name(place);
        try { writer.Write(now.Path!, new ShortcutLink(Launcher, vitomirFolder, Launcher)); }
        catch (Exception e) when (Expected(e)) { return (Status(place), $"The Vitomir shortcut on {where} could not be made: {e.Message}"); }
        var after = Status(place);
        return (after, now.State == ShortcutState.Moved ? $"The Vitomir shortcut on {where} now opens {Launcher} (it opened {now.PointsAt})."
                                                        : $"A Vitomir shortcut was made on {where}.");
    }

    /// <summary>A line for the Setup page's row (empty when nothing needs saying).</summary>
    public static string Note(ShortcutStatus s) => s.State switch
    {
        ShortcutState.Moved => $"It opens Vitomir where it was before: {s.PointsAt}.",
        ShortcutState.NotOurs => $"A shortcut named Vitomir is there that opens {s.PointsAt}: not Vitomir's, so it is left alone.",
        ShortcutState.NoFolder => "This machine has no such folder.",
        _ => "",
    };

    /// <summary>Vitomir made it: it opens a Vitomir launcher (here or wherever the Vitomir folder was).</summary>
    static bool Ours(ShortcutLink link) =>
        System.IO.Path.GetFileName(link.Target.Trim().Trim('"')).Equals(LauncherName, StringComparison.OrdinalIgnoreCase);

    static bool Same(string a, string b)
    {
        try { return System.IO.Path.GetFullPath(a.Trim().Trim('"')).Equals(System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or COMException or ArgumentException;
}

/// <summary>
/// **Windows' shortcuts**: the folders are the user's known folders — the Desktop (wherever it is redirected, OneDrive's included)
/// and Programs (the Start menu's Programs folder) — and a <c>.lnk</c> is read and written by the shell's
/// own ShellLink object (<c>IShellLinkW</c> and <c>IPersistFile</c>, through COM), as Explorer makes one: no script.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShortcutWriter : IShortcutWriter
{
    static readonly Guid DesktopFolder = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");   // FOLDERID_Desktop
    static readonly Guid ProgramsFolder = new("A77F5D77-2E2B-44C3-A6A2-ABA601054A51");  // FOLDERID_Programs

    public string? Folder(ShortcutPlace place)
    {
        var id = place == ShortcutPlace.Desktop ? DesktopFolder : ProgramsFolder;
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var text) != 0) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    public ShortcutLink? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);                       // STGM_READ
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4);    // SLGP_RAWPATH: as written, not resolved
            var folder = new StringBuilder(1024);
            link.GetWorkingDirectory(folder, folder.Capacity);
            var icon = new StringBuilder(1024);
            link.GetIconLocation(icon, icon.Capacity, out _);
            return new ShortcutLink(target.ToString(), folder.ToString(), icon.ToString());
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    public void Write(string path, ShortcutLink link)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var shell = (IShellLinkW)new ShellLink();
        try
        {
            shell.SetPath(link.Target);
            shell.SetWorkingDirectory(link.WorkingFolder);
            shell.SetIconLocation(link.IconPath, 0);
            shell.SetDescription("Vitomir");
            ((IPersistFile)shell).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    public void Delete(string path) => File.Delete(path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder folder, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relative, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
