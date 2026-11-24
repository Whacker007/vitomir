// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's Setup page (the setup a page of the launcher): the setup through its SetupService.
using System.Globalization;
using Vitomir.Platform.Host;
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>A line of a page's words: plain, or a warning (said in amber).</summary>
public readonly record struct PageLine(string Text, bool Warn = false, bool Detail = false, bool Caution = false);

/// <summary>
/// **The Setup page**: the setup, step by step, **through
/// <see cref="SetupService"/>** (never by reading <c>--setup</c>'s text), so it does what the command line does by the same calls:
/// <list type="number">
/// <item>the copies of the game found (<see cref="SetupService.Find"/>: GOG's and Steam's own copies), one selected
/// (<see cref="SetupService.Choose"/> first), or a folder typed or browsed to (<see cref="UseFolder"/>);</item>
/// <item>the check (<see cref="StartCheck"/>, by size) and its result in words (<see cref="CheckWords"/>); the import
/// (<see cref="StartImport"/>) with its progress, which can be stopped (<see cref="Cancel"/>) and is carried on where it stopped by
/// the next (DataImport keeps every file already there and right);</item>
/// <item>1.1's own saves listed (<see cref="SetupService.Originals"/>), the ones selected imported (<see cref="StartSaves"/>), never
/// over a save there without asking (<see cref="AskReplace"/>, then <see cref="Replace"/>: <c>--replace</c>'s rule, the old one kept
/// as <c>.previous</c>; or <see cref="CancelReplace"/>);</item>
/// <item>FFmpeg found (<see cref="SetupService.FFmpeg"/>) or <c>winget install --exact --id Gyan.FFmpeg</c> offered, run in a console
/// window the player sees only when the button is pressed (<see cref="InstallFFmpeg"/>).</item>
/// </list>
/// The long steps run on a thread of their own; their progress and their end are taken on the launcher's thread by
/// <see cref="Poll"/>. Nothing here knows SDL or the screen: the launcher fills its data from this.
/// </summary>
public sealed class SetupPage(SetupService service, ILauncherShell shell, Action<string> say)
{
    /// <summary>The same page over another service (the launcher's, once the player chose or moved the user folder): its shell and its log kept.</summary>
    public SetupPage For(SetupService other) => new(other, shell, say);

    readonly object gate = new();
    Task? task;
    CancellationTokenSource? cancel;
    Action? done;
    SetupProgress? progress;
    string busy = "";

    /// <summary>The service it works through.</summary>
    public SetupService Service => service;

    /// <summary>The installs found by the last look.</summary>
    public IReadOnlyList<InstallCandidate> Installs { get; private set; } = [];

    /// <summary>The copy of the game selected (the one the steps work on), or null when none was found.</summary>
    public InstallCandidate? Chosen { get; private set; }

    /// <summary>The folder box's words (set them through <see cref="Typed"/> when the player types).</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Whether **Another folder** is selected (the radio of the folder box, in the same group as the copies found) rather than one of the
    /// copies found: its folder, once used (<see cref="UseFolder"/>), is then the copy of the game the steps work on.
    /// </summary>
    public bool Other { get; private set; }

    /// <summary>Whether game files are imported (a marker in the user folder): Verify files has something to verify.</summary>
    public bool HasImport => Kept().HasImport;

    // Whether something is imported, and whether it is ready, read from the disk once a step's end or a look and kept: the launcher fills
    // its screen every frame, and reading the imported files' record each time cost it most of a frame.
    (int At, bool HasImport, bool Ready, bool ReadyDeveloper)? kept;
    int looks;

    (int At, bool HasImport, bool Ready, bool ReadyDeveloper) Kept()
    {
        int at = Ends * 100_000 + looks;
        if (kept is { } k && k.At == at) return k;
        var now = (at, ImportedData.ReadMarker(service.DataFolder) is not null, service.Imported(false).Ready, service.Imported(true).Ready);
        kept = now;
        return now;
    }

    /// <summary>The last verification of the imported game files, or null.</summary>
    public DataVerifyReport? VerifyReport { get; private set; }

    /// <summary>Whether Repair is offered: the last verification found something wrong it can mend (missing, changed or extra files).</summary>
    public bool CanRepair => VerifyReport is { Refusal: null, AllGood: false };

    /// <summary>Why the folder typed was not taken, or null.</summary>
    public string? PathWhy { get; private set; }

    /// <summary>The last check's report, or null.</summary>
    public DataCheckReport? CheckReport { get; private set; }

    /// <summary>The last import's report, or null.</summary>
    public DataImportReport? ImportReport { get; private set; }

    /// <summary>What the last check or import said, in words.</summary>
    public IReadOnlyList<PageLine> Result { get; private set; } = [];

    /// <summary>1.1's own saves and characters in the selected copy of the game.</summary>
    public IReadOnlyList<OriginalItem> Saves { get; private set; } = [];

    /// <summary>The saves selected to import (by id).</summary>
    public HashSet<string> Picked { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the last import of saves said.</summary>
    public IReadOnlyList<PageLine> SaveResult { get; private set; } = [];

    /// <summary>The saves the last import found a save already in the slot of (to be asked about), or empty.</summary>
    public IReadOnlyList<SaveImportResult> AskReplace { get; private set; } = [];

    /// <summary>FFmpeg as found by the last look, or null.</summary>
    public FFmpegFound? FFmpeg { get; private set; }

    /// <summary>What the FFmpeg line says beyond whether it was found (winget's end), or empty.</summary>
    public string FFmpegNote { get; private set; } = "";

    /// <summary>Whether a step runs now (the check, the import, the saves' import, winget).</summary>
    public bool Busy { get { lock (gate) return task is { IsCompleted: false } || done is not null; } }

    /// <summary>What runs now (<c>check</c>, <c>import</c>, <c>saves</c>, <c>winget</c>), or empty.</summary>
    public string BusyWith { get { lock (gate) return Busy ? busy : ""; } }

    /// <summary>
    /// The running step's progress (0 to 1), the part it is in (an import first checks every size, <c>check</c>, then copies,
    /// <c>import</c>) and the file it is at; or null.
    /// </summary>
    public (float Share, string Step, string Item)? Progress
    {
        get
        {
            lock (gate)
            {
                if (progress is not { Total: > 0 } p) return null;
                return ((float)Math.Clamp(p.Done / (double)p.Total, 0, 1), p.Step, p.Item);
            }
        }
    }

    /// <summary>A test's hook: told each progress report on the step's own thread (it may <see cref="Cancel"/>).</summary>
    internal Action<SetupProgress>? OnProgress { get; set; }

    /// <summary>Whether the imported data is ready now (a start would play from it).</summary>
    public bool Imported(bool developer) => developer ? Kept().ReadyDeveloper : Kept().Ready;

    // ---- looking ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// **A look**: the copies of the game found again (the folder typed among them when it is one), the one selected kept when it is
    /// still found (else the first of a store's), its saves listed, FFmpeg looked for.
    /// </summary>
    public void Refresh()
    {
        looks++;
        // The copies found by the stores and the usual folders are the radios; a folder typed stays the Another folder radio's.
        Installs = service.Find();
        if (Other)
        {
            string typed = Path.Trim().Trim('"');
            Chosen = typed.Length > 0 && InstallFinder.IsInstall(typed) && Chosen is not null ? OfFolder(typed, service.Find(typed)) : null;
        }
        else
        {
            var keep = Chosen is null ? null : Installs.FirstOrDefault(c => Same(c.Folder, Chosen.Folder));
            Chosen = keep ?? SetupService.Choose(Installs);
            Other = Installs.Count == 0;            // nothing found: the folder box is the one choice, selected
        }
        ListSaves();
        LookForFFmpeg();
        say($"setup page: {Installs.Count} copies of the game found" + (Chosen is null ? "" : $", {Chosen.Folder} selected"));
    }

    /// <summary>The copy of the game at <paramref name="index"/> of those found selected (the folder box cleared: it is another choice).</summary>
    public void Choose(int index)
    {
        if (Busy || index < 0 || index >= Installs.Count) return;
        Chosen = Installs[index];
        Other = false;
        Path = "";
        PathWhy = null;
        CheckReport = null;
        ImportReport = null;
        Result = [];
        ListSaves();
        say($"setup page: selected {Chosen.Folder}");
    }

    /// <summary>
    /// **Another folder** selected (its radio): the folder box's folder is the copy of the game once it is used; until then none is
    /// selected, and the steps wait for Use this folder.
    /// </summary>
    public void ChooseOther()
    {
        if (Busy || Other) return;
        Other = true;
        Chosen = null;
        CheckReport = null;
        ImportReport = null;
        Result = [];
        ListSaves();
        say("setup page: Another folder selected");
    }

    /// <summary>The folder box's words changed by typing: Another folder selected, the folder not used until Use this folder (or Enter).</summary>
    public void Typed(string text)
    {
        if (text == Path) return;
        Path = text;
        PathWhy = null;
        if (Other && Chosen is null) return;
        Other = true;
        Chosen = null;
        CheckReport = null;
        ImportReport = null;
        Result = [];
        ListSaves();
    }

    /// <summary>The copy of the game a folder typed is, of those <paramref name="found"/> for it (<c>--from</c>'s way); null when none is.</summary>
    static InstallCandidate? OfFolder(string typed, IReadOnlyList<InstallCandidate> found) => SetupService.Choose(found, typed);

    /// <summary>
    /// **The folder typed (or browsed to) used**: refused, said, when it is not a copy of the game (it has no <c>Resource.nob</c>); else
    /// selected: the copy found of that folder's radio when it is one of them (the box cleared), else Another folder with it.
    /// </summary>
    public bool UseFolder(string? folder = null)
    {
        if (Busy) return false;
        if (folder is not null) Path = folder;
        string typed = Path.Trim().Trim('"');
        Other = true;
        if (typed.Length == 0) { PathWhy = "Type the folder of your copy of the game (the one that holds Resource.nob), or select Browse\u2026 to find it."; Chosen = null; return false; }
        if (!Directory.Exists(typed)) { PathWhy = $"{typed} is not a folder on this computer."; Chosen = null; return false; }
        if (!InstallFinder.IsInstall(typed)) { PathWhy = $"{typed} is not a copy of Vampire: The Masquerade - Redemption: it has no Resource.nob."; Chosen = null; return false; }
        PathWhy = null;
        Path = typed;
        Installs = service.Find();
        if (SetupService.Choose(Installs, typed) is { } listed)
        {
            // One of the copies found: its own radio selected, and the box cleared.
            Chosen = listed;
            Other = false;
            Path = "";
        }
        else Chosen = OfFolder(typed, service.Find(typed));
        CheckReport = null;
        ImportReport = null;
        Result = [];
        ListSaves();
        say($"setup page: the folder {typed} used and selected");
        return true;
    }

    void ListSaves()
    {
        Picked.Clear();
        AskReplace = [];
        SaveResult = [];
        try { Saves = Chosen is null ? [] : service.Originals(Chosen.Folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Saves = []; SaveResult = [new($"The original game's saves could not be read: {e.Message}", true)]; }
    }

    /// <summary>Names joined as a sentence says them: <c>SP0</c>, <c>SP0 and SP1</c>, <c>SP0, SP1 and SP2</c>.</summary>
    static string Join(IEnumerable<string> names)
    {
        var all = names.ToList();
        return all.Count <= 1 ? string.Concat(all) : string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1];
    }

    /// <summary>FFmpeg looked for again (where the game looks for it).</summary>
    public void LookForFFmpeg()
    {
        try { FFmpeg = SetupService.FFmpeg(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { FFmpeg = null; }
    }

    // ---- the steps ------------------------------------------------------------------------------------------------------------

    /// <summary>**The check** of the selected copy of the game, by size (the import checks every file's SHA-256 as it copies it).</summary>
    public bool StartCheck()
    {
        if (Chosen is not { } install) return false;
        return Start("check", (p, c) =>
        {
            var report = service.Check(install.Folder, CheckDepth.Sizes, p, c);
            return () =>
            {
                CheckReport = report;
                Result = CheckWords(report);
                say($"setup page: checked {install.Folder}: {(report.GameWhole ? "whole" : $"{report.Problems.Count} problems")}");
                foreach (var line in Result) say($"setup page:   {line.Text.Trim()}");
            };
        }, stopped: "Check cancelled.");
    }

    /// <summary>
    /// **The import** of the selected copy of the game's files into the user folder, its progress shown; stopped by <see cref="Cancel"/>, carried
    /// on by the next (each file already there and right is kept).
    /// </summary>
    public bool StartImport()
    {
        if (Chosen is not { } install) return false;
        return Start("import", (p, c) =>
        {
            var report = service.ImportData(install.Folder, p, c);
            return () =>
            {
                ImportReport = report;
                Result = ImportWords(report, install);
                if (report.Accepted) ListSaves();
                say($"setup page: import from {install.Folder}: {(report.Accepted ? $"{report.Copied} copied, {report.Kept} kept" : report.Refusal.ToString())}");
                foreach (var line in Result) say($"setup page:   {line.Text.Trim()}");
            };
        }, stopped: "Import cancelled. Select Import to continue.");
    }

    /// <summary>
    /// **Verify files**: the imported game files read whole and each checked against its manifest's size and SHA-256 (<see cref="SetupService.Verify"/>),
    /// with its progress and Cancel; the result in words: all good, or each file missing, changed or extra, and Repair offered.
    /// </summary>
    public bool StartVerify()
    {
        if (!HasImport) return false;
        VerifyReport = null;
        return Start("verify", (p, c) =>
        {
            var report = service.Verify(p, c);
            return () =>
            {
                VerifyReport = report;
                Result = VerifyWords(report);
                say($"setup page: verified the imported game files: {(report.Refusal ?? (report.AllGood ? "all good" : $"{report.Missing.Count()} missing, {report.Changed.Count()} changed, {report.Extra.Count} extra"))}");
                foreach (var line in SetupCommand.VerifyLines(report).Skip(1)) say($"setup page: {line.Trim()}");
            };
        }, stopped: "Verify cancelled: nothing was changed.");
    }

    /// <summary>
    /// **Repair**: the files the last verification found missing or changed copied again from the copy of the game they were imported from
    /// (when it is there and its own files are the manifest's), the extra ones set aside; else what to do (which copy to select, Import again).
    /// </summary>
    public bool StartRepair()
    {
        if (VerifyReport is not { } verified || !CanRepair) return false;
        return Start("repair", (p, c) =>
        {
            var report = service.Repair(verified, p, c);
            return () =>
            {
                VerifyReport = null;
                Result = report.Repaired
                    ? [new($"Repaired: {N(report.Copied.Count)} {(report.Copied.Count == 1 ? "file" : "files")} copied again"
                           + (report.SetAside > 0 ? $", {N(report.SetAside)} extra set aside" : "") + ".")]
                    : [new($"Not repaired: {SetupWords.Plain(report.Refusal!)}.", true)];
                say($"setup page: repair: {(report.Repaired ? $"{report.Copied.Count} copied again from {report.Source}, {report.SetAside} set aside" : report.Refusal)}");
            };
        }, stopped: "Repair cancelled.");
    }

    /// <summary>A verification's result in the page's words.</summary>
    public static IReadOnlyList<PageLine> VerifyWords(DataVerifyReport report)
    {
        if (report.Refusal is { } why) return [new($"Not verified: {SetupWords.Plain(why)}.", true)];
        if (report.AllGood)
            return [new($"All {N(report.Files)} files are as they should be.")];
        // What happened and what to do first; each file behind Details (D1), its SHA-256 in launcher.log only.
        int count = report.Problems.Count + report.Extra.Count;
        string what = report.Extra.Count == 0 ? "missing or changed" : report.Problems.Count == 0 ? "extra, over the game's own" : "missing, changed or extra";
        var lines = new List<PageLine> { new($"{N(count)} {(count == 1 ? "file is" : "files are")} {what}: select Repair.", true) };
        lines.AddRange(report.Missing.Take(10).Select(p => new PageLine($"    Missing: {p.Path}", false, true)));
        lines.AddRange(report.Changed.Take(10).Select(p => new PageLine($"    Changed: {p.Path}", false, true)));
        lines.AddRange(report.Extra.Take(10).Select(e => new PageLine($"    Extra: {e}", false, true)));
        int more = Math.Max(0, report.Missing.Count() - 10) + Math.Max(0, report.Changed.Count() - 10) + Math.Max(0, report.Extra.Count - 10);
        if (more > 0) lines.Add(new($"    and {N(more)} more (each named in launcher.log)", false, true));
        return lines;
    }

    /// <summary>**The saves selected imported**, each into its slot; a slot that holds a save is not written over (<see cref="AskReplace"/>).</summary>
    public bool StartSaves()
    {
        if (Chosen is not { } install || Picked.Count == 0) return false;
        var picks = Saves.Where(s => Picked.Contains(s.Id)).Select(s => new OriginalPick(s.Id)).ToList();
        say($"setup page: importing the saves selected, {string.Join(", ", picks.Select(p => p.Id))}, from {install.Folder}");
        return Start("saves", (_, c) =>
        {
            var results = service.ImportOriginals(install.Folder, picks, replace: false, c);
            return () => Took(results, asked: true);
        }, stopped: "The import of the saves was cancelled.");
    }

    /// <summary>
    /// The answer to <see cref="AskReplace"/>: those saves imported over the ones there (each kept as <c>.previous</c>), or the existing
    /// ones kept (those saves not imported, taken off the selection).
    /// </summary>
    public bool Replace(bool yes)
    {
        var asked = AskReplace;
        AskReplace = [];
        if (!yes || asked.Count == 0 || Chosen is not { } install)
        {
            if (asked.Count > 0)
            {
                SaveResult = [.. SaveResult, new($"Kept the existing {Join(asked.Select(a => a.Item.Id))}.")];
                foreach (var a in asked)
                {
                    Picked.Remove(a.Item.Id);
                    say($"setup page: {a.Item.Id}: not imported: {a.Target} kept as it was (the player kept the existing one)");
                }
            }
            return false;
        }
        var picks = asked.Select(a => new OriginalPick(a.Item.Id)).ToList();
        say($"setup page: replacing with {string.Join(", ", picks.Select(p => p.Id))} (the player selected Replace; each one there kept as .previous)");
        return Start("saves", (_, c) =>
        {
            var results = service.ImportOriginals(install.Folder, picks, replace: true, c);
            return () => Took(results, asked: false);
        }, stopped: "The import of the saves was cancelled.");
    }

    /// <summary>
    /// The replace notice cancelled: nothing written for the saves asked about, which stay selected (the player may change the
    /// selection and import again).
    /// </summary>
    public void CancelReplace()
    {
        var asked = AskReplace;
        AskReplace = [];
        if (asked.Count == 0) return;
        SaveResult = [.. SaveResult, new($"{Join(asked.Select(a => a.Item.Id))} not imported.")];
        foreach (var a in asked) say($"setup page: {a.Item.Id}: not imported: the player cancelled the replace notice; {a.Target} kept as it was, the save still selected");
    }

    /// <summary>Every save that can be imported selected.</summary>
    public void SelectAll()
    {
        if (Busy) return;
        foreach (var s in Saves) if (s.ImportsAs is not null) Picked.Add(s.Id);
    }

    /// <summary>The selection emptied.</summary>
    public void ClearSelection()
    {
        if (Busy) return;
        Picked.Clear();
    }

    void Took(IReadOnlyList<SaveImportResult> results, bool asked)
    {
        var lines = asked ? new List<PageLine>() : [.. SaveResult.Where(l => !l.Warn)];
        var taken = new List<SaveImportResult>();
        // Those imported said in one short line (E2): where each went is in launcher.log.
        var imported = results.Where(r => r.Imported).Select(r => r.Item.Id).ToList();
        if (imported.Count > 0) lines.Add(new($"Imported {Join(imported)}."));
        foreach (var r in results)
        {
            if (r.Imported) continue;
            else if (r.Refused && asked && r.Target is not null && File.Exists(r.Target)) taken.Add(r);
            else lines.Add(new(SetupWords.Plain(r.Refused ? "Not imported: " + r.Why : "Failed: " + r.Why), true));
        }
        SaveResult = lines;
        AskReplace = taken;
        // Each in the launcher's log (a log once had no line for them): what, into which slot or name, replaced or not.
        foreach (var r in results)
        {
            string what = $"{r.Item.Id} \"{SetupWords.Plain(r.Item.Description)}\"";
            if (r.Imported)
                say($"setup page: {what}: imported into {r.Target}" + (File.Exists(r.Target + ".previous") && !asked ? $", replacing the one there (kept as {System.IO.Path.GetFileName(r.Target)}.previous)" : ""));
            else if (r.Refused && asked && r.Target is not null && File.Exists(r.Target)) say($"setup page: {what}: {r.Target} already exists: the player is asked whether to replace it");
            else say($"setup page: {what}: {(r.Refused ? "refused" : "failed")}: {SetupWords.Plain(r.Why)}");
            if (!r.Imported && !r.Refused) foreach (var line in r.Lines.TakeLast(4)) say($"setup page:   {SetupWords.Plain(line)}");
        }
        // Those imported come off the selection; those asked about stay selected until answered.
        foreach (var r in results) if (r.Imported) Picked.Remove(r.Item.Id);
        if (taken.Count == 0 && asked) Picked.RemoveWhere(id => results.Any(r => r.Item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>**winget** run in a console window the player sees (<see cref="FFmpegSearch.WingetLine"/>), FFmpeg looked for again at its end.</summary>
    public bool InstallFFmpeg()
    {
        Task<int> running;
        try { running = shell.RunVisible(FFmpegSearch.WingetLine); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            FFmpegNote = $"winget could not be started ({e.Message}): install it from the Microsoft Store (App Installer), or run {FFmpegSearch.WingetLine} yourself.";
            return false;
        }
        say($"setup page: running {FFmpegSearch.WingetLine} in a console window");
        return Start("winget", (_, c) =>
        {
            int code = running.GetAwaiter().GetResult();
            return () =>
            {
                LookForFFmpeg();
                FFmpegNote = FFmpeg is not null ? "" : code == 0
                    ? "winget finished, but FFmpeg is not found yet: select Check again, or start the launcher again."
                    : $"winget ended with {code}.";
            };
        }, stopped: "");
    }

    /// <summary>Cancels the running step (the import carries on from there the next time).</summary>
    public void Cancel()
    {
        lock (gate) cancel?.Cancel();
    }

    bool Start(string what, Func<IProgress<SetupProgress>, CancellationToken, Action> work, string stopped)
    {
        lock (gate)
        {
            if (task is { IsCompleted: false } || done is not null) return false;
            busy = what;
            progress = null;
            if (what is "check" or "import" or "verify" or "repair") Result = [];
            cancel?.Dispose();
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            var reports = new Reports(this);
            task = Task.Run(() =>
            {
                Action end;
                try { end = work(reports, token); }
                catch (OperationCanceledException) { end = () => { if (stopped.Length > 0) Result = [new(stopped, true)]; say($"setup page: {what} stopped"); }; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
                {
                    end = () => { Result = [new($"The {what} stopped: {SetupWords.Plain(e.Message)}", true)]; say($"setup page: {what} failed: {e.Message}"); };
                }
                lock (gate) done = end;
            }, CancellationToken.None);
        }
        return true;
    }

    sealed class Reports(SetupPage page) : IProgress<SetupProgress>
    {
        public void Report(SetupProgress value)
        {
            lock (page.gate) page.progress = value;
            page.OnProgress?.Invoke(value);
        }
    }

    /// <summary>How many steps' ends have been taken (by <see cref="Poll"/> or <see cref="Wait"/>): the launcher looks again when it moves.</summary>
    public int Ends { get; private set; }

    /// <summary>**A step's end taken** on the launcher's thread: its words set. Answers whether one ended.</summary>
    public bool Poll()
    {
        Action? end;
        lock (gate)
        {
            if (done is null) return false;
            end = done;
            done = null;
            task = null;
            busy = "";
            progress = null;
        }
        end();
        Ends++;
        return true;
    }

    /// <summary>Waits for the running step and takes its end (tests, the launcher's scripted steps).</summary>
    public void Wait(TimeSpan? within = null)
    {
        Task? t;
        lock (gate) t = task;
        if (t is not null && !t.Wait(within ?? TimeSpan.FromMinutes(15))) throw new TimeoutException($"the setup's {busy} did not end");
        Poll();
    }

    // ---- the words ------------------------------------------------------------------------------------------------------------

    static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    static bool Same(string a, string b) => a.Equals(b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// **What a check found, in the page's words**: whether the game's files are all there as Vitomir needs them (or which are not, and
    /// so the copy cannot be used), the store's note, the SDK's sources, the files left out told apart (those that would replace one
    /// of 1.1's own files, and the additions), and the rest passed over.
    /// </summary>
    public static IReadOnlyList<PageLine> CheckWords(DataCheckReport report)
    {
        var lines = new List<PageLine>();
        var game = report.Manifest.Of(DataManifest.Game).ToList();
        if (report.GameWhole)
            lines.Add(new($"All {N(game.Count)} game files are there ({SetupWords.Size(report.Manifest.Bytes(DataManifest.Game))}): ready to import."));
        else
        {
            lines.Add(new($"{N(report.Problems.Count)} of the game's {N(game.Count)} files are not as Vitomir needs them, so this copy cannot be used:", true));
            foreach (var p in report.Problems.Take(6)) lines.Add(new("    " + SetupWords.Plain(p.ToString()), true));
            if (report.Problems.Count > 6) lines.Add(new($"    ... and {N(report.Problems.Count - 6)} more (vitomir-platform --setup check lists them all)", true));
        }
        if (report.Store == Store.Unknown) lines.Add(new("This is not a GOG or Steam copy, so it is not supported.", true));
        if (report.Replacing.Count > 0)
        {
            lines.Add(new($"{N(report.Replacing.Count)} {(report.Replacing.Count == 1 ? "file" : "files")} from a mod in your copy {(report.Replacing.Count == 1 ? "is" : "are")} left out. Add mods on the Modules page."));
            lines.Add(new($"    Left out: {SetupCommand.LeftOutFolders(report.Replacing)}", false, true));
        }
        // What does not change what the player does: behind Details, and in launcher.log (H3, D1).
        if (report.Adding.Count > 0)
            lines.Add(new($"    Left out, adding to the game only: {N(report.Adding.Count)} {(report.Adding.Count == 1 ? "file" : "files")} ({SetupCommand.LeftOutFolders(report.Adding)})", false, true));
        lines.Add(new("    " + report.Scripts switch
        {
            PartState.Whole => $"The SDK's {N(report.Manifest.Of(DataManifest.Scripts).Count())} script sources: not needed (the game runs Codex.nob's compiled scripts), not imported",
            PartState.Broken => $"The SDK's Codex folder, {N(report.ScriptProblems.Count)} of its sources changed or missing (a mod's): not imported",
            _ => "The game's scripts: Codex.nob's own compiled classes",
        }, false, true));
        lines.Add(new($"    {N(report.Ignored)} other files not read (the programs, the store's files, the saves, the SDK)", false, true));
        return lines;
    }

    /// <summary>What an import did, in the page's words.</summary>
    public IReadOnlyList<PageLine> ImportWords(DataImportReport report, InstallCandidate install)
    {
        var lines = new List<PageLine>();
        if (!report.Accepted)
        {
            lines.Add(new(report.Refusal switch
            {
                ImportRefusal.DataNotWhole => $"Not imported: {N(report.Problems.Count)} of the game's files are not as Vitomir needs them:",
                ImportRefusal.NoRoom => "Not imported: there is not enough room.",
                _ => "Not imported:",
            }, true));
            foreach (var p in report.Problems.Take(6)) lines.Add(new("    " + SetupWords.Plain(p), true));
            if (report.Problems.Count > 6) lines.Add(new($"    ... and {N(report.Problems.Count - 6)} more", true));
            if (report.Refusal == ImportRefusal.DataNotWhole && !install.Supported) lines.Add(new($"This folder is {SetupWords.Unsupported}.", true));
            return lines;
        }
        var m = report.Marker!;
        lines.Add(new($"Imported {N(m.Files)} files ({SetupWords.Size(m.Bytes)}). The game is ready: go to Play."));
        lines.Add(new($"    {N(report.Copied)} copied and {N(report.Kept)} already there, each checked, into {service.DataFolder}", false, true));
        if (report.OptionsCopied) lines.Add(new("    the original game's options (masquerade.ini) were imported too", false, true));
        if (report.Overriding.Count > 0) lines.Add(new($"    Left out: {N(report.Overriding.Count)} files that are not the original game's (Check files lists them)", false, true));
        if (report.SetAside > 0) lines.Add(new($"    {N(report.SetAside)} extra files moved aside from the imported folder (never deleted)", false, true));
        return lines;
    }

    /// <summary>
    /// What a save or character of 1.1's would be imported as, in words: its slot, or the character's file in the Crypt, or that one
    /// already exists there (the player is asked before it is replaced), or why it cannot be.
    /// </summary>
    public string Into(OriginalItem item) =>
        item.ImportsAs is null ? "cannot be imported: " + SetupWords.Plain(item.Why ?? "it cannot be read")
        : item.Kind == OriginalKind.Character
            ? service.KeptCharacter(item.ImportsAs) is not null ? "already imported" : ""
        : File.Exists(System.IO.Path.Combine(service.SavesFolder, item.ImportsAs)) ? "already imported"
        : "";
}
