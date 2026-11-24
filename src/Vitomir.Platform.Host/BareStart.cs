// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game started on its own (vitomir-platform.exe double-clicked, no command line) before Vitomir is set up: a message box
// that sends the player to the launcher.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Setup;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>The box a start with no command line shows when Vitomir is not set up: its title, its text, and whether the launcher can be opened.</summary>
public interface IStartDialog
{
    /// <summary>Shows it and waits; answers true for **Open the launcher**, false for **Close** (or the box closed).</summary>
    bool AskOpenLauncher(string title, string text, bool launcherFound);
}

/// <summary>
/// **A start with no command line** (<c>vitomir-platform.exe</c> double-clicked, the launcher not run): the readiness check
/// (<see cref="Readiness.ForGame"/>) first; when anything a start needs is not ready, a Windows message box,
/// <i>Vitomir isn't set up yet: open the launcher to set it up.</i>, naming what is not ready, with **Open the launcher** (it is
/// started, and this program ends) and **Close**; exit code 3. With a command line (a developer's, the launcher's own start, the tests)
/// there is no box: the start's own refusals and exit codes, as before.
/// </summary>
public static class BareStart
{
    /// <summary>The box's title.</summary>
    public const string Title = "Vitomir";

    /// <summary>The box's first line.</summary>
    public const string Lead = "Vitomir isn't set up yet: open the launcher to set it up.";

    /// <summary>The box's two answers.</summary>
    public const string OpenWord = "Open the launcher", CloseWord = "Close";

    /// <summary>
    /// The box's text split for a dialog that has a main line and a detail (Windows' TaskDialog): the main line is the text up to its first
    /// blank line (<see cref="Lead"/>), the detail the rest.
    /// </summary>
    public static (string Main, string Detail) Split(string text)
    {
        int at = text.IndexOf("\n\n", StringComparison.Ordinal);
        return at < 0 ? (text, "") : (text[..at], text[(at + 2)..]);
    }

    /// <summary>The box's text: <see cref="Lead"/>, then each item that stops the start, by its name and line.</summary>
    public static string Text(ReadinessReport report, bool launcherFound) =>
        Lead + "\n\n" + string.Join("\n", report.Items.Where(i => i.Stops && i.State == ReadyState.Failed).Select(i => $"{i.Name}: {SetupWords.Plain(i.Line)}"))
        + (launcherFound ? "" : "\n\nvitomir-launcher.exe is not beside this program: unzip Vitomir's whole zip again.");

    /// <summary>The launcher this program would open: <c>vitomir-launcher.exe</c> at the top of the Vitomir folder (the package's), else beside this program (a developer's build), or null.</summary>
    public static string? LauncherPath(string programFolder)
    {
        string name = OperatingSystem.IsWindows() ? "vitomir-launcher.exe" : "vitomir-launcher";
        foreach (var folder in new[] { UserFolderPlace.TopOf(programFolder), programFolder })
            if (Path.Combine(folder, name) is var path && File.Exists(path)) return path;
        return null;
    }

    /// <summary>
    /// **The check and the box**: null when every item a start needs is ready (the game goes on), else the exit code (3) after the box
    /// (<paramref name="dialog"/>) was answered; <paramref name="open"/> starts the launcher (answers why not, or null). Each step said.
    /// </summary>
    public static int? Run(ReadinessReport report, IStartDialog dialog, string? launcher, Func<string, string?> open, Action<string> say)
    {
        foreach (var item in report.Items) say($"start: {item.Name}: {item.Word}: {SetupWords.Plain(item.Line)}");
        if (report.Ready) return null;
        say($"start: with no command line, and Vitomir is not set up ({report.FirstStopping!.Name}): the box sends the player to the launcher");
        if (dialog.AskOpenLauncher(Title, Text(report, launcher is not null), launcher is not null) && launcher is not null)
            say(open(launcher) is { } why ? $"start: the launcher could not be started: {why}" : $"start: the launcher started ({launcher})");
        return SetupCommand.Exit.NotTheData;
    }

    /// <summary>
    /// **Why a run with a command line may not use the user folder**, or null: the user folder's readiness item failed (an ini with a
    /// problem, its folder or drive not there, Program Files, not writable). A first start's "no user folder yet" is not a refusal there:
    /// the usual place is used and made when first needed, as before (a developer's <c>--setup</c> on a new machine).
    /// </summary>
    public static string? CommandLineRefusal(UserFolderChoice choice, FolderPlaces places)
    {
        var item = Readiness.UserFolder(choice, places);
        return item.State == ReadyState.Failed && item.Fix != "choices" ? SetupWords.Plain(item.Line) : null;
    }

    /// <summary>This program's own check: its user folder's choice, this machine's places, a developer's <c>VTMR_GAME_DIR</c>, its modules beside it.</summary>
    public static ReadinessReport Check() =>
        Readiness.ForGame(UserData.Choice, FolderPlaces.ThisMachine(), Environment.GetEnvironmentVariable("VTMR_GAME_DIR"), ModuleLoader.ProgramModules, ModuleLoader.ProgramGames);

    /// <summary>Starts the launcher as a process of its own; null when started, else why not.</summary>
    public static string? Open(string launcher)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo(launcher) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launcher)! });
            return started is null ? "it did not start" : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return e.Message; }
    }
}

/// <summary>The box as SDL shows it (Windows' own message box): **Open the launcher** (Enter) and **Close** (Escape).</summary>
public sealed unsafe class SdlStartDialog : IStartDialog
{
    public static readonly SdlStartDialog Instance = new();

    public bool AskOpenLauncher(string title, string text, bool launcherFound)
    {
        var strings = new List<IntPtr>();
        byte* Utf8(string s) { var p = Marshal.StringToCoTaskMemUTF8(s); strings.Add(p); return (byte*)p; }
        try
        {
            var buttons = stackalloc SDL.SDL_MessageBoxButtonData[2];
            int count = 0;
            if (launcherFound)
                buttons[count++] = new SDL.SDL_MessageBoxButtonData { flags = SDL.SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT, buttonID = 1, text = Utf8(BareStart.OpenWord) };
            buttons[count++] = new SDL.SDL_MessageBoxButtonData
            {
                flags = SDL.SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT | (launcherFound ? 0 : SDL.SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT),
                buttonID = 0, text = Utf8(BareStart.CloseWord),
            };
            var data = new SDL.SDL_MessageBoxData
            {
                flags = SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION | SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_BUTTONS_LEFT_TO_RIGHT,
                title = Utf8(title), message = Utf8(text), numbuttons = count, buttons = buttons,
            };
            int chosen = 0;
            return SDL_ShowMessageBox(&data, &chosen) && chosen == 1;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
        finally
        {
            foreach (var p in strings) Marshal.FreeCoTaskMem(p);
        }
    }
}

/// <summary>
/// **The box as Windows' own TaskDialog** (comctl32 6's <c>TaskDialogIndirect</c>): SDL's message box sizes its buttons too narrowly on
/// Windows and cut their words ("pen the launche"); the TaskDialog sizes its buttons to their words. Its title <see cref="BareStart.Title"/>,
/// its main line <see cref="BareStart.Lead"/>, the detail the items not ready, the buttons **Open the launcher** (Enter, when the launcher is
/// there) and **Close** (Escape). comctl32 6 is reached through an activation context made for the call (the program has no manifest of
/// its own: shell32.dll's resource 124, else a manifest file written to the temporary folder and read at once). Anything that fails falls
/// back to SDL's box (<see cref="SdlStartDialog"/>), and elsewhere than Windows SDL's box is used outright.
/// </summary>
public sealed class WindowsStartDialog : IStartDialog
{
    public static readonly WindowsStartDialog Instance = new();

    /// <summary>The dialog used on this machine: this one on Windows, SDL's elsewhere.</summary>
    public static IStartDialog ForThisMachine => OperatingSystem.IsWindows() ? Instance : SdlStartDialog.Instance;

    const int OpenId = 1001, CloseId = 2;   // Close is IDCANCEL, so Escape and the title bar's close answer it

    public bool AskOpenLauncher(string title, string text, bool launcherFound)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Show(title, text, launcherFound) is { } answer) return answer;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or COMException or IOException or UnauthorizedAccessException) { }
        }
        return SdlStartDialog.Instance.AskOpenLauncher(title, text, launcherFound);
    }

    /// <summary>The TaskDialog shown in comctl32 6's activation context; true or false for its answer, null when it could not be shown.</summary>
    static unsafe bool? Show(string title, string text, bool launcherFound)
    {
        var (main, detail) = BareStart.Split(text);
        IntPtr context = Context();
        IntPtr cookie = IntPtr.Zero;
        bool active = context != InvalidHandle && ActivateActCtx(context, out cookie);
        var strings = new List<IntPtr>();
        IntPtr Wide(string s) { var p = Marshal.StringToHGlobalUni(s); strings.Add(p); return p; }
        try
        {
            var buttons = stackalloc TaskDialogButton[2];
            int count = 0;
            if (launcherFound) buttons[count++] = new TaskDialogButton { Id = OpenId, Text = Wide(BareStart.OpenWord) };
            buttons[count++] = new TaskDialogButton { Id = CloseId, Text = Wide(BareStart.CloseWord) };
            var config = new TaskDialogConfig
            {
                Size = (uint)sizeof(TaskDialogConfig),
                Flags = AllowCancellation | PositionOnScreen,
                WindowTitle = Wide(title),
                MainIcon = (IntPtr)InformationIcon,
                MainInstruction = Wide(main),
                Content = detail.Length > 0 ? Wide(detail) : IntPtr.Zero,
                ButtonCount = (uint)count,
                Buttons = buttons,
                DefaultButton = launcherFound ? OpenId : CloseId,
            };
            int pressed = 0;
            int hr = TaskDialogIndirect(&config, &pressed, null, null);
            return hr < 0 ? null : pressed == OpenId;
        }
        finally
        {
            foreach (var p in strings) Marshal.FreeHGlobal(p);
            if (active) DeactivateActCtx(0, cookie);
            if (context != InvalidHandle) ReleaseActCtx(context);
        }
    }

    /// <summary>An activation context for comctl32 6: shell32.dll's own manifest (resource 124), else one written for the call; or invalid.</summary>
    static IntPtr Context()
    {
        string system = Environment.SystemDirectory;
        var fromShell = new ActCtx
        {
            Size = (uint)Marshal.SizeOf<ActCtx>(),
            Flags = ResourceNameValid | AssemblyDirectoryValid,
            Source = Path.Combine(system, "shell32.dll"),
            AssemblyDirectory = system,
            ResourceName = (IntPtr)124,
        };
        IntPtr context = CreateActCtxW(ref fromShell);
        if (context != InvalidHandle) return context;
        string file = Path.Combine(Path.GetTempPath(), $"vitomir-comctl6-{Environment.ProcessId}.manifest");
        try
        {
            File.WriteAllText(file, Manifest);
            var fromFile = new ActCtx { Size = (uint)Marshal.SizeOf<ActCtx>(), Source = file };
            return CreateActCtxW(ref fromFile);
        }
        finally
        {
            try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The manifest that asks for comctl32 6.</summary>
    public const string Manifest = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
          <dependency>
            <dependentAssembly>
              <assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/>
            </dependentAssembly>
          </dependency>
        </assembly>
        """;

    static readonly IntPtr InvalidHandle = new(-1);
    const uint ResourceNameValid = 0x008, AssemblyDirectoryValid = 0x004;
    const int AllowCancellation = 0x0008, PositionOnScreen = 0;
    const ushort InformationIcon = 0xFFFD;   // TD_INFORMATION_ICON, MAKEINTRESOURCEW(-3)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ActCtx
    {
        public uint Size;
        public uint Flags;
        public string? Source;
        public ushort ProcessorArchitecture;
        public ushort LangId;
        public string? AssemblyDirectory;
        public IntPtr ResourceName;
        public string? ApplicationName;
        public IntPtr Module;
    }

    // commctrl.h declares both in pshpack1.h: packed, on 32 and 64 bits alike.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct TaskDialogButton
    {
        public int Id;
        public IntPtr Text;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct TaskDialogConfig
    {
        public uint Size;
        public IntPtr Parent;
        public IntPtr Instance;
        public int Flags;
        public int CommonButtons;
        public IntPtr WindowTitle;
        public IntPtr MainIcon;
        public IntPtr MainInstruction;
        public IntPtr Content;
        public uint ButtonCount;
        public TaskDialogButton* Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public TaskDialogButton* RadioButtons;
        public int DefaultRadioButton;
        public IntPtr VerificationText;
        public IntPtr ExpandedInformation;
        public IntPtr ExpandedControlText;
        public IntPtr CollapsedControlText;
        public IntPtr FooterIcon;
        public IntPtr Footer;
        public IntPtr Callback;
        public IntPtr CallbackData;
        public uint Width;
    }

    [DllImport("comctl32.dll", ExactSpelling = true)]
    static extern unsafe int TaskDialogIndirect(TaskDialogConfig* config, int* button, int* radioButton, int* verificationChecked);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern IntPtr CreateActCtxW(ref ActCtx context);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ActivateActCtx(IntPtr context, out IntPtr cookie);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeactivateActCtx(uint flags, IntPtr cookie);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    static extern void ReleaseActCtx(IntPtr context);
}
