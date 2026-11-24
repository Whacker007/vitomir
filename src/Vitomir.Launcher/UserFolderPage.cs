// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The user folder as the launcher shows it: where it is and how it was chosen, a first start's choices, and the move.
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>
/// **The launcher's user folder**, knowing no SDL: the choice as it stands (<see cref="UserFolderChoice"/>: <c>VITOMIR_USERDATA</c>, then
/// <c>vitomir.ini</c>, then the usual place), its readiness item, the first start's three choices (<see cref="UserFolderSetup"/>), and
/// **Move…** (<see cref="UserFolderMove"/>) run on a thread of its own with its progress and Cancel. The Play page and the Folders page
/// show it; the launcher takes a new folder once a choice or a move has made it the one in use (<see cref="Launcher"/>'s rebinding).
/// </summary>
public sealed class UserFolderPage(UserFolderChoice choice, FolderPlaces places, ISetupMachine machine, string madeBy, Action<string> say)
{
    readonly object gate = new();
    CancellationTokenSource? cancel;
    Task? moving;
    UserFolderMoveReport? ended;
    SetupProgress? progress;

    /// <summary>The choice as it stands now (read again after each choice and move).</summary>
    public UserFolderChoice Choice { get; private set; } = choice;

    /// <summary>The machine's places the folder is checked against.</summary>
    public FolderPlaces Places { get; } = places;

    /// <summary>The user folder's readiness item.</summary>
    public ReadyItem Item => Readiness.UserFolder(Choice, Places);

    /// <summary>Null when <c>vitomir.ini</c> can be written in the Vitomir folder (Use an existing one…, Make portable, Move…), else why not.</summary>
    public string? IniWhy => Choice.Source == UserFolderSource.Variable
        ? $"{UserFolderPlace.VariableName} names your user folder (a developer's), and it beats vitomir.ini"
        : UserFolderSetup.IniWhy(Choice.Place, Places);

    /// <summary>The last choice's or move's words.</summary>
    public IReadOnlyList<PageLine> Result { get; private set; } = [];

    /// <summary>Keep the old folder after a move (the Folders page's checkbox; off by default).</summary>
    public bool KeepOld { get; set; }

    /// <summary>The choice read again (the ini may have been written).</summary>
    public void Reload() => Choice = UserFolderChoice.Resolve(Choice.Place);

    UserFolderAnswer Answered(string what, UserFolderAnswer answer)
    {
        say($"folders page: {what}: {(answer.Done ? "done" : "not done")}: {answer.Line}");
        Result = [new(answer.Line, !answer.Done)];
        Reload();
        return answer;
    }

    /// <summary>**Use the usual place** (made and marked; a bad ini mended).</summary>
    public UserFolderAnswer UseUsual() => Answered("use the usual place", UserFolderSetup.UseUsual(Choice.Place, Places, madeBy));

    /// <summary>**Use an existing one…**: the folder the picker answered, taken when it looks like a user folder of Vitomir's.</summary>
    public UserFolderAnswer UseExisting(string folder) => Answered($"use an existing one ({folder})", UserFolderSetup.UseExisting(Choice.Place, folder, Places));

    /// <summary>**Make portable**: <c>UserData</c> inside the Vitomir folder.</summary>
    public UserFolderAnswer MakePortable() => Answered("make portable", UserFolderSetup.MakePortable(Choice.Place, Places, madeBy));

    // ---- the move ---------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a move runs now.</summary>
    public bool Moving { get { lock (gate) return moving is not null || ended is not null; } }

    /// <summary>The running move's progress, or null.</summary>
    public SetupProgress? Progress { get { lock (gate) return progress; } }

    /// <summary>Where the running move copies to.</summary>
    public string MovingTo { get; private set; } = "";

    /// <summary>How many moves' ends have been taken.</summary>
    public int Ends { get; private set; }

    /// <summary>**The move checked first** (<see cref="UserFolderMove.Plan"/>): the plan, or null with why it is refused.</summary>
    public UserFolderMovePlan? Plan(string target, out string? refused)
    {
        Reload();
        var plan = UserFolderMove.Plan(Choice, target, Places, machine, out refused);
        say(plan is null ? $"folders page: move to {target}: refused: {refused}" : $"folders page: move to {plan.To}: {plan.Files} files, {SetupWords.Size(plan.Bytes)}"
            + (plan.Resuming ? ", carrying on a move that stopped" : "") + string.Concat(plan.Warnings.Select(w => "; warning: " + w)));
        return plan;
    }

    /// <summary>**The move started** on a thread of its own; <paramref name="switched"/> is told the new folder once the ini names it (before the old one is removed).</summary>
    public void StartMove(UserFolderMovePlan plan, Action<string> switched)
    {
        lock (gate)
        {
            if (moving is not null || ended is not null) return;
            Result = [];
            progress = null;
            MovingTo = plan.To;
            cancel?.Dispose();
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            bool keep = KeepOld;
            var reports = new Reports(this);
            say($"folders page: moving {plan.From.Folder} to {plan.To}{(keep ? ", the old folder kept" : "")}");
            moving = Task.Run(() =>
            {
                var report = UserFolderMove.Run(plan, keep, madeBy, reports, token, switched);
                lock (gate) ended = report;
            }, CancellationToken.None);
        }
    }

    sealed class Reports(UserFolderPage page) : IProgress<SetupProgress>
    {
        public void Report(SetupProgress value) { lock (page.gate) page.progress = value; }
    }

    /// <summary>**Cancel**: the move stops at its next file; the old folder stays the one in use.</summary>
    public void CancelMove()
    {
        lock (gate) cancel?.Cancel();
    }

    /// <summary>**A move's end taken** on the launcher's thread: its report (the result's words set), or null when none ended.</summary>
    public UserFolderMoveReport? Poll()
    {
        UserFolderMoveReport? report;
        lock (gate)
        {
            if (ended is null) return null;
            report = ended;
            ended = null;
            moving = null;
            progress = null;
        }
        Ends++;
        say($"folders page: move: {report.Line} ({report.Copied + report.Kept} files, {SetupWords.Size(report.Bytes)}, each checked by its SHA-256)");
        Result = [new(report.Line, !report.Done)];
        Reload();
        return report;
    }

    /// <summary>Waits for the running move (tests, the scripted steps); its end is taken by the next <see cref="Poll"/>.</summary>
    public void Wait(TimeSpan? within = null)
    {
        Task? t;
        lock (gate) t = moving;
        if (t is not null && !t.Wait(within ?? TimeSpan.FromMinutes(15))) throw new TimeoutException("the move did not end");
    }

    /// <summary>The ini's lines as the Folders page shows them (its path and what it says, or that there is none).</summary>
    public IReadOnlyList<PageLine> IniLines()
    {
        var ini = Choice.Ini;
        if (Choice.Source == UserFolderSource.Variable && !ini.Exists) return [new($"{UserFolderPlace.VariableName} names the user folder (a developer's); no {VitomirIni.FileName} at {ini.Path}.")];
        if (!ini.Exists) return [new($"No {VitomirIni.FileName} at {ini.Path}: the usual place is used.")];
        // Its lines, a bad one in red (F12: the problem said once, the readiness line above says what to do); a problem of no line said.
        var problems = Choice.Problems.Concat(ini.Problems).Distinct().ToList();
        var bad = problems.Where(p => p.Line > 0).Select(p => p.Line).ToHashSet();
        var lines = new List<PageLine> { new($"{VitomirIni.FileName} at {ini.Path} says:") };
        for (int n = 1; n <= ini.Lines.Count; n++)
        {
            string l = ini.Lines[n - 1];
            if (l.Trim().Length == 0 || l.TrimStart().StartsWith(';') || l.TrimStart().StartsWith('#')) continue;
            lines.Add(new("    " + l.Trim(), bad.Contains(n)));
        }
        lines.AddRange(problems.Where(p => p.Line == 0).Select(p => new PageLine(p.Words, true)));
        return lines;
    }
}
