// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's Modules page: the player's modules of the Classic game through the PlayerModuleStore, and the problems list.
using System.Globalization;
using System.Text.RegularExpressions;
using Vitomir.Platform.Assets;
using Vitomir.Platform.PlayerModules;
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>One module of the game's list as the Modules page shows it: its id, whether it is on, the module (null when it is not imported or does not open, with why).</summary>
public sealed record ModuleView(string Id, bool On, PlayerModule? Module, string? Broken);

/// <summary>
/// **A pack whose module id is in the list already, from another source** (the import refused it as
/// <see cref="ModuleRefusal.AlreadyThere"/>): the pack, the id, the module in the list (its name and where it came from). The launcher
/// asks *Replace X?* and answers with <see cref="ModulesPage.StartReplace"/> or <see cref="ModulesPage.ForgetReplace"/>.
/// </summary>
public sealed record ReplaceQuestion(string Source, string Id, string Name, string From);

/// <summary>
/// **The Modules page's working half**: the player's modules of the Classic game **through <see cref="PlayerModuleStore"/>** (the same calls
/// <c>vitomir-platform --setup module-…</c> makes): the game's list in the player's order, each on or off (<see cref="Toggle"/>), moved a
/// place (<see cref="Move"/>), sorted by dependencies (<see cref="Sort"/>), removed (<see cref="Remove"/>), a pack added from a folder, a zip
/// or a <c>.nob</c> with its progress (<see cref="StartAdd"/>, <see cref="Cancel"/>); and **the problems list**, the start's own resolution and
/// checks (<see cref="PlayerModuleStore.Prepare"/>) made again after every change on a thread of its own (<see cref="Recheck"/>), with the
/// acceptance of a combination's warnings (<see cref="Accept"/>). The import and the checks run on threads of their own; their ends are
/// taken on the launcher's thread by <see cref="Poll"/>. Nothing here knows SDL or the screen.
/// </summary>
public sealed class ModulesPage
{
    readonly PlayerModuleStore store;
    readonly Func<string?> baseFolder;
    readonly Action<string> say;
    readonly object gate = new();
    Task? adding, checking;
    CancellationTokenSource? cancelAdd, cancelCheck;
    Action? addDone, checkDone;
    SetupProgress? progress;
    int generation;

    /// <param name="store">The game's player modules (the user folder's).</param>
    /// <param name="baseFolder">The base game's files the checks read, as the game's start would take them (the imported data, else a developer's install), or null.</param>
    /// <param name="say">The launcher's log.</param>
    public ModulesPage(PlayerModuleStore store, Func<string?> baseFolder, Action<string> say)
    {
        this.store = store;
        this.baseFolder = baseFolder;
        this.say = say;
    }

    /// <summary>The store it works through.</summary>
    public PlayerModuleStore Store => store;

    /// <summary>The game's list in the player's order, then any module imported and not listed (off).</summary>
    public IReadOnlyList<ModuleView> Rows { get; private set; } = [];

    /// <summary>What the list's file got wrong (it does not read), or null.</summary>
    public string? ListProblem { get; private set; }

    /// <summary>The last check of the modules on (null before the first, or while none is on and nothing is wrong).</summary>
    public ModuleStart? Start { get; private set; }

    /// <summary>Whether the check of the modules on runs now (its result is not yet the combination's).</summary>
    public bool Checking { get { lock (gate) return checking is { IsCompleted: false } || checkDone is not null || stale; } }
    bool stale;

    /// <summary>Whether a pack is being imported now.</summary>
    public bool Importing { get { lock (gate) return adding is { IsCompleted: false } || addDone is not null; } }

    /// <summary>The import's progress (0 to 1) and the file it is at, or null.</summary>
    public (float Share, string Item)? Progress
    {
        get
        {
            lock (gate)
                return progress is { Total: > 0 } p ? ((float)Math.Clamp(p.Done / (double)p.Total, 0, 1), p.Item) : null;
        }
    }

    /// <summary>What the pack being imported is (its file or folder's name), while it is.</summary>
    public string AddingName { get; private set; } = "";

    /// <summary>What the last import or sort said, in words (the refused files listed, each with why).</summary>
    public IReadOnlyList<PageLine> Result { get; private set; } = [];

    /// <summary>The pack the last add found a module of its id in the list for, waiting for the player's answer (Replace or Cancel), or null.</summary>
    public ReplaceQuestion? ReplaceAsked { get; private set; }

    /// <summary>
    /// The modules whose details are open under their rows (one or several; none when the page first shows): a row's chevron or its name
    /// opens and closes them.
    /// </summary>
    public HashSet<string> Open { get; } = new(StringComparer.Ordinal);

    /// <summary>The modules whose list of files not used (the program files left out at their import) is open in their details.</summary>
    public HashSet<string> NotUsedOpen { get; } = new(StringComparer.Ordinal);

    /// <summary>The module being verified now (its Verify), or null.</summary>
    public string? Verifying { get { lock (gate) return verifying is { IsCompleted: false } || verifyDone is not null ? verifyingId : null; } }
    Task? verifying;
    Action? verifyDone;
    string? verifyingId;

    /// <summary>What the last Verify said, and of which module.</summary>
    public (string Id, IReadOnlyList<PageLine> Lines)? VerifyResult { get; private set; }

    /// <summary>How many changes and ends have been taken (the launcher fills its lists again when it moves).</summary>
    public int Ends { get; private set; }

    /// <summary>A test's hook: told each progress report of an import on its own thread.</summary>
    internal Action<SetupProgress>? OnProgress { get; set; }

    // ---- the list ---------------------------------------------------------------------------------------------------------------

    /// <summary>**A look**: the game's list and the imported modules read again, and the modules on checked again.</summary>
    public void Refresh()
    {
        ReadList();
        Recheck();
    }

    void ReadList()
    {
        var list = store.List(out var problem);
        ListProblem = problem;
        var (installed, broken) = store.Installed();
        var rows = new List<ModuleView>();
        foreach (var e in list.Modules)
            rows.Add(new(e.Id, e.Enabled, installed.GetValueOrDefault(e.Id), installed.ContainsKey(e.Id) ? null
                : broken.TryGetValue(e.Id, out var why) ? SetupWords.Plain(why) : "it is in the list, and is not imported"));
        foreach (var m in installed.Values.Where(m => !list.Modules.Any(e => e.Id == m.Id)).OrderBy(m => m.Id, StringComparer.Ordinal))
            rows.Add(new(m.Id, false, m, null));
        foreach (var (id, why) in broken.Where(b => !list.Modules.Any(e => e.Id == b.Key)).OrderBy(b => b.Key, StringComparer.Ordinal))
            rows.Add(new(id, false, null, SetupWords.Plain(why)));
        Rows = rows;
        Open.RemoveWhere(id => !rows.Any(r => r.Id == id));
        NotUsedOpen.RemoveWhere(id => !rows.Any(r => r.Id == id));
        Ends++;
    }

    /// <summary>A module turned on or off (its checkbox); the list read and checked again.</summary>
    public void Toggle(string id, bool on)
    {
        if (Importing) return;
        if (store.Enable(id, on) is { } why) { Result = [new(Cap(why) + ".", true)]; return; }
        say($"modules page: {id} {(on ? "on" : "off")}");
        Changed();
    }

    /// <summary>A module moved a place up (<paramref name="by"/> -1) or down (+1).</summary>
    public void Move(string id, int by)
    {
        if (Importing) return;
        if (store.Move(id, by) is { } why) { Result = [new(Cap(why) + ".", true)]; return; }
        say($"modules page: {id} moved {(by < 0 ? "up" : "down")}");
        Changed();
    }

    /// <summary>
    /// **A row dropped at another place** (dragged by its handle, or moved by Alt+Up or Alt+Down): the module at <paramref name="from"/> of
    /// the list put at <paramref name="to"/>, the others keeping their order; the list read and checked again.
    /// </summary>
    public void MoveTo(int from, int to)
    {
        if (Importing || from < 0 || from >= Rows.Count || to < 0 || to >= Rows.Count || from == to) return;
        var ids = Rows.Select(r => r.Id).ToList();
        string id = ids[from];
        ids.RemoveAt(from);
        ids.Insert(to, id);
        var listed = store.List(out _).Modules.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        if (store.Order([.. ids.Where(listed.Contains)]) is { } why) { Result = [new(Cap(why) + ".", true)]; return; }
        say($"modules page: {id} moved from place {from + 1} to {to + 1}");
        Changed();
    }

    /// <summary>
    /// **Verify** (a module's details): its files hashed against its import record (<see cref="PlayerModule.VerifyAndMark"/>) on a thread of
    /// its own; a module whose files are not as imported is an error at every check until it is removed and added again, and said so.
    /// </summary>
    public bool StartVerify(string id)
    {
        if (Rows.FirstOrDefault(r => r.Id == id)?.Module is not { } module) return false;
        lock (gate)
        {
            if (verifying is { IsCompleted: false } || verifyDone is not null) return false;
            verifyingId = id;
            VerifyResult = null;
            say($"modules page: verifying {id}");
            verifying = Task.Run(() =>
            {
                Action end;
                try
                {
                    var problems = module.VerifyAndMark();
                    end = () =>
                    {
                        int files = module.Record.Files.Count(f => f.In is null) + module.Record.Archives.Count;
                        VerifyResult = (id, problems.Count == 0
                            ? [new(files == 1 ? "Verified: its file is as imported." : $"Verified: all {N(files)} files are as imported.")]
                            : [new($"{N(problems.Count)} of its files {(problems.Count == 1 ? "is" : "are")} not as imported: {string.Join("; ", problems.Take(4).Select(SetupWords.Plain))}{(problems.Count > 4 ? "; ..." : "")}.", true),
                               new("Remove it and add it again from its pack.", true)]);
                        say($"modules page: verified {id}: {(problems.Count == 0 ? "as imported" : $"{problems.Count} files not as imported, marked an error until it is added again")}");
                        if (problems.Count > 0) Changed();
                    };
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    end = () => { VerifyResult = (id, [new($"It could not be verified: {SetupWords.Plain(e.Message)}.", true)]); say($"modules page: verify of {id} failed: {e.Message}"); };
                }
                lock (gate) verifyDone = end;
            }, CancellationToken.None);
        }
        return true;
    }

    /// <summary>**Sort by dependencies**: each module after what it needs or loads after, said in a line.</summary>
    public void Sort()
    {
        if (Importing) return;
        var moved = store.SortByDependencies();
        Result = [new(moved.Count == 0
            ? "Already in order: each module is after the modules it needs or loads after."
            : $"Sorted by dependencies: {string.Join(", ", moved)} moved after the modules {(moved.Count == 1 ? "it needs" : "they need")} or {(moved.Count == 1 ? "loads" : "load")} after.")];
        say($"modules page: sorted by dependencies: {(moved.Count == 0 ? "in order already" : string.Join(", ", moved) + " moved")}");
        Changed();
    }

    /// <summary>A module removed (its imported copy; the pack it came from is never touched). Answers why not, or null.</summary>
    public string? Remove(string id)
    {
        if (Importing) return "a module is being imported";
        string name = Rows.FirstOrDefault(r => r.Id == id)?.Module?.Manifest.Name ?? id;
        try
        {
            if (store.Remove(id) is { } why) return why;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return SetupWords.Plain(e.Message); }
        Result = [new($"Removed {name}: its imported copy was deleted from your user folder. The original pack is not touched.")];
        say($"modules page: removed {id}");
        Changed();
        return null;
    }

    void Changed()
    {
        ReadList();
        Recheck();
    }

    // ---- adding a pack ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// **Adds a pack** (a folder, a zip or a <c>.nob</c>) on a thread of its own: imported by <see cref="PlayerModuleStore.Add"/>, with 1.1's
    /// own spelling of its paths when the base game's files are at hand; at its end the refused files listed with why, the documents kept
    /// aside counted, the notes said, and the list checked again. Nothing while one runs.
    /// </summary>
    public bool StartAdd(string source) => StartAdd(source, null, false, null);

    /// <summary>
    /// **Replace** (the answer to <see cref="ReplaceAsked"/>): the pack imported over the module of its id (<see cref="ModuleImportOptions.Replace"/>),
    /// which keeps its place in the list and whether it is on; the files of the old copy the pack does not have are taken out.
    /// </summary>
    public bool StartReplace()
    {
        if (ReplaceAsked is not { } q) return false;
        ReplaceAsked = null;
        say($"modules page: replacing {q.Id} (from {q.From}) with {q.Source}");
        return StartAdd(q.Source, q.Id, true, "replaced");
    }

    /// <summary>The Replace question answered Cancel: nothing changes.</summary>
    public void ForgetReplace()
    {
        if (ReplaceAsked is not { } q) return;
        ReplaceAsked = null;
        say($"modules page: {q.Id} not replaced (cancelled)");
        Ends++;
    }

    /// <summary>
    /// **Update** (a module's details): the module added again from where it came from (its record's source: the folder, the zip or the
    /// <c>.nob</c>), keeping its place and whether it is on; what it holds there now is taken in and anything else taken out of the copy.
    /// Nothing when the source is not there (<see cref="UpdateBlocked"/> says so).
    /// </summary>
    public bool StartUpdate(string id)
    {
        if (Rows.FirstOrDefault(r => r.Id == id)?.Module is not { } m || UpdateBlocked(m) is not null) return false;
        return StartAdd(m.Record.Source, m.Id, true, "updated");
    }

    /// <summary>Why a module cannot be updated from where it came from (its folder or file is not there any more), or null when it can.</summary>
    public static string? UpdateBlocked(PlayerModule m)
    {
        string source = m.Record.Source;
        bool folder = m.Record.SourceKind is "folder" or "";
        if (source.Length > 0 && (folder ? Directory.Exists(source) : File.Exists(source))) return null;
        return folder ? $"Its folder, {source}, is not there any more: use Add a module\u2026 with its new folder."
                      : $"Its file, {source}, is not there any more: use Add a module\u2026 with the file where it is now.";
    }

    bool StartAdd(string source, string? id, bool replace, string? verb)
    {
        lock (gate)
        {
            if (adding is { IsCompleted: false } || addDone is not null) return false;
            progress = null;
            cancelAdd?.Dispose();
            cancelAdd = new CancellationTokenSource();
            var token = cancelAdd.Token;
            AddingName = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
            Result = [];
            ReplaceAsked = null;
            cancelCheck?.Cancel();
            say($"modules page: {(verb == "updated" ? "updating " + id + " from" : "adding")} {source}");
            var reports = new Reports(this);
            adding = Task.Run(() =>
            {
                Action end;
                try
                {
                    string? folder = baseFolder();
                    using var install = folder is null ? null : GameInstall.TryOpen(folder);
                    var report = store.Add(source, new ModuleImportOptions { Id = id, Replace = replace, By = Platform.Host.PlatformVersion.Text, Progress = reports }, install, token);
                    end = () => Added(source, report, verb);
                }
                catch (OperationCanceledException)
                {
                    end = () => { Result = [new($"Adding {AddingName} cancelled. Add it again to carry on.", true)]; say("modules page: the import was cancelled"); };
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
                {
                    end = () => { Result = [new($"The import of {AddingName} stopped: {SetupWords.Plain(e.Message)}", true)]; say($"modules page: the import failed: {e.Message}"); };
                }
                lock (gate) addDone = end;
            }, CancellationToken.None);
        }
        return true;
    }

    void Added(string source, ModuleImportReport report, string? verb)
    {
        if (report.Reason == ModuleRefusal.AlreadyThere && report.There is { } there)
        {
            // Not the command line's refusal: the launcher asks whether to replace it (the import ran up to finding the id; nothing written).
            ReplaceAsked = new(source, there.Id, there.Manifest.Name, there.Record.Source);
            Result = [];
            say($"modules page: {there.Id} is in the list already (from {there.Record.Source}): asking whether to replace it with {source}");
            Changed();
            return;
        }
        var lines = new List<PageLine>();
        var refused = report.LeftOut.Where(o => o.Class is "code" or "program").ToList();
        var documents = report.LeftOut.Where(o => o.Class == "document").ToList();
        if (report.Module is { } now && report.There is { } was)
        {
            // An update or a replace (or the same pack added again): what changed, in one line; the files put in by hand that went, named.
            lines.Add(new(Changes(now, was.Record, verb ?? "updated")));
            var gone = report.Removed.Where(r => !was.Record.Files.Any(f => f.In is null && f.Path.Equals(r, StringComparison.OrdinalIgnoreCase))
                                                 && !was.Record.Archives.Any(a => a.Path.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();
            if (gone.Count > 0)
                lines.Add(new($"Taken out of Vitomir's copy, as they are not in its {(now.Record.SourceKind is "folder" or "" ? "folder" : "file")}: "
                              + string.Join(", ", gone.Take(6).Select(SetupWords.Plain)) + (gone.Count > 6 ? $" and {N(gone.Count - 6)} more" : "") + "."));
        }
        else if (report.Module is { } added)
            lines.Add(new($"Added {added.Manifest.Name} {added.Manifest.Version}: on, at the end of the list."));
        if (report.Module is { } m)
        {
            lines.Add(new($"    {Cap(KindWords(m))}: {N(m.Record.Files.Count)} {(m.Record.Files.Count == 1 ? "file" : "files")}, {Size(m.Record.Files.Sum(f => f.Size))}", false, true));
            if (m.Rules && report.There is null) lines.Add(new("It is a rules module: every player of a network game needs it, and saves name it."));
            int broken = m.Record.Files.Count(PlayerModuleChecks.DoesNotRead);
            if (broken > 0) lines.Add(new($"{N(broken)} of its files cannot be read: they are listed with the problems below, and the game does not start with it on until they are fixed.", true));
        }
        else lines.Add(new($"{AddingName} was not added: {SetupWords.Plain(report.Refusal ?? "it was refused")}.", true));
        if (refused.Count > 0)
        {
            // The count said, plainly (they were never imported: not an error); the files behind Details (D1), each in launcher.log.
            lines.Add(new(NotUsedLine(refused.Count) + "."));
            foreach (var o in refused.Take(12)) lines.Add(new($"    {SetupWords.Plain(o.Path)}: {SetupWords.Plain(o.Why)}", false, true));
            if (refused.Count > 12) lines.Add(new($"    and {N(refused.Count - 12)} more (each named in launcher.log)", false, true));
            foreach (var o in refused) say($"modules page: refused ({o.Class}): {o.Path}: {o.Why}");
        }
        if (documents.Count > 0)
            lines.Add(new($"Kept aside, never read by the game: {N(documents.Count)} {(documents.Count == 1 ? "document" : "documents")} ({string.Join(", ", documents.Take(4).Select(d => SetupWords.Plain(Path.GetFileName(d.Path))))}{(documents.Count > 4 ? ", ..." : "")}), in its Documents folder."));
        foreach (var n in report.Notes) lines.Add(new(Cap(SetupWords.Plain(n)) + "."));
        Result = lines;
        say($"modules page: {(report.Module is { } done ? $"{(report.There is null ? "added" : verb ?? "updated")} {done.Id} {done.Manifest.Version}" : $"not added: {report.Refusal}")}; "
            + $"{refused.Count} refused, {documents.Count} documents kept aside{(report.Removed.Count > 0 ? $", {report.Removed.Count} files taken out of its copy ({string.Join(", ", report.Removed)})" : "")}");
        Changed();
    }

    /// <summary>Cancels the running import (the next add of the same pack carries on from there).</summary>
    public void Cancel()
    {
        lock (gate) cancelAdd?.Cancel();
    }

    sealed class Reports(ModulesPage page) : IProgress<SetupProgress>
    {
        public void Report(SetupProgress value)
        {
            lock (page.gate) page.progress = value;
            page.OnProgress?.Invoke(value);
        }
    }

    // ---- the problems list ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// **The modules on checked again**, as a start checks them, on a thread of its own (an earlier check still running is cancelled and its
    /// result dropped); its end taken by <see cref="Poll"/>. Not while a pack is imported (its end checks again).
    /// </summary>
    public void Recheck()
    {
        lock (gate)
        {
            if (adding is { IsCompleted: false } || addDone is not null) { stale = true; return; }
            cancelCheck?.Cancel();
            if (!Rows.Any(r => r.On) && ListProblem is null)
            {
                // None on: nothing to check, and the game plays as 1.1 made it (no thread, no wait).
                ++generation;
                checking = null;
                checkDone = null;
                stale = false;
                Start = null;
                Ends++;
                return;
            }
            stale = true;
            cancelCheck = new CancellationTokenSource();
            var token = cancelCheck.Token;
            int mine = ++generation;
            checking = Task.Run(() =>
            {
                Action end;
                try
                {
                    string? folder = baseFolder();
                    using var install = folder is null ? null : GameInstall.TryOpen(folder);
                    var start = store.Prepare(install, cancel: token);
                    end = () =>
                    {
                        Start = start;
                        say($"modules page: checked {start.Order.Count} modules on: {start.Errors.Count()} errors, {start.Warnings.Count()} warnings"
                            + (start.Warnings.Any() ? start.Accepted ? " (accepted)" : " (not accepted)" : "") + (start.Cached ? ", the checks kept from the last" : ""));
                    };
                }
                catch (OperationCanceledException) { end = () => { }; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
                {
                    end = () =>
                    {
                        Start = new ModuleStart([], [new(ProblemLevel.Error, "", "", $"the modules could not be checked: {SetupWords.Plain(e.Message)}", "try again; if it stays, turn the modules off") { Kind = "check" }], "", false, false);
                        say($"modules page: the check failed: {e.Message}");
                    };
                }
                lock (gate)
                    if (mine == generation) { checkDone = end; }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// **Play anyway with these**: the warnings of the combination on accepted (kept with the modules' versions and hashes; any change asks
    /// again). Answers why not, or null.
    /// </summary>
    public string? Accept()
    {
        if (Checking) return "the modules are being checked";
        if (Start is not { } start) return "the modules have not been checked yet";
        if (store.Accept(start) is { } why) return why;
        say($"modules page: accepted {start.Warnings.Count()} warnings of the combination {start.Combination[..12]}");
        Start = start with { Accepted = true };
        Ends++;
        return null;
    }

    /// <summary>Why Play cannot start the game with the modules on now, or null when it can.</summary>
    public string? PlayBlocked()
    {
        if (Importing) return "Unavailable while a module is being imported.";
        if (Checking) return "Unavailable while the enabled modules are being checked.";
        if (Start is not { } s) return null;
        int errors = s.Errors.Count(), warnings = s.Warnings.Count();
        if (errors > 0) return $"The enabled modules have {N(errors)} {(errors == 1 ? "error" : "errors")}, so the game does not start with them: fix {(errors == 1 ? "it" : "them")} or turn those modules off (the problems are listed below and on the Modules page).";
        if (warnings > 0 && !s.Accepted) return $"The enabled modules have {N(warnings)} {(warnings == 1 ? "warning" : "warnings")} not accepted for this combination: select Play anyway with these, or change the modules.";
        return null;
    }

    /// <summary>Whether the combination on has warnings the player can accept now (no error, not accepted yet, checked).</summary>
    public bool CanAccept => !Importing && !Checking && Start is { } s && !s.Errors.Any() && s.Warnings.Any() && !s.Accepted;

    // ---- the end of a step ----------------------------------------------------------------------------------------------------------

    /// <summary>**An import's or a check's end taken** on the launcher's thread. Answers whether one ended.</summary>
    public bool Poll()
    {
        Action? add, check, verified;
        lock (gate)
        {
            add = addDone;
            check = checkDone;
            verified = verifyDone;
            if (add is not null) { addDone = null; adding = null; progress = null; }
            if (check is not null) { checkDone = null; checking = null; stale = false; }
            if (verified is not null) { verifyDone = null; verifying = null; }
        }
        add?.Invoke();
        check?.Invoke();
        verified?.Invoke();
        if (add is null && check is null && verified is null) return false;
        Ends++;
        return true;
    }

    /// <summary>Waits for the running import and check and takes their ends (tests, the launcher's scripted steps).</summary>
    public void Wait(TimeSpan? within = null)
    {
        var until = DateTime.UtcNow + (within ?? TimeSpan.FromMinutes(15));
        while (true)
        {
            Task? a, c, v;
            lock (gate) (a, c, v) = (adding, checking, verifying);
            foreach (var t in new[] { a, c, v })
                if (t is not null && !t.Wait(until - DateTime.UtcNow is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero))
                    throw new TimeoutException("the modules page's step did not end");
            Poll();
            if (!Importing && !Checking && Verifying is null) return;
            if (DateTime.UtcNow > until) throw new TimeoutException("the modules page's step did not end");
            Thread.Sleep(5);
        }
    }

    // ---- the words ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// **What an update or a replace changed**, in one line: <c>Texturemod updated: 12 files (1 changed, 1 new).</c>, or <c>…: 12 files, none
    /// changed.</c>; by each file's path and SHA-256 against the record it had.
    /// </summary>
    public static string Changes(PlayerModule now, ModuleRecord was, string verb)
    {
        static Dictionary<string, string> Of(ModuleRecord r)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in r.Files.Where(f => f.In is null)) map[f.Path] = f.Sha256;
            foreach (var a in r.Archives) map["archive " + a.Path] = a.Sha256;
            return map;
        }
        var before = Of(was);
        var after = Of(now.Record);
        int changed = after.Count(a => before.TryGetValue(a.Key, out var sha) && sha != a.Value);
        int added = after.Keys.Count(k => !before.ContainsKey(k));
        int gone = before.Keys.Count(k => !after.ContainsKey(k));
        var parts = new List<string>();
        if (changed > 0) parts.Add($"{N(changed)} changed");
        if (added > 0) parts.Add($"{N(added)} new");
        if (gone > 0) parts.Add($"{N(gone)} no longer in it");
        int files = now.Record.Files.Count;
        string count = $"{N(files)} {(files == 1 ? "file" : "files")}";
        return $"{now.Manifest.Name} {verb}: {count}{(parts.Count > 0 ? $" ({string.Join(", ", parts)})" : ", none changed")}.";
    }

    static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>A module's kinds as the page says them: <c>look, interface</c>, and <c>rules</c> when it is a rules module.</summary>
    public static string KindWords(PlayerModule m) =>
        PackKindWords.Words(m.Kinds) + (m.Rules ? m.RulesByActions && !PackKindWords.IsRules(m.Kinds) ? "; rules (its interface opens the vault or advancement)" : "; rules" : "");

    /// <summary>What a module needs, loads after and cannot be played with, as one line (empty when nothing).</summary>
    public static string Needs(PlayerModuleManifest m)
    {
        var parts = new List<string>();
        if (m.Dependencies.Count > 0) parts.Add("Needs " + string.Join(", ", m.Dependencies.Select(d => $"{d.Id} {d.Versions.Words}")));
        if (m.LoadAfter.Count > 0) parts.Add("loads after " + string.Join(", ", m.LoadAfter));
        if (m.Incompatible.Count > 0) parts.Add("cannot be played with " + string.Join(", ", m.Incompatible));
        if (parts.Count == 0) return "";
        string line = string.Join("; ", parts) + ".";
        return Cap(line);
    }

    /// <summary>
    /// **A module's details**: its description, where it came from and when, its files by kind, the models its textures were made for and
    /// what its author says fits, its interface bindings, the documents kept aside and what was refused.
    /// </summary>
    public static IReadOnlyList<PageLine> Details(ModuleView row)
    {
        var lines = new List<PageLine>();
        if (row.Module is not { } m)
        {
            lines.Add(new($"{row.Id}: {row.Broken}.", true));
            lines.Add(new("Add it again from its pack, or remove it."));
            return lines;
        }
        var man = m.Manifest;
        if (man.Description.Length > 0) lines.Add(new(Cap(SetupWords.Plain(man.Description)) + (man.Description.EndsWith('.') ? "" : ".")));
        lines.Add(new($"Id {m.Id}, version {man.Version}{(man.Author.Length > 0 ? $", by {SetupWords.Plain(man.Author)}" : "")}; imported "
                      + $"{m.Record.ImportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} from {SetupWords.Plain(m.Record.Source)}"
                      + (m.Record.Layout == "jsgme" ? " (a JSGME pack)" : "") + "."));
        foreach (var kind in new[] { "look", "world", "interface" })
        {
            var of = m.Record.Files.Where(f => f.Kind == kind).ToList();
            if (of.Count == 0) continue;
            var folders = of.GroupBy(f => f.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count())
                            .Select(g => $"{g.Key}/ {N(g.Count())}").Take(5);
            string what = kind switch { "look" => "Look", "world" => "World", _ => "Interface" };
            lines.Add(new($"{what}: {N(of.Count)} {(of.Count == 1 ? "file" : "files")}, {Size(of.Sum(f => f.Size))} ({string.Join(", ", folders)})."));
        }
        if (m.Record.Archives.Count > 0) lines.Add(new($"Archives kept whole: {string.Join(", ", m.Record.Archives.Select(a => a.Path))}."));
        // Each line only when it says something (H3).
        if (Needs(man) is { Length: > 0 } needs) lines.Add(new(needs));
        if (man.MadeFor is { Count: > 0 } bases)
            lines.Add(new("Made for: " + string.Join(", ", bases.Select(b => b == Platform.Modules.AssetId.OneOne ? "1.1's models" : $"{b}'s models")) + "."));
        if (man.Fits.Count > 0)
            lines.Add(new("Fits, by its author: " + string.Join("; ", man.Fits.Select(f => f.Value is null ? $"every file with {f.Key}'s models" : $"{N(f.Value.Count)} files with {f.Key}'s models")) + "."));
        foreach (var (element, action) in man.Actions)
            lines.Add(InterfaceActions.Find(action) is { } a
                ? InterfaceActions.OneOneBarElements.Contains(element)
                    ? new($"Button {element}: bound to {a.Name}, not used (1.1's own element keeps what it does).")
                    : new($"Button {element}: {a.Words}.")
                : new($"Button {element}: bound to \"{action}\", which is not one of Vitomir's interface actions.", true));
        var docs = m.Record.LeftOut.Where(o => o.Class == "document").ToList();
        if (docs.Count > 0)
            lines.Add(new($"Documents kept aside (in {Path.Combine(m.Folder, PlayerModule.DocumentsFolderName)}): "
                          + string.Join(", ", docs.Take(8).Select(d => SetupWords.Plain(d.Path))) + (docs.Count > 8 ? $" and {N(docs.Count - 8)} more" : "") + "."));
        var broken = m.Record.Files.Where(PlayerModuleChecks.DoesNotRead).ToList();
        if (broken.Count > 0) lines.Add(new($"{N(broken.Count)} {(broken.Count == 1 ? "file does" : "files do")} not read: {string.Join(", ", broken.Take(3).Select(f => f.Path))}{(broken.Count > 3 ? ", ..." : "")}.", true));
        return lines;
    }

    /// <summary>The files a pack carried that its import left out (program files: modules carry data only), each with why.</summary>
    public static IReadOnlyList<(string Path, string Why)> NotUsed(PlayerModule m) =>
        [.. m.Record.LeftOut.Where(o => o.Class is "code" or "program").Select(o => (SetupWords.Plain(o.Path), SetupWords.Plain(o.Why)))];

    /// <summary>The folded line of a module's files not used: <c>58 files not used (program files Vitomir does not run)</c>.</summary>
    public static string NotUsedLine(int count) => $"{N(count)} {(count == 1 ? "file" : "files")} not used (program files Vitomir does not run)";

    /// <summary>
    /// **The list of a module's files not used**, opened from its folded line: first, plainly, that they were never imported (its folder
    /// holds only its data), so there is nothing to clean; then each file and why, the first 40 and how many more.
    /// </summary>
    public static IReadOnlyList<PageLine> NotUsedLines(PlayerModule m)
    {
        var files = NotUsed(m);
        if (files.Count == 0) return [];
        var lines = new List<PageLine> { new("They were never imported: the module's folder holds only its data, so there is nothing to clean.") };
        foreach (var (path, why) in files.Take(40)) lines.Add(new($"{path}: {why}", false, true));
        if (files.Count > 40) lines.Add(new($"and {N(files.Count - 40)} more", false, true));
        return lines;
    }

    /// <summary>
    /// **One problem as the page says it**: its level's word and the module, what is wrong, the files it is about (the first three and how
    /// many more), and what to do in the page's words (the command line's hints left out).
    /// </summary>
    public static (string Head, string Files, string ToDo) Words(PlayerModuleProblem p, IReadOnlyDictionary<string, string>? names = null)
    {
        // The modules by the names the list shows them by (one term per idea); a module not added keeps its id.
        string Named(string text)
        {
            if (names is null) return text;
            foreach (var (id, name) in names.OrderByDescending(n => n.Key.Length))
                if (id != name) text = Regex.Replace(text, $"(?<![\\w-]){Regex.Escape(id)}(?![\\w-])", name.Replace("$", "$$", StringComparison.Ordinal));
            return text;
        }
        string level = p.Level switch { ProblemLevel.Error => "Error", ProblemLevel.Warning => "Warning", _ => "Note" };
        string who = p.Module.Length > 0 ? Named(p.Module) : "the modules";
        if (p.Kind == "added")
        {
            // Files put into Vitomir's copy by hand: one problem of the module's, every file named (up to eight), in plain words (ours).
            var put = p.Files.Count > 0 ? p.Files : [p.File];
            string listed = string.Join(", ", put.Take(8).Select(SetupWords.Plain)) + (put.Count > 8 ? $" and {N(put.Count - 8)} more" : "");
            return ($"{level}: {who} has files Vitomir did not add.",
                    $"{(put.Count == 1 ? "This file was" : "These files were")} put into Vitomir's copy of the module by hand: {listed}. "
                    + "Vitomir plays only what it added, so it can check a module is the same on every machine.",
                    $"To do: put {(put.Count == 1 ? "the file" : "the files")} in the module's own folder and select Update, or turn the module off.");
        }
        string text = Named(SetupWords.Plain(p.Text));
        string files = p.Files.Count > 1
            ? string.Join(", ", p.Files.Take(3)) + (p.Files.Count > 3 ? $" and {N(p.Files.Count - 3)} more" : "")
            : p.File;
        string todo = Named(p.PageToDo);
        // "Error: Story Bits needs …" where the check says "it needs …" (H5); another "it" may be a file's, so it stays.
        string head = text.StartsWith("it needs ", StringComparison.Ordinal) ? $"{level}: {who} {text[3..]}." : $"{level}: {who}: {text}.";
        return (head, files.Length > 0 ? "Files: " + SetupWords.Plain(files) : "", todo.Length > 0 ? "To do: " + SetupWords.Plain(todo) + "." : "");
    }

    /// <summary>A size as the page says it: <c>755 MB</c>, <c>1.3 GB</c>, <c>under 0.1 MB</c> (never <c>0.0 MB</c>).</summary>
    public static string Size(long bytes) => bytes > 0 && bytes < 100_000 ? "under 0.1 MB" : SetupWords.Size(bytes);
}
